using AmaScan.Classes;
using AmaScan.Data;
using AmaScan.sqliteModels;
using Newtonsoft.Json.Linq;
using SQLite;
using System.Net.Http.Json;
using Microsoft.Maui.Dispatching;

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
        ApiUrlEntry.IsEnabled = false;
        LocationSwitch.Toggled += LocationSwitch_Toggled;
        LocationSwitch.IsToggled = AppConfig.IsOnSite;
        LoadWarehouses();
    }

    private void LocationSwitch_Toggled(object sender, ToggledEventArgs e)
    {
        AppConfig.IsOnSite = e.Value;
        ApiUrlEntry.Text = AppConfig.ApiBaseUrl;
    }

    private async void LoadWarehouses()
    {
        var _databaseHelper = AmaScanDatabase.GetDatabaseHelper();
        try
        {
            // Run heavy operations on background thread
            var result = await Task.Run(async () =>
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
                        return new { warehouses = new List<Warehouse>(), showAlert = true, alertMessage = "No warehouses found in API response." };
                    }
                }

                // Load from local DB
                var warehouses = await _databaseHelper.GetWarehousesAsync();
                var warehouseList = new List<Warehouse>
                {
                    new Warehouse { Code = "", Description = "Select Warehouse" }
                };

                if (warehouses != null && warehouses.Any())
                {
                    warehouseList.AddRange(warehouses);
                }

                return new { warehouses = warehouseList, showAlert = false, alertMessage = "" };
            });

            _warehouseList = result.warehouses;
           
            // Update UI on main thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SetupWarehousePicker(DefaultPickingWarehousePicker, "DefaultPickingWarehouseCode");
                SetupWarehousePicker(DefaultReceivingWarehousePicker, "DefaultReceivingWarehouseCode");
                SetupWarehousePicker(WarehousePickerR1, "RejectWarehouse1Code");
                SetupWarehousePicker(WarehousePickerR2, "RejectWarehouse2Code");
                SetupWarehousePicker(ReturnsWarehousePicker, "ReturnsWarehouseCode");
            });

            if (result.showAlert)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Info", result.alertMessage, "OK");
                });
            }
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Error", $"Failed to load warehouses: {ex.Message}", "OK");
            });
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
        // No need to save ApiUrlEntry.Text, as URL is now controlled by the switch
        ConfirmationLabel.Text = $"Location mode and Default Warehouse saved.";
        ConfirmationLabel.IsVisible = true;
            // Save all warehouse selections
            SaveWarehouseSelection(DefaultPickingWarehousePicker, "DefaultPickingWarehouseCode");
            SaveWarehouseSelection(DefaultReceivingWarehousePicker, "DefaultReceivingWarehouseCode");
            SaveWarehouseSelection(WarehousePickerR1, "RejectWarehouse1Code");
            SaveWarehouseSelection(WarehousePickerR2, "RejectWarehouse2Code");
            SaveWarehouseSelection(ReturnsWarehousePicker, "ReturnsWarehouseCode");

            ConfirmationLabel.Text = "API URL and Default Warehouse saved.";
            ConfirmationLabel.IsVisible = true;
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
            var databaseHelper = AmaScanDatabase.GetDatabaseHelper();
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

