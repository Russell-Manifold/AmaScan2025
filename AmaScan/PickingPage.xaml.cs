using AmaScan.sqliteModels;
using AmaScan.Classes;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Linq;
using SQLite;

namespace AmaScan;

public partial class PickingPage : ContentPage, INotifyPropertyChanged
{
    private readonly DatabaseHelper _dbHelper;
    private readonly UserSession _userSession;
    private SoHeader _soHeader;
    private ObservableCollection<SoLine> _soLines;
    private SoLine _selectedLine;

    public event PropertyChangedEventHandler PropertyChanged;

    #region Properties
    public string SoNumber => _soHeader?.Reference ?? "";
    public string PickerName => _soHeader?.Picker ?? "";
    public ObservableCollection<SoLine> SoLines => _soLines;

    public SoLine SelectedLine
    {
        get => _selectedLine;
        set
        {
            if (_selectedLine != value)
            {
                _selectedLine = value;
                OnPropertyChanged();
            }
        }
    }
    #endregion

    #region Constructor and Lifecycle
    public PickingPage()
    {
        InitializeComponent();
        BindingContext = this;
        _dbHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));
        _userSession = App.Services.GetRequiredService<UserSession>();
        _soLines = new ObservableCollection<SoLine>();

        _soHeader = PickingWorkflowSession.CurrentSoHeader;
        if (_soHeader == null)
        {
            Shell.Current.GoToAsync("..");
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _soHeader = PickingWorkflowSession.CurrentSoHeader;
        if (_soHeader == null)
        {
            Shell.Current.GoToAsync("..");
            return;
        }

        InitializeUI();
        LoadSelectedLine();
        LoadSoLinesAsync();
    }

    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    #endregion

    #region Initialization
    private void InitializeUI()
    {
        ManualInputSwitch.IsToggled = false;
        BarcodeEntry.IsReadOnly = true;

        OnPropertyChanged(nameof(SoNumber));
        OnPropertyChanged(nameof(PickerName));
    }

    private async void LoadSelectedLine()
    {
        if (PickingWorkflowSession.CurrentSoLine != null)
        {
            // Get the fresh line from database to ensure we have the latest data
            var freshLine = await _dbHelper.GetSoLineByBarcodeAsync(_soHeader.Reference, PickingWorkflowSession.CurrentSoLine.ItemBarcode);

            if (freshLine != null)
            {
                // Update the session with the fresh line
                PickingWorkflowSession.CurrentSoLine = freshLine;
                StartPickingForSelectedLine(freshLine, false);
            }
            else
            {
                await DisplayAlert("Error", "Selected line not found in database.", "OK");
                await Shell.Current.GoToAsync("..");
            }
        }
        else
        {
            SelectedLine = null;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("No Item Selected", "Please select an item to pick.", "OK");
                await Shell.Current.GoToAsync("..");
            });
        }
    }
    #endregion

    private string GetCurrentUserName() => _userSession.CurrentUser?.UserName ?? "Unknown User";

    #region UI Management
    private void StartPickingForSelectedLine(SoLine selectedLine, bool showInput = true)
    {
        SelectedLine = selectedLine;

        SelectedItemFrame.BindingContext = selectedLine;
        SelectedItemFrame.IsVisible = true;

        if (showInput)
        {
            PickingInputSection.IsVisible = true;
            StartPickingButton.IsVisible = false;
            BarcodeEntry.Text = selectedLine.ItemBarcode;
            QuantityEntry.Focus();
        }
        else
        {
            PickingInputSection.IsVisible = false;
            StartPickingButton.IsVisible = true;
            ClearInputFields();
        }

        UpdateInputState();
        OnPropertyChanged(nameof(SelectedLine));
    }

    private void UpdateInputState()
    {
        if (SelectedLine == null) return;

        bool isFullyPicked = SelectedLine.PickedQty >= SelectedLine.OrderedQty;

        BarcodeEntry.IsEnabled = !isFullyPicked;
        QuantityEntry.IsEnabled = !isFullyPicked;
        SaveButton.IsEnabled = !isFullyPicked;

        if (isFullyPicked)
        {
            SaveButton.Text = "Fully Picked";
            SaveButton.BackgroundColor = Colors.Gray;
            SaveButton.TextColor = Colors.White;
        }
        else
        {
            SaveButton.Text = "Save";
            SaveButton.BackgroundColor = Colors.Green;
            SaveButton.TextColor = Colors.White;
        }
    }

    private void ClearInputFields()
    {
        BarcodeEntry.Text = string.Empty;
        QuantityEntry.Text = string.Empty;
    }
    #endregion

    #region Data Loading
    private async Task LoadSoLinesAsync()
    {
        try
        {
            var lines = await _dbHelper.GetSoLinesByOrderNoAsync(_soHeader.Reference);
            _soLines.Clear();
            foreach (var line in lines)
            {
                _soLines.Add(line);
            }
            OnPropertyChanged(nameof(SoLines));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load SO lines: {ex.Message}", "OK");
        }
    }
    #endregion

    #region Event Handlers
    private async void OnStartPickingClicked(object sender, EventArgs e)
    {
        string currentUserName = GetCurrentUserName();

        // Set the header-level user for picking
        await _dbHelper.SetPhaseUserAsync(_soHeader.Reference, currentUserName, "picking");;

        PickingInputSection.IsVisible = true;
        StartPickingButton.IsVisible = false;
        ClearInputFields();
        BarcodeEntry.Focus();
    }

    private async void OnBarcodeEntered(object sender, EventArgs e)
    {
        try
        {
            var scannedBarcode = BarcodeEntry.Text?.Trim();
            if (string.IsNullOrEmpty(scannedBarcode))
            {
                await DisplayAlert("Error", "Please enter a barcode.", "OK");
                return;
            }

            var matchingLine = await _dbHelper.GetSoLineByBarcodeAsync(_soHeader.Reference, scannedBarcode);

            if (matchingLine == null)
            {
                await DisplayAlert("Error", $"Item with barcode '{scannedBarcode}' not found in this SO.", "OK");
                return;
            }

            if (!string.IsNullOrEmpty(QuantityEntry.Text))
            {
                await ProcessPickingItem(matchingLine);
            }
            else
            {
                QuantityEntry.Focus();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to process barcode: {ex.Message}", "OK");
        }
    }

    private async void OnSaveProgressClicked(object sender, EventArgs e) => await SaveProgressAsync();

    private async void OnSave_Click(object sender, EventArgs e)
    {
        try
        {
            LoadingOverlay.IsVisible = true;
            loadingIndicator.IsRunning = true;

            foreach (var line in _soLines)
            {
                await _dbHelper.UpdateSoLineAsync(line);
            }

            await _dbHelper.UpdateSoHeaderAsync(_soHeader);
            PickingWorkflowSession.CurrentSoHeader = _soHeader;

            await DisplayAlert("Success", "Progress saved successfully.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to save progress: {ex.Message}", "OK");
        }
        finally
        {
            LoadingOverlay.IsVisible = false;
            loadingIndicator.IsRunning = false;
        }
    }

    private async void OnRestartClicked(object sender, EventArgs e)
    {
        if (SelectedLine == null)
        {
            await DisplayAlert("No Item Selected", "Please select an item to reset.", "OK");
            return;
        }

        bool confirm = await DisplayAlert(
            "Reset Line",
            $"Reset {SelectedLine.ItemDesc}?\n\nThis will clear all quantities.",
            "Yes", "No"
        );

        if (!confirm) return;

        await ResetSelectedLine();
    }

    private async void OnCompleteClicked(object sender, EventArgs e)
    {
        try
        {
            if (SelectedLine == null)
            {
                await DisplayAlert("No Item Selected", "Please select an item to complete.", "OK");
                return;
            }

            if (!SelectedLine.Picked)
            {
                var currentLineOutstanding = SelectedLine.OutstandingPickingQty;
                await DisplayAlert("Incomplete",
                    $"The selected item must be picked before completing.\n\n" +
                    $"{currentLineOutstanding} items still need picking", "OK");
                return;
            }

            bool confirmed = await DisplayAlert("Confirm Completion", "Item picked. Proceed to complete picking?", "Yes", "No");
            if (!confirmed) return;

            // Check if all lines are picked
            if (await _dbHelper.AreAllLinesPickedAsync(_soHeader.Reference))
            {
                // All items are picked, prompt to complete the entire order
                bool completeOrder = await DisplayAlert("All Items Picked",
                    $"All items have been picked for SO {_soHeader.Reference}!\n\n" +
                    "Would you like to complete the entire picking phase now?", "Complete Order", "Continue Picking");

                if (completeOrder)
                {
                    // Complete the entire order
                    await CompletePickingPhase();
                }
                else
                {
                    // Go back to documents page to continue
                    await DisplayAlert("Line Complete",
                        $"Picking completed for {SelectedLine.ItemDesc}!\n\n" +
                        $"Returning to documents page to continue.", "OK");
                    await Shell.Current.GoToAsync("..");
                }
            }
            else
            {
                // Not all items picked, go back to documents page
                await DisplayAlert("Line Complete",
                    $"Picking completed for {SelectedLine.ItemDesc}!\n\n" +
                    $"Returning to documents page to select next item.", "OK");

                // Navigate back to documents page
                await Shell.Current.GoToAsync("..");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
        }
    }

    private async Task CompletePickingPhase()
    {
        try
        {
            var soLines = await _dbHelper.GetSoLinesByOrderNoAsync(_soHeader.Reference);
            bool success = await SendToApiForCompletionAsync(_soHeader.Reference, soLines);

            if (success)
            {
                // Set header flags to indicate picking is complete
                _soHeader.Picked = true;
                await _dbHelper.UpdateSoHeaderAsync(_soHeader);

                PickingWorkflowSession.Clear();
                await DisplayAlert("Picking Phase Complete",
                    $"Picking successfully completed for entire SO!\n\n" +
                    $"Next phase: Packing\n\n" +
                    $"Order: {_soHeader.Reference}", "OK");

                var dashboardPage = App.Services.GetRequiredService<Dashboard>();
                await Navigation.PushAsync(dashboardPage);
            }
            else
            {
                await DisplayAlert("Error", "Failed to complete picking. Please try again.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
        }
    }

    private async void OnCompleteHeaderClicked(object sender, EventArgs e)
    {
        try
        {
            // Check if all lines are picked
            if (!await _dbHelper.AreAllLinesPickedAsync(_soHeader.Reference))
            {
                await DisplayAlert("Incomplete",
                    "Not all items have been picked yet.\n\n" +
                    "All items must be picked before completing the header.", "OK");
                return;
            }

            bool confirmed = await DisplayAlert("Confirm Header Completion",
                "All items picked. Complete picking phase for entire SO?", "Yes", "No");
            if (!confirmed) return;

            await CompletePickingPhase();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
        }
    }

    private void OnManualInputToggled(object sender, ToggledEventArgs e)
    {
        BarcodeEntry.IsReadOnly = !e.Value;
        BarcodeEntry.Focus();
    }

    private void QuantityEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.NewTextValue)) return;

        if (!int.TryParse(e.NewTextValue, out int _))
        {
            ((Entry)sender).Text = e.OldTextValue;
        }
    }

    private async void QuantityEntry_Completed(object sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(BarcodeEntry.Text) && !string.IsNullOrEmpty(QuantityEntry.Text))
        {
            await SaveProgressAsync();
        }
    }
    #endregion

    #region Business Logic
    private async Task ProcessPickingItem(SoLine matchingLine)
    {
        if (!int.TryParse(QuantityEntry.Text, out int quantity) || quantity <= 0)
        {
            await DisplayAlert("Error", "Invalid quantity entered.", "OK");
            return;
        }

        // Check if the scanned barcode matches the currently selected line
        if (SelectedLine?.ItemBarcode != matchingLine.ItemBarcode)
        {
            await DisplayAlert("Wrong Item",
                $"You scanned {matchingLine.ItemDesc} but you are currently working on {SelectedLine?.ItemDesc}.\n\n" +
                "Please scan the correct item barcode.", "OK");
            return;
        }

        var lineInCollection = _soLines.FirstOrDefault(l => l.Id == matchingLine.Id);
        if (lineInCollection == null)
        {
            await DisplayAlert("Error", "Line not found in collection.", "OK");
            return;
        }

        if (lineInCollection.PickedQty >= lineInCollection.OrderedQty)
        {
            await DisplayAlert("Already Picked",
                $"{lineInCollection.ItemDesc} has already been fully picked ({lineInCollection.PickedQty}/{lineInCollection.OrderedQty}).\n\n" +
                "No further picking allowed.", "OK");
            return;
        }

        if ((lineInCollection.PickedQty + quantity) > lineInCollection.OrderedQty)
        {
            await DisplayAlert("Over-Picking Not Allowed",
                $"Cannot pick {quantity} more items. This would exceed the ordered quantity of {lineInCollection.OrderedQty}.\n\n" +
                $"Already picked: {lineInCollection.PickedQty}\n" +
                $"Remaining: {lineInCollection.OrderedQty - lineInCollection.PickedQty}", "OK");
            return;
        }

        await UpdatePickingQuantity(lineInCollection, quantity);
    }

    private async Task UpdatePickingQuantity(SoLine lineInCollection, int quantity)
    {
        lineInCollection.PickedQty += quantity;

        lineInCollection.Picked = lineInCollection.PickedQty == lineInCollection.OrderedQty;
        lineInCollection.PickCompleteDateTime = lineInCollection.Picked ? DateTime.Now : null;

        // Ensure PickedBy is set when completing the line
        if (string.IsNullOrEmpty(lineInCollection.PickedBy))
        {
            lineInCollection.PickedBy = GetCurrentUserName();
        }

        // Ensure PickStartDateTime is set if it's not already set
        if (lineInCollection.PickStartDateTime == null)
        {
            lineInCollection.PickStartDateTime = DateTime.Now;
        }

        await _dbHelper.UpdateSoLineAsync(lineInCollection);

        if (SelectedLine?.Id == lineInCollection.Id)
        {
            SelectedLine = lineInCollection;
            SelectedItemFrame.BindingContext = lineInCollection;
            OnPropertyChanged(nameof(SelectedLine));
            UpdateInputState();
        }

        ClearInputFields();
        BarcodeEntry.Focus();

        if (lineInCollection.Picked)
        {
            await DisplayAlert("Picking Complete",
                $"{lineInCollection.ItemDesc} has been fully picked ({lineInCollection.PickedQty}/{lineInCollection.OrderedQty})\n\n" +
                "Picking complete, needs packing", "OK");
        }
    }

    private async Task SaveProgressAsync()
    {
        try
        {
            var scannedBarcode = BarcodeEntry.Text?.Trim();
            if (string.IsNullOrEmpty(scannedBarcode))
            {
                await DisplayAlert("Error", "Please enter a barcode.", "OK");
                return;
            }

            var matchingLine = await _dbHelper.GetSoLineByBarcodeAsync(_soHeader.Reference, scannedBarcode);

            if (matchingLine == null)
            {
                await DisplayAlert("Error", $"Item with barcode '{scannedBarcode}' not found in this SO.", "OK");
                return;
            }

            await ProcessPickingItem(matchingLine);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to save progress: {ex.Message}", "OK");
        }
    }

    private async Task ResetSelectedLine()
    {
        // Reset the selected line (only picking-related fields)
        SelectedLine.PickedQty = 0;
        SelectedLine.Picked = false;
        SelectedLine.PickStartDateTime = null;
        SelectedLine.PickCompleteDateTime = null;
        SelectedLine.PickedBy = null;

        // Also update the line in the collection
        var lineInCollection = _soLines.FirstOrDefault(l => l.Id == SelectedLine.Id);
        if (lineInCollection != null)
        {
            lineInCollection.PickedQty = 0;
            lineInCollection.Picked = false;
            lineInCollection.PickStartDateTime = null;
            lineInCollection.PickCompleteDateTime = null;
            lineInCollection.PickedBy = null;
        }

        await _dbHelper.UpdateSoLineAsync(SelectedLine);

        // Reset the Picker field in the header (this field exists in the database)
        _soHeader.Picker = null;
        _soHeader.PickStarted = false;
        await _dbHelper.UpdateSoHeaderAsync(_soHeader);

        PickingInputSection.IsVisible = false;
        StartPickingButton.IsVisible = true;
        SelectedItemFrame.IsVisible = true;

        SelectedItemFrame.BindingContext = null;
        SelectedItemFrame.BindingContext = SelectedLine;
        OnPropertyChanged(nameof(SelectedLine));

        UpdateInputState();

        await DisplayAlert("Reset Complete", $"Successfully reset {SelectedLine.ItemDesc}", "OK");
    }

    private async Task<bool> SendToApiForCompletionAsync(string soNumber, List<SoLine> soLines)
    {
        try
        {
            // TODO: Implement API call when needed
            // var result = await ApiService.SubmitCompletedSoAsync(soNumber, soLines);
            // return result.IsSuccess;
            return true;
        }
        catch
        {
            return false;
        }
    }
    #endregion
}