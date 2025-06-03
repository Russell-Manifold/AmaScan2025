using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using AmaScan.Classes;
using AmaScan.sqliteModels;
using SQLite;

namespace AmaScan
{
    public partial class ReceivingPage : ContentPage, INotifyPropertyChanged
    {
        private readonly DatabaseHelper _dbHelper;
        private PoHeader _poHeader;
        public ICommand OnPoLineLongPressed { get; }
        public event PropertyChangedEventHandler PropertyChanged;

        public string PoNumber => _poHeader?.OrderNo ?? "";
        public string DueDateFormatted => _poHeader?.DueDate.ToString("yyyy-MM-dd") ?? "";
        public string Status => _poHeader?.Status ?? "";

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            _poHeader = ReceivingSession.CurrentPoHeader;  // Refresh PO header here

            // Update UI bindings
            OnPropertyChanged(nameof(PoNumber));
            OnPropertyChanged(nameof(DueDateFormatted));
            OnPropertyChanged(nameof(Status));

            AcceptSwitch_Toggled(AcceptSwitch, new ToggledEventArgs(AcceptSwitch.IsToggled));

            if (!string.IsNullOrEmpty(_poHeader?.OrderNo))
            {
                await LoadPoLinesAsync(_poHeader.OrderNo);
            }
        }

        public ReceivingPage()
        {
            InitializeComponent();
            BindingContext = this;
            _dbHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));

            _poHeader = ReceivingSession.CurrentPoHeader;

            if (_poHeader == null)
            {
                // Handle missing session case gracefully
                Shell.Current.GoToAsync("..");
            }
        }

        protected void OnPropertyChanged(string propertyName) =>
         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public ObservableCollection<PoLine> PoLines { get; set; } = new();

        private async Task LoadPoLinesAsync(string poNumber)
        {
            var lines = await _dbHelper.GetPoLinesByOrderNoAsync(poNumber);

            PoLines.Clear();
            foreach (var line in lines)
            {
                PoLines.Add(line);
            }

            OnPropertyChanged(nameof(PoLines));
        }

        private void OnStartReceivingClicked(object sender, EventArgs e)
        {
            ReceivingInputSection.IsVisible = true;
            StartReceivingButton.IsVisible = false;
            BarcodeEntry.Text = string.Empty;
            QuantityEntry.Text = string.Empty;
            BarcodeEntry.Focus();
        }

        private async void OnBarcodeEntered(object sender, EventArgs e)
        {
            QuantityEntry.Focus();
        }
        private async void OnSaveProgressClicked(object sender, EventArgs e)
        {
            var scannedBarcode = BarcodeEntry.Text?.Trim();
            if (string.IsNullOrEmpty(scannedBarcode))
            {
                await DisplayAlert("Error", "Please enter a barcode.", "OK");
                return;
            }

            var stockItem = await _dbHelper.ResolveStockItemByBarcodeAsync(scannedBarcode);
            if (stockItem == null || string.IsNullOrEmpty(stockItem.bar_code))
            {
                await DisplayAlert("Error", $"Scanned barcode '{scannedBarcode}' not found in stock items.", "OK");
                return;
            }

            var matchingLine = PoLines.FirstOrDefault(line =>
                line.ItemBarcode.Equals(stockItem.bar_code, StringComparison.OrdinalIgnoreCase));

            if (matchingLine == null)
            {
                await DisplayAlert("Error", $"Item '{stockItem.bar_code}' not found in this PO.", "OK");
                return;
            }

            var packSize = stockItem.pack.GetValueOrDefault(1);
            if (!decimal.TryParse(QuantityEntry.Text, out decimal thisQty) || thisQty <= 0)
            {
                await DisplayAlert("Error", "Invalid quantity entered.", "OK");
                return;
            }

            decimal thisTotQty = thisQty * packSize;

            // ?? Use switch toggle to decide whether Accept or Reject
            if (AcceptSwitch.IsToggled)
            {
                matchingLine.ScanAcceptQty += thisTotQty;
            }
            else
            {
                matchingLine.ScanRejectQty += thisTotQty;
            }

            // Append to ReceivedString
            if (string.IsNullOrWhiteSpace(matchingLine.ReceivedString))
                matchingLine.ReceivedString = $"{thisTotQty}";
            else
                matchingLine.ReceivedString += $" + {thisTotQty}";

            await _dbHelper.UpdatePoLineAsync(matchingLine);
            OnPropertyChanged(nameof(PoLines));

            BarcodeEntry.Text = string.Empty;
            QuantityEntry.Text = string.Empty;
            BarcodeEntry.Focus();
        }
        private async void OnRestartClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Restart", "Clear all received/rejected quantities?", "Yes", "No");
            if (confirm)
            {
                foreach (var line in PoLines)
                {
                    line.ScanAcceptQty = 0;
                    line.ScanRejectQty = 0;
                    await _dbHelper.UpdatePoLineAsync(line);
                }
                OnPropertyChanged(nameof(PoLines));
            }
        }

        private async void OnCompleteClicked(object sender, EventArgs e)
        {
            try
            {
                var poLines = await _dbHelper.GetPoLinesByOrderNoAsync(PoNumber);

                // Step 1: Check for discrepancies
                bool hasDiscrepancies = poLines.Any(line =>
                    line.OrderedQty != (line.ScanAcceptQty + line.ScanRejectQty));

                if (hasDiscrepancies)
                {
                    bool confirm = await DisplayAlert("Confirm Submission", "Proceed to generate GRV?", "Yes", "No");
                    if (!confirm)
                        return;

                    // Step 3: Require supervisor authorisation
                    await DisplayAlert("Not authorised", "Discrepancies found. Supervisor authorisation is required.", "OK");
                    bool authorised = await PromptSupervisorauthorisationAsync();

                    bool confirmed = await DisplayAlert("Confirm Submission", "Proceed to generate GRV with discrepancies?", "Yes", "No");
                    if (!confirmed)
                        return;
                }
                else
                {
                    // Step 2: Confirm with user
                    bool confirmed = await DisplayAlert("Confirm Submission", "No discrepancies found. Proceed to generate GRV?", "Yes", "No");
                    if (!confirmed)
                        return;
                }

                // Step 4: Send data to API
                bool success = await SendToApiForGrvAsync(PoNumber, poLines);

                if (success)
                    await DisplayAlert("Success", "GRV successfully generated.", "OK");
                else
                    await DisplayAlert("Error", "Failed to generate GRV. Please try again.", "OK");
            }
            catch (Exception ex)
            {
                // Optional: Log error
                await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
            }
        }

        private async Task<bool> PromptSupervisorauthorisationAsync()
        {
           // string username = await DisplayPromptAsync("Supervisor authorisation", "Enter supervisor username:");
            string password = await DisplayPromptAsync("Password", "Enter supervisor password:", "OK", "Cancel", "Password", -1, Keyboard.Text);

            if (string.IsNullOrWhiteSpace(password))
                return false;

            //return await ApiService.ValidateSupervisorAsync(username, password);
            return true;
        }

        private async Task<bool> SendToApiForGrvAsync(string poNumber, List<PoLine> poLines)
        {
            try
            {
                // var result = await ApiService.SubmitCompletedPoAsync(poNumber, poLines);
                // return result.IsSuccess;
                return true;
            }
            catch
            {
                return false;
            }
        }
        public ICommand ResetPoLineCommand => new Command<PoLine>(async (line) =>
        {
            if (line == null) return;

            bool confirm = await Shell.Current.DisplayAlert("Reset Line", "Are you sure you want to reset this line?", "Yes", "No");
            if (!confirm) return;

            line.ScanAcceptQty = 0;
            line.ScanRejectQty = 0;
            line.ReceivedString = string.Empty;
            await _dbHelper.UpdatePoLineAsync(line);

            // Refresh the list
            await LoadPoLinesAsync(PoNumber);
        });

        private void AcceptSwitch_Toggled(object sender, ToggledEventArgs e)
        {
            if (e.Value)
            {
                // Accept mode — use green
                SaveButton.BackgroundColor = Colors.Green;
                SaveButton.TextColor = Colors.White;
                SaveButton.Text = "Accept";
            }
            else
            {
                // Reject mode — use red
                SaveButton.BackgroundColor = Colors.Red;
                SaveButton.TextColor = Colors.White;
                SaveButton.Text = "Save as Rejected";
            }
        }

        private void OnSave_Click(object sender, EventArgs e)
        {

        }

        private void OnManualInputToggled(object sender, ToggledEventArgs e)
        {
            if (e.Value)
            {
                // Enable manual typing
                BarcodeEntry.IsReadOnly = false;
                BarcodeEntry.Focus();
            }
            else
            {
                // Revert to scan-only (read-only, suppress keyboard)
                BarcodeEntry.IsReadOnly = true;
                BarcodeEntry.Focus(); // Still allows scanner input
            }
        }

        private void QuantityEntry_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(e.NewTextValue, out int val))
            {
                if (e.NewTextValue.Length >= 7)
                {
                    QuantityEntry.Text = string.Empty;
                   
                }
            }      
        }
    }
}