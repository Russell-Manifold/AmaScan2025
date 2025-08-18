using AmaScan.Classes;
using AmaScan.sqliteModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using static AmaScan.Classes.DatabaseHelper;
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
            _poHeader = await App.Db.GetPoHeaderByOrderNoAsync(poNumber).ConfigureAwait(false);
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
            // single background fetch (uses cache if already loaded)
            var warehouses = await WarehouseCache.GetAsync();

            // single marshal to the UI thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // prepend the “Select Warehouse” item
                WarehouseList = new ObservableCollection<Warehouse>(
                    new[] { new Warehouse { Code = "", Description = "Select Warehouse" } }
                    .Concat(warehouses));

                // set default
                var defaultCode = Preferences.Get("DefaultReceivingWarehouseCode", "");
                SelectedWarehouse = WarehouseList.FirstOrDefault(w => w.Code == defaultCode)?? WarehouseList.FirstOrDefault();
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load warehouses: {ex.Message}", "OK");
        }
    }
    //private async Task LoadWarehousesAsync()
    //{
    //    try
    //    {
    //        // Run heavy operations on background thread
    //        var result = await Task.Run(async () =>
    //        {
    //            bool hasWarehouses = await App.Db.HasWarehousesAsync();
    //            if (!hasWarehouses)
    //            {
    //                string url = $"{AppConfig.ApiBaseUrl}warehouses/get-warehouses";
    //                var response = await _httpClient.GetFromJsonAsync<WarehouseResponse>(url);
    //                if (response?.data != null && response.data.Any())
    //                {
    //                    await App.Db.SaveWarehousesAsync(response.data);
    //                    Preferences.Set("HasPopulatedWarehouses", true);
    //                }
    //                else
    //                {
    //                    return new { warehouseData = new List<Warehouse>(), showAlert = true, alertMessage = "No warehouses found in API response." };
    //                }
    //            }

    //            var warehouseData = await App.Db.GetWarehousesAsync();
    //            return new { warehouseData, showAlert = false, alertMessage = "" };
    //        });

    //        // Update UI on main thread
    //        await MainThread.InvokeOnMainThreadAsync(() =>
    //        {
    //            var fullList = new ObservableCollection<Warehouse>(
    //                new[] { new Warehouse { Code = "", Description = "Select Warehouse" } }.Concat(result.warehouseData)
    //            );

    //            WarehouseList = fullList;

    //            // Set default receiving warehouse from settings
    //            string defaultReceivingCode = Preferences.Get("DefaultReceivingWarehouseCode", "");
    //            if (!string.IsNullOrEmpty(defaultReceivingCode))
    //            {
    //                var defaultWarehouse = WarehouseList.FirstOrDefault(w => w.Code == defaultReceivingCode);
    //                if (defaultWarehouse != null)
    //                {
    //                    SelectedWarehouse = defaultWarehouse;
    //                }
    //                else
    //                {
    //                    SelectedWarehouse = WarehouseList.FirstOrDefault();
    //                }
    //            }
    //            else
    //            {
    //                SelectedWarehouse = WarehouseList.FirstOrDefault();
    //            }
    //        });

    //        if (result.showAlert)
    //        {
    //            await MainThread.InvokeOnMainThreadAsync(async () =>
    //            {
    //                await DisplayAlert("Info", result.alertMessage, "OK");
    //            });
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        await DisplayAlert("Error", $"Failed to load warehouses: {ex.Message}", "OK");
    //    }
    //}

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

            if (SelectedWarehouse?.Code is null || string.IsNullOrWhiteSpace(SelectedWarehouse.Code))
            {
                await DisplayAlert("Warehouse Required", "Please select a warehouse above.", "OK");
                return;
            }

            SetLoading(true);

            // Update header
            _poHeader.SuppInvNumber = string.IsNullOrWhiteSpace(SuppInvNumber) ? _poHeader.SuppInvNumber : SuppInvNumber;
            _poHeader.DNnumber = string.IsNullOrWhiteSpace(DNnumber) ? _poHeader.DNnumber : DNnumber;
            _poHeader.Status = "Loaded";

            await App.Db.UpdatePoHeaderAsync(_poHeader);

            // Navigate with proper encoding
            string poNumber = Uri.EscapeDataString(_poHeader.OrderNo);
            await App.Db.DeleteAllExceptPoAsync(poNumber);
            await Shell.Current.GoToAsync($"{nameof(ReceivingPage)}?po={poNumber}");
        }
        catch (Exception ex)
        {
            // Log ex.StackTrace somewhere for debugging
            await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void SetLoading(bool isLoading)
    {
        loadingIndicator.IsVisible = isLoading;
        loadingIndicator.IsRunning = isLoading;
    }

        //private async void OnAcceptClicked(object sender, EventArgs e)
    //{
    //    try
    //    {
    //        if (_poHeader == null)
    //        {
    //            await DisplayAlert("Error", "No PO loaded. Please restart the process.", "OK");
    //            await Shell.Current.GoToAsync("..");
    //            return;
    //        }
    //        if (string.IsNullOrWhiteSpace(DNnumber) && string.IsNullOrWhiteSpace(SuppInvNumber))
    //        {
    //            await DisplayAlert("Required", "Please enter at least one document number.", "OK");
    //            return;
    //        }

    //        // Validate warehouse selection
    //        if (SelectedWarehouse == null || string.IsNullOrWhiteSpace(SelectedWarehouse.Code))
    //        {
    //            await DisplayAlert("Warehouse Required", 
    //                "Please select a warehouse above.", "OK");
    //            return;
    //        }

    //        loadingIndicator.IsVisible = true;
    //        loadingIndicator.IsRunning = true;

    //        // Update header
    //        if (!string.IsNullOrWhiteSpace(SuppInvNumber))
    //            _poHeader.SuppInvNumber = SuppInvNumber;
    //        if (!string.IsNullOrWhiteSpace(DNnumber))
    //            _poHeader.DNnumber = DNnumber;
    //        _poHeader.Status = "Loaded";

    //        await App.Db.UpdatePoHeaderAsync(_poHeader);


    //        // Navigate with proper encoding
    //        string poNumber = Uri.EscapeDataString(_poHeader.OrderNo);
    //        await App.Db.DeleteAllExceptPoAsync(poNumber);
    //        await Shell.Current.GoToAsync($"{nameof(ReceivingPage)}?po={poNumber}");
    //    }
    //    catch (Exception ex)
    //    {
    //        await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
    //    }
    //    finally
    //    {
    //        loadingIndicator.IsVisible = false;
    //        loadingIndicator.IsRunning = false;
    //    }
    //}

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


