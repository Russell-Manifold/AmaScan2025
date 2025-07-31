using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AmaScan.Classes;
using AmaScan.Data;
using AmaScan.sqliteModels;
using SQLite;

namespace AmaScan;
public partial class AuthorizationDocumentsPage : ContentPage, INotifyPropertyChanged
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

    public AuthorizationDocumentsPage(DatabaseHelper databaseHelper)
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

            // Load all SO lines for the current SO
            var allLines = await databaseHelper.GetSoLinesByOrderNoAsync(_soHeader.Reference);

            // Clear and reload the collection with all items
            _soLines.Clear();
            foreach (var line in allLines)
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
            // Check if authorization has been started by a different user at header level
            var userSession = App.Services.GetRequiredService<UserSession>();
            string currentUserName = userSession.CurrentUser?.UserName ?? "Unknown User";

            // Check if any user has started authorization for this SO
            if (await _dbHelper.HasAnyUserStartedPhaseAsync(_soHeader.Reference, "authorization"))
            {
                // Check if the current user is the one who started authorization
                if (!await _dbHelper.HasUserStartedPhaseAsync(_soHeader.Reference, currentUserName, "authorization"))
                {
                    await DisplayAlert("Access Denied",
                        $"Authorization for {_soHeader.Reference} was started by another user.\n\n" +
                        "You are not authorized to authorize this order.", "OK");
                    return;
                }
            }

            // Check if authorization prerequisites are met (all previous phases must be complete)
            if (!await _dbHelper.CanAuthorizeOrderAsync(_soHeader.Reference))
            {
                await DisplayAlert("Authorization Unavailable",
                    "Authorization is unavailable until all previous phases (picking, packing, checking) are completed for all lines.\n\n" +
                    "Please ensure all items have been picked, packed, and checked before attempting authorization.", "OK");
                return;
            }

            // Set the selected SO line in the session
            PickingWorkflowSession.CurrentSoLine = soLine;
            // Navigate to the authorization page
            await Shell.Current.GoToAsync(nameof(AuthorizationPage));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to navigate to authorization: {ex.Message}", "OK");
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}