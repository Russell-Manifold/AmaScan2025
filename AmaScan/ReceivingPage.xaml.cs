using AmaScan.Classes;
using AmaScan.sqliteModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AmaScan
{
    [QueryProperty(nameof(PoQuery), "po")]
    public partial class ReceivingPage : ContentPage, INotifyPropertyChanged, IDisposable
    {
        private PoHeader _poHeader;
        private string _poQuery;
        private bool _isDisposed;
        private CancellationTokenSource _loadingCts;

        public string PoQuery
        {
            get => _poQuery;
            set
            {
                if (_poQuery != value)
                {
                    _poQuery = Uri.UnescapeDataString(value);
                    _ = LoadPoAsync(_poQuery);
                }
            }
        }

        public ICommand OnPoLineLongPressed { get; }
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly ObservableCollection<PoLine> _poLines = new();
        public ObservableCollection<PoLine> PoLines => _poLines;

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

        public ReceivingPage(DatabaseHelper databaseHelper)
        {
            InitializeComponent();
            BindingContext = this;
            _loadingCts = new CancellationTokenSource();
        }

        private async Task LoadPoAsync(string poNumber)
        {
            if (string.IsNullOrWhiteSpace(poNumber)) return;

            try
            {
                _loadingCts?.Cancel();
                _loadingCts = new CancellationTokenSource();
                var ct = _loadingCts.Token;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    loadingIndicator.IsVisible = true;
                    loadingIndicator.IsRunning = true;
                });

                _poHeader = await App.Db.GetPoHeaderByOrderNoAsync(poNumber);
                if (_poHeader == null)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await DisplayAlert("Error", $"PO not found: {poNumber}", "OK");
                        await Shell.Current.GoToAsync("..");
                    });
                    return;
                }

                ct.ThrowIfCancellationRequested();

                var lines = await App.Db.GetPoLinesByOrderNoAsync(_poHeader.OrderNo);
                ct.ThrowIfCancellationRequested();

                // Update UI on main thread
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    OnPropertyChanged(nameof(PoNumber));
                    OnPropertyChanged(nameof(DueDateFormatted));
                    OnPropertyChanged(nameof(Status));
                    
                    _poLines.Clear();
                    foreach (var line in lines)
                    {
                        _poLines.Add(line);
                    }
                    
                    AcceptSwitch_Toggled(AcceptSwitch, new ToggledEventArgs(AcceptSwitch.IsToggled));
                });
            }
            catch (OperationCanceledException)
            {
                // Operation was cancelled, do nothing
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Error", $"Failed to load PO: {ex.Message}", "OK");
                });
            }
            finally
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    loadingIndicator.IsVisible = false;
                    loadingIndicator.IsRunning = false;
                });
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void OnStartReceivingClicked(object sender, EventArgs e)
        {
            if (ReceivingInputSection == null || StartReceivingButton == null) return;

            ReceivingInputSection.IsVisible = true;
            StartReceivingButton.IsVisible = false;
            if (BarcodeEntry != null)
            {
                BarcodeEntry.Text = string.Empty;
                BarcodeEntry.Focus();
            }
            if (QuantityEntry != null)
            {
                QuantityEntry.Text = string.Empty;
            }
        }

        private void OnBarcodeEntered(object sender, EventArgs e)
        {
            QuantityEntry?.Focus();
        }

        private async void OnSaveProgressClicked(object sender, EventArgs e)
        {
            if (BarcodeEntry == null || QuantityEntry == null) return;

            var scannedBarcode = BarcodeEntry.Text?.Trim();
            if (string.IsNullOrEmpty(scannedBarcode))
            {
                await DisplayAlert("Error", "Please enter a barcode.", "OK");
                return;
            }

            try
            {
                var stockItem = await App.Db.ResolveStockItemByBarcodeAsync(scannedBarcode);
                if (stockItem == null || string.IsNullOrEmpty(stockItem.bar_code))
                {
                    ClearInputs();
                    await DisplayAlert("Error", $"Barcode not found: {scannedBarcode}", "OK");
                    return;
                }

                var matchingLine = PoLines.FirstOrDefault(line =>
                    line.ItemBarcode.Equals(stockItem.bar_code, StringComparison.OrdinalIgnoreCase));

                if (matchingLine == null)
                {
                    ClearInputs();
                    await DisplayAlert("Error", $"Item not in PO: {stockItem.bar_code}", "OK");
                    return;
                }

                if (!decimal.TryParse(QuantityEntry.Text, out decimal thisQty) || thisQty <= 0)
                {
                    await DisplayAlert("Error", "Invalid quantity.", "OK");
                    QuantityEntry.Text = "0";
                    return;
                }

                decimal thisTotQty = thisQty * stockItem.pack.GetValueOrDefault(1);

                if (AcceptSwitch.IsToggled)
                    matchingLine.ScanAcceptQty += thisTotQty;
                else
                    matchingLine.ScanRejectQty += thisTotQty;

                matchingLine.ReceivedString = string.IsNullOrWhiteSpace(matchingLine.ReceivedString)
                    ? thisTotQty.ToString()
                    : $"{matchingLine.ReceivedString} + {thisTotQty}";

                await App.Db.UpdatePoLineAsync(matchingLine);

                // Move the scanned line to the top of the list (in-memory only)
                var idx = PoLines.IndexOf(matchingLine);
                if (idx > 0)
                {
                    PoLines.Move(idx, 0);
                }

                ClearInputs();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Save failed: {ex.Message}", "OK");
            }
        }

        private void ClearInputs()
        {
            if (BarcodeEntry != null)
            {
                BarcodeEntry.Text = string.Empty;
                BarcodeEntry.Focus();
            }
            if (QuantityEntry != null)
            {
                QuantityEntry.Text = string.Empty;
            }
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
                    line.ReceivedString = string.Empty;
                    await App.Db.UpdatePoLineAsync(line);
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
                var poLines = await App.Db.GetPoLinesByOrderNoAsync(PoNumber);

                bool hasDiscrepancies = poLines.Any(line =>
                    line.OrderedQty != (line.ScanAcceptQty + line.ScanRejectQty));

                if (hasDiscrepancies)
                {
                    bool confirm = await DisplayAlert("Confirm Submission", "Proceed to generate GRV?", "Yes", "No");
                    if (!confirm)
                        return;

                    await DisplayAlert("Not authorised", "Discrepancies found. Supervisor authorisation is required.", "OK");
                    bool authorised = await PromptSupervisorauthorisationAsync();

                    bool confirmed = await DisplayAlert("Confirm Submission", "Proceed to generate GRV with discrepancies?", "Yes", "No");
                    if (!confirmed)
                        return;
                }
                else
                {
                    bool confirmed = await DisplayAlert("Confirm Submission", "No discrepancies found. Proceed to generate GRV?", "Yes", "No");
                    if (!confirmed)
                        return;
                }

                bool success = await SendToApiForGrvAsync(PoNumber, poLines);

                if (success)
                {
                    await DisplayAlert("Success", "GRV successfully generated.", "OK");
                    await Shell.Current.GoToAsync("..");
                }
                else
                    await DisplayAlert("Error", "Failed to generate GRV. Please try again.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
            }
        }

        private async Task<bool> PromptSupervisorauthorisationAsync()
        {
            string password = await DisplayPromptAsync("Password", "Enter supervisor password:", "OK", "Cancel", "Password", -1, Keyboard.Text);

            if (string.IsNullOrWhiteSpace(password))
                return false;

            return true;
        }

        private async Task<bool> SendToApiForGrvAsync(string poNumber, List<PoLine> poLines)
        {
            try
            {
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
            await App.Db.UpdatePoLineAsync(line);

            await LoadPoAsync(PoNumber);
        });

        private void AcceptSwitch_Toggled(object sender, ToggledEventArgs e)
        {
            if (e.Value)
            {
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
                BarcodeEntry.IsReadOnly = false;
                BarcodeEntry.Focus();
            }
            else
            {
                BarcodeEntry.IsReadOnly = true;
                BarcodeEntry.Focus();
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
                var warehouses = await App.Db.GetWarehousesAsync();
                WarehouseList = new ObservableCollection<Warehouse>(warehouses ?? new List<Warehouse>());

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
        public void Dispose()
        {
            if (_isDisposed) return;

            try
            {
                _loadingCts?.Cancel();
                _loadingCts?.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }

            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}