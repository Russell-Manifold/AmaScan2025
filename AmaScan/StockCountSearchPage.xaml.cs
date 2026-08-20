using AmaScan.Classes;
using AmaScan.sqliteModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace AmaScan;

[QueryProperty(nameof(BatchNoQuery), "batchNo")] // Move the attribute to the class declaration
public partial class StockCountSearchPage : ContentPage, INotifyPropertyChanged
{
    private string _batchNoQuery;
    private ObservableCollection<StockCountItem> _items = new();
    private bool _isLoading;

    public string BatchNoQuery
    {
        get => _batchNoQuery;
        set
        {
            if (_batchNoQuery != value)
            {
                _batchNoQuery = Uri.UnescapeDataString(value);
                OnPropertyChanged();
                // Trigger loading of items when the batchNoQuery is set
                _ = LoadItemsAsync();
            }
        }
    }

    public ObservableCollection<StockCountItem> Items
    {
        get => _items;
        set { _items = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public StockCountSearchPage(DatabaseHelper databaseHelper)
    {
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        //await LoadItemsAsync();
    }

    private async Task LoadItemsAsync()
    {
        if (string.IsNullOrWhiteSpace(_batchNoQuery)) return;

        IsLoading = true;
        try
        {
            var list = await App.Db.GetStockCountItemsByBatchAsync(_batchNoQuery);
            //Debug.WriteLine($"Loaded {list.Count} items for batch {_batchNoQuery}");
            Items = new ObservableCollection<StockCountItem>(list);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Error loading items: " + ex.Message);
            await DisplayAlert("Error", $"Failed to load items: {ex.Message}", "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void OnItemTapped(object sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is StockCountItem item)
            await NavigateToCountAsync(item);
    }

    private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is StockCountItem item)
            await NavigateToCountAsync(item);
    }

    private async Task NavigateToCountAsync(StockCountItem item)
    {
        string code = Uri.EscapeDataString(item.StockCode);
        await Shell.Current.GoToAsync($"{nameof(StockCountPage)}?stockCode={code}");
    }
}
