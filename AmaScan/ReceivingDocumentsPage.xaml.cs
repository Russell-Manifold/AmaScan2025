using AmaScan.Classes;
using AmaScan.sqliteModels;
using Data.Model;
using SQLite;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using static AmaScan.SettingsPage;

namespace AmaScan;

[QueryProperty(nameof(PoQuery), "po")]
public partial class ReceivingDocumentsPage : ContentPage, INotifyPropertyChanged
{
    private static readonly HttpClient _httpClient = new();
    private string _poQuery;
    public string PoQuery
    {
        get => _poQuery;
        set
        {
            if (_poQuery != value)
            {
                _poQuery = Uri.UnescapeDataString(value);
                // Avoid fire-and-forget, use OnAppearing to trigger load
            }
        }
    }

    private PoHeader _poHeader;
    private string _dnNumber;
    private readonly DatabaseHelper _databaseHelper = new(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));

    public string DNnumber
    {
        get => _dnNumber;
        set
        {
            if (_dnNumber != value)
            {
                _dnNumber = value;
                OnPropertyChanged();
            }
        }
    }

    private string _suppInvNumber;
    public string SuppInvNumber
    {
        get => _suppInvNumber;
        set
        {
            if (_suppInvNumber != value)
            {
                _suppInvNumber = value;
                OnPropertyChanged();
            }
        }
    }

    public string OrderNo => _poHeader?.OrderNo ?? "";

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

    public string SelectedWarehouseCode => SelectedWarehouse?.Code;
    public string SelectedWarehouseDescription => SelectedWarehouse?.Description;

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
                OnPropertyChanged(nameof(SelectedWarehouseCode));
                OnPropertyChanged(nameof(SelectedWarehouseDescription));
            }
        }
    }

    public ReceivingDocumentsPage()
    {
        InitializeComponent();
        BindingContext = this;
        WarehousePicker.SelectedIndexChanged += WarehousePicker_SelectedIndexChanged;
        WarehousePicker.IsVisible = false;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!string.IsNullOrWhiteSpace(PoQuery) && _poHeader == null)
        {
            await LoadPoHeaderAsync(PoQuery);
        }
    }

    private async Task LoadPoHeaderAsync(string poNumber)
    {
        loadingIndicator.IsVisible = true;
        loadingIndicator.IsRunning = true;

        try
        {
            _poHeader = await _databaseHelper.GetPoHeaderByOrderNoAsync(poNumber).ConfigureAwait(false);
            if (_poHeader == null)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Error", $"PO not found: {poNumber}", "OK");
                    await Shell.Current.GoToAsync("..");
                });
                return;
            }
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                DNnumber = _poHeader.DNnumber ?? "";
                SuppInvNumber = _poHeader.SuppInvNumber ?? "";
                OnPropertyChanged(nameof(OrderNo));
            });
            await LoadWarehousesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await DisplayAlert("Error", $"Failed to load PO: {ex.Message}", "OK"));
        }
        finally
        {
            loadingIndicator.IsVisible = false;
            loadingIndicator.IsRunning = false;
        }
    }     

    private async Task LoadWarehousesAsync()
    {
        try
        {

            var savedCode = Preferences.Get("DefaultWarehouseCode", "");
            bool hasWarehouses = await _databaseHelper.HasWarehousesAsync().ConfigureAwait(false);
            if (!hasWarehouses && !Preferences.Get("HasPopulatedWarehouses", false))

            // Check if warehouses exist locally
            bool hasWarehouses = await _databaseHelper.HasWarehousesAsync();

            if (!hasWarehouses)

            {
                string url = $"{AppConfig.ApiBaseUrl}warehouses/get-warehouses";
                var response = await _httpClient.GetFromJsonAsync<WarehouseResponse>(url).ConfigureAwait(false);
                if (response?.data != null && response.data.Any())
                {
                    await _databaseHelper.SaveWarehousesAsync(response.data).ConfigureAwait(false);
                    Preferences.Set("HasPopulatedWarehouses", true);
                }
                else
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                        await DisplayAlert("Info", "No warehouses found in API response.", "OK"));
                    return;
                }
            }
            var warehouseData = await _databaseHelper.GetWarehousesAsync().ConfigureAwait(false);
            var fullList = new ObservableCollection<Warehouse>(
                new[] { new Warehouse { Code = "", Description = "Select Warehouse" } }.Concat(warehouseData)
            );

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                WarehouseList = fullList;
                // Find the intended default warehouse
                var mainWarehouse = WarehouseList.FirstOrDefault(w =>
                    !string.IsNullOrWhiteSpace(w.Description) &&
                    w.Description.ToLower().Contains("main"))
                    ?? WarehouseList.FirstOrDefault();
                // Only set if not already set
                if (SelectedWarehouse == null)
                {
                    SelectedWarehouse = mainWarehouse;


            WarehouseList = fullList;

            // Set default receiving warehouse from settings
            string defaultReceivingCode = Preferences.Get("DefaultReceivingWarehouseCode", "");
            if (!string.IsNullOrEmpty(defaultReceivingCode))
            {
                var defaultWarehouse = WarehouseList.FirstOrDefault(w => w.Code == defaultReceivingCode);
                if (defaultWarehouse != null)
                {
                    SelectedWarehouse = defaultWarehouse;
                }
                else
                {
                    // Fallback to first warehouse if default not found
                    SelectedWarehouse = WarehouseList.FirstOrDefault();

                }
            }
            else
            {
                // No default set, select first warehouse
                SelectedWarehouse = WarehouseList.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await DisplayAlert("Error", $"Failed to load warehouses: {ex.Message}", "OK"));
        }
    }

    private async void OnAcceptClicked(object sender, EventArgs e)
    {
        try
        {
            if (_poHeader == null)
            {
                await DisplayAlert("Error", "No PO loaded. Please restart the process.", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }
            if (string.IsNullOrWhiteSpace(DNnumber) && string.IsNullOrWhiteSpace(SuppInvNumber))
            {
                await DisplayAlert("Required", "Please enter at least one document number.", "OK");
                return;
            }
            loadingIndicator.IsVisible = true;
            loadingIndicator.IsRunning = true;
            if (!string.IsNullOrWhiteSpace(SuppInvNumber))
                _poHeader.SuppInvNumber = SuppInvNumber;
            if (!string.IsNullOrWhiteSpace(DNnumber))
                _poHeader.DNnumber = DNnumber;
            _poHeader.Status = "Loaded";
            await _databaseHelper.UpdatePoHeaderAsync(_poHeader).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(async () =>
                await Shell.Current.GoToAsync($"{nameof(ReceivingPage)}?po={_poHeader.OrderNo}"));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
        }
        finally
        {
            loadingIndicator.IsVisible = false;
            loadingIndicator.IsRunning = false;
        }
    }

    private void OnChangeWarehouseClicked(object sender, EventArgs e)
    {
        WarehousePicker.IsVisible = true;
        WarehousePicker.Focus();
    }

    private void WarehousePicker_SelectedIndexChanged(object sender, EventArgs e)
    {
        WarehousePicker.IsVisible = false;
        // The SelectedWarehouse binding will update the label automatically
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}


