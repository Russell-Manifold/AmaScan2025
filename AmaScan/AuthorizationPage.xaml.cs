using AmaScan.sqliteModels;
using AmaScan.Classes;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Linq;
using SQLite;

namespace AmaScan;

public partial class AuthorizationPage : ContentPage, INotifyPropertyChanged
{
    private readonly DatabaseHelper _dbHelper;
    private readonly UserSession _userSession;
    private SoHeader _soHeader;
    private ObservableCollection<SoLine> _soLines;
    private SoLine _selectedLine;

    public event PropertyChangedEventHandler PropertyChanged;

    #region Properties
    public string SoNumber => _soHeader?.Reference ?? "";
    public string AuthorizerName => _selectedLine?.AuthorizedBy ?? "";
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
    public AuthorizationPage()
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
        OnPropertyChanged(nameof(AuthorizerName));
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
                StartAuthorizationForSelectedLine(freshLine, false);
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
                await DisplayAlert("No Item Selected", "Please select an item to authorize.", "OK");
                await Shell.Current.GoToAsync("..");
            });
        }
    }
    #endregion

    private string GetCurrentUserName() => _userSession.CurrentUser?.UserName ?? "Unknown User";

    #region UI Management
    private void StartAuthorizationForSelectedLine(SoLine selectedLine, bool showInput = true)
    {
        SelectedLine = selectedLine;

        SelectedItemFrame.BindingContext = selectedLine;
        SelectedItemFrame.IsVisible = true;

        if (showInput)
        {
            AuthorizationInputSection.IsVisible = true;
            StartAuthorizationButton.IsVisible = false;
            BarcodeEntry.Text = selectedLine.ItemBarcode;
            QuantityEntry.Focus();
        }
        else
        {
            AuthorizationInputSection.IsVisible = false;
            StartAuthorizationButton.IsVisible = true;
            ClearInputFields();
        }

        UpdateInputState();
        OnPropertyChanged(nameof(SelectedLine));
    }

    private void UpdateInputState()
    {
        if (SelectedLine == null) return;

        bool isFullyAuthorized = SelectedLine.AuthorizedQty >= SelectedLine.CheckedQty;

        BarcodeEntry.IsEnabled = !isFullyAuthorized;
        QuantityEntry.IsEnabled = !isFullyAuthorized;
        SaveButton.IsEnabled = !isFullyAuthorized;
        AuthorizeAllButton.IsEnabled = !isFullyAuthorized;

        if (isFullyAuthorized)
        {
            SaveButton.Text = "Fully Authorized";
            SaveButton.BackgroundColor = Colors.Gray;
            SaveButton.TextColor = Colors.White;
            AuthorizeAllButton.IsEnabled = false;
        }
        else
        {
            SaveButton.Text = "Save";
            SaveButton.BackgroundColor = Colors.Green;
            SaveButton.TextColor = Colors.White;
            AuthorizeAllButton.IsEnabled = true;
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
            var allLines = await _dbHelper.GetSoLinesByOrderNoAsync(_soHeader.Reference);

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
    private async void OnStartAuthorizationClicked(object sender, EventArgs e)
    {
        string currentUserName = GetCurrentUserName();

        // Set the header-level user for authorization
        await _dbHelper.SetPhaseUserAsync(_soHeader.Reference, currentUserName, "authorization");

        AuthorizationInputSection.IsVisible = true;
        StartAuthorizationButton.IsVisible = false;
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

            // If quantity is already entered, process immediately
            if (!string.IsNullOrEmpty(QuantityEntry.Text))
            {
                await ProcessAuthorizationItem(matchingLine);
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

    private async void OnResetAllClicked(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert(
            "RESET ALL PHASES",
            $"This will reset ALL phases for order {_soHeader.Reference}:\n\n" +
            "• Picking - All picked quantities will be cleared\n" +
            "• Packing - All packed quantities will be cleared\n" +
            "• Checking - All checked quantities will be cleared\n" +
            "• Authorization - All authorized quantities will be cleared\n\n" +
            "This action cannot be undone!\n\n" +
            "Are you absolutely sure you want to reset everything?",
            "YES, RESET ALL", "Cancel"
        );

        if (!confirm) return;

        // Double confirmation for safety
        bool finalConfirm = await DisplayAlert(
            "FINAL CONFIRMATION",
            $"You are about to reset ALL phases for order {_soHeader.Reference}.\n\n" +
            "This will clear all progress and return the order to its initial state.\n\n" +
            "Click 'CONFIRM' to proceed or 'Cancel' to abort.",
            "CONFIRM", "Cancel"
        );

        if (!finalConfirm) return;

        await ResetSelectedLineAllPhases();
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

            if (!SelectedLine.Authorized)
            {
                var currentLineOutstanding = SelectedLine.AuthorizationOutstandingQty;
                await DisplayAlert("Incomplete",
                    $"The selected item must be authorized before completing.\n\n" +
                    $"{currentLineOutstanding} items still need authorization", "OK");
                return;
            }

            bool confirmed = await DisplayAlert("Confirm Completion", "Item authorized. Proceed to complete authorization?", "Yes", "No");
            if (!confirmed) return;

            // Check if all lines are authorized
            if (await _dbHelper.AreAllLinesAuthorizedAsync(_soHeader.Reference))
            {
                // All items are authorized, prompt to complete the entire order
                bool completeOrder = await DisplayAlert("All Items Authorized",
                    $"All items have been authorized for {_soHeader.Reference}!\n\n" +
                    "Would you like to complete the entire authorization phase now?", "Complete Order", "Continue Authorization");

                if (completeOrder)
                {
                    // Complete the entire order
                    await CompleteAuthorizationPhase();
                }
                else
                {
                    // Go back to documents page to continue
                    await DisplayAlert("Line Complete",
                        $"Authorization completed for {SelectedLine.ItemDesc}!\n\n" +
                        $"Returning to documents page to continue.", "OK");
                    await Shell.Current.GoToAsync("..");
                }
            }
            else
            {
                // Not all items authorized, go back to documents page
                await DisplayAlert("Line Complete",
                    $"Authorization completed for {SelectedLine.ItemDesc}!\n\n" +
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

    private async Task CompleteAuthorizationPhase()
    {
        try
        {
            var soLines = await _dbHelper.GetSoLinesByOrderNoAsync(_soHeader.Reference);
            bool success = await SendToApiForCompletionAsync(_soHeader.Reference, soLines);

            if (success)
            {
                // Set header flags to indicate authorization is complete
                _soHeader.Authed = true;
                await _dbHelper.UpdateSoHeaderAsync(_soHeader);

                PickingWorkflowSession.Clear();
                await DisplayAlert("Authorization Phase Complete",
                    $"Authorization successfully completed for entire SO!\n\n" +
                    $"Order: {_soHeader.Reference}", "OK");

                var dashboardPage = App.Services.GetRequiredService<Dashboard>();
                await Navigation.PushAsync(dashboardPage);
            }
            else
            {
                await DisplayAlert("Error", "Failed to complete authorization. Please try again.", "OK");
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
            // Check if all lines are authorized
            if (!await _dbHelper.AreAllLinesAuthorizedAsync(_soHeader.Reference))
            {
                await DisplayAlert("Incomplete",
                    "Not all items have been authorized yet.\n\n" +
                    "All items must be authorized before completing the header.", "OK");
                return;
            }

            bool confirmed = await DisplayAlert("Confirm Header Completion",
                "All items authorized. Complete authorization phase for entire SO?", "Yes", "No");
            if (!confirmed) return;

            await CompleteAuthorizationPhase();
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

    private async void OnAuthorizeAllClicked(object sender, EventArgs e)
    {
        if (SelectedLine == null)
        {
            await DisplayAlert("No Item Selected", "Please select an item first.", "OK");
            return;
        }

        bool confirm = await DisplayAlert(
            "Authorize All",
            $"Authorize all remaining items for {SelectedLine.ItemDesc}?\n\n" +
            $"This will authorize {SelectedLine.AuthorizationOutstandingQty} items.",
            "Yes", "No"
        );

        if (!confirm) return;

        await AuthorizeAllRemainingItems();
    }
    #endregion

    #region Business Logic
    private async Task ProcessAuthorizationItem(SoLine matchingLine)
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

        if (lineInCollection.AuthorizedQty >= lineInCollection.CheckedQty)
        {
            await DisplayAlert("Already Authorized",
                $"{lineInCollection.ItemDesc} has already been fully authorized ({lineInCollection.AuthorizedQty}/{lineInCollection.CheckedQty}).\n\n" +
                "No further authorization allowed.", "OK");
            return;
        }

        if ((lineInCollection.AuthorizedQty + quantity) > lineInCollection.CheckedQty)
        {
            await DisplayAlert("Over-Authorization Not Allowed",
                $"Cannot authorize {quantity} more items. This would exceed the checked quantity of {lineInCollection.CheckedQty}.\n\n" +
                $"Already authorized: {lineInCollection.AuthorizedQty}\n" +
                $"Remaining: {lineInCollection.CheckedQty - lineInCollection.AuthorizedQty}", "OK");
            return;
        }

        await UpdateAuthorizationQuantity(lineInCollection, quantity);
    }

    private async Task UpdateAuthorizationQuantity(SoLine lineInCollection, int quantity)
    {
        lineInCollection.AuthorizedQty += quantity;

        lineInCollection.AuthCompleteDateTime = lineInCollection.AuthorizedQty == lineInCollection.CheckedQty ? DateTime.Now : null;

        // Set Authorized flag when quantities match exactly
        lineInCollection.Authorized = lineInCollection.AuthorizedQty == lineInCollection.CheckedQty;

        // Always ensure AuthorizedBy is set when user is working on the line
        if (string.IsNullOrEmpty(lineInCollection.AuthorizedBy))
        {
            lineInCollection.AuthorizedBy = GetCurrentUserName();
        }

        // Ensure AuthStartDateTime is set if it's not already set
        if (lineInCollection.AuthStartDateTime == null)
        {
            lineInCollection.AuthStartDateTime = DateTime.Now;
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

        if (lineInCollection.Authorized)
        {
            await DisplayAlert("Authorization Complete",
                $"{lineInCollection.ItemDesc} has been fully authorized ({lineInCollection.AuthorizedQty}/{lineInCollection.CheckedQty})\n\n" +
                "Authorization complete", "OK");
        }
    }

    private async Task AuthorizeAllRemainingItems()
    {
        if (SelectedLine == null) return;

        var outstandingQty = SelectedLine.AuthorizationOutstandingQty;
        if (outstandingQty <= 0)
        {
            await DisplayAlert("Already Complete", "All items have already been authorized.", "OK");
            return;
        }

        await UpdateAuthorizationQuantity(SelectedLine, (int)outstandingQty);
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

            await ProcessAuthorizationItem(matchingLine);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to save progress: {ex.Message}", "OK");
        }
    }

    private async Task ResetSelectedLine()
    {
        // Reset the selected line
        SelectedLine.AuthorizedQty = 0;
        SelectedLine.AuthStartDateTime = null;
        SelectedLine.AuthCompleteDateTime = null;
        SelectedLine.AuthorizedBy = null;
        SelectedLine.Authorized = false;

        // Also update the line in the collection
        var lineInCollection = _soLines.FirstOrDefault(l => l.Id == SelectedLine.Id);
        if (lineInCollection != null)
        {
            lineInCollection.AuthorizedQty = 0;
            lineInCollection.AuthStartDateTime = null;
            lineInCollection.AuthCompleteDateTime = null;
            lineInCollection.AuthorizedBy = null;
            lineInCollection.Authorized = false;
        }

        await _dbHelper.UpdateSoLineAsync(SelectedLine);

        AuthorizationInputSection.IsVisible = false;
        StartAuthorizationButton.IsVisible = true;
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


    private async Task ResetSelectedLineAllPhases()
    {
        try
        {
            LoadingOverlay.IsVisible = true;
            loadingIndicator.IsRunning = true;

            // Reset the selected line - ALL phases
            // Reset Picking phase
            SelectedLine.PickedQty = 0;
            SelectedLine.PickStartDateTime = null;
            SelectedLine.PickCompleteDateTime = null;
            SelectedLine.PickedBy = null;
            SelectedLine.Picked = false;

            // Reset Packing phase
            SelectedLine.PackedQty = 0;
            SelectedLine.PackStartDateTime = null;
            SelectedLine.PackCompleteDateTime = null;
            SelectedLine.PackedBy = null;
            SelectedLine.Packed = false;

            // Reset Checking phase
            SelectedLine.CheckedQty = 0;
            SelectedLine.CheckStartDateTime = null;
            SelectedLine.CheckCompleteDateTime = null;
            SelectedLine.CheckedBy = null;
            SelectedLine.Checked = false;

            // Reset Authorization phase
            SelectedLine.AuthorizedQty = 0;
            SelectedLine.AuthStartDateTime = null;
            SelectedLine.AuthCompleteDateTime = null;
            SelectedLine.AuthorizedBy = null;
            SelectedLine.Authorized = false;

            // Also update the line in the collection
            var lineInCollection = _soLines.FirstOrDefault(l => l.Id == SelectedLine.Id);
            if (lineInCollection != null)
            {
                // Reset Picking phase
                lineInCollection.PickedQty = 0;
                lineInCollection.PickStartDateTime = null;
                lineInCollection.PickCompleteDateTime = null;
                lineInCollection.PickedBy = null;
                lineInCollection.Picked = false;

                // Reset Packing phase
                lineInCollection.PackedQty = 0;
                lineInCollection.PackStartDateTime = null;
                lineInCollection.PackCompleteDateTime = null;
                lineInCollection.PackedBy = null;
                lineInCollection.Packed = false;

                // Reset Checking phase
                lineInCollection.CheckedQty = 0;
                lineInCollection.CheckStartDateTime = null;
                lineInCollection.CheckCompleteDateTime = null;
                lineInCollection.CheckedBy = null;
                lineInCollection.Checked = false;

                // Reset Authorization phase
                lineInCollection.AuthorizedQty = 0;
                lineInCollection.AuthStartDateTime = null;
                lineInCollection.AuthCompleteDateTime = null;
                lineInCollection.AuthorizedBy = null;
                lineInCollection.Authorized = false;
            }

            await _dbHelper.UpdateSoLineAsync(SelectedLine);

            // Update UI
            OnPropertyChanged(nameof(SelectedLine));
            OnPropertyChanged(nameof(SoLines));

            // Reset UI state
            AuthorizationInputSection.IsVisible = false;
            StartAuthorizationButton.IsVisible = true;
            ClearInputFields();

            // Update the selected item frame
            SelectedItemFrame.BindingContext = null;
            SelectedItemFrame.BindingContext = SelectedLine;
            UpdateInputState();

            await DisplayAlert("Reset Complete",
                $"All phases have been reset for {SelectedLine.ItemDesc}.\n\n" +
                "The item has been returned to its initial state.\n\n" +
                "You can now start the workflow from the beginning.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to reset selected line: {ex.Message}", "OK");
        }
        finally
        {
            LoadingOverlay.IsVisible = false;
            loadingIndicator.IsRunning = false;
        }
    }
    #endregion
}
