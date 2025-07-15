using AmaScan.Classes;
using AmaScan.sqliteModels;
using SQLite;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AmaScan
{
    [QueryProperty(nameof(PoQuery), "po")]
    public partial class ReceivingPage : ContentPage, INotifyPropertyChanged
    {
        private readonly DatabaseHelper _databaseHelper = new(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));
        private PoHeader _poHeader;
        private string _poQuery;

        public string PoQuery
        {
            get => _poQuery;
            set
            {
                _poQuery = Uri.UnescapeDataString(value);
                _ = LoadPoAsync(_poQuery);
            }
        }

        public ICommand OnPoLineLongPressed { get; }
        public event PropertyChangedEventHandler PropertyChanged;

        private ObservableCollection<Warehouse> _warehouseList = new();
        public ObservableCollection<Warehouse> WarehouseList
        {
            get => _warehouseList;
            set
            {
                if (_warehouseList != value)
                {
                    _warehouseList = value;
                    OnPropertyChanged();
                }
            }
        }

        private Warehouse _selectedWarehouse;
        public Warehouse SelectedWarehouse
        {
            get => _selectedWarehouse;
            set
            {
                if (_selectedWarehouse != value)
                {
                    _selectedWarehouse = value;
                    OnPropertyChanged();
                }
            }
        }

        public string PoNumber => _poHeader?.OrderNo ?? "";
        public string DueDateFormatted => _poHeader?.DueDate.ToString("yyyy-MM-dd") ?? "";
        public string Status => _poHeader?.Status ?? "";

        public List<Item> ItemList { get; set; }

        public ReceivingPage()
        {
            InitializeComponent();
            BindingContext = this;
        }

        private async Task LoadPoAsync(string poNumber)
        {
            loadingIndicator.IsVisible = true;
            loadingIndicator.IsRunning = true;

            try
            {
                if (string.IsNullOrWhiteSpace(poNumber))
                {
                    await DisplayAlert("Error", "No PO number received.", "OK");
                    await Shell.Current.GoToAsync("..");
                    return;
                }

                _poHeader = await _databaseHelper.GetPoHeaderByOrderNoAsync(poNumber);

                if (_poHeader == null)
                {
                    await DisplayAlert("Error", $"PO not found in local database: {poNumber}", "OK");
                    await Shell.Current.GoToAsync("..");
                    return;
                }

                OnPropertyChanged(nameof(PoNumber));
                OnPropertyChanged(nameof(DueDateFormatted));
                OnPropertyChanged(nameof(Status));

                // Load warehouses first, then set up the switch
                await LoadWarehousesAsync();
                AcceptSwitch_Toggled(AcceptSwitch, new ToggledEventArgs(AcceptSwitch.IsToggled));
                await LoadPoLinesAsync(_poHeader.OrderNo);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load PO data: {ex.Message}", "OK");
            }
            finally
            {
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;
            }
        }

        protected void OnPropertyChanged(string propertyName) =>
         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public ObservableCollection<PoLine> PoLines { get; set; } = new();

        private async Task LoadPoLinesAsync(string poNumber)
        {
            var lines = await _databaseHelper.GetPoLinesByOrderNoAsync(poNumber);

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

            var stockItem = await _databaseHelper.ResolveStockItemByBarcodeAsync(scannedBarcode);
            if (stockItem == null || string.IsNullOrEmpty(stockItem.bar_code))
            {
                BarcodeEntry.Text = string.Empty;
                QuantityEntry.Text = "0";
                BarcodeEntry.Focus();
                await DisplayAlert("Error", $"Scanned barcode '{scannedBarcode}' not found in stock items.", "OK");
                return;
            }

            var matchingLine = PoLines.FirstOrDefault(line =>
                line.ItemBarcode.Equals(stockItem.bar_code, StringComparison.OrdinalIgnoreCase));

            if (matchingLine == null)
            {
                BarcodeEntry.Text = string.Empty;
                QuantityEntry.Text = "0";
                BarcodeEntry.Focus();
                await DisplayAlert("Error", $"Item '{stockItem.bar_code}' not found in this PO.", "OK");
                return;
            }

            var packSize = stockItem.pack.GetValueOrDefault(1);
            if (!decimal.TryParse(QuantityEntry.Text, out decimal thisQty) || thisQty <= 0)
            {
                await DisplayAlert("Error", "Invalid quantity entered.", "OK");
                QuantityEntry.Text = "0";
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

            await _databaseHelper.UpdatePoLineAsync(matchingLine);
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
                    await _databaseHelper.UpdatePoLineAsync(line);
                }
                OnPropertyChanged(nameof(PoLines));
            }
        }

        private async void OnCompleteClicked(object sender, EventArgs e)
        {
            try
            {
                if (_poHeader == null)
                {
                    await DisplayAlert("Error", "No PO loaded. Please restart the process.", "OK");
                    await Shell.Current.GoToAsync("..");
                    return;
                }
                var poLines = await _databaseHelper.GetPoLinesByOrderNoAsync(PoNumber);

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
                {
                    await DisplayAlert("Success", "GRV successfully generated.", "OK");
                    // Clear session state after PO completion
                    await Shell.Current.GoToAsync("..");
                }
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
            await _databaseHelper.UpdatePoLineAsync(line);

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
                RejStorePicker.IsVisible = false;
                lblRejStore.IsVisible = false;

                // Set default receiving warehouse for accepts
                SetDefaultReceivingWarehouse();
            }
            else
            {
                // Reject mode — use red
                SaveButton.BackgroundColor = Colors.Red;
                SaveButton.TextColor = Colors.White;
                SaveButton.Text = "Save as Rejected";
                LoadRejectWarehouses();
                RejStorePicker.IsVisible = true;
                lblRejStore.IsVisible = true;
            }
        }

        private void SetDefaultReceivingWarehouse()
        {
            string defaultReceivingCode = Preferences.Get("DefaultReceivingWarehouseCode", "");
            if (!string.IsNullOrEmpty(defaultReceivingCode))
            {
                var defaultWarehouse = WarehouseList.FirstOrDefault(w => w.Code == defaultReceivingCode);
                if (defaultWarehouse != null)
                {
                    SelectedWarehouse = defaultWarehouse;
                }
            }
        }

        private void LoadRejectWarehouses()
        {
            var rejectWarehouses = new ObservableCollection<Warehouse>();

            // Add reject warehouses from settings
            AddRejectWarehouse(rejectWarehouses, "RejectWarehouse1Code");
            AddRejectWarehouse(rejectWarehouses, "RejectWarehouse2Code");

            // Set the reject warehouse list to only show the 2 configured reject warehouses
            if (rejectWarehouses.Any())
            {
                RejStorePicker.ItemsSource = rejectWarehouses;
                RejStorePicker.SelectedItem = rejectWarehouses.First();
            }
        }

        private void AddRejectWarehouse(ObservableCollection<Warehouse> rejectWarehouses, string preferenceKey)
        {
            string warehouseCode = Preferences.Get(preferenceKey, "");
            if (!string.IsNullOrEmpty(warehouseCode))
            {
                var warehouse = WarehouseList.FirstOrDefault(w => w.Code == warehouseCode);
                if (warehouse != null)
                {
                    rejectWarehouses.Add(warehouse);
                }
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

        public class Item
        {
            public string Name { get; set; }
        }

        private void LoadWarehouses()
        {

            // Load all warehouses from database
            _ = LoadWarehousesAsync();
        }

        private async Task LoadWarehousesAsync()
        {
            try
            {
                var warehouses = await _databaseHelper.GetWarehousesAsync();
                var warehouseList = new ObservableCollection<Warehouse>();

                if (warehouses != null && warehouses.Any())
                {
                    foreach (var warehouse in warehouses)
                    {
                        warehouseList.Add(warehouse);
                    }
                }

                WarehouseList = warehouseList;

                // Set default warehouse based on current mode
                if (AcceptSwitch.IsToggled)
                {
                    SetDefaultReceivingWarehouse();
                }
                else
                {
                    LoadRejectWarehouses();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load warehouses: {ex.Message}", "OK");
            }
        }


    }
}