using AmaScan.Classes;
using AmaScan.sqliteModels;
using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace AmaScan
{
    [QueryProperty(nameof(BatchNoQuery), "batchNo")]
    public partial class StockCountSearchPage : ContentPage, INotifyPropertyChanged
    {
        private readonly DatabaseHelper _databaseHelper;
        private string _batchNoQuery;
        private StockCountItem _foundItem;
        private StockItem _stockItem;

        public string BatchNoQuery
        {
            get => _batchNoQuery;
            set
            {
                if (_batchNoQuery != value)
                {
                    _batchNoQuery = Uri.UnescapeDataString(value);
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Color StatusColor
        {
            get
            {
                if (_foundItem == null) return Colors.LightBlue;

                // Optimized logic to reduce property access
                if (_foundItem.CountComplete)
                {
                    return _foundItem.ConfirmCountQty != _foundItem.Level ? Colors.Orange : Colors.LightGreen;
                }
                
                return Colors.LightBlue;
            }
        }

        public StockCountSearchPage()
        {
            InitializeComponent();
            BindingContext = this;
            _databaseHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            InitializeUI();
            ClearSearchResults();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            // Clear references to help garbage collection
            _foundItem = null;
            _stockItem = null;
            ClearSearchResults();
        }

        private void InitializeUI()
        {
            ManualInputSwitch.IsToggled = false;
            BarcodeEntry.IsReadOnly = true;
            SearchResultFrame.IsVisible = false;
            StartCountingButton.IsVisible = false;
            BarcodeEntry.Focus();
        }

        private void ClearSearchResults()
        {
            SearchResultFrame.IsVisible = false;
            StartCountingButton.IsVisible = false;
            BarcodeEntry.Text = string.Empty;
            _foundItem = null;
            OnPropertyChanged(nameof(StatusColor));
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void OnManualInputToggled(object sender, ToggledEventArgs e)
        {
            BarcodeEntry.IsReadOnly = !e.Value;
            BarcodeEntry.Focus();
        }

        private async void OnBarcodeEntered(object sender, EventArgs e)
        {
            string scannedBarcode = BarcodeEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(scannedBarcode))
            {
                await DisplayAlert("Error", "Please scan a barcode.", "OK");
                return;
            }

            await SearchForItem(scannedBarcode);
        }

        private async void OnSearchClicked(object sender, EventArgs e)
        {
            string scannedBarcode = BarcodeEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(scannedBarcode))
            {
                await DisplayAlert("Error", "Please enter a barcode to search.", "OK");
                return;
            }

            await SearchForItem(scannedBarcode);
        }

        private async Task SearchForItem(string barcode)
        {
            try
            {
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;
                SearchResultFrame.IsVisible = false;
                StartCountingButton.IsVisible = false;

                // Check StockItem table for barcodes (main, pack, and alternate)
                _stockItem = await _databaseHelper.ResolveStockItemByBarcodeAsync(barcode);
                
                if (_stockItem == null)
                {
                    await DisplayAlert("Error", $"Barcode not found: {barcode}", "OK");
                    BarcodeEntry.Text = string.Empty;
                    BarcodeEntry.Focus();
                    return;
                }

                // Check if this item is in the current stock count batch
                _foundItem = await _databaseHelper.GetStockCountItemByCodeAndBatchAsync(_stockItem.stock_code, _batchNoQuery);
                
                if (_foundItem == null)
                {
                    await DisplayAlert("Error", $"Item {_stockItem.stock_description} is not in the current stock count batch.", "OK");
                    BarcodeEntry.Text = string.Empty;
                    BarcodeEntry.Focus();
                    return;
                }

                // Display the found item
                DisplayFoundItem();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Search failed: {ex.Message}", "OK");
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        }

        private void DisplayFoundItem()
        {
            ResultStockDescription.Text = _foundItem.StockDescription;
            ResultStockCode.Text = $"Code: {_foundItem.StockCode}";
            
            string status = _foundItem.CountComplete ? "Complete" : "Incomplete";
            ResultStatus.Text = $"Status: {status}";
            
            SearchResultFrame.IsVisible = true;
            StartCountingButton.IsVisible = true;
            OnPropertyChanged(nameof(StatusColor));
        }

        private async void OnStartCountingClicked(object sender, EventArgs e)
        {
            if (_foundItem == null)
            {
                await DisplayAlert("Error", "No item selected to count.", "OK");
                return;
            }

            // Navigate to the stock count page with the found item
            string stockCode = Uri.EscapeDataString(_foundItem.StockCode);
            await Shell.Current.GoToAsync($"{nameof(StockCountPage)}?stockCode={stockCode}");
        }
    }
} 