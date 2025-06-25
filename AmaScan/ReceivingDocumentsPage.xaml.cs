using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AmaScan.Classes;
using AmaScan.sqliteModels;
using SQLite;

namespace AmaScan;
public partial class ReceivingDocumentsPage : ContentPage, INotifyPropertyChanged
{
    //private PoHeader _poHeader => ReceivingSession.CurrentPoHeader;

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

    // New: OrderNo property bound to "Receiving PO" label
    public string OrderNo => _poHeader?.OrderNo ?? "";

    // Warehouse related
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

        // Initialize PO header fields
        //DNnumber = _poHeader?.DNnumber ?? "";
        //SuppInvNumber = _poHeader?.SuppInvNumber ?? "";

        LoadWarehouses();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _poHeader = ReceivingSession.CurrentPoHeader;

        if (_poHeader == null)
        {
            // Show an error if the session is missing
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("Error", "No PO found in session.", "OK");
                await Shell.Current.GoToAsync("..");
            });
            return;
        }

        DNnumber = _poHeader.DNnumber ?? "";
        SuppInvNumber = _poHeader.SuppInvNumber ?? "";

        OnPropertyChanged(nameof(OrderNo));
    }

    private void LoadWarehouses()
    {
        // Example data - replace with API call to get warehouses
        WarehouseList = new ObservableCollection<Warehouse>
        {
            new Warehouse { Code = "001", Description = "Main Warehouse" },
            new Warehouse { Code = "003", Description = "Factory Sales" },
        };

        // Optionally select default warehouse here
        SelectedWarehouse = WarehouseList.FirstOrDefault();
    }

    private async void OnAcceptClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DNnumber) && string.IsNullOrWhiteSpace(SuppInvNumber))
        {
            await DisplayAlert("Required", "Please enter at least one document number.", "OK");
            return;
        }
        loadingIndicator.IsVisible = true;
        loadingIndicator.IsRunning = true;

        _poHeader.DNnumber = string.Empty;
        _poHeader.SuppInvNumber = string.Empty;
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
            var databaseHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));
            await databaseHelper.UpdatePoHeaderAsync(_poHeader);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"error 145 occurred: {ex.Message}", "OK");
        }
        // Save updated header
        
       
        // Update session data
        try
        {
            ReceivingSession.CurrentPoHeader = _poHeader;
            ReceivingSession.DeliveryNote = DNnumber;
            ReceivingSession.SupplierInvoice = SuppInvNumber;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"error 159 occurred: {ex.Message}", "OK");
        }
       
        // TODO: Save the SelectedWarehouse if needed to session or DB

        await Shell.Current.GoToAsync(nameof(ReceivingPage));
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}


