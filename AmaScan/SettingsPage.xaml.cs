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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        WarehousePicker.Items.Clear();
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
            var warehouseList = new List<Warehouse>
            {
                new Warehouse { Code = "", Description = "Select Warehouse" }
            };

            if (warehouses != null && warehouses.Any())
            {
                warehouseList.AddRange(warehouses);
            }
            WarehousePicker.ItemsSource = warehouseList;
            WarehousePicker.ItemDisplayBinding = new Binding("Description");

            foreach (var w in warehouses)
            {
                Console.WriteLine($"Warehouse: {w.Code} - {w.Description}");
            }

            string savedWarehouseCode = Preferences.Get("DefaultWarehouseCode", "");
            var selectedIndex = warehouseList.FindIndex(w => w.Code == savedWarehouseCode);
            WarehousePicker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load warehouses: {ex.Message}", "OK");
        }
    }

    private void OnSaveClicked(object sender, EventArgs e)
    {
        // No need to save ApiUrlEntry.Text, as URL is now controlled by the switch
        ConfirmationLabel.Text = $"Location mode and Default Warehouse saved.";
        ConfirmationLabel.IsVisible = true;
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

