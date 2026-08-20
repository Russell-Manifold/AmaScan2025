using AmaScan.Classes;
using AmaScan.Models;
using AmaScan.sqliteModels;
using AndroidX.Lifecycle;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AmaScan;

public partial class StockCountMain : ContentPage, INotifyPropertyChanged
{
    private readonly HttpClient _httpClient;

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }
    private bool _isLoading;

    public StockCountMain(DatabaseHelper databaseHelper)
    {
        InitializeComponent();
        BindingContext = this;
        _httpClient = AppConfig.GetHttpClient();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ReferenceEntry.Focus();
    }

    /*  ENTRY HANDLERS  */
    private async void OnReferenceCompleted(object sender, EventArgs e)
        => await ConfirmAndUploadAsync(ReferenceEntry.Text.ToUpper()?.Trim());

    private async void OnLoadClicked(object sender, EventArgs e)
        => await ConfirmAndUploadAsync(ReferenceEntry.Text.ToUpper()?.Trim());

    /*  NEW FLOW  */
    private async Task ConfirmAndUploadAsync(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            await DisplayAlert("Validation", "Please enter a stock-take reference.", "OK");
            return;
        }
        IsLoading = true;
        try
        {
            // 1.  call your existing upload logic
            bool ok = await OnUploadDataClicked(reference);
            if (!ok) return;  
            // 2.  navigate to counting screen
            await SelectStockCountSessionAndNavigate(reference);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /*  UPLOAD  */
    private async Task<bool> OnUploadDataClicked(string batchNo)
    {
        // 1.  pull latest lines from server
        var lines = await LoadStockCountDataFromApiAsync(batchNo);
        if (lines == null || !lines.Any())
        {
            await DisplayAlert("No Data", $"No items found for {batchNo}", "OK");
            return false;
        }

        // 2.  store them locally (optional)
        await App.Db.SaveStockCountItemsAsync(lines);
        // 3.  user feedback
        await DisplayAlert("Success", $"{lines.Count} Lines uploaded for {batchNo}", "OK");
        return true;
    }

    /*  NAVIGATION  */
    private async Task SelectStockCountSessionAndNavigate(string batchNo)
    {
        string encoded = Uri.EscapeDataString(batchNo);
        await Shell.Current.GoToAsync($"{nameof(StockCountSearchPage)}?batchNo={encoded}");
    }

    private async Task<List<StockCountItem>> LoadStockCountDataFromApiAsync(string batchNo)
    {
        try
        {
            string url = $"{AppConfig.ApiBaseUrl}GetStockCountItems/{Uri.EscapeDataString(batchNo)}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<StockCountItem>>(json) ?? new List<StockCountItem>();
            }

            Console.WriteLine($"API call failed with status: {response.StatusCode}");
            return new List<StockCountItem>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calling stock count API: {ex.Message}");
            return new List<StockCountItem>();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}