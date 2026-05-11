using AmaScan.Classes;
using AmaScan.Data;
using AmaScan.sqliteModels;
using Data.Model;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using static AmaScan.Classes.DatabaseHelper;

namespace AmaScan
{
    [QueryProperty(nameof(PoQuery), "po")]
    public partial class ReceivingPage : ContentPage, INotifyPropertyChanged, IDisposable
    {
        private PoHeader _poHeader;
        private string _poQuery;
        private volatile int _loadGuard;
        private bool _isDisposed;
        private CancellationTokenSource _loadingCts;

        public string PoQuery
        {
            get => _poQuery;
            set
            {
                if (_poQuery == value) return;
                _poQuery = Uri.UnescapeDataString(value);

                if (Interlocked.CompareExchange(ref _loadGuard, 1, 0) == 0)
                    _ = LoadPoAsync(_poQuery);
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

        private int _loadAttempt;
        private bool _isAcceptMode = true;

        public ICommand ResetPoLineCommand { get; }

        public ReceivingPage(DatabaseHelper databaseHelper)
        {
            InitializeComponent();
            BindingContext = this;
            _loadingCts = new CancellationTokenSource();
            
            ResetPoLineCommand = new Command<PoLine>(async line =>
            {
                if (line == null) return;

                bool ok = await Shell.Current.DisplayAlert(
                    "Reset Line",
                    $"Are you sure you want to reset {line.ItemCode}?",
                    "Yes", "No");

                if (!ok) return;

                line.ScanAcceptQty = 0;
                line.ScanRejectQty = 0;
                line.ReceivedString = string.Empty;

                await App.Db.UpdatePoLineAsync(line);
                await LoadPoAsync(PoNumber);
            });

            LoadWarehouses();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            WedgeCatcher.Focus();           // steal focus so wedge lands here
        }

        protected override async void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);

            // Allow re-entry only if value changed *after* navigation (atomic check)
            if (!string.IsNullOrWhiteSpace(_poQuery) &&
                Interlocked.CompareExchange(ref _loadGuard, 1, 0) == 0)
            {
                _ = LoadPoAsync(_poQuery);
            }
        }

        private void OnWedgeCompleted(object sender, EventArgs e)
        {
            var code = WedgeCatcher.Text.Trim();
            if (!string.IsNullOrEmpty(code))
            {
                BarcodeEntry.Text = code;       // copy to the visible box
                OnBarcodeEntered(BarcodeEntry, EventArgs.Empty); // reuse your logic
            }
            WedgeCatcher.Text = string.Empty;   // clear for next scan
            WedgeCatcher.Focus();               // keep focus
        }

#if ANDROID
        private void OnBarcodeFocused(object sender, FocusEventArgs e)
        {
            // Hide soft keyboard but keep the field able to receive wedge input
            var entry = (Entry)sender;
            if (entry.Handler?.PlatformView is Android.Widget.EditText editText)
            {
                var imm = Android.App.Application.Context
                             .GetSystemService(Android.Content.Context.InputMethodService)
                             as Android.Views.InputMethods.InputMethodManager;
                imm?.HideSoftInputFromWindow(editText.WindowToken, 0);
            }
        }
#endif
        private async Task LoadPoAsync(string poNumber)
        {
            if (string.IsNullOrWhiteSpace(poNumber)) return;

            _poHeader = null;
            _poLines.Clear();

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

                var headerTask = App.Db.GetPoHeaderByOrderNoAsync(poNumber);
                var linesTask = App.Db.GetPoLinesByOrderNoAsync(poNumber);

                await Task.WhenAll(headerTask, linesTask).ConfigureAwait(false);

                _poHeader = headerTask.Result;
                var lines = linesTask.Result;

                // Set audit fields on first open
                if (_poHeader != null && string.IsNullOrWhiteSpace(_poHeader.Receiver))
                {
                    _poHeader.Receiver = App.Services.GetRequiredService<UserSession>().CurrentUser?.UserName;
                    _poHeader.ReceiveStartTime = DateTime.Now;
                    _poHeader.DeviceName = AppConfig.DeviceName;
                    _poHeader.TotalLines = lines.Count;
                    await App.Db.UpdatePoHeaderAsync(_poHeader);
                }

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
                    loadingIndicator.IsVisible = false;
                    loadingIndicator.IsRunning = false;

                    UpdateModeUI();
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
                Interlocked.Exchange(ref _loadGuard, 0); // release the lock
            }                                 
         }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private async void OnBarcodeEntered(object sender, EventArgs e)
        {
            var code = BarcodeEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(code)) return;

            var stockItem = await App.Db.ResolveStockItemByBarcodeAsync(code);
            if (stockItem != null)
            {
                DescriptionLabel.Text = stockItem.stock_description;
                DescriptionLabel.TextColor = Colors.Black;
                DescriptionLabel.FontAttributes = FontAttributes.Bold;

                var packSize = stockItem.pack.GetValueOrDefault(1);
                if (packSize > 1)
                {
                    PackInfoLabel.IsVisible = true;
                    PackInfoLabel.Text = $"Pack size: {packSize} � enter number of packs (total = qty � {packSize})";
                }
                else
                {
                    PackInfoLabel.IsVisible = false;
                }

                QuantityEntry?.Focus();
            }
            else
            {
                DescriptionLabel.Text = "Barcode not found � check and try again";
                DescriptionLabel.TextColor = Colors.OrangeRed;
                DescriptionLabel.FontAttributes = FontAttributes.Italic;
                PackInfoLabel.IsVisible = false;
            }
        }

        private void OnBarcodeTextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                DescriptionLabel.Text = "Scan a barcode to begin";
                DescriptionLabel.TextColor = Colors.Gray;
                DescriptionLabel.FontAttributes = FontAttributes.Italic;
                PackInfoLabel.IsVisible = false;
            }
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

                if (_isAcceptMode)
                    matchingLine.ScanAcceptQty += thisTotQty;
                else
                    matchingLine.ScanRejectQty += thisTotQty;

                var modeLabel = _isAcceptMode ? "A" : "R";
                matchingLine.ReceivedString = string.IsNullOrWhiteSpace(matchingLine.ReceivedString)
                    ? $"{modeLabel}:{thisTotQty}"
                    : $"{matchingLine.ReceivedString} | {modeLabel}:{thisTotQty}";

                await App.Db.UpdatePoLineAsync(matchingLine);

                // Move the scanned line to the top of the list (in-memory only)
                var idx = PoLines.IndexOf(matchingLine);
                if (idx > 0)
                {
                    PoLines.Move(idx, 0);
                }

                ClearInputs();

                // Auto-prompt if all lines are fully received
                if (PoLines.All(l => l.OutstandingQty == 0))
                {
                    bool complete = await DisplayAlert("All Lines Received", "All lines are fully received. Complete GRV now?", "Yes", "No");
                    if (complete)
                        await CompleteGrvAsync();
                }
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
                QuantityEntry.Text = string.Empty;

            DescriptionLabel.Text = "Scan a barcode to begin";
            DescriptionLabel.TextColor = Colors.Gray;
            DescriptionLabel.FontAttributes = FontAttributes.Italic;
            PackInfoLabel.IsVisible = false;
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
                }
                await App.Db.UpdateAllAsync(PoLines.ToList());
                OnPropertyChanged(nameof(PoLines));
            }
        }

        private async void OnCompleteClicked(object sender, EventArgs e)
        {
            await CompleteGrvAsync();
        }

        private async Task CompleteGrvAsync()
        {
            try
            {
                if (_poHeader == null)
                {
                    await DisplayAlert("Error", "No PO loaded. Please restart the process.", "OK");
                    await Shell.Current.GoToAsync(nameof(ReceivingMain));
                    return;
                }
                var poLines = PoLines.ToList();

                bool hasDiscrepancies = poLines.Any(line =>
                    line.OrderedQty != (line.ScanAcceptQty + line.ScanRejectQty));

                if (hasDiscrepancies)
                {
                    bool confirm = await DisplayAlert("Discrepancies Found", "Quantities do not match the PO. Supervisor authorisation is required to proceed.", "Authorise", "Cancel");
                    if (!confirm)
                        return;

                    bool authorised = await PromptSupervisorauthorisationAsync();
                    if (!authorised)
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
                    await Shell.Current.GoToAsync(nameof(ReceivingMain));
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
            string username = App.Services.GetRequiredService<UserSession>().CurrentUser?.UserName ?? "";
            string password = await DisplayPromptAsync("Supervisor Authorisation", $"Enter password for {username}:", "OK", "Cancel", "Password", -1, Keyboard.Text);

            if (string.IsNullOrWhiteSpace(password))
                return false;

            try
            {
                var client = AppConfig.GetHttpClient();
                var payload = new { Username = username, Password = password };
                string json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("GetUser/GetUserAsync", content);
                if (!response.IsSuccessStatusCode)
                {
                    await DisplayAlert("Unauthorised", "Invalid password.", "OK");
                    return false;
                }

                string responseContent = await response.Content.ReadAsStringAsync();
                var user = JsonConvert.DeserializeObject<User>(responseContent);

                if (user == null || !user.CanAuthReceiving)
                {
                    await DisplayAlert("Unauthorised", "This user does not have authority to authorise receiving discrepancies.", "OK");
                    return false;
                }

                // Record the authorizer on the PO header
                if (_poHeader != null)
                {
                    _poHeader.Authorised = user.UserName;
                    await App.Db.UpdatePoHeaderAsync(_poHeader);
                }

                return true;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Authorisation failed: {ex.Message}", "OK");
                return false;
            }
        }

        private async Task<bool> SendToApiForGrvAsync(string poNumber, List<PoLine> poLines)
        {
            try
            {
                // Map PoLines to GrvLines — warehouse is always the device's default receiving warehouse
                string defaultWh = (Preferences.Get("DefaultReceivingWarehouseCode", "") ?? "").PadLeft(3, '0');
                var grvLines = poLines
                    .Where(l => l.ScanAcceptQty > 0 || l.ScanRejectQty > 0)
                    .Select(l => new GrvLine
                    {
                        LineNo = l.LineNo,
                        ItemCode = l.ItemCode,
                        ScanAcceptQty = l.ScanAcceptQty,
                        ScanRejectQty = l.ScanRejectQty,
                        CostPrice = l.CostPrice,
                        CostPricePer = l.CostPricePer,
                        VatCode = l.VatCode,
                        VatRate = l.VatRate,
                        WarehouseId = defaultWh
                    })
                    .ToList();

                if (!grvLines.Any())
                {
                    await DisplayAlert("Error", "No scanned quantities to submit.", "OK");
                    return false;
                }

                // Determine reject warehouse
                string rejectWarehouseCode = "";
                if (RejStorePicker.IsVisible && SelectedWarehouse != null)
                {
                    rejectWarehouseCode = SelectedWarehouse.Code;
                }
                else if (poLines.Any(l => l.ScanRejectQty > 0))
                {
                    rejectWarehouseCode = Preferences.Get("RejectWarehouse1Code", "");
                }

                // Build receiving audit header
                int scannedLines = poLines.Count(l => l.ScanAcceptQty > 0 || l.ScanRejectQty > 0);
                int discrepancyLines = poLines.Count(l => l.OrderedQty != (l.ScanAcceptQty + l.ScanRejectQty));
                var header = new GrvHeader
                {
                    Receiver = _poHeader?.Receiver,
                    ReceiveStartTime = _poHeader?.ReceiveStartTime,
                    ReceiveEndTime = DateTime.Now,
                    Authorised = _poHeader?.Authorised,
                    DeviceName = _poHeader?.DeviceName ?? AppConfig.DeviceName,
                    TotalLines = poLines.Count,
                    ScannedLines = scannedLines,
                    DiscrepancyLines = discrepancyLines
                };

                IGrvService grvService = new OmniGrvService();
                var result = await grvService.SendAsync(
                    poNumber,
                    _poHeader?.AcctCode ?? "",
                    _poHeader?.BranchCode ?? "HO",
                    rejectWarehouseCode,
                    grvLines,
                    header);

                if (result.Success)
                {
                    // Update local PO header with completion audit
                    if (_poHeader != null)
                    {
                        _poHeader.ReceiveEndTime = header.ReceiveEndTime;
                        _poHeader.GrvNumber = result.ReferenceNumber;
                        _poHeader.ScannedLines = scannedLines;
                        _poHeader.DiscrepancyLines = discrepancyLines;
                        _poHeader.iscompleted = true;
                        await App.Db.UpdatePoHeaderAsync(_poHeader);
                    }

                    if (result.DocumentType == GrvDocumentType.DeliveryNote)
                        await DisplayAlert("Success", $"Delivery Note {result.ReferenceNumber} created.", "OK");
                    else
                        await DisplayAlert("Success", $"Supplier Invoice {result.ReferenceNumber} created.", "OK");
                    return true;
                }
                else
                {
                    await DisplayAlert("Error", $"Failed to generate GRV: {result.ErrorMessage}", "OK");
                    return false;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
                return false;
            }
        }

        //public ICommand ResetPoLineCommand => new Command<PoLine>(async (line) =>
        //{
        //    if (line == null) return;

        //    bool confirm = await Shell.Current.DisplayAlert("Reset Line", "Are you sure you want to reset this line?", "Yes", "No");
        //    if (!confirm) return;

        //    line.ScanAcceptQty = 0;
        //    line.ScanRejectQty = 0;
        //    line.ReceivedString = string.Empty;
        //    await App.Db.UpdatePoLineAsync(line);

        //    await LoadPoAsync(PoNumber);
        //});

        private void OnAcceptRejectToggled(object sender, ToggledEventArgs e)
        {
            _isAcceptMode = !e.Value; // false = Accept, true = Reject
            UpdateModeUI();
        }

        private void UpdateModeUI()
        {
            if (_isAcceptMode)
            {
                ToggleModeLabel.Text = "Accept";
                ToggleModeLabel.TextColor = Colors.Green;
                AcceptRejectSwitch.IsToggled = false;
                SaveButton.BackgroundColor = Colors.Green;
                SaveButton.Text = "Save as Accepted";
                RejStorePicker.IsVisible = false;
                lblRejStore.IsVisible = false;
                SetDefaultReceivingWarehouse();
            }
            else
            {
                ToggleModeLabel.Text = "Reject";
                ToggleModeLabel.TextColor = Colors.Red;
                AcceptRejectSwitch.IsToggled = true;
                SaveButton.BackgroundColor = Colors.Red;
                SaveButton.Text = "Save as Rejected";
                LoadRejectWarehouses();
                RejStorePicker.IsVisible = true;
                lblRejStore.IsVisible = true;
            }
        }

        private void OnQuantityCompleted(object sender, EventArgs e)
        {
            OnSaveProgressClicked(sender, e);
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
            if (string.IsNullOrEmpty(e.NewTextValue)) return;
            if (!int.TryParse(e.NewTextValue, out _))
                ((Entry)sender).Text = e.OldTextValue;
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
                var warehouses = await WarehouseCache.GetAsync();
                WarehouseList = new ObservableCollection<Warehouse>(warehouses);

                // Set default warehouse based on current mode
                UpdateModeUI();
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