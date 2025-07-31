using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AmaScan.Classes;
using AmaScan.Data;
using AmaScan.sqliteModels;
using SQLite;

namespace AmaScan;
public partial class PickingDocumentsPage : ContentPage, INotifyPropertyChanged
{
    private ObservableCollection<SoLine> _soLines;
    private bool _isLoading;
    private SoHeader _soHeader;
    private DatabaseHelper _dbHelper;

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

    public PickingDocumentsPage(DatabaseHelper databaseHelper)
    {
        InitializeComponent();
        BindingContext = this;
        _soLines = new ObservableCollection<SoLine>();
        _dbHelper = databaseHelper;
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
            IsLoading = true;

            // Clear the collection first
            _soLines.Clear();

            // Get the current SO header from session
            _soHeader = PickingWorkflowSession.CurrentSoHeader;
            if (_soHeader == null)
            {
                await DisplayAlert("Error", "No SO selected. Please go back and select an SO.", "OK");
                await Navigation.PopAsync();
                return;
            }

            // Update UI bindings for SO header
            OnPropertyChanged(nameof(SoNumber));
            OnPropertyChanged(nameof(CustomerName));
            OnPropertyChanged(nameof(DueDate));

            var databaseHelper = AmaScanDatabase.GetDatabaseHelper();

            // Load SO lines for the current SO using Reference (which contains the SO number)
            var lines = await databaseHelper.GetSoLinesByOrderNoAsync(_soHeader.Reference);

            // Clear and reload the collection
            _soLines.Clear();
            foreach (var line in lines)
            {
                _soLines.Add(line);
            }

            // Notify that SoLines collection has changed
            OnPropertyChanged(nameof(SoLines));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load SO lines: {ex.Message}", "OK");
        }
        finally
        {
            IsLoading = false;
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
            // Check if picking has been started by a different user at header level
            var userSession = App.Services.GetRequiredService<UserSession>();
            string currentUserName = userSession.CurrentUser?.UserName ?? "Unknown User";

            // Check if any user has started picking for this SO
            if (await _dbHelper.HasAnyUserStartedPhaseAsync(_soHeader.Reference, "picking"))
            {
                // Check if the current user is the one who started picking
                if (!await _dbHelper.HasUserStartedPhaseAsync(_soHeader.Reference, currentUserName, "picking"))
                {
                    await DisplayAlert("Access Denied",
                        $"Picking for {_soHeader.Reference} was started by another user.\n\n" +
                        "You are not authorized to pick this order.", "OK");
                    return;
                }
            }

            // Check if packing has started
            if (soLine.PackStarted)
            {
                await DisplayAlert("Picking Unavailable",
                    $"{soLine.ItemDesc} has already been started for packing.\n\n" +
                    "Picking cannot be modified once packing has started. Please complete packing first.", "OK");
                return;
            }

            // Set the selected SO line in the session
            PickingWorkflowSession.CurrentSoLine = soLine;
            // Navigate to the picking page
            await Shell.Current.GoToAsync(nameof(PickingPage));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to navigate to picking: {ex.Message}", "OK");
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}