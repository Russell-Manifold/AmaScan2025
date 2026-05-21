using AmaScan.Classes;
using AmaScan.Data;
using AmaScan.sqliteModels;
using Data.Model;
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

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Guard: default receiving warehouse must be set per device
        string defaultWh = Preferences.Get("DefaultReceivingWarehouseCode", "");
        if (string.IsNullOrWhiteSpace(defaultWh))
        {
            await DisplayAlert("Setup Required", "Please select a default receiving warehouse in Settings before receiving.", "OK");
            await Shell.Current.GoToAsync(nameof(SettingsPage));
            return;
        }

        // Clear in-memory variables
        _currentPoResponse = null;
        _poHeader = null;

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
            // Step 1: Check local database first
            var existingPo = await App.Db.GetPoHeaderByOrderNoAsync(poNumber);

            // Step 2: Fetch from API (either PO not found locally or user wants fresh data)
            bool success = await FetchPoFromApiAsync(poNumber);
            if (!success)
            {
                await DisplayAlert("Not Found", $"PO {poNumber} not found on server.", "OK");
                return;
            }

            // Step 3: If PO exists locally, merge fresh data with existing workflow data
            if (existingPo != null)
            {
                await MergeFreshDataWithExistingAsync(poNumber);
            }
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

    private async Task LoadExistingPoForDisplayAsync(string poNumber)
    {
        try
        {
            // Load existing PO header
            var existingPo = await App.Db.GetPoHeaderByOrderNoAsync(poNumber);
            if (existingPo == null) return;

            // Load existing PO lines
            var existingLines = await App.Db.GetPoLinesByOrderNoAsync(poNumber);
            if (!existingLines.Any()) return;

            // Convert PoLine to display format (similar to PurchaseOrderLine)
            var displayLines = existingLines.Select(line => new PurchaseOrderLine
            {
                DocNum = line.OrderNo,
                LineNo = line.LineNo,
                ItemCode = line.ItemCode,
                ItemDesc = line.ItemDesc,
                ItemBarcode = line.ItemBarcode,
                PackBarcode = line.PackBarcode,
                PackSize = line.PackSize,
                no_of_packs = line.NoOfPacks,
                OrderedQty = (int)line.OrderedQty,
                CostPrice = line.CostPrice,
                CostPricePer = line.CostPricePer,
                VatCode = line.VatCode,
                VatRate = line.VatRate,
                ScanAcceptQty = (int)line.ScanAcceptQty,
                ScanRejectQty = (int)line.ScanRejectQty,
                BinLocation = line.BinLocation,
                WhID = line.WhID
            }).ToList();

            // Create display response - ensure we're using a fresh collection
            _currentPoResponse = new PurchaseOrderResponse
            {
                OrderNo = existingPo.OrderNo,
                SupplierName = existingPo.SupplierName,
                DueDate = existingPo.DueDate,
                Status = existingPo.Status,
                Lines = displayLines
            };

            // Update UI on main thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Show header
                supplierLabel.Text = $"Supplier: {existingPo.SupplierName}";
                dueDateLabel.Text = $"Due: {_currentPoResponse.DueDate:yyyy-MM-dd}";
                poHeaderFrame.IsVisible = true;

                // Clear and re-bind lines to prevent duplication
                poLinesView.ItemsSource = null;
                poLinesView.ItemsSource = _currentPoResponse.Lines;
                poLinesView.IsVisible = true;

                // Make the "Load PO" button visible
                LoadPOButton.IsVisible = true;
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load existing PO: {ex.Message}", "OK");
        }
    }

    private async Task<bool> FetchPoFromApiAsync(string poNumber)
    {
        string url = $"{AppConfig.ApiBaseUrl}GetPurchaseOrder/{Uri.EscapeDataString(poNumber)}";
        _currentPoResponse = await _httpClient.GetFromJsonAsync<PurchaseOrderResponse>(url);

        if (_currentPoResponse == null || _currentPoResponse.Lines == null || !_currentPoResponse.Lines.Any())
        {
            return false;
        }

        _currentPoResponse.OrderNo = poNumber;

        // Update UI on main thread
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            // Show header
            supplierLabel.Text = $"Supplier: {_currentPoResponse.SupplierName}";
            dueDateLabel.Text = $"Due Date: {_currentPoResponse.DueDate:yyyy-MM-dd}";
            poHeaderFrame.IsVisible = true;

            // Show lines
            poLinesView.ItemsSource = _currentPoResponse.Lines;
            poLinesView.IsVisible = true;

            // Make the "Load PO" button visible
            LoadPOButton.IsVisible = true;
        });

        return true;
    }

    private async Task MergeFreshDataWithExistingAsync(string poNumber)
    {
        try
        {
            var existingLinesBefore = await App.Db.GetPoLinesByOrderNoAsync(poNumber);
            var existingLineKeys = existingLinesBefore
                .Select(l => $"{l.ItemCode}_{l.ItemBarcode}")
                .ToHashSet();
            var freshLineKeys = _currentPoResponse.Lines
                .Select(l => $"{l.ItemCode}_{l.ItemBarcode}")
                .ToHashSet();

            // Merge fresh data with existing workflow data
            await App.Db.MergePoDataAsync(_currentPoResponse);

            // Get lines after merge for comparison
            var existingLinesAfter = await App.Db.GetPoLinesByOrderNoAsync(poNumber);

            // Calculate merge summary
            var newLines = freshLineKeys.Except(existingLineKeys).Count();
            var removedLines = existingLineKeys.Except(freshLineKeys).Count();
            var updatedLines = existingLinesBefore.Count - removedLines;

            // Reload the merged data for display
            await LoadExistingPoForDisplayAsync(poNumber);

            // Only show merge summary when something actually changed
            if (newLines > 0 || removedLines > 0)
            {
                var summary = $"Merge Complete!\n\n" +
                             $"{updatedLines} lines updated\n" +
                             $"{newLines} new lines added\n" +
                             $"{removedLines} lines removed\n\n" +
                             $"Your received quantities have been preserved.";

                if (removedLines > 0)
                    await DisplayAlert("Merge Summary", summary + "\n\nNote: Removed lines have been deleted locally, even if they had progress.", "OK");
                else
                    await DisplayAlert("Merge Summary", summary, "OK");
            }
        }
        catch (Exception ex)
        {
            if (!ex.Message.ToLower().ToString().Contains("same key")) await DisplayAlert("Merge Error", $"Failed to merge fresh data: {ex.Message}", "OK");
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
             var existingPo = await App.Db.GetPoHeaderByOrderNoAsync(poNumber);
            if (existingPo != null)
            {
                // PO is already loaded and merged, just navigate
                await Shell.Current.GoToAsync($"{nameof(ReceivingDocumentsPage)}?po={poNumber}");
                return;
            }

            await SaveToLocalDatabaseAsync(_currentPoResponse);

            await DisplayAlert("Success", "PO has been loaded for offline receiving.", "OK");

            bool startReceiving = await DisplayAlert("Start Receiving?", "Would you like to start receiving this PO now?", "Yes", "No");
            if (startReceiving)
            {
                await Shell.Current.GoToAsync($"{nameof(ReceivingDocumentsPage)}?po={poNumber}");
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
            SupplierName = response.SupplierName,
            AcctCode = response.SupplierCode,
            BranchCode = response.BranchCode ?? "HO",
            JsonData = JsonSerializer.Serialize(response),
            iscompleted = false
        };

        // Insert or update the PoHeader
        await App.Db.InsertAsync(poHeader);

        // Batch build and insert all lines in a single transaction
        var poLines = response.Lines.Select(line => new PoLine
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
            CostPrice = line.CostPrice,
            CostPricePer = line.CostPricePer,
            VatCode = line.VatCode,
            VatRate = line.VatRate,
            ReceivedQty = 0,
            BinLocation = line.BinLocation,
            WhID = !string.IsNullOrWhiteSpace(line.WhID) ? line.WhID.PadLeft(3, '0') : line.WhID,
            GRNum = null
        }).ToList();

        await App.Db.InsertAllAsync(poLines);
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert("Log Out", "Are you sure you want to log out?", "Yes", "No");
        if (!confirm) return;
        App.Services.GetRequiredService<UserSession>().CurrentUser = null;
        await Navigation.PopToRootAsync();
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
                await App.Db.DeletePoAsync(_currentPoResponse.OrderNo);

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