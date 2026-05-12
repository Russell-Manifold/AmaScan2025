using AmaScan.Classes;
using AmaScan.Data;
using AmaScan.sqliteModels;
using Data.Model;
using System.Net.Http.Json;
using System.Net;

namespace AmaScan;

public partial class PickingMain : ContentPage
{
    private readonly HttpClient _httpClient = new();
    private readonly UserSession _userSession;
    private SalesOrderResponse _currentSoResponse;
    private SoHeader _soHeader;

    public PickingMain()
    {
        InitializeComponent();
        _userSession = App.Services.GetRequiredService<UserSession>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadNextOrderAsync();
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
        sequenceLabel.Text = string.Empty;
        soHeaderFrame.IsVisible = false;
        LoadSOButton.IsVisible = false;
        //nextOrderLabel.IsVisible = false;
        sequenceLabel.IsVisible = false;

        soLinesView.ItemsSource = null;
        soLinesView.IsVisible = false;
    }

    private async Task LoadNextOrderAsync()
    {
        // Check if user is logged in
        if (_userSession?.CurrentUser == null || string.IsNullOrWhiteSpace(_userSession.CurrentUser.UserName))
        {
            ShowSearchSection();
            return;
        }

        string pickerUsername = _userSession.CurrentUser.UserName;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            loadingIndicator.IsVisible = true;
            loadingIndicator.IsRunning = true;
            SearchSection.IsVisible = false;
        });

        try
        {
            // Fetch next order by user and sequence
            var result = await Task.Run(async () =>
            {
                try
                {
                    string url = $"{AppConfig.ApiBaseUrl}GetSalesOrderByUserAndSequence/{pickerUsername}";
                    var response = await _httpClient.GetAsync(url);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return (success: false, notFound: true, response: (SalesOrderResponse)null, error: (string)null);
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        return (success: false, notFound: false, response: (SalesOrderResponse)null, error: response.ReasonPhrase);
                    }

                    var salesOrderResponse = await response.Content.ReadFromJsonAsync<SalesOrderResponse>();
                    return (success: true, notFound: false, response: salesOrderResponse, error: (string)null);
                }
                catch (Exception ex)
                {
                    return (success: false, notFound: false, response: (SalesOrderResponse)null, error: ex.Message);
                }
            });

            if (result.notFound)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("No Orders Assigned",
                        "You don't have any orders assigned to you. You can search for orders manually.", "OK");
                    ShowSearchSection();
                });
                return;
            }

            if (!result.success || result.response == null)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Error",
                        $"Could not load next order: {result.error ?? "Unknown error"}", "OK");
                    ShowSearchSection();
                });
                return;
            }

            // Validate picker matches logged-in user
            if (!string.IsNullOrWhiteSpace(result.response.Picker) &&
                !result.response.Picker.Equals(pickerUsername, StringComparison.OrdinalIgnoreCase))
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Validation Error",
                        $"Order is assigned to a different picker ({result.response.Picker}).", "OK");
                    ShowSearchSection();
                });
                return;
            }

            _currentSoResponse = result.response;
            await ProcessLoadedOrderAsync();
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Error", $"Failed to load next order: {ex.Message}", "OK");
                ShowSearchSection();
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;
            });
        }
    }

    private void ShowSearchSection()
    {
        SearchSection.IsVisible = true;
        //nextOrderLabel.IsVisible = false;
        sequenceLabel.IsVisible = false;
        soHeaderFrame.IsVisible = false;
        soLinesView.IsVisible = false;
        LoadSOButton.IsVisible = false;
    }

    private async Task ProcessLoadedOrderAsync()
    {
        if (_currentSoResponse == null || _currentSoResponse.Lines == null || !_currentSoResponse.Lines.Any())
        {
            ShowSearchSection();
            return;
        }

        string soNumber = _currentSoResponse.Reference;
        if (string.IsNullOrEmpty(soNumber))
        {
            ShowSearchSection();
            return;
        }

        // Update UI to show next order info
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            SearchSection.IsVisible = false;
            //nextOrderLabel.IsVisible = true;
            sequenceLabel.IsVisible = true;
            sequenceLabel.Text = _currentSoResponse.Sequence.HasValue
                ? $"Next Sequence: {_currentSoResponse.Sequence.Value}"
                : "Sequence: N/A";
        });

        // Check if order already exists locally
        var existingSo = await App.Db.GetSoHeaderByOrderNoAsync(soNumber);

        if (existingSo != null)
        {
            // Merge fresh data with existing
            await App.Db.MergeSoDataAsync(_currentSoResponse);

            // Display the existing order (merge helper will show it)
            await SoMergeHelper.HandleSoFetchAndMergeAsync(soNumber, _currentSoResponse,
                customerLabel, dueDateLabel, soHeaderFrame, soLinesView, LoadSOButton, "Picking");
            return;
        }

        // New order - use merge helper to display
        await SoMergeHelper.HandleSoFetchAndMergeAsync(soNumber, _currentSoResponse,
            customerLabel, dueDateLabel, soHeaderFrame, soLinesView, LoadSOButton, "Picking");
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await LoadNextOrderAsync();
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

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            loadingIndicator.IsVisible = true;
            loadingIndicator.IsRunning = true;
            //nextOrderLabel.IsVisible = false;
            sequenceLabel.IsVisible = false;
        });

        // Clear session values related to header
        PickingWorkflowSession.Clear();

        try
        {
            // Run heavy operations on background thread
            var result = await Task.Run(async () =>
            {
                // Fetch from API
                string url = $"{AppConfig.ApiBaseUrl}GetSalesOrder/{Uri.EscapeDataString($"IO{soNumber}")}";
                var response = await _httpClient.GetFromJsonAsync<SalesOrderResponse>(url);
                return response;
            });

            _currentSoResponse = result;

            if (_currentSoResponse == null || _currentSoResponse.Lines == null || !_currentSoResponse.Lines.Any())
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Not Found", $"SO {soNumber} not found on server.", "OK");
                });
                return;
            }

            // Use merge helper for normal operation
            await SoMergeHelper.HandleSoFetchAndMergeAsync(soNumber, _currentSoResponse,
                customerLabel, dueDateLabel, soHeaderFrame, soLinesView, LoadSOButton, "Picking");
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Error", $"Could not load SO: {soNumber} : Message:- {ex.Message}", "OK");
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;
            });
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

            // Run database operations on background thread
            var result = await Task.Run(async () =>
            {
                var existingSo = await App.Db.GetSoHeaderByOrderNoAsync(soNumber);

                if (existingSo != null)
                {
                    return new { hasExistingSo = true, existingSo, savedSo = (SoHeader)null };
                }

                await SaveToLocalDatabaseAsync(_currentSoResponse);
                var savedSo = await App.Db.GetSoHeaderByOrderNoAsync(soNumber);

                return new { hasExistingSo = false, existingSo = (SoHeader)null, savedSo };
            });

            // Handle existing SO case
            if (result.hasExistingSo)
            {
                PickingWorkflowSession.CurrentSoHeader = result.existingSo;
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Shell.Current.GoToAsync(nameof(PickingDocumentsPage));
                });
                return;
            }

            // Handle new SO case
            PickingWorkflowSession.CurrentSoHeader = result.savedSo;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync(nameof(PickingDocumentsPage));
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Error", $"Failed to load SO: {ex.Message}", "OK");
            });
        }
    }

    private async Task SaveToLocalDatabaseAsync(SalesOrderResponse response)
    {
        if (response == null || response.Lines == null || !response.Lines.Any())
            throw new ArgumentException("Invalid sales order data.");

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
            Picker = response.Picker,
            Sequence = response.Sequence.HasValue ? (double?)response.Sequence.Value : null,
            JsonData = System.Text.Json.JsonSerializer.Serialize(response)
        };

        // Insert or update the SoHeader
        await App.Db.InsertAsync(soHeader);

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
            await App.Db.InsertAsync(line);
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

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert("Log Out", "Are you sure you want to log out?", "Yes", "No");
        if (!confirm) return;
        _userSession.CurrentUser = null;
        await Navigation.PopToRootAsync();
    }
}