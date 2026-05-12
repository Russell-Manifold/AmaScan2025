using AmaScan.Classes;
using AmaScan.Data;
using AmaScan.sqliteModels;
using Data.Model;
using Microsoft.Maui.Dispatching;
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

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            loadingIndicator.IsVisible = true;
            loadingIndicator.IsRunning = true;
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

            // Use merge helper for normal operation with proper threading
            await SoMergeHelper.HandleSoFetchAndMergeAsync(soNumber, _currentSoResponse, customerLabel, dueDateLabel, soHeaderFrame, soLinesView, LoadSOButton, "Packing");
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

            // Show loading indicator
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                loadingIndicator.IsVisible = true;
                loadingIndicator.IsRunning = true;
            });

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
                bool goToPacking = await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    return await DisplayAlert("Resume Packing?",
                        "This SO is already loaded. Would you like to resume packing?", "Yes", "No");
                });

                if (goToPacking)
                {
                    PickingWorkflowSession.CurrentSoHeader = result.existingSo;
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await Shell.Current.GoToAsync(nameof(PackingDocumentsPage));
                    });
                    return;
                }
                else
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await DisplayAlert("Cancelled", "You chose not to resume packing.", "OK");
                    });
                    return;
                }
            }

            // Handle new SO case
            PickingWorkflowSession.CurrentSoHeader = result.savedSo;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Success", "SO has been loaded for offline packing.", "OK");
            });

            bool startPacking = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                return await DisplayAlert("Start Packing?",
                    "Would you like to start packing this SO now?", "Yes", "No");
            });

            if (startPacking)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Shell.Current.GoToAsync(nameof(PackingDocumentsPage));
                });
            }
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Error", $"Failed to load SO: {ex.Message}", "OK");
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

    private async Task SaveToLocalDatabaseAsync(SalesOrderResponse response)
    {
        if (response == null || response.Lines == null || !response.Lines.Any())
            throw new ArgumentException("Invalid sales order data.");

            string soNumber = response.Reference; // Use Reference as the SO number
            if (string.IsNullOrEmpty(soNumber)) throw new ArgumentException("Invalid SO number.");

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
        await App.Db.InsertAsync(soHeader);

        // Batch create lines for better performance
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

        // Batch insert lines for better performance
        await Task.Run(async () =>
        {
            foreach (var line in soLines)
            {
                await App.Db.InsertAsync(line);
            }
        });
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
        App.Services.GetRequiredService<UserSession>().CurrentUser = null;
        await Navigation.PopToRootAsync();
    }
}