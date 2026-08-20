using AmaScan.Classes;
using AmaScan.Models;
using AmaScan.sqliteModels;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;

namespace AmaScan;

public partial class PickingPage : ContentPage, INotifyPropertyChanged
{
    private readonly UserSession _userSession;
    private SoHeader _soHeader;
    private ObservableCollection<SoLine> _soLines;
    private SoLine _selectedLine;

    public event PropertyChangedEventHandler PropertyChanged;

    #region Properties
    public string SoNumber => _soHeader?.Reference ?? "";
    public string PickerName => _soHeader?.Picker ?? "";
    public string PackerName => _soHeader?.Packer ?? "";
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
    public PickingPage(DatabaseHelper databaseHelper)
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
        OnPropertyChanged(nameof(PickerName));
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
            await App.Db.DeleteAllExceptSoAsync(_soHeader.Reference);
            var lines = await App.Db.GetSoLinesByOrderNoAsync(_soHeader.Reference);
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
        await App.Db.SetPhaseUserAsync(_soHeader.Reference, currentUserName, "picking");
        _soHeader.PickStarted = true;

        // Claim the order on the server. Don't block picking if this fails — the picker can work
        // offline and completion posts the real numbers — but say so, rather than failing silently
        // and leaving the order looking unclaimed to everyone else.
        if (!await SendPickStartedToApiAsync(_soHeader))
        {
            await DisplayAlert("Not Claimed On Server",
                $"Could not mark {_soHeader.Reference} as started.\n\n" +
                "You can carry on picking, but another picker may still see this order as free.", "OK");
        }

        // Set the line-level flag
        if (SelectedLine != null)
        {
            SelectedLine.PickStarted = true;
            await App.Db.UpdateSoLineAsync(SelectedLine);
        }

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

            var matchingLine = await App.Db.GetSoLineByBarcodeAsync(_soHeader.Reference, scannedBarcode);

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
                await App.Db.UpdateSoLineAsync(line);
            }

            // Check if all lines are picked and update header accordingly
            bool allLinesPicked = await App.Db.AreAllLinesPickedAsync(_soHeader.Reference);
            _soHeader.Picked = allLinesPicked;

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
            if (await App.Db.AreAllLinesPickedAsync(_soHeader.Reference))
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
            var soLines = await App.Db.GetSoLinesByOrderNoAsync(_soHeader.Reference);

            // Send the per-line picked quantities, not just "done" — the next stage may run on a
            // different device, which downloads the order from the server and would otherwise see
            // PickedQty = 0. This call also stamps the header flags (Picked, plus Packed when
            // packing is switched off) per the workflow matrix.
            var stageResult = await StageSyncService.SendAsync(
                _soHeader.Reference, StageSyncService.StagePicking, soLines);

            if (!stageResult.Success)
            {
                await DisplayAlert("Picking Not Saved",
                    $"Could not save the picked quantities for {_soHeader.Reference}:\n\n{stageResult.ErrorMessage}\n\n" +
                    "The order has been left un-picked so you can retry.", "OK");
                return;
            }

            // Set header flags to indicate picking is complete
            _soHeader.Picked = true;
            if (!WorkflowConfig.UsePacking) _soHeader.Packed = true;
            await App.Db.UpdateSoHeaderAsync(_soHeader);

            PickingWorkflowSession.Clear();
            await DisplayAlert("Picking Phase Complete",
                $"Picking successfully completed for entire SO!\n\n" +
                $"Next phase: {(WorkflowConfig.UsePacking ? "Packing" : "Checking")}\n\n" +
                $"Order: {_soHeader.Reference}", "OK");

            // Pop back to PickingMain so the next SO can be entered. Do NOT push a fresh dashboard
            // here: PickingMain is a DI SINGLETON that is still sitting in this Shell stack, so
            // navigating into picking again from a pushed dashboard would put the very same page
            // instance into the stack twice. Checking already does it this way.
            // Stack: PickingMain -> PickingDocumentsPage -> PickingPage, so "../.." pops both.
            await Shell.Current.GoToAsync("../..");
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
            if (!await App.Db.AreAllLinesPickedAsync(_soHeader.Reference))
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

        // With no packing stage, picking is the last stage before checking and owns BOTH flags —
        // otherwise the pack columns stay at zero and checking has nothing to measure against.
        // Must run after PickedBy/PickStartDateTime are set above, since it copies them.
        // The server applies the same rule in UpdateSalesOrderStageController.
        if (!WorkflowConfig.UsePacking)
        {
            lineInCollection.Packed = lineInCollection.Picked;
            lineInCollection.PackedQty = lineInCollection.PickedQty;
            lineInCollection.PackedBy = lineInCollection.PickedBy;
            lineInCollection.PackStartDateTime = lineInCollection.PickStartDateTime;
            lineInCollection.PackCompleteDateTime = lineInCollection.PickCompleteDateTime;
        }

        await App.Db.UpdateSoLineAsync(lineInCollection);

        // Check if all lines are picked and update header accordingly
        bool allLinesPicked = await App.Db.AreAllLinesPickedAsync(_soHeader.Reference);
        if (allLinesPicked && !_soHeader.Picked)
        {
            _soHeader.Picked = true;
            await App.Db.UpdateSoHeaderAsync(_soHeader);
        }
        else if (!allLinesPicked && _soHeader.Picked)
        {
            // If a line was reset and not all lines are picked anymore, update header
            _soHeader.Picked = false;
            await App.Db.UpdateSoHeaderAsync(_soHeader);
        }

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

            var matchingLine = await App.Db.GetSoLineByBarcodeAsync(_soHeader.Reference, scannedBarcode);

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
        // Capture BEFORE clearing. _soHeader.Picked alone is not enough: on a device that has just
        // downloaded the order fresh, SaveToLocalDatabaseAsync builds the header without the flag,
        // so it reads false even though the server has the order fully picked. The line's own
        // Picked bit does come across from the server, so it is the reliable signal.
        bool wasPickedOnServer = _soHeader.Picked || SelectedLine.Picked;

        // Reset the selected line (only picking-related fields)
        SelectedLine.PickedQty = 0;
        SelectedLine.Picked = false;
        SelectedLine.PickStarted = false;
        SelectedLine.PickStartDateTime = null;
        SelectedLine.PickCompleteDateTime = null;
        SelectedLine.PickedBy = null;

        // Also update the line in the collection
        var lineInCollection = _soLines.FirstOrDefault(l => l.Id == SelectedLine.Id);
        if (lineInCollection != null)
        {
            lineInCollection.PickedQty = 0;
            lineInCollection.Picked = false;
            lineInCollection.PickStarted = false;
            lineInCollection.PickStartDateTime = null;
            lineInCollection.PickCompleteDateTime = null;
            lineInCollection.PickedBy = null;
        }

        // With no packing stage, picking mirrors itself into the pack columns — so a reset has to
        // clear those too. Otherwise the line keeps PackedQty/Packed from before the reset and
        // still reads as packed, which then feeds the checking stage a stale basis quantity.
        if (!WorkflowConfig.UsePacking)
        {
            foreach (var target in new[] { SelectedLine, lineInCollection })
            {
                if (target == null) continue;
                target.PackedQty = 0;
                target.Packed = false;
                target.PackStarted = false;
                target.PackedBy = null;
                target.PackStartDateTime = null;
                target.PackCompleteDateTime = null;
            }
        }

        await App.Db.UpdateSoLineAsync(SelectedLine);

        // If picking had already been COMPLETED and posted, this reset has to be undone on the
        // server too. Otherwise the header keeps Picked = 1, a checker on another device sails
        // through WorkflowGate and checks against the stale quantity this reset just cleared.
        // Under Pick→Check picking also owns Packed, so clear that as well.
        if (wasPickedOnServer)
        {
            if (!await SendPickResetToApiAsync(_soHeader))
            {
                await DisplayAlert("Reset Not Sent",
                    $"{_soHeader.Reference} was reset on this device, but the server still has it as " +
                    "fully picked.\n\nRe-pick and complete the order to clear this, or the checker " +
                    "may work off the old quantities.", "OK");
            }
        }

        // Check if all lines are still picked after reset, and update header accordingly
        bool allLinesPicked = await App.Db.AreAllLinesPickedAsync(_soHeader.Reference);
        _soHeader.Picked = allLinesPicked;

        // Reset the Picker field in the header (this field exists in the database)
        _soHeader.Picker = null;
        _soHeader.PickStarted = false;
        await App.Db.UpdateSoHeaderAsync(_soHeader);

        PickingInputSection.IsVisible = false;
        StartPickingButton.IsVisible = true;
        SelectedItemFrame.IsVisible = true;

        SelectedItemFrame.BindingContext = null;
        SelectedItemFrame.BindingContext = SelectedLine;
        OnPropertyChanged(nameof(SelectedLine));

        UpdateInputState();

        await DisplayAlert("Reset Complete", $"Successfully reset {SelectedLine.ItemDesc}", "OK");
    }

    /// <summary>
    /// Clears the server's completed-picking flags after a line was reset here, so the order stops
    /// satisfying the authorization filter and the next stage's gate blocks it again. Sends Packed
    /// as well when picking owns that flag (no packing stage).
    /// </summary>
    private Task<bool> SendPickResetToApiAsync(SoHeader soHeader) =>
        PostHeaderAsync(new SalesHeaderUpdateRequest
        {
            Reference = soHeader.Reference,
            Picked = false,
            Packed = WorkflowConfig.UsePacking ? (bool?)null : false
        });

    /// <summary>
    /// Tells the server picking has STARTED and claims the order for this picker.
    ///
    /// It deliberately does NOT send Picked. It used to send Picked = soHeader.Picked, and after a
    /// login the local header is rebuilt from scratch as false — so simply re-opening an order that
    /// was already picked wrote Picked = 0 back over it and dropped it out of Authorize Release.
    /// Completion flags belong to api/UpdateSalesOrderStage and to the reset path above.
    ///
    /// It also does not send Packer: the web picking-slip page assigns one, and the server only
    /// writes that column when a value is supplied.
    /// </summary>
    private Task<bool> SendPickStartedToApiAsync(SoHeader soHeader) =>
        PostHeaderAsync(new SalesHeaderUpdateRequest
        {
            Reference = soHeader.Reference,
            Picker = soHeader.Picker,
            PickStarted = true
        });

    private async Task<bool> PostHeaderAsync(SalesHeaderUpdateRequest payload)
    {
        try
        {
            using var client = new HttpClient();

            string json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            string url = $"{AppConfig.ApiBaseUrl}UpdateSalesOrderHeader";
            var response = await client.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }
            else
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"API Error: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SendPickStartedToApiAsync Error: {ex.Message}");
            return false;
        }
    }
    #endregion
}