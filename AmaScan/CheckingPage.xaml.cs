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

        // TEMP (2026-05-21): Picking/packing workflow paused — compare against OrderedQty
        // instead of PickedQty so checking works without a prior pick/pack.
        //// bool isFullyChecked = SelectedLine.CheckedQty >= SelectedLine.PickedQty;
        bool isFullyChecked = SelectedLine.CheckedQty >= SelectedLine.OrderedQty;

        // Line is "done" when Checked is true — either fully checked (qty match)
        // or explicitly marked short via OnMarkShortClicked.
        bool isCompleted = SelectedLine.Checked;

        BarcodeEntry.IsEnabled = !isCompleted;
        QuantityEntry.IsEnabled = !isCompleted;
        SaveButton.IsEnabled = !isCompleted;
        MarkShortButton.IsEnabled = !isCompleted;

        if (isCompleted)
        {
            SaveButton.Text = isFullyChecked ? "Fully Checked" : "Marked Short";
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

            // TEMP (2026-05-21): Nothing on this page mutates the SoHeader, so writing it
            // back here just clobbers any header changes another user/phase made between
            // load and save. Restore if/when this page actually edits header fields.
            //// await App.Db.UpdateSoHeaderAsync(_soHeader);
            //// PickingWorkflowSession.CurrentSoHeader = _soHeader;

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
                // TEMP (2026-05-21): Picking/packing workflow paused — outstanding qty based
                // on OrderedQty rather than CheckingOutstandingQty (which assumes PackedQty).
                //// var currentLineOutstanding = SelectedLine.CheckingOutstandingQty;
                var currentLineOutstanding = SelectedLine.OrderedQty - SelectedLine.CheckedQty;
                await DisplayAlert("Incomplete",
                    $"The selected item must be checked before completing.\n\n" +
                    $"{currentLineOutstanding} items still need checking", "OK");
                return;
            }

            // Determine state first, then ask the user the right question once.
            bool allChecked = await App.Db.AreAllLinesCheckedAsync(_soHeader.Reference);

            if (allChecked)
            {
                bool completeOrder = await DisplayAlert("All Items Checked",
                    $"All items have been checked for {_soHeader.Reference}.\n\n" +
                    "Complete the checking phase now?", "Complete Order", "Continue");

                if (completeOrder)
                {
                    await CompleteCheckingPhase();
                }
                else
                {
                    await Shell.Current.GoToAsync("..");
                }
            }
            else
            {
                bool confirmed = await DisplayAlert("Confirm Line Complete",
                    $"Mark {SelectedLine.ItemDesc} as checked?", "Yes", "No");
                if (!confirmed) return;

                await DisplayAlert("Line Complete",
                    $"Checking completed for {SelectedLine.ItemDesc}.\n\n" +
                    "Returning to documents page to select next item.", "OK");

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
                    // TEMP (2026-05-21): Authorization phase moved out of this app for the
                    // current milestone. Restore the next-phase line if/when it returns.
                    //// $"Next phase: Authorization\n\n" +
                    $"Order: {_soHeader.Reference}", "OK");

                // Return to the SO entry screen (CheckingMain) rather than the dashboard,
                // so the user can immediately enter the next SO number to check.
                // CheckingMain.OnAppearing clears the previous session/UI on arrival.
                // Stack: CheckingMain -> CheckingDocumentsPage -> CheckingPage, so "../.." pops both.
                await Shell.Current.GoToAsync("../..");
            }
            // On failure, SendToApiForCompletionAsync already surfaced the specific
            // reason; leave the SO un-completed so the user can retry.
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Unexpected error: {ex.Message}", "OK");
        }
    }

    private async void OnMarkShortClicked(object sender, EventArgs e)
    {
        try
        {
            if (SelectedLine == null)
            {
                await DisplayAlert("No Item Selected", "Please select an item.", "OK");
                return;
            }

            if (SelectedLine.Checked)
            {
                await DisplayAlert("Already Complete", "This line is already marked complete.", "OK");
                return;
            }

            decimal shortQty = SelectedLine.OrderedQty - SelectedLine.CheckedQty;
            if (shortQty <= 0)
            {
                await DisplayAlert("Nothing to Short",
                    "Checked quantity already meets the ordered quantity. Use Complete instead.", "OK");
                return;
            }

            bool confirmed = await DisplayAlert("Mark Short?",
                $"{SelectedLine.ItemDesc}\n\n" +
                $"Checked: {SelectedLine.CheckedQty}\n" +
                $"Ordered: {SelectedLine.OrderedQty}\n" +
                $"Short by: {shortQty}\n\n" +
                "This will close out the line with the shortfall. The remainder will be reconciled at the next stage.",
                "Mark Short", "Cancel");
            if (!confirmed) return;

            var lineInCollection = _soLines.FirstOrDefault(l => l.Id == SelectedLine.Id) ?? SelectedLine;

            lineInCollection.Checked = true;
            lineInCollection.CheckCompleteDateTime = DateTime.Now;
            // TEMP (2026-05-21): Picking/packing workflow paused — flip Picked/Packed flags
            // so downstream views show the line as if it went through the full workflow.
            // No timestamps set; restore proper Pick/Pack handling when those phases return.
            lineInCollection.Picked = true;
            lineInCollection.Packed = true;
            if (string.IsNullOrEmpty(lineInCollection.CheckedBy))
                lineInCollection.CheckedBy = GetCurrentUserName();
            if (lineInCollection.CheckStartDateTime == null)
                lineInCollection.CheckStartDateTime = DateTime.Now;
            if (!lineInCollection.CheckStarted)
                lineInCollection.CheckStarted = true;

            await App.Db.UpdateSoLineAsync(lineInCollection);

            SelectedLine = lineInCollection;
            SelectedItemFrame.BindingContext = lineInCollection;
            OnPropertyChanged(nameof(SelectedLine));
            UpdateInputState();

            // Run the same post-Checked flow as OnCompleteClicked, minus the
            // line-confirm prompt (user already confirmed via Mark Short).
            bool allChecked = await App.Db.AreAllLinesCheckedAsync(_soHeader.Reference);

            if (allChecked)
            {
                bool completeOrder = await DisplayAlert("All Items Checked",
                    $"All items have been checked for {_soHeader.Reference}.\n\n" +
                    "Complete the checking phase now?", "Complete Order", "Continue");

                if (completeOrder)
                    await CompleteCheckingPhase();
                else
                    await Shell.Current.GoToAsync("..");
            }
            else
            {
                await DisplayAlert("Line Marked Short",
                    $"{SelectedLine.ItemDesc} marked short ({SelectedLine.CheckedQty}/{SelectedLine.OrderedQty}).\n\n" +
                    "Returning to documents page to select next item.", "OK");

                await Shell.Current.GoToAsync("..");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to mark short: {ex.Message}", "OK");
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

        // TEMP (2026-05-21): Picking/packing workflow paused — cap on OrderedQty.
        // Restore the PickedQty/PackedQty comparisons when picking/packing returns.
        //// if (lineInCollection.CheckedQty >= lineInCollection.PickedQty)
        //// {
        ////     await DisplayAlert("Already Checked",
        ////         $"{lineInCollection.ItemDesc} has already been fully checked ({lineInCollection.CheckedQty}/{lineInCollection.PickedQty}).\n\n" +
        ////         "No further checking allowed.", "OK");
        ////     return;
        //// }
        ////
        //// if ((lineInCollection.CheckedQty + quantity) > lineInCollection.PickedQty)
        //// {
        ////     await DisplayAlert("Over-Checking Not Allowed",
        ////         $"Cannot check {quantity} more items. This would exceed the packed quantity of {lineInCollection.PackedQty}.\n\n" +
        ////         $"Already checked: {lineInCollection.CheckedQty}\n" +
        ////         $"Remaining: {lineInCollection.PickedQty - lineInCollection.CheckedQty}", "OK");
        ////     return;
        //// }

        if (lineInCollection.CheckedQty >= lineInCollection.OrderedQty)
        {
            await DisplayAlert("Already Checked",
                $"{lineInCollection.ItemDesc} has already been fully checked ({lineInCollection.CheckedQty}/{lineInCollection.OrderedQty}).\n\n" +
                "No further checking allowed.", "OK");
            return;
        }

        if ((lineInCollection.CheckedQty + quantity) > lineInCollection.OrderedQty)
        {
            await DisplayAlert("Over-Checking Not Allowed",
                $"Cannot check {quantity} more items. This would exceed the ordered quantity of {lineInCollection.OrderedQty}.\n\n" +
                $"Already checked: {lineInCollection.CheckedQty}\n" +
                $"Remaining: {lineInCollection.OrderedQty - lineInCollection.CheckedQty}", "OK");
            return;
        }

        await UpdateCheckingQuantity(lineInCollection, quantity);
    }

    private async Task UpdateCheckingQuantity(SoLine lineInCollection, int quantity)
    {
        lineInCollection.CheckedQty += quantity;

        // TEMP (2026-05-21): Picking/packing workflow paused — completion based on OrderedQty.
        //// lineInCollection.CheckCompleteDateTime = lineInCollection.CheckedQty == lineInCollection.PickedQty ? DateTime.Now : null;
        //// // Set Checked flag when quantities match exactly
        //// lineInCollection.Checked = lineInCollection.CheckedQty == lineInCollection.PickedQty;
        lineInCollection.CheckCompleteDateTime = lineInCollection.CheckedQty == lineInCollection.OrderedQty ? DateTime.Now : null;

        // Set Checked flag when quantities match exactly
        lineInCollection.Checked = lineInCollection.CheckedQty == lineInCollection.OrderedQty;

        // TEMP (2026-05-21): Picking/packing workflow paused — flip Picked/Packed flags
        // so downstream views show the line as if it went through the full workflow.
        // No timestamps set; restore proper Pick/Pack handling when those phases return.
        if (lineInCollection.Checked)
        {
            lineInCollection.Picked = true;
            lineInCollection.Packed = true;
        }

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
            // TEMP (2026-05-21): Picking/packing workflow paused — show OrderedQty in alert.
            //// await DisplayAlert("Checking Complete",
            ////     $"{lineInCollection.ItemDesc} has been fully checked ({lineInCollection.CheckedQty}/{lineInCollection.PickedQty})\n\n" +
            ////     "Checking complete, needs authorization", "OK");
            await DisplayAlert("Checking Complete",
                $"{lineInCollection.ItemDesc} has been fully checked ({lineInCollection.CheckedQty}/{lineInCollection.OrderedQty})", "OK");
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
            // Build the delivery note lines from the checked SO lines. The server keys
            // each line back to its SalesOrderLines row by SoLLineNo (captured at load).
            var lines = soLines.Select(l => new DelNoteLine
            {
                LineNo = l.SoLLineNo,
                ItemCode = l.ItemCode,
                ItemDesc = l.ItemDesc,
                ItemBarcode = l.ItemBarcode,
                OrderedQty = l.OrderedQty,
                CheckedQty = l.CheckedQty,
                CheckedBy = l.CheckedBy,
                CheckStartDateTime = l.CheckStartDateTime,
                CheckCompleteDateTime = l.CheckCompleteDateTime
            }).ToList();

            // Build the audit header. Every line is guaranteed Checked at this point;
            // a discrepancy is any line where the checked qty differs from ordered.
            var header = new DelNoteHeader
            {
                CreatedBy = GetCurrentUserName(),
                CreateStartTime = soLines.Min(l => l.CheckStartDateTime),
                CreateEndTime = DateTime.Now,
                DeviceName = AppConfig.DeviceName,
                TotalLines = soLines.Count,
                CheckedLines = soLines.Count(l => l.CheckedQty > 0),
                DiscrepancyLines = soLines.Count(l => l.CheckedQty != l.OrderedQty)
            };

            // Outbound warehouse for the delivery note comes from the app's saved
            // default picking warehouse (set on the Settings page).
            string warehouseCode = Preferences.Get("DefaultPickingWarehouseCode", "");

            IDelNoteService delNoteService = new OmniDelNoteService();
            var result = await delNoteService.SendAsync(
                soNumber,
                null,            // customerBranchCode → server default ("HO")
                warehouseCode,
                null,            // status → server default ("Outstanding")
                lines,
                header);

            if (result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"Customer delivery note created: {result.ReferenceNumber}");
                return true;
            }

            System.Diagnostics.Debug.WriteLine($"CustomerDeliveryNote API Error: {result.ErrorMessage}");
            await DisplayAlert("Delivery Note Failed",
                $"Could not create the delivery note for {soNumber}:\n\n{result.ErrorMessage}", "OK");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SendToApiForCompletionAsync Error: {ex.Message}");
            await DisplayAlert("Error", $"Failed to send delivery note: {ex.Message}", "OK");
            return false;
        }
    }
    #endregion
}