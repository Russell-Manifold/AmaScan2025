using AmaScan.Classes;
using AmaScan.sqliteModels;
using Data.Model;
using SQLite;
using System.Net.Http.Json;

namespace AmaScan;

public partial class PackingMain : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private SalesOrderResponse _currentSoResponse;
    private SoHeader _soHeader;





    public PackingMain()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ClearSessionAndUI();
    }

    private void ClearSessionAndUI()
    {
        // Clear in-memory variables
        _currentSoResponse = null;
        _soHeader = null;

        // Clear session state
        PickingWorkflowSession.Clear();

        // Clear UI elements
        soEntry.Text = string.Empty;
        customerLabel.Text = string.Empty;
        dueDateLabel.Text = string.Empty;
        soHeaderFrame.IsVisible = false;
        LoadSOButton.IsVisible = false;

        soLinesView.ItemsSource = null;
        soLinesView.IsVisible = false;
    }

    private async void OnFetchSOClicked(object sender, EventArgs e)
    {
        soEntry.Unfocus();
        string soNumber = soEntry.Text?.Trim();
        if (string.IsNullOrEmpty(soNumber))
        {
            await DisplayAlert("Validation", "Please enter a SO number.", "OK");
            return;
        }

        loadingIndicator.IsVisible = true;
        loadingIndicator.IsRunning = true;

        // Clear session values related to header
        PickingWorkflowSession.Clear();

        try
        {
            // Fetch from API
            string url = $"{AppConfig.ApiBaseUrl}GetSalesOrder/{Uri.EscapeDataString($"IO{soNumber}")}";
            _currentSoResponse = await _httpClient.GetFromJsonAsync<SalesOrderResponse>(url);

            if (_currentSoResponse == null || _currentSoResponse.Lines == null || !_currentSoResponse.Lines.Any())
            {
                await DisplayAlert("Not Found", $"SO {soNumber} not found on server.", "OK");
                return;
            }

            // Use merge helper for normal operation
            await SoMergeHelper.HandleSoFetchAndMergeAsync(soNumber, _currentSoResponse,
                customerLabel, dueDateLabel, soHeaderFrame, soLinesView, LoadSOButton, "Packing");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not load SO: {soNumber} : Message:- {ex.Message}", "OK");
        }
        finally
        {
            loadingIndicator.IsVisible = false;
            loadingIndicator.IsRunning = false;
        }
    }

    private async void OnLoadSOClicked(object sender, EventArgs e)
    {
        try
        {
            if (_currentSoResponse == null)
            {
                await DisplayAlert("Error", "No SO data to save.", "OK");
                return;
            }

            string soNumber = _currentSoResponse.Reference;
            if (string.IsNullOrEmpty(soNumber))
            {
                await DisplayAlert("Error", "Invalid SO number.", "OK");
                return;
            }

            var databaseHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));

            var existingSo = await databaseHelper.GetSoHeaderByOrderNoAsync(soNumber);
            if (existingSo != null)
            {
                bool goToPacking = await DisplayAlert("Resume Packing?",
                    "This SO is already loaded. Would you like to resume packing?", "Yes", "No");

                if (goToPacking)
                {
                    PickingWorkflowSession.CurrentSoHeader = existingSo;
                    await Shell.Current.GoToAsync(nameof(PackingDocumentsPage));
                    return;
                }
                else
                {
                    await DisplayAlert("Cancelled", "You chose not to resume packing.", "OK");
                    return;
                }
            }

            await SaveToLocalDatabaseAsync(_currentSoResponse);

            // Set the session header for navigation
            var savedSo = await databaseHelper.GetSoHeaderByOrderNoAsync(soNumber);
            PickingWorkflowSession.CurrentSoHeader = savedSo;

            await DisplayAlert("Success", "SO has been loaded for offline packing.", "OK");

            bool startPacking = await DisplayAlert("Start Packing?",
                "Would you like to start packing this SO now?", "Yes", "No");

            if (startPacking)
            {
                await Shell.Current.GoToAsync(nameof(PackingDocumentsPage));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load SO: {ex.Message}", "OK");
        }
    }

    private async Task SaveToLocalDatabaseAsync(SalesOrderResponse response)
    {
        if (response == null || response.Lines == null || !response.Lines.Any())
            throw new ArgumentException("Invalid sales order data.");

        var databaseHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));

        string soNumber = response.Reference; // Use Reference as the SO number
        if (string.IsNullOrEmpty(soNumber))
            throw new ArgumentException("Invalid SO number.");

        // Save the SoHeader with the relevant fields
        var soHeader = new SoHeader
        {
            Reference = soNumber, // This is the SO number that matches the SQL table
            CustomerOrderNo = response.CustomerOrderNo,
            CustomerName = response.CustomerName,
            AreaDescription = response.AreaDescription,
            DueDate = response.DueDate,
            OrderStatus = response.OrderStatus,
            JsonData = System.Text.Json.JsonSerializer.Serialize(response)
        };

        // Insert or update the SoHeader
        await databaseHelper.InsertAsync(soHeader);

        // Batch insert lines
        var soLines = response.Lines.Select(line => new SoLine
        {
            DocNum = soNumber,
            CustomerAccount = line.CustomerAccount,
            CustomerName = line.CustomerName,
            ItemCode = line.ItemCode,
            ItemDesc = line.ItemDesc,
            ItemBarcode = line.ItemBarcode,
            PackSize = line.PackSize,
            PackBarcode = line.PackBarcode,
            NoOfPacks = line.NoOfPacks,
            OrderedQty = line.OrderedQty,
            PickedQty = 0,
            PackedQty = 0,
            CheckedQty = 0,
            AuthorizedQty = 0,
            Bin = line.Bin,
            Picked = false,
            Packed = false,
            Checked = false,
            Authorized = false
        }).ToList();

        // Insert each line individually
        foreach (var line in soLines)
        {
            await databaseHelper.InsertAsync(line);
        }
    }

    private async void OnResetClicked(object sender, EventArgs e)
    {
        if (_currentSoResponse != null)
        {
            bool confirm = await DisplayAlert("Reset", "Are you sure you want to reset? This will clear all current data.", "Yes", "No");
            if (confirm) ClearSessionAndUI();
        }
    }
}