using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AmaScan.Classes;
using AmaScan.Data;
using AmaScan.sqliteModels;
using SQLite;

namespace AmaScan;
public partial class PackingDocumentsPage : ContentPage, INotifyPropertyChanged
{
    private ObservableCollection<SoLine> _soLines;
    private bool _isLoading;
    private SoHeader _soHeader;
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

    public ObservableCollection<SoLine> SoLines
    {
        get => _soLines;
        set
        {
            if (_soLines != value)
            {
                _soLines = value;
                OnPropertyChanged();
            }
        }
    }

    // SO Header properties for display
    public string SoNumber => _soHeader?.Reference ?? "";
    public string CustomerName => _soHeader?.CustomerName ?? "";
    public DateTime DueDate => _soHeader?.DueDate ?? DateTime.Now;

    public PackingDocumentsPage(DatabaseHelper databaseHelper)
    {
        InitializeComponent();
        BindingContext = this;
        _soLines = new ObservableCollection<SoLine>();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadSoLines();
    }

    private async void LoadSoLines()
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsLoading = true;
                _soLines.Clear();
            });

            // Get the current SO header from session
            _soHeader = PickingWorkflowSession.CurrentSoHeader;
            if (_soHeader == null)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Error", "No SO selected. Please go back and select an SO.", "OK");
                    await Navigation.PopAsync();
                });
                return;
            }

            // Update UI bindings for SO header on main thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                OnPropertyChanged(nameof(SoNumber));
                OnPropertyChanged(nameof(CustomerName));
                OnPropertyChanged(nameof(DueDate));
            });

            // Run database operations on background thread
            var allLines = await Task.Run(async () =>
            {
                var databaseHelper = AmaScanDatabase.GetDatabaseHelper();
                return await databaseHelper.GetSoLinesByOrderNoAsync(_soHeader.Reference);
            });

            // Update UI on main thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Clear and reload the collection with all items
                _soLines.Clear();
                foreach (var line in allLines)
                {
                    _soLines.Add(line);
                }

                // Notify that SoLines collection has changed
                OnPropertyChanged(nameof(SoLines));
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Error", $"Failed to load SO lines: {ex.Message}", "OK");
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

    private async void OnSoLineSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is SoLine selectedLine)
        {
            await SelectSoLineAndNavigate(selectedLine);
        }
    }

    private async void OnItemTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is SoLine soLine)
        {
            await SelectSoLineAndNavigate(soLine);
        }
    }

    private async Task SelectSoLineAndNavigate(SoLine soLine)
    {
        try
        {
            // Check if packing has been started by a different user at header level
            var userSession = App.Services.GetRequiredService<UserSession>();
            string currentUserName = userSession.CurrentUser?.UserName ?? "Unknown User";

            // Check if any user has started packing for this SO
            if (await App.Db.HasAnyUserStartedPhaseAsync(_soHeader.Reference, "packing"))
            {
                // Check if the current user is the one who started packing
                if (!await App.Db.HasUserStartedPhaseAsync(_soHeader.Reference, currentUserName, "packing"))
                {
                    await DisplayAlert("Access Denied",
                        $"Packing for {_soHeader.Reference} was started by another user.\n\n" +
                        "You are not authorized to pack this order.", "OK");
                    return;
                }
            }

            // Check if the item has been picked
            if (!soLine.Picked)
            {
                await DisplayAlert("Packing Unavailable",
                    $"{soLine.ItemDesc} has not been picked yet.\n\n" +
                    "Packing is unavailable until picking is completed.", "OK");
                return;
            }

            // Check if checking has started
            if (soLine.CheckStarted)
            {
                await DisplayAlert("Packing Unavailable",
                    $"{soLine.ItemDesc} has already been started for checking.\n\n" +
                    "Packing cannot be modified once checking has started. Please complete checking first.", "OK");
                return;
            }

            // Set the selected SO line in the session
            PickingWorkflowSession.CurrentSoLine = soLine;
            // Navigate to the packing page
            await Shell.Current.GoToAsync(nameof(PackingPage));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to navigate to packing: {ex.Message}", "OK");
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}