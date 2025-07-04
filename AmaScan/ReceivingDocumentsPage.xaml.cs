using AmaScan.Classes;
using AmaScan.sqliteModels;
using SQLite;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using static Android.App.DownloadManager;

namespace AmaScan;

[QueryProperty(nameof(PoQuery), "po")]
public partial class ReceivingDocumentsPage : ContentPage, INotifyPropertyChanged
{
    private string _poQuery;
    public string PoQuery
    {
        get => _poQuery;
        set
        {
            _poQuery = Uri.UnescapeDataString(value);
            _ = LoadPoHeaderAsync(_poQuery);
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

    public ReceivingDocumentsPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    private async Task LoadPoHeaderAsync(string poNumber)
    {
        try
        {
            _poHeader = await _databaseHelper.GetPoHeaderByOrderNoAsync(poNumber);

            if (_poHeader == null)
            {
                await DisplayAlert("Error", $"PO not found: {poNumber}", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            DNnumber = _poHeader.DNnumber ?? "";
            SuppInvNumber = _poHeader.SuppInvNumber ?? "";
            OnPropertyChanged(nameof(OrderNo));

            await LoadWarehousesAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load PO: {ex.Message}", "OK");
        }
    }

    private async Task LoadWarehousesAsync()
    {
        try
        {
            var savedCode = Preferences.Get("DefaultWarehouseCode", "");
            var localWarehouses = await _databaseHelper.GetWarehousesAsync();

            var fullList = new ObservableCollection<Warehouse>(
                new[] { new Warehouse { Code = "", Description = "Select Warehouse" } }.Concat(localWarehouses)
            );

            WarehouseList = fullList;

            // Delay to avoid auto-popup on Picker
            await Task.Delay(100);
            try
            {
                SelectedWarehouse = WarehouseList.FirstOrDefault(w => w.Description.ToString().ToLower().Contains("main")) ?? WarehouseList.FirstOrDefault();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to set default warehouse: {ex.Message}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load warehouses: {ex.Message}", "OK");
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

            try
            {
                if (!string.IsNullOrWhiteSpace(SuppInvNumber))
                {
                    _poHeader.SuppInvNumber = SuppInvNumber;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"error 128 occurred: {ex.Message}", "OK");
            }
            try
            {
                if (!string.IsNullOrWhiteSpace(DNnumber))
                {
                    _poHeader.DNnumber = DNnumber;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"error 136 occurred: {ex.Message}", "OK");
            }
            _poHeader.Status = "Loaded"; // Update status to In Progress
            try
            {
                await _databaseHelper.UpdatePoHeaderAsync(_poHeader);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"error 145 occurred: {ex.Message}", "OK");
            }
            await Shell.Current.GoToAsync($"{nameof(ReceivingPage)}?po={_poHeader.OrderNo}");
            loadingIndicator.IsVisible = false;
            loadingIndicator.IsRunning = false;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
        }
    }

   public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}


