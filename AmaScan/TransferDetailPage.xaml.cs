using AmaScan.Classes;
using AmaScan.Models;
using Data.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Windows.Input;
using static AmaScan.TransferMainPage;

namespace AmaScan;

public partial class TransferDetailPage : ContentPage, INotifyPropertyChanged
{
    private readonly HttpClient _httpClient;
    private WHtrfRequestHeader _transferRequest;
    public string RequisitionNumber => _transferRequest?.requisition_number;
    public DateTime DueDate => _transferRequest?.due_date_and_time ?? DateTime.MinValue;
    public string SourceWarehouse => _transferRequest?.source_warehouse_description;
    public string DestinationWarehouse => _transferRequest?.destination_warehouse_description;

    public ObservableCollection<TransferLineViewModel> TransferLines { get; } = new();

    private bool _isScanningEnabled;
    public bool IsScanningEnabled
    {
        get => _isScanningEnabled;
        set
        {
            if (_isScanningEnabled != value)
            {
                _isScanningEnabled = value;
                OnPropertyChanged(nameof(IsScanningEnabled));
            }
        }
    }

    public bool CanStartTransfer => TransferLines.All(l => l.IsComplete);

    public TransferDetailPage()
    {
        InitializeComponent();
        _httpClient = AppConfig.GetHttpClient();
        BindingContext = this;
    }

    public async Task LoadTransferDetailsAsync(WHtrfRequestHeader transferRequest)
    {
        _transferRequest = transferRequest;

        BindingContext = this;

        TransferLines.Clear();
        foreach (var line in _transferRequest.lines)
        {
            TransferLines.Add(new TransferLineViewModel(line));
        }

        IsScanningEnabled = false;

        OnPropertyChanged(nameof(CanStartTransfer));
        OnPropertyChanged(nameof(RequisitionNumber));
        OnPropertyChanged(nameof(DueDate));
        OnPropertyChanged(nameof(SourceWarehouse));
        OnPropertyChanged(nameof(DestinationWarehouse));
    }
    private void BeginScanning()
    {
        IsScanningEnabled = true;
    }

    private void OnLineScannedQtyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransferLineViewModel.ScannedQty))
        {
            OnPropertyChanged(nameof(CanStartTransfer));
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (TransferState.CurrentTransferHeader != null)
        {
            await LoadTransferDetailsAsync(TransferState.CurrentTransferHeader);
        }

        foreach (var line in TransferLines)
        {
            line.PropertyChanged += OnLineScannedQtyChanged;
        }
    }

    protected override void OnDisappearing()
    {
        foreach (var line in TransferLines)
        {
            line.PropertyChanged -= OnLineScannedQtyChanged;
        }
        base.OnDisappearing();
    }

    // Add a button click handler for Start Transfer finalization, see below.

    public ICommand ScanCommand => new Command<TransferLineViewModel>(async line =>
    {
        // TODO: Integrate real barcode scanner here
        // For now, simulate scanning by incrementing scanned qty by 1

        line.ScannedQty++;
        OnPropertyChanged(nameof(CanStartTransfer));
    });

    private async void StartTransferButton_Clicked(object sender, EventArgs e)
    {
        if (!CanStartTransfer)
        {
            await DisplayAlert("Incomplete", "Please scan all items with correct quantities before starting transfer.", "OK");
            return;
        }

        bool confirmed = await DisplayAlert("Confirm", "Start Transfer now?", "Yes", "No");
        if (!confirmed)
            return;

        try
        {
            // Prepare payload with scanned quantities
            var payload = new
            {
                requisition_number = _transferRequest.requisition_number,
                lines = TransferLines.Select(l => new
                {
                    stock_code = l.StockCode,
                    scanned_qty = l.ScannedQty
                }).ToList()
            };

            var response = await _httpClient.PostAsJsonAsync($"{AppConfig.ApiBaseUrl}WarehouseTransfer/Start", payload);

            if (response.IsSuccessStatusCode)
            {
                await DisplayAlert("Success", "Transfer started successfully!", "OK");
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                await DisplayAlert("Error", $"Failed to start transfer: {error}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private void OnAcceptClicked(object sender, EventArgs e)
    {
        string barcode = entryBarcode.Text?.Trim();
        if (string.IsNullOrEmpty(barcode)) return;

        if (!int.TryParse(entryQuantity.Text, out int qty) || qty <= 0)
        {
            DisplayAlert("Invalid Quantity", "Please enter a valid quantity.", "OK");
            return;
        }

        var item = TransferLines
            .FirstOrDefault(x => string.Equals(x.BarCode, barcode, StringComparison.OrdinalIgnoreCase));

        if (item == null)
        {
            DisplayAlert("Item Not Found", $"Barcode '{barcode}' not found in this transfer.", "OK");
            return;
        }

        int maxScannable = item.OutstandingQty - item.ScannedQty;
        int qtyToAdd = Math.Min(qty, maxScannable);

        item.ScannedQty += qtyToAdd;

        if (qtyToAdd < qty)
            DisplayAlert("Partial", $"Only {qtyToAdd} units accepted due to outstanding limits.", "OK");

        entryBarcode.Text = string.Empty;
        entryQuantity.Text = string.Empty;

        OnPropertyChanged(nameof(CanStartTransfer));
    }

    private void OnBarcodeEntered(object sender, EventArgs e)
    {
        entryQuantity.Focus();
    }
    
}
