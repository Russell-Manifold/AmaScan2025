using AmaScan.Classes;
using AmaScan.sqliteModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Data.Model;

namespace AmaScan
{
    public partial class ReturnsPage : ContentPage, INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient = AppConfig.GetHttpClient();
        private SoHeader _soHeader;
        private ObservableCollection<SoLine> _soLines = new();
        private string _returnsWarehouse = "Loading...";
        private string _selectedReturnReason = "Damaged";
        private sqliteModels.StockItem _resolvedStockItem;

        public string ReturnsWarehouse
        {
            get => _returnsWarehouse;
            set
            {
                if (_returnsWarehouse != value)
                {
                    _returnsWarehouse = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedReturnReason
        {
            get => _selectedReturnReason;
            set
            {
                if (_selectedReturnReason != value)
                {
                    _selectedReturnReason = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<string> ReturnReasons { get; } = new List<string>
        {
            "Damaged",
            "Expired",
            "Wrong Item",
            "Quality Issue",
            "Customer Return",
            "Overstock",
            "Other"
        };

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<SoLine> SoLines => _soLines;

        public ReturnsPage()
        {
            InitializeComponent();
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            ClearUI();
            try
            {
                await LoadReturnsWarehouseAsync();
            }
            catch (Exception ex)
            {
                ReturnsWarehouse = "Error loading warehouse";
                System.Diagnostics.Debug.WriteLine($"Failed to load warehouse: {ex.Message}");
            }
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BarcodeEntry?.Focus(); // Auto-focus when page loads
            });
        }

        private void ClearUI()
        {
            _soLines.Clear();
            _soHeader = null;
            _resolvedStockItem = null;

            soEntry.Text = string.Empty;
            BarcodeEntry.Text = string.Empty;
            QuantityEntry.Text = string.Empty;
            Comments.Text = string.Empty;
            ddReason.SelectedIndex = -1;
            DescriptionLabel.Text = "Scan a barcode to see item description";
            DescriptionLabel.TextColor = Colors.Gray;
            DescriptionLabel.FontAttributes = FontAttributes.Italic;
            ManualInputSwitch.IsToggled = false;
#if ANDROID
            try
            {
                if (BarcodeEntry.Handler?.PlatformView is Android.Widget.EditText et)
                    et.ShowSoftInputOnFocus = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearUI Android error: {ex.Message}");
            }
#endif

            OnPropertyChanged(nameof(SoLines));
        }



        private async void OnSaveProgressClicked(object sender, EventArgs e)
        {
            string soNumber = soEntry.Text?.Trim();
            string barcode = BarcodeEntry.Text?.Trim();
            string quantityText = QuantityEntry.Text?.Trim();

            if (string.IsNullOrWhiteSpace(barcode))
            {
                await DisplayAlert("Error", "Please enter a barcode.", "OK");
                BarcodeEntry.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(quantityText))
            {
                await DisplayAlert("Error", "Please enter a return quantity.", "OK");
                QuantityEntry.Focus();
                return;
            }

            if (!decimal.TryParse(quantityText, out decimal quantity) || quantity <= 0)
            {
                await DisplayAlert("Error", "Please enter a valid quantity.", "OK");
                QuantityEntry.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedReturnReason))
            {
                await DisplayAlert("Error", "Please select a return reason.", "OK");
                return;
            }

            LoadingOverlay.IsVisible = true;
            loadingIndicator.IsRunning = true;

            try
            {
                string itemCode = "";
                string itemDesc = "";

                // If SO number is provided, try to get item details from SO
                if (!string.IsNullOrWhiteSpace(soNumber))
                {
                    await LoadSoDataAsync(soNumber);

                    if (_soHeader != null)
                    {
                        var soLine = await GetItemByBarcode(barcode);
                        if (soLine != null)
                        {
                            itemCode = soLine.ItemCode;
                            itemDesc = soLine.ItemDesc;
                        }
                    }
                }

                // Use cached item from barcode scan, or fall back to a fresh lookup
                if (string.IsNullOrEmpty(itemCode))
                {
                    var stockItem = _resolvedStockItem ?? await App.Db.ResolveStockItemByBarcodeAsync(barcode);
                    if (stockItem != null)
                    {
                        itemCode = stockItem.stock_code;
                        itemDesc = stockItem.stock_description;
                    }
                }

                if (string.IsNullOrEmpty(itemCode))
                {
                    itemCode = barcode;
                    itemDesc = "Unknown Item";
                }

                // Show confirmation dialog
                bool confirmed = await DisplayAlert("Confirm Return",
                    $"Process return for {itemDesc}?\n\nQuantity: {quantity}\nReason: {SelectedReturnReason}",
                    "Yes", "No");

                if (!confirmed) return;

                // Create and save return line locally
                var returnLine = new sqliteModels.ReturnLine
                {
                    OrderNumber = soNumber ?? "",
                    ItemBarcode = barcode,
                    ItemCode = itemCode,
                    ItemDesc = itemDesc,
                    ReturnQty = quantity,
                    ReturnReason = SelectedReturnReason
                };

                var userSession = App.Services.GetRequiredService<UserSession>();
                string userName = userSession.CurrentUser?.UserName;
                await App.Db.SaveReturnLineAsync(returnLine, userName);

                // Submit to API
                string returnsWarehouseCode = Preferences.Get("ReturnsWarehouseCode", "");

                var payload = new
                {
                    warehouseCode = returnsWarehouseCode,
                    itemCode = itemCode,
                    quantity = quantity,
                    reference = soNumber ?? "",
                    narrative = SelectedReturnReason,
                    comments = Comments.Text?.Trim() ?? ""
                };

                string json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("StockJournalEntry", content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    string referenceNumber = "";
                    try
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        if (doc.RootElement.TryGetProperty("referenceNumber", out var refProp))
                            referenceNumber = refProp.GetString();
                    }
                    catch
                    {
                        referenceNumber = responseBody.Trim();
                    }

                    await DisplayAlert("Success", $"Return processed: {referenceNumber}", "OK");
                }
                else
                {
                    await DisplayAlert("Error", $"Failed to submit return: {responseBody}", "OK");
                    return;
                }

                // Navigate back to dashboard
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to save return: {ex.Message}", "OK");
            }
            finally
            {
                LoadingOverlay.IsVisible = false;
                loadingIndicator.IsRunning = false;
            }
        }

        private async void OnRestartClicked(object sender, EventArgs e)
        {
            bool answer = await DisplayAlert("Restart", "Are you sure you want to restart the returns process?", "Yes", "No");
            if (answer)
            {
                ClearUI();
            }
        }

        private async Task LoadReturnsWarehouseAsync()
        {
            var warehouseCode = Preferences.Get("ReturnsWarehouseCode", "");
            if (string.IsNullOrEmpty(warehouseCode))
            {
                ReturnsWarehouse = "No warehouse set";
                return;
            }

            // Get the warehouse description directly from the database
            var description = await App.Db.GetWarehouseDescriptionAsync(warehouseCode);
            ReturnsWarehouse = description ?? $"Warehouse: {warehouseCode}";
        }

        private async Task LoadSoDataAsync(string soNumber)
        {
            try
            {
                // First, try to load SO header from local database
                _soHeader = await App.Db.GetSoHeaderByOrderNoAsync(soNumber);

                if (_soHeader == null)
                {
                    // SO not found in local database, try to fetch from API
                    try
                    {
                        string url = $"{AppConfig.ApiBaseUrl}GetSalesOrder/{Uri.EscapeDataString($"IO{soNumber}")}";
                        var salesOrderResponse = await _httpClient.GetFromJsonAsync<SalesOrderResponse>(url);

                        if (salesOrderResponse == null || salesOrderResponse.Lines == null || !salesOrderResponse.Lines.Any())
                        {
                            await DisplayAlert("Not Found", $"SO {soNumber} not found on server.", "OK");
                            return;
                        }

                        // Save the SO data to local database
                        await App.Db.MergeSoDataAsync(salesOrderResponse);

                        // Now load from local database
                        _soHeader = await App.Db.GetSoHeaderByOrderNoAsync(soNumber);

                        if (_soHeader == null)
                        {
                            await DisplayAlert("Error", $"Failed to save SO {soNumber} to local database.", "OK");
                            return;
                        }
                    }
                    catch (Exception apiEx)
                    {
                        await DisplayAlert("Error", $"SO {soNumber} not found in local database and could not be fetched from server: {apiEx.Message}", "OK");
                        return;
                    }
                }

                // Load SO lines from database
                var lines = await App.Db.GetSoLinesByOrderNoAsync(soNumber);
                _soLines.Clear();

                foreach (var line in lines)
                {
                    _soLines.Add(line);
                }

                OnPropertyChanged(nameof(SoLines));
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load SO data: {ex.Message}", "OK");
            }
        }

        private async Task<SoLine> GetItemByBarcode(string barcode)
        {
            // Find the item in the loaded SO lines
            return _soLines.FirstOrDefault(line =>
                line.ItemBarcode == barcode ||
                line.PackBarcode == barcode);
        }

        private void QuantityEntry_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.NewTextValue)) return;

            if (!int.TryParse(e.NewTextValue, out int _))
            {
                ((Entry)sender).Text = e.OldTextValue;
            }
        }

        private void OnManualInputToggled(object sender, ToggledEventArgs e)
        {
#if ANDROID
            try
            {
                if (BarcodeEntry.Handler?.PlatformView is Android.Widget.EditText editText)
                {
                    editText.ShowSoftInputOnFocus = e.Value;
                    if (!e.Value)
                    {
                        var activity = Platform.CurrentActivity;
                        if (activity != null)
                        {
                            var imm = (Android.Views.InputMethods.InputMethodManager)
                                activity.GetSystemService(Android.Content.Context.InputMethodService);
                            imm?.HideSoftInputFromWindow(editText.WindowToken, 0);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ManualInputToggle error: {ex.Message}");
            }
#endif
            BarcodeEntry.Focus();
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private void OnBarcodeFocused(object sender, FocusEventArgs e)
        {
            if (sender is VisualElement ve)
            {
                ve.BackgroundColor = Color.FromArgb("#E6F7FF"); // Light blue highlight
            }
        }

        private void OnBarcodeUnfocused(object sender, FocusEventArgs e)
        {
            if (sender is VisualElement ve)
            {
                ve.BackgroundColor = Colors.Transparent;
            }
        }
        private async void OnBarcodeCompleted(object sender, TextChangedEventArgs e)
        {
            var code = e.NewTextValue?.Trim();
            _resolvedStockItem = null;
            if (string.IsNullOrWhiteSpace(code) || code.Length < 10)
            {
                DescriptionLabel.Text = "Scan a barcode to see item description";
                DescriptionLabel.TextColor = Colors.Gray;
                DescriptionLabel.FontAttributes = FontAttributes.Italic;
                return;
            }

            try
            {
                _resolvedStockItem = await App.Db.ResolveStockItemByBarcodeAsync(code);
                if (_resolvedStockItem == null)
                {
                    DescriptionLabel.Text = "Item not found in local database";
                    DescriptionLabel.TextColor = Colors.OrangeRed;
                    DescriptionLabel.FontAttributes = FontAttributes.Italic;
                    return;
                }

                DescriptionLabel.Text = _resolvedStockItem.stock_description;
                DescriptionLabel.TextColor = Colors.Black;
                DescriptionLabel.FontAttributes = FontAttributes.Bold;
            }
            catch (Exception ex)
            {
                DescriptionLabel.Text = "Error looking up item";
                DescriptionLabel.TextColor = Colors.OrangeRed;
                DescriptionLabel.FontAttributes = FontAttributes.Italic;
                System.Diagnostics.Debug.WriteLine($"Barcode lookup error: {ex.Message}");
            }
        }

        private void OnBarcodeEntryCompleted(object sender, EventArgs e)
        {
            QuantityEntry.Focus();
        }
    }
}