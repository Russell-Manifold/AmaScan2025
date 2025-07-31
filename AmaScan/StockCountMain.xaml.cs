using AmaScan.Classes;
using AmaScan.sqliteModels;
using AmaScan.Models;
using SQLite;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using System.Linq;
using Microsoft.Maui.Dispatching;

namespace AmaScan;

public partial class StockCountMain : ContentPage, INotifyPropertyChanged
{
    private readonly DatabaseHelper _databaseHelper;
    private readonly HttpClient _httpClient;
    private ObservableCollection<StockCountSession> _stockCountSessions;
    private bool _isLoading;

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<StockCountSession> StockCountSessions
    {
        get => _stockCountSessions;
        set
        {
            if (_stockCountSessions != value)
            {
                _stockCountSessions = value;
                OnPropertyChanged();
            }
        }
    }

    public StockCountMain(DatabaseHelper databaseHelper)
    {
        InitializeComponent();
        BindingContext = this;
        _databaseHelper = databaseHelper;
        _httpClient = AppConfig.GetHttpClient();
        _stockCountSessions = new ObservableCollection<StockCountSession>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadStockCountSessionsAsync();
    }

    private async Task LoadStockCountSessionsAsync()
    {
        try
        {
            // TODO: When API endpoint for all stock count batches is created, implement proper data loading
            // Example API call: GET /api/GetStockCountBatches
            // This should return all available stock count batches with their progress
            // Then we can replace the dummy data with real data from the API
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsLoading = true;
                StockCountSessions.Clear();
            });

            // Create dummy sessions for now
            var sessions = CreateDummySessions();
            
            // Run API operations on background thread
            var result = await Task.Run(async () =>
            {
                // For QWERTY session, try to load real data from API
                var qwertySession = sessions.FirstOrDefault(s => s.BatchNo == "QWERTY");
                if (qwertySession != null)
                {
                    try
                    {
                        var stockCountItems = await LoadStockCountDataFromApiAsync("QWERTY");
                        if (stockCountItems != null && stockCountItems.Any())
                        {
                            // Save the API data to local database
                            await _databaseHelper.SaveStockCountItemsAsync(stockCountItems);
                            
                            // Update the session with real counts
                            qwertySession.TotalItems = stockCountItems.Count;
                            qwertySession.CompletedItems = stockCountItems.Count(item => item.CountComplete);
                        }
                    }
                    catch (Exception ex)
                    {
                        // If API call fails, keep the dummy data
                        Console.WriteLine($"Failed to load QWERTY data from API: {ex.Message}");
                    }
                }
                
                return sessions;
            });
            
            // Update UI on main thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                foreach (var session in result)
                {
                    StockCountSessions.Add(session);
                }
                
                // Notify that StockCountSessions collection has changed
                OnPropertyChanged(nameof(StockCountSessions));
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Error", $"Failed to load stock count sessions: {ex.Message}", "OK");
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsLoading = false;
            });
        }
    }

    private async Task<List<StockCountItem>> LoadStockCountDataFromApiAsync(string batchNo)
    {
        try
        {
            // Construct the API endpoint URL
            string url = $"{AppConfig.ApiBaseUrl}GetStockCountItems/{Uri.EscapeDataString(batchNo)}";
            
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var stockCountItems = JsonConvert.DeserializeObject<List<StockCountItem>>(json);
                return stockCountItems ?? new List<StockCountItem>();
            }
            else
            {
                Console.WriteLine($"API call failed with status: {response.StatusCode}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calling stock count API: {ex.Message}");
            return null;
        }
    }

    private List<StockCountSession> CreateDummySessions()
    {
        return new List<StockCountSession>
        {
            new StockCountSession 
            { 
                BatchNo = "QWERTY", 
                TotalItems = 4, 
                CompletedItems = 2 
            },
            new StockCountSession 
            { 
                BatchNo = "ABC123", 
                TotalItems = 6, 
                CompletedItems = 0 
            },
            new StockCountSession 
            { 
                BatchNo = "XYZ789", 
                TotalItems = 3, 
                CompletedItems = 1 
            }
        };
    }





    private async void OnSessionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is StockCountSession selectedSession)
        {
            await SelectStockCountSessionAndNavigate(selectedSession);
        }
    }

    private async void OnSessionTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is StockCountSession stockCountSession)
        {
            await SelectStockCountSessionAndNavigate(stockCountSession);
        }
    }

            private async Task SelectStockCountSessionAndNavigate(StockCountSession stockCountSession)
        {
            try
            {
                // Navigate directly to search page with the batch number
                string batchNo = Uri.EscapeDataString(stockCountSession.BatchNo);
                await Shell.Current.GoToAsync($"{nameof(StockCountSearchPage)}?batchNo={batchNo}");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to navigate to stock count: {ex.Message}", "OK");
            }
        }

    private async void OnUploadDataClicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is StockCountSession session)
        {
            try
            {
                bool confirm = await DisplayAlert(
                    "Upload Data", 
                    $"Upload completed data for '{session.BatchNo}' to server?", 
                    "Yes", "No"
                );

                if (confirm)
                {
                    // TODO: Implement actual upload logic
                    await DisplayAlert("Success", $"Data uploaded for {session.BatchNo}", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to upload data: {ex.Message}", "OK");
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
} 