using AmaScan.Classes;
using AmaScan.sqliteModels;
using Data.Model;
using SQLite;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace AmaScan;

public partial class ReceivingMain : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private PurchaseOrderResponse _currentPoResponse;
    private PoHeader _poHeader;

    public ReceivingMain()
	{
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Startup Error: " + ex);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Clear in-memory variables
        _currentPoResponse = null;
        _poHeader = null;

        // Clear session state
        ReceivingSession.CurrentPoHeader = null;
        ReceivingSession.SupplierInvoice = null;
        ReceivingSession.DeliveryNote = null;

        // Clear UI elements
        poEntry.Text = string.Empty;
        supplierLabel.Text = string.Empty;
        dueDateLabel.Text = string.Empty;
        poHeaderFrame.IsVisible = false;
        LoadPOButton.IsVisible = false;

        poLinesView.ItemsSource = null;
        poLinesView.IsVisible = false;
    }

    private async void OnFetchPOClicked(object sender, EventArgs e)
    {
        poEntry.Unfocus();
        string poNumber = poEntry.Text?.Trim();
        if (string.IsNullOrEmpty(poNumber))
        {
            await DisplayAlert("Validation", "Please enter a PO number.", "OK");
            return;
        }

        loadingIndicator.IsVisible = true;
        loadingIndicator.IsRunning = true;

        try
        {
            // Clear session values related to header
            ReceivingSession.CurrentPoHeader = null;
            ReceivingSession.SupplierInvoice = null;
            ReceivingSession.DeliveryNote = null;

            string url = $"{AppConfig.ApiBaseUrl}GetPurchaseOrder/{Uri.EscapeDataString(poNumber)}";
            _currentPoResponse = await _httpClient.GetFromJsonAsync<PurchaseOrderResponse>(url);

            if (_currentPoResponse == null || _currentPoResponse.Lines == null || !_currentPoResponse.Lines.Any())
            {
                await DisplayAlert("Not Found", "No data found for this PO.", "OK");
                return;
            }

            _currentPoResponse.OrderNo = poNumber;

            ReceivingSession.CurrentPoHeader = new PoHeader
            {
                OrderNo = _currentPoResponse.OrderNo,
                Status = "Started",
                DueDate = _currentPoResponse.DueDate,
                // Add more properties if needed
            };

            // Show header
            supplierLabel.Text = $"Supplier: {_currentPoResponse.SupplierName}";
            dueDateLabel.Text = $"Due Date: {_currentPoResponse.DueDate:yyyy-MM-dd}";
            poHeaderFrame.IsVisible = true;

            // Show lines
            poLinesView.ItemsSource = _currentPoResponse.Lines;
            poLinesView.IsVisible = true;

            // Once the fetch is done, make the "Load PO" button visible
            LoadPOButton.IsVisible = true;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not load PO: {poNumber} : Message:- {ex.Message}", "OK");
        }
        finally
        {
            loadingIndicator.IsVisible = false;
            loadingIndicator.IsRunning = false;
        }
    }


    private async void OnLoadPoClicked(object sender, EventArgs e)
    {
        try
        {
            if (_currentPoResponse == null)
            {
                await DisplayAlert("Error", "No PO data to save.", "OK");
                return;
            }

            string poNumber = _currentPoResponse.OrderNo;
            var databaseHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));

            #region Removef For Testing
            var existingPo = await databaseHelper.GetPoHeaderByOrderNoAsync(poNumber);
            ReceivingSession.CurrentPoHeader = existingPo;
            if (existingPo != null)
            {
                bool goToReceiving = await DisplayAlert("Resume Receiving?", "This PO is already loaded. Would you like to resume receiving?", "Yes", "No");
                if (goToReceiving)
                {
                    await Shell.Current.GoToAsync(nameof(ReceivingDocumentsPage));
                    return;
                }
                else
                {
                    await DisplayAlert("Cancelled", "You chose not to resume receiving.", "OK");
                    return;
                }
            }
            #endregion

            await SaveToLocalDatabaseAsync(_currentPoResponse);
            if (existingPo == null)
            {
                existingPo = await databaseHelper.GetPoHeaderByOrderNoAsync(poNumber);
                ReceivingSession.CurrentPoHeader = existingPo;
            }
            await DisplayAlert("Success", "PO has been loaded for offline receiving.", "OK");

            bool startReceiving = await DisplayAlert("Start Receiving?", "Would you like to start receiving this PO now?", "Yes", "No");
            if (startReceiving)
            {
                await Shell.Current.GoToAsync(nameof(ReceivingDocumentsPage));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load PO: {ex.Message}", "OK");
        }
    }

    public async Task SaveToLocalDatabaseAsync(PurchaseOrderResponse response)
    {
        if (response == null || response.Lines == null || !response.Lines.Any())
            throw new ArgumentException("Invalid purchase order data.");

        var databaseHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));

        //var existing = await databaseHelper.GetPoHeaderByOrderNoAsync(response.OrderNo);

        //if (existing != null)
        //{
        //    throw new InvalidOperationException($"PO {response.OrderNo} is already loaded on this device.");
        //}

        // Save the PoHeader with the relevant fields
        var poHeader = new PoHeader
        {
            OrderNo = response.OrderNo,
            DueDate = response.DueDate,
            Status = response.Status,
            JsonData = JsonSerializer.Serialize(response), // store full response as Json
            iscompleted = false // Assuming default is not completed
        };

        // Insert or update the PoHeader
        await databaseHelper.InsertAsync(poHeader);

        // Save each line as PoLine
        foreach (var line in response.Lines)
        {
            var poLine = new PoLine
            {
                OrderNo = line.DocNum,
                LineNo = line.LineNo,
                ItemCode = line.ItemCode,
                ItemDesc = line.ItemDesc,
                ItemBarcode = line.ItemBarcode,
                PackBarcode = line.PackBarcode,
                PackSize = line.PackSize,
                NoOfPacks = line.no_of_packs,
                OrderedQty = line.OrderedQty,
                ReceivedQty = 0, // default as 0
                BinLocation = line.BinLocation,
                WhID = line.WhID,
                GRNum = null // to be updated during receiving process
            };

            // Insert each PoLine
            await databaseHelper.InsertAsync(poLine);
        }
    }

    private async void Reset_Clicked(object sender, EventArgs e)
    {
        if (_currentPoResponse != null)
        {
            if (string.IsNullOrWhiteSpace(_currentPoResponse.OrderNo))
            {
                await DisplayAlert("Error", "No PO loaded to reset.", "OK");
                return;
            }
            bool confirm = await DisplayAlert("Reset PO", $"Are you sure you want to remove PO {_currentPoResponse.OrderNo} from this device?", "Yes", "No");
            if (!confirm) return;

            try
            {
                // Delete from database
                var databaseHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));
                await databaseHelper.DeletePoAsync(_currentPoResponse.OrderNo);

                ReceivingSession.SupplierInvoice = null;
                ReceivingSession.DeliveryNote = null;

                _currentPoResponse = null;

                supplierLabel.Text = string.Empty;
                dueDateLabel.Text = string.Empty;
                poHeaderFrame.IsVisible = false;
                LoadPOButton.IsVisible = false;

                poLinesView.ItemsSource = null;
                poLinesView.IsVisible = false;

                await DisplayAlert("Reset", "PO removed from device. You can fetch it again.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to reset PO. {ex.Message}", "OK");
            }
        }
    }
}