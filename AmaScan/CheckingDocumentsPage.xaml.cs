using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AmaScan.Classes;
using AmaScan.Data;
using AmaScan.sqliteModels;
using SQLite;

namespace AmaScan;
public partial class CheckingDocumentsPage : ContentPage, INotifyPropertyChanged
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

    public CheckingDocumentsPage(DatabaseHelper databaseHelper)
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

            // Load all SO lines for the current SO
            var allLines = await App.Db.GetSoLinesByOrderNoAsync(_soHeader.Reference);

            // Unchecked lines first (top), completed lines last (bottom);
            // keep a stable order within each group by line number.
            var orderedLines = allLines
                .OrderBy(l => l.Checked)
                .ThenBy(l => l.SoLLineNo)
                .ToList();

            // Clear and reload the collection with all items
            _soLines.Clear();
            foreach (var line in orderedLines)
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
            // Check if checking has been started by a different user at header level
            var userSession = App.Services.GetRequiredService<UserSession>();
            string currentUserName = userSession.CurrentUser?.UserName ?? "Unknown User";

            // Check if any user has started checking for this SO
            if (await App.Db.HasAnyUserStartedPhaseAsync(_soHeader.Reference, "checking"))
            {
                // Check if the current user is the one who started checking
                if (!await App.Db.HasUserStartedPhaseAsync(_soHeader.Reference, currentUserName, "checking"))
                {
                    await DisplayAlert("Access Denied",
                        $"Checking for {_soHeader.Reference} was started by another user.\n\n" +
                        "You are not authorized to check this order.", "OK");
                    return;
                }
            }

            // The stage immediately before checking must be complete for this line. Which stage
            // that is depends on the company's workflow (WorkflowConfig): packing if it runs,
            // otherwise picking, otherwise nothing — Check-only lets checking start straight away.
            if (WorkflowConfig.UsePacking && !soLine.Packed)
            {
                await DisplayAlert("Checking Unavailable",
                    $"{soLine.ItemDesc} has not been packed yet.\n\n" +
                    "Checking is unavailable until packing is completed.", "OK");
                return;
            }

            if (!WorkflowConfig.UsePacking && WorkflowConfig.UsePicking && !soLine.Picked)
            {
                await DisplayAlert("Checking Unavailable",
                    $"{soLine.ItemDesc} has not been picked yet.\n\n" +
                    "Checking is unavailable until picking is completed.", "OK");
                return;
            }

            // TEMP (2026-05-21): Authorization phase moved out of this app for the current
            // milestone. Restore this gate if/when authorization returns.
            //// Check if authorization has started
            //if (soLine.AuthStarted)
            //{
            //    await DisplayAlert("Checking Unavailable",
            //        $"{soLine.ItemDesc} has already been started for authorization.\n\n" +
            //        "Checking cannot be modified once authorization has started. Please complete authorization first.", "OK");
            //    return;
            //}

            // Set the selected SO line in the session
            PickingWorkflowSession.CurrentSoLine = soLine;
            // Navigate to the checking page
            await Shell.Current.GoToAsync(nameof(CheckingPage));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to navigate to checking: {ex.Message}", "OK");
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}