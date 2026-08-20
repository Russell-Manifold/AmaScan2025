using System.Collections.ObjectModel;
using System.Text.Json;
using AmaScan.Classes;
using AmaScan.sqliteModels;
using Data.Model;

namespace AmaScan;
public partial class TransferMainPage : ContentPage
{
    private readonly HttpClient _httpClient;
    private readonly ObservableCollection<WHtrfRequestHeader> _transferHeaders = new();

    public TransferMainPage()
    {
        InitializeComponent();
        _httpClient = AppConfig.GetHttpClient();
        TransfersCollectionView.ItemsSource = _transferHeaders;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadTransferRequestsAsync();
    }

    private async Task LoadTransferRequestsAsync()
    {
        try
        {
            var stockItems = await App.Db.GetAllStockItemsAsync();  // your helper
            var barcodeDict = stockItems.ToDictionary(s => s.stock_code, s => s.bar_code ?? string.Empty);

            string url = $"{AppConfig.ApiBaseUrl}OutstandingTrfRequests";
            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            var wrapper = JsonSerializer.Deserialize<WHtrfResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _transferHeaders.Clear();

            if (wrapper?.Value != null)
            {
                var groupedHeaders = wrapper.Value
                    .GroupBy(item => new
                    {
                        item.requisition_number,
                        item.due_date_and_time,
                        item.source_warehouse,
                        item.source_warehouse_description,
                        item.destination_warehouse,
                        item.destination_warehouse_description
                    })
                    .Select(g => new WHtrfRequestHeader
                    {
                        requisition_number = g.Key.requisition_number,
                        due_date_and_time = g.Key.due_date_and_time,
                        source_warehouse = g.Key.source_warehouse,
                        source_warehouse_description = g.Key.source_warehouse_description,
                        destination_warehouse = g.Key.destination_warehouse,
                        destination_warehouse_description = g.Key.destination_warehouse_description,
                        lines = g.Select(line => new WHtrfRequestLine
                        {
                            stock_code = line.stock_code,
                            stock_description = line.stock_description,
                            outstanding_qty_to_deliver = line.outstanding_qty_to_deliver,
                            bar_code = barcodeDict.TryGetValue(line.stock_code, out var bc) ? bc : string.Empty
                        }).ToList()
                    });

                foreach (var header in groupedHeaders)
                    _transferHeaders.Add(header);
            }
            else
            {
                await DisplayAlert("No Data", "No transfer requests found.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load transfers: {ex.Message}", "OK");
        }
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert("Log Out", "Are you sure you want to log out?", "Yes", "No");
        if (!confirm) return;
        App.Services.GetRequiredService<UserSession>().CurrentUser = null;
        await Navigation.PopToRootAsync();
    }

    private async void Frame_Tapped(object sender, EventArgs e)
    {
        if (sender is Frame frame && frame.BindingContext is WHtrfRequestHeader selectedHeader)
        {
            TransferState.CurrentTransferHeader = selectedHeader;
            await Shell.Current.GoToAsync(nameof(TransferDetailPage));
        }
    }
    public static class TransferState
    {
        public static WHtrfRequestHeader CurrentTransferHeader;
    }
}
