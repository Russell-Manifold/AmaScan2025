using AmaScan.Classes;
using AmaScan.sqliteModels;
using Newtonsoft.Json.Linq;
using SQLite;
using System.Net.Http.Json;

namespace AmaScan;

public partial class SettingsPage : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly UserSession _userSession;
    private List<Warehouse> _warehouseList = new();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadWarehouses();
    }

    public SettingsPage()
    {
        InitializeComponent();
        ApiUrlEntry.Text = AppConfig.ApiBaseUrl;
    }

    private async void LoadWarehouses()
    {
        var _databaseHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));
        try
        {
            var hasLocalWarehouses = await _databaseHelper.HasWarehousesAsync();

            if (!hasLocalWarehouses)
            {
                // First-time load from API
                string url = $"{AppConfig.ApiBaseUrl}warehouses/get-warehouses";
                var response = await new HttpClient().GetFromJsonAsync<WarehouseResponse>(url);

                if (response?.data != null && response.data.Any())
                {
                    await _databaseHelper.SaveWarehousesAsync(response.data);
                    Preferences.Set("HasPopulatedWarehouses", true); // Optional: track explicitly
                }
                else
                {
                    await DisplayAlert("Info", "No warehouses found in API response.", "OK");
                }
            }

            // Load from local DB
            var warehouses = await _databaseHelper.GetWarehousesAsync();
            _warehouseList = new List<Warehouse>
            {
                new Warehouse { Code = "", Description = "Select Warehouse" }
            };

            if (warehouses != null && warehouses.Any())
            {
                _warehouseList.AddRange(warehouses);
            }
           
            SetupWarehousePicker(DefaultPickingWarehousePicker, "DefaultPickingWarehouseCode");
            SetupWarehousePicker(DefaultReceivingWarehousePicker, "DefaultReceivingWarehouseCode");
            SetupWarehousePicker(WarehousePickerR1, "RejectWarehouse1Code");
            SetupWarehousePicker(WarehousePickerR2, "RejectWarehouse2Code");
            SetupWarehousePicker(ReturnsWarehousePicker, "ReturnsWarehouseCode");

            foreach (var w in warehouses)
            {
                Console.WriteLine($"Warehouse: {w.Code} - {w.Description}");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load warehouses: {ex.Message}", "OK");
        }
    }

    private void SetupWarehousePicker(Picker picker, string preferenceKey)
    {
        picker.ItemsSource = _warehouseList;
        picker.ItemDisplayBinding = new Binding("Description");

        string savedWarehouseCode = Preferences.Get(preferenceKey, "");
        var selectedIndex = _warehouseList.FindIndex(w => w.Code == savedWarehouseCode);
        picker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
    }

    private void OnSaveClicked(object sender, EventArgs e)
    {
        var newUrl = ApiUrlEntry.Text?.Trim();

        if (!string.IsNullOrWhiteSpace(newUrl))
        {
            AppConfig.ApiBaseUrl = newUrl;

            // Save all warehouse selections
            SaveWarehouseSelection(DefaultPickingWarehousePicker, "DefaultPickingWarehouseCode");
            SaveWarehouseSelection(DefaultReceivingWarehousePicker, "DefaultReceivingWarehouseCode");
            SaveWarehouseSelection(WarehousePickerR1, "RejectWarehouse1Code");
            SaveWarehouseSelection(WarehousePickerR2, "RejectWarehouse2Code");
            SaveWarehouseSelection(ReturnsWarehousePicker, "ReturnsWarehouseCode");

            ConfirmationLabel.Text = "API URL and Default Warehouse saved.";
            ConfirmationLabel.IsVisible = true;
        }
        else
        {
            DisplayAlert("Validation", "Please enter a valid URL.", "OK");
        }
    }

    private void SaveWarehouseSelection(Picker picker, string preferenceKey)
    {
        var selectedWarehouse = picker.SelectedItem as Warehouse;
        if (selectedWarehouse != null)
        {
            Preferences.Set(preferenceKey, selectedWarehouse.Code);
        }
    }

    private async void OnUpdateStockClicked(object sender, EventArgs e)
    {
        try
        {
            ConfirmationLabel.Text = "Updating stock...";
            ConfirmationLabel.IsVisible = true;

            string url = $"{AppConfig.ApiBaseUrl}GetStockListing";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string content = await response.Content.ReadAsStringAsync();
            var root = JObject.Parse(content);
            var valueArray = root["Value"];

            if (valueArray == null || !valueArray.Any())
            {
                ConfirmationLabel.Text = "No stock items returned.";
                return;
            }
            var allItems = valueArray.ToObject<List<StockItem>>();
            var databaseHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));
            await databaseHelper.SaveStockItemsAsync(allItems);

            ConfirmationLabel.Text = $"Stock updated. {allItems.Count} items saved.";
        }
        catch (Exception ex)
        {
            ConfirmationLabel.Text = $"Error: {ex.Message}";
        }
    }

    public class WarehouseResponse
    {
        public string message { get; set; }
        public List<Warehouse> data { get; set; }
    }

}

