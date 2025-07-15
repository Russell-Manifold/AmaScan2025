using AmaScan.Classes;
using AmaScan.sqliteModels;
using SQLite;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Storage;
using System.Net.Http.Json;
using Data.Model;

namespace AmaScan
{
    public partial class ReturnsPage : ContentPage, INotifyPropertyChanged
    {
        private readonly DatabaseHelper _databaseHelper = new(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));
        private readonly HttpClient _httpClient = new();
        private SoHeader _soHeader;
        private ObservableCollection<SoLine> _soLines = new();
        private string _returnsWarehouse = "Loading...";
        private string _selectedReturnReason = "Damaged";
        
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
            await LoadReturnsWarehouseAsync();
        }

        private void ClearUI()
        {
            _soLines.Clear();
            _soHeader = null;

            // Clear UI elements
            soEntry.Text = string.Empty;

            BarcodeEntry.Text = string.Empty;
            QuantityEntry.Text = string.Empty;

            // Reset manual input toggle
            ManualInputSwitch.IsToggled = false;
            BarcodeEntry.IsReadOnly = true;

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

            try
            {
                LoadingOverlay.IsVisible = true;
                loadingIndicator.IsRunning = true;

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

                // If we don't have item details from SO, try to get from stock items
                if (string.IsNullOrEmpty(itemCode))
                {
                    var stockItem = await _databaseHelper.ResolveStockItemByBarcodeAsync(barcode);
                    if (stockItem != null)
                    {
                        itemCode = stockItem.stock_code;
                        itemDesc = stockItem.stock_description;
                    }
                }

                // If still no item details, use barcode as item code and empty description
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

                // Create and save return line
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
                await _databaseHelper.SaveReturnLineAsync(returnLine, userSession.CurrentUser?.UserName);

                await DisplayAlert("Success", $"Return processed for {itemDesc}", "OK");

                // Navigate back to dashboard
                var dashboardPage = App.Services.GetRequiredService<Dashboard>();
                await Navigation.PushAsync(dashboardPage);
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

        private async void OnSave_Click(object sender, EventArgs e)
        {
            await DisplayAlert("Info", "Returns are saved immediately when processed.", "OK");
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
            var description = await _databaseHelper.GetWarehouseDescriptionAsync(warehouseCode);
            ReturnsWarehouse = description ?? $"Warehouse: {warehouseCode}";
        }

        private async Task LoadSoDataAsync(string soNumber)
        {
            try
            {
                // First, try to load SO header from local database
                _soHeader = await _databaseHelper.GetSoHeaderByOrderNoAsync(soNumber);

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
                        await _databaseHelper.MergeSoDataAsync(salesOrderResponse);

                        // Now load from local database
                        _soHeader = await _databaseHelper.GetSoHeaderByOrderNoAsync(soNumber);

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
                var lines = await _databaseHelper.GetSoLinesByOrderNoAsync(soNumber);
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
            BarcodeEntry.IsReadOnly = !e.Value;
            BarcodeEntry.Focus();
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


}