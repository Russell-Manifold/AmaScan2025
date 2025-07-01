using AmaScan.Classes;
using AmaScan.sqliteModels;
using Data.Model;
using SQLite;
using System.Net.Http.Json;

namespace AmaScan;

public partial class AuthorizationMain : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private SalesOrderResponse _currentSoResponse;
    private SoHeader _soHeader;

    public AuthorizationMain()
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
        try
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

            string url = $"{AppConfig.ApiBaseUrl}GetSalesOrder/{Uri.EscapeDataString($"IO{soNumber}")}";
            _currentSoResponse = await _httpClient.GetFromJsonAsync<SalesOrderResponse>(url);

            if (_currentSoResponse == null || _currentSoResponse.Lines == null || !_currentSoResponse.Lines.Any())
            {
                await DisplayAlert("Not Found", "No data found for this SO.", "OK");
                return;
            }

            PickingWorkflowSession.CurrentSoHeader = new SoHeader
            {
                Reference = _currentSoResponse.Reference,
                CustomerOrderNo = _currentSoResponse.CustomerOrderNo,
                CustomerName = _currentSoResponse.CustomerName,
                DueDate = _currentSoResponse.DueDate,
                OrderStatus = _currentSoResponse.OrderStatus,
                JsonData = System.Text.Json.JsonSerializer.Serialize(_currentSoResponse)
            };

            // Show header
            customerLabel.Text = $"Customer: {_currentSoResponse.CustomerName}";
            dueDateLabel.Text = $"Due Date: {_currentSoResponse.DueDate:yyyy-MM-dd}";
            soHeaderFrame.IsVisible = true;

            // Show lines
            soLinesView.ItemsSource = _currentSoResponse.Lines;
            soLinesView.IsVisible = true;

            // Once the fetch is done, make the "Load SO" button visible
            LoadSOButton.IsVisible = true;
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlert("Network Error", "Could not connect to the server. Please check your connection and try again.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Could not load SO: {ex.Message}", "OK");
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
                bool goToAuthorization = await DisplayAlert("Resume Authorization?",
                    "This SO is already loaded. Would you like to resume authorization?", "Yes", "No");

                if (goToAuthorization)
                {
                    PickingWorkflowSession.CurrentSoHeader = existingSo;
                    await Shell.Current.GoToAsync(nameof(AuthorizationDocumentsPage));
                    return;
                }
                else
                {
                    await DisplayAlert("Cancelled", "You chose not to resume authorization.", "OK");
                    return;
                }
            }

            await SaveToLocalDatabaseAsync(_currentSoResponse);
            await DisplayAlert("Success", "SO has been loaded for offline authorization.", "OK");

            bool startAuthorization = await DisplayAlert("Start Authorization?",
                "Would you like to start authorization this SO now?", "Yes", "No");

            if (startAuthorization)
            {
                await Shell.Current.GoToAsync(nameof(AuthorizationDocumentsPage));
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
            DocNum = soNumber, // Use the same SO number for DocNum
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
            CheckedQty = 0,
            AuthorizedQty = 0,
            Bin = line.Bin,
            Picked = false,
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