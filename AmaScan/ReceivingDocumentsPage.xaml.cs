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
        DNnumber = _poHeader?.DNnumber ?? "";
        SuppInvNumber = _poHeader?.SuppInvNumber ?? "";

        // Load warehouses (replace with your actual data source or API call)
        LoadWarehouses();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _poHeader = ReceivingSession.CurrentPoHeader;

        DNnumber = _poHeader?.DNnumber ?? "";
        SuppInvNumber = _poHeader?.SuppInvNumber ?? "";

        // These help update bindings if PO changed
        OnPropertyChanged(nameof(OrderNo));
    }

    private void LoadWarehouses()
    {
        // Example data - replace with API call to get warehouses
        //WarehouseList = new ObservableCollection<Warehouse>
        //{
        //    new Warehouse { Id = "WH1", WarehouseName = "Main Warehouse" },
        //    new Warehouse { Id = "WH2", WarehouseName = "Secondary Warehouse" },
        //};

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

        _poHeader.DNnumber = DNnumber;
        _poHeader.SuppInvNumber = SuppInvNumber;

        // Save updated header
        var databaseHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));
        await databaseHelper.UpdatePoHeaderAsync(_poHeader);

        // Update session data
        ReceivingSession.CurrentPoHeader = _poHeader;
        ReceivingSession.DeliveryNote = DNnumber;
        ReceivingSession.SupplierInvoice = SuppInvNumber;

        // TODO: Save the SelectedWarehouse if needed to session or DB

        await Shell.Current.GoToAsync(nameof(ReceivingPage));
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}


