using AmaScan.sqliteModels;
using AmaScan.Classes;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Linq;
using SQLite;

namespace AmaScan;

public partial class CheckingPage : ContentPage, INotifyPropertyChanged
{
    private readonly UserSession _userSession;
    private SoHeader _soHeader;
    private ObservableCollection<SoLine> _soLines;
    private SoLine _selectedLine;

    public event PropertyChangedEventHandler PropertyChanged;

    #region Properties
    public string SoNumber => _soHeader?.Reference ?? "";
    public string CheckerName => _selectedLine?.CheckedBy ?? "";
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
    public CheckingPage(DatabaseHelper databaseHelper)
    {
        InitializeComponent();
        BindingContext = this;
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
        OnPropertyChanged(nameof(CheckerName));
    }

    private async void LoadSelectedLine()
    {
        if (PickingWorkflowSession.CurrentSoLine != null)
        {
            // Get the fresh line from database to ensure we have the latest data
            var freshLine = await App.Db.GetSoLineByBarcodeAsync(_soHeader.Reference, PickingWorkflowSession.CurrentSoLine.ItemBarcode);

            if (freshLine != null)
            {
                // Update the session with the fresh line
                PickingWorkflowSession.CurrentSoLine = freshLine;
                StartCheckingForSelectedLine(freshLine, false);
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
                await DisplayAlert("No Item Selected", "Please select an item to check.", "OK");
                await Shell.Current.GoToAsync("..");
            });
        }
    }
    #endregion

    private string GetCurrentUserName() => _userSession.CurrentUser?.UserName ?? "Unknown User";

    #region UI Management
    private void StartCheckingForSelectedLine(SoLine selectedLine, bool showInput = true)
    {
        SelectedLine = selectedLine;

        SelectedItemFrame.BindingContext = selectedLine;
        SelectedItemFrame.IsVisible = true;

        if (showInput)
        {
            CheckingInputSection.IsVisible = true;
            StartCheckingButton.IsVisible = false;
            BarcodeEntry.Text = selectedLine.ItemBarcode;
            QuantityEntry.Focus();
        }
        else
        {
            CheckingInputSection.IsVisible = false;
            StartCheckingButton.IsVisible = true;
            ClearInputFields();
        }

        UpdateInputState();
        OnPropertyChanged(nameof(SelectedLine));
    }

    private void UpdateInputState()
    {
        if (SelectedLine == null) return;

        bool isFullyChecked = SelectedLine.CheckedQty >= SelectedLine.PickedQty;

        BarcodeEntry.IsEnabled = !isFullyChecked;
        QuantityEntry.IsEnabled = !isFullyChecked;
        SaveButton.IsEnabled = !isFullyChecked;

        if (isFullyChecked)
        {
            SaveButton.Text = "Fully Checked";
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
            var allLines = await App.Db.GetSoLinesByOrderNoAsync(_soHeader.Reference);

            _soLines.Clear();
            foreach (var line in allLines)
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
    private async void OnStartCheckingClicked(object sender, EventArgs e)
    {
        string currentUserName = GetCurrentUserName();

        // Set the header-level user for checking
        await App.Db.SetPhaseUserAsync(_soHeader.Reference, currentUserName, "checking");

        // Set the line-level flag
        if (SelectedLine != null)
        {
            SelectedLine.CheckStarted = true;
            await App.Db.UpdateSoLineAsync(SelectedLine);
        }

        CheckingInputSection.IsVisible = true;
        StartCheckingButton.IsVisible = false;
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

            var matchingLine = await App.Db.GetSoLineByBarcodeAsync(_soHeader.Reference, scannedBarcode);

            if (matchingLine == null)
            {
                await DisplayAlert("Error", $"Item with barcode '{scannedBarcode}' not found in this SO.", "OK");
                return;
            }

            // If quantity is already entered, process immediately
            if (!string.IsNullOrEmpty(QuantityEntry.Text))
            {
                await ProcessCheckingItem(matchingLine);
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
                await App.Db.UpdateSoLineAsync(line);
            }

            await App.Db.UpdateSoHeaderAsync(_soHeader);
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

            if (!SelectedLine.Checked)
            {
                var currentLineOutstanding = SelectedLine.CheckingOutstandingQty;
                await DisplayAlert("Incomplete",
                    $"The selected item must be checked before completing.\n\n" +
                    $"{currentLineOutstanding} items still need checking", "OK");
                return;
            }

            bool confirmed = await DisplayAlert("Confirm Completion", "Item checked. Proceed to complete checking?", "Yes", "No");
            if (!confirmed) return;

            // Check if all lines are checked
            if (await App.Db.AreAllLinesCheckedAsync(_soHeader.Reference))
            {
                // All items are checked, prompt to complete the entire order
                bool completeOrder = await DisplayAlert("All Items Checked",
                    $"All items have been checked for {_soHeader.Reference}!\n\n" +
                    "Would you like to complete the entire checking phase now?", "Complete Order", "Continue Checking");

                if (completeOrder)
                {
                    // Complete the entire order
                    await CompleteCheckingPhase();
                }
                else
                {
                    // Go back to documents page to continue
                    await DisplayAlert("Line Complete",
                        $"Checking completed for {SelectedLine.ItemDesc}!\n\n" +
                        $"Returning to documents page to continue.", "OK");
                    await Shell.Current.GoToAsync("..");
                }
            }
            else
            {
                // Not all items checked, go back to documents page
                await DisplayAlert("Line Complete",
                    $"Checking completed for {SelectedLine.ItemDesc}!\n\n" +
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

    private async Task CompleteCheckingPhase()
    {
        try
        {
            var soLines = await App.Db.GetSoLinesByOrderNoAsync(_soHeader.Reference);
            bool success = await SendToApiForCompletionAsync(_soHeader.Reference, soLines);

            if (success)
            {
                // Set header flags to indicate checking is complete
                _soHeader.Checked = true;
                await App.Db.UpdateSoHeaderAsync(_soHeader);

                PickingWorkflowSession.Clear();
                await DisplayAlert("Checking Phase Complete",
                    $"Checking successfully completed for entire SO!\n\n" +
                    $"Next phase: Authorization\n\n" +
                    $"Order: {_soHeader.Reference}", "OK");

                var dashboardPage = App.Services.GetRequiredService<Dashboard>();
                await Navigation.PushAsync(dashboardPage);
            }
            else
            {
                await DisplayAlert("Error", "Failed to complete checking. Please try again.", "OK");
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
            // Check if all lines are checked
            if (!await App.Db.AreAllLinesCheckedAsync(_soHeader.Reference))
            {
                await DisplayAlert("Incomplete",
                    "Not all items have been checked yet.\n\n" +
                    "All items must be checked before completing the header.", "OK");
                return;
            }

            bool confirmed = await DisplayAlert("Confirm Header Completion",
                "All items checked. Complete checking phase for entire SO?", "Yes", "No");
            if (!confirmed) return;

            await CompleteCheckingPhase();
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
    private async Task ProcessCheckingItem(SoLine matchingLine)
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

        if (lineInCollection.CheckedQty >= lineInCollection.PickedQty)
        {
            await DisplayAlert("Already Checked",
                $"{lineInCollection.ItemDesc} has already been fully checked ({lineInCollection.CheckedQty}/{lineInCollection.PickedQty}).\n\n" +
                "No further checking allowed.", "OK");
            return;
        }

        if ((lineInCollection.CheckedQty + quantity) > lineInCollection.PickedQty)
        {
            await DisplayAlert("Over-Checking Not Allowed",
                $"Cannot check {quantity} more items. This would exceed the packed quantity of {lineInCollection.PackedQty}.\n\n" +
                $"Already checked: {lineInCollection.CheckedQty}\n" +
                $"Remaining: {lineInCollection.PickedQty - lineInCollection.CheckedQty}", "OK");
            return;
        }

        await UpdateCheckingQuantity(lineInCollection, quantity);
    }

    private async Task UpdateCheckingQuantity(SoLine lineInCollection, int quantity)
    {
        lineInCollection.CheckedQty += quantity;

        lineInCollection.CheckCompleteDateTime = lineInCollection.CheckedQty == lineInCollection.PickedQty ? DateTime.Now : null;

        // Set Checked flag when quantities match exactly
        lineInCollection.Checked = lineInCollection.CheckedQty == lineInCollection.PickedQty;

        // Ensure CheckedBy is set when completing the line
        if (string.IsNullOrEmpty(lineInCollection.CheckedBy))
        {
            lineInCollection.CheckedBy = GetCurrentUserName();
        }

        // Ensure CheckStartDateTime is set if it's not already set
        if (lineInCollection.CheckStartDateTime == null)
        {
            lineInCollection.CheckStartDateTime = DateTime.Now;
        }

        // Set CheckStarted flag when checking begins (first quantity added)
        if (!lineInCollection.CheckStarted)
        {
            lineInCollection.CheckStarted = true;
        }

        await App.Db.UpdateSoLineAsync(lineInCollection);

        if (SelectedLine?.Id == lineInCollection.Id)
        {
            SelectedLine = lineInCollection;
            SelectedItemFrame.BindingContext = lineInCollection;
            OnPropertyChanged(nameof(SelectedLine));
            UpdateInputState();
        }

        ClearInputFields();
        BarcodeEntry.Focus();

        if (lineInCollection.Checked)
        {
            await DisplayAlert("Checking Complete",
                $"{lineInCollection.ItemDesc} has been fully checked ({lineInCollection.CheckedQty}/{lineInCollection.PickedQty})\n\n" +
                "Checking complete, needs authorization", "OK");
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

            var matchingLine = await App.Db.GetSoLineByBarcodeAsync(_soHeader.Reference, scannedBarcode);

            if (matchingLine == null)
            {
                await DisplayAlert("Error", $"Item with barcode '{scannedBarcode}' not found in this SO.", "OK");
                return;
            }

            await ProcessCheckingItem(matchingLine);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to save progress: {ex.Message}", "OK");
        }
    }

    private async Task ResetSelectedLine()
    {
        // Reset the selected line
        SelectedLine.CheckedQty = 0;
        SelectedLine.CheckStarted = false;
        SelectedLine.CheckStartDateTime = null;
        SelectedLine.CheckCompleteDateTime = null;
        SelectedLine.CheckedBy = null;
        SelectedLine.Checked = false;

        // Also update the line in the collection
        var lineInCollection = _soLines.FirstOrDefault(l => l.Id == SelectedLine.Id);
        if (lineInCollection != null)
        {
            lineInCollection.CheckedQty = 0;
            lineInCollection.CheckStarted = false;
            lineInCollection.CheckStartDateTime = null;
            lineInCollection.CheckCompleteDateTime = null;
            lineInCollection.CheckedBy = null;
            lineInCollection.Checked = false;
        }

        await App.Db.UpdateSoLineAsync(SelectedLine);

        CheckingInputSection.IsVisible = false;
        StartCheckingButton.IsVisible = true;
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