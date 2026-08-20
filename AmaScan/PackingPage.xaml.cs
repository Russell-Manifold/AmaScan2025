using AmaScan.sqliteModels;
using AmaScan.Models;
using AmaScan.Classes;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Linq;
using SQLite;

namespace AmaScan;

public partial class PackingPage : ContentPage, INotifyPropertyChanged
{
    private readonly UserSession _userSession;
    private SoHeader _soHeader;
    private ObservableCollection<SoLine> _soLines;
    private SoLine _selectedLine;

    public event PropertyChangedEventHandler PropertyChanged;

    #region Properties
    public string SoNumber => _soHeader?.Reference ?? "";
    public string PackerName => _selectedLine?.PackedBy ?? "";
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
    public PackingPage(DatabaseHelper databaseHelper)
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
        OnPropertyChanged(nameof(PackerName));
    }

    private async void LoadSelectedLine()
    {
        if (PickingWorkflowSession.CurrentSoLine != null)
        {
            try
            {
                // Get the fresh line from database to ensure we have the latest data
                var freshLine = await Task.Run(async () =>
                {
                    return await App.Db.GetSoLineByBarcodeAsync(_soHeader.Reference, PickingWorkflowSession.CurrentSoLine.ItemBarcode);
                });

                if (freshLine != null)
                {
                    // Update the session with the fresh line
                    PickingWorkflowSession.CurrentSoLine = freshLine;
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        StartPackingForSelectedLine(freshLine, false);
                    });
                }
                else
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await DisplayAlert("Error", "Selected line not found in database.", "OK");
                        await Shell.Current.GoToAsync("..");
                    });
                }
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Error", $"Failed to load selected line: {ex.Message}", "OK");
                    await Shell.Current.GoToAsync("..");
                });
            }
        }
        else
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SelectedLine = null;
            });
            
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("No Item Selected", "Please select an item to pack.", "OK");
                await Shell.Current.GoToAsync("..");
            });
        }
    }
    #endregion

    private string GetCurrentUserName() => _userSession.CurrentUser?.UserName ?? "Unknown User";

    // ONE definition of the basis, shared with DatabaseHelper.UpdateWorkflowStatus — they used to
    // disagree (that one measured packing against OrderedQty), which would silently mark a line
    // packed on merge that this page still considered outstanding.
    private static decimal PackBasisQty(SoLine line) => WorkflowConfig.PackBasisQty(line);

    private static string PackBasisName => WorkflowConfig.UsePicking ? "picked" : "ordered";

    #region UI Management
    private void StartPackingForSelectedLine(SoLine selectedLine, bool showInput = true)
    {
        SelectedLine = selectedLine;

        SelectedItemFrame.BindingContext = selectedLine;
        SelectedItemFrame.IsVisible = true;

        if (showInput)
        {
            PackingInputSection.IsVisible = true;
            StartPackingButton.IsVisible = false;
            BarcodeEntry.Text = selectedLine.ItemBarcode;
            QuantityEntry.Focus();
        }
        else
        {
            PackingInputSection.IsVisible = false;
            StartPackingButton.IsVisible = true;
            ClearInputFields();
        }

        UpdateInputState();
        OnPropertyChanged(nameof(SelectedLine));
    }

    private void UpdateInputState()
    {
        if (SelectedLine == null) return;

        bool isFullyPacked = SelectedLine.PackedQty >= PackBasisQty(SelectedLine);

        BarcodeEntry.IsEnabled = !isFullyPacked;
        QuantityEntry.IsEnabled = !isFullyPacked;
        SaveButton.IsEnabled = !isFullyPacked;

        if (isFullyPacked)
        {
            SaveButton.Text = "Fully Packed";
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
            var allLines = await Task.Run(async () =>
            {
                return await App.Db.GetSoLinesByOrderNoAsync(_soHeader.Reference);
            });

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _soLines.Clear();
                foreach (var line in allLines)
                {
                    _soLines.Add(line);
                }
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
    }
    #endregion

    #region Event Handlers
    private async void OnStartPackingClicked(object sender, EventArgs e)
    {
        string currentUserName = GetCurrentUserName();

        // Set the header-level user for packing
        await App.Db.SetPhaseUserAsync(_soHeader.Reference, currentUserName, "packing");

        // Set the line-level flag
        if (SelectedLine != null)
        {
            SelectedLine.PackStarted = true;
            await App.Db.UpdateSoLineAsync(SelectedLine);
        }

        PackingInputSection.IsVisible = true;
        StartPackingButton.IsVisible = false;
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
                await ProcessPackingItem(matchingLine);
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
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                LoadingOverlay.IsVisible = true;
                loadingIndicator.IsRunning = true;
            });

            // Run database operations on background thread
            await Task.Run(async () =>
            {
                foreach (var line in _soLines)
                {
                    await App.Db.UpdateSoLineAsync(line);
                }

                await App.Db.UpdateSoHeaderAsync(_soHeader);
            });

            PickingWorkflowSession.CurrentSoHeader = _soHeader;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Success", "Progress saved successfully.", "OK");
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Error", $"Failed to save progress: {ex.Message}", "OK");
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                LoadingOverlay.IsVisible = false;
                loadingIndicator.IsRunning = false;
            });
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

            // Check if checking has started for this item
            if (SelectedLine.CheckStarted)
            {
                await DisplayAlert("Completion Unavailable",
                    $"{SelectedLine.ItemDesc} has already been started for checking.\n\n" +
                    "Packing cannot be modified once checking has started. Please complete checking first.", "OK");
                return;
            }

            if (!SelectedLine.Packed)
            {
                var currentLineOutstanding = PackBasisQty(SelectedLine) - SelectedLine.PackedQty;
                await DisplayAlert("Incomplete",
                    $"The selected item must be packed before completing.\n\n" +
                    $"{currentLineOutstanding} items still need packing", "OK");
                return;
            }

            bool confirmed = await DisplayAlert("Confirm Completion", "Item packed. Proceed to complete packing?", "Yes", "No");
            if (!confirmed) return;

            // Check if all lines are packed
            if (await App.Db.AreAllLinesPackedAsync(_soHeader.Reference))
            {
                // All items are packed, prompt to complete the entire order
                bool completeOrder = await DisplayAlert("All Items Packed",
                    $"All items have been packed for {_soHeader.Reference}!\n\n" +
                    "Would you like to complete the entire packing phase now?", "Complete Order", "Continue Packing");

                if (completeOrder)
                {
                    // Complete the entire order
                    await CompletePackingPhase();
                }
                else
                {
                    // Go back to documents page to continue
                    await DisplayAlert("Line Complete",
                        $"Packing completed for {SelectedLine.ItemDesc}!\n\n" +
                        $"Returning to documents page to continue.", "OK");
                    await Shell.Current.GoToAsync("..");
                }
            }
            else
            {
                // Not all items packed, go back to documents page
                await DisplayAlert("Line Complete",
                    $"Packing completed for {SelectedLine.ItemDesc}!\n\n" +
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

    private async Task CompletePackingPhase()
    {
        try
        {
            var soLines = await App.Db.GetSoLinesByOrderNoAsync(_soHeader.Reference);
            var stageResult = await SendToApiForCompletionAsync(_soHeader.Reference, soLines);

            if (!stageResult.Success)
            {
                await DisplayAlert("Packing Not Saved",
                    $"Could not save the packed quantities for {_soHeader.Reference}:\n\n{stageResult.ErrorMessage}\n\n" +
                    "The order has been left un-packed so you can retry.", "OK");
                return;
            }

            // Set header flags to indicate packing is complete. With no picking stage, packing is
            // the last stage before checking and owns both flags.
            _soHeader.Packed = true;
            if (!WorkflowConfig.UsePicking) _soHeader.Picked = true;
            await App.Db.UpdateSoHeaderAsync(_soHeader);

            PickingWorkflowSession.Clear();
            await DisplayAlert("Packing Phase Complete",
                $"Packing successfully completed for entire SO!\n\n" +
                $"Next phase: Checking\n\n" +
                $"Order: {_soHeader.Reference}", "OK");

            // Pop back to PackingMain so the next SO can be entered. Do NOT push a fresh dashboard
            // here: PackingMain is a DI SINGLETON still sitting in this Shell stack, so navigating
            // into packing again from a pushed dashboard would put the same page instance into the
            // stack twice. Stack: PackingMain -> PackingDocumentsPage -> PackingPage.
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
            // Check if all lines are packed
            if (!await App.Db.AreAllLinesPackedAsync(_soHeader.Reference))
            {
                await DisplayAlert("Incomplete",
                    "Not all items have been packed yet.\n\n" +
                    "All items must be packed before completing the header.", "OK");
                return;
            }

            bool confirmed = await DisplayAlert("Confirm Header Completion",
                "All items packed. Complete packing phase for entire SO?", "Yes", "No");
            if (!confirmed) return;

            await CompletePackingPhase();
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
    private async Task ProcessPackingItem(SoLine matchingLine)
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

        decimal basisQty = PackBasisQty(lineInCollection);

        if (lineInCollection.PackedQty >= basisQty)
        {
            await DisplayAlert("Already Packed",
                $"{lineInCollection.ItemDesc} has already been fully packed ({lineInCollection.PackedQty}/{basisQty}).\n\n" +
                "No further packing allowed.", "OK");
            return;
        }

        if ((lineInCollection.PackedQty + quantity) > basisQty)
        {
            await DisplayAlert("Over-Packing Not Allowed",
                $"Cannot pack {quantity} more items. This would exceed the {PackBasisName} quantity of {basisQty}.\n\n" +
                $"Already packed: {lineInCollection.PackedQty}\n" +
                $"Remaining: {basisQty - lineInCollection.PackedQty}", "OK");
            return;
        }

        await UpdatePackingQuantity(lineInCollection, quantity);
    }

    private async Task UpdatePackingQuantity(SoLine lineInCollection, int quantity)
    {
        lineInCollection.PackedQty += quantity;

        decimal basisQty = PackBasisQty(lineInCollection);

        lineInCollection.PackCompleteDateTime = lineInCollection.PackedQty == basisQty ? DateTime.Now : null;

        // Set Packed flag when quantities match exactly
        lineInCollection.Packed = lineInCollection.PackedQty == basisQty;

        // Ensure PackedBy is set when completing the line
        if (string.IsNullOrEmpty(lineInCollection.PackedBy))
        {
            lineInCollection.PackedBy = GetCurrentUserName();
        }

        // Ensure PackStartDateTime is set if it's not already set
        if (lineInCollection.PackStartDateTime == null)
        {
            lineInCollection.PackStartDateTime = DateTime.Now;
        }

        // With no picking stage, packing is the last stage before checking and owns BOTH flags —
        // otherwise the pick columns stay at zero, which reads as "nothing picked" in the row
        // colours, discrepancy checks and PackingOutstandingQty. Must run after PackedBy/
        // PackStartDateTime are set above, since it copies them. Same rule server-side in
        // UpdateSalesOrderStageController.
        if (!WorkflowConfig.UsePicking)
        {
            lineInCollection.Picked = lineInCollection.Packed;
            lineInCollection.PickedQty = lineInCollection.PackedQty;
            lineInCollection.PickedBy = lineInCollection.PackedBy;
            lineInCollection.PickStartDateTime = lineInCollection.PackStartDateTime;
            lineInCollection.PickCompleteDateTime = lineInCollection.PackCompleteDateTime;
        }

        // Run database update on background thread
        await Task.Run(async () =>
        {
            await App.Db.UpdateSoLineAsync(lineInCollection);
        });

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (SelectedLine?.Id == lineInCollection.Id)
            {
                SelectedLine = lineInCollection;
                SelectedItemFrame.BindingContext = lineInCollection;
                OnPropertyChanged(nameof(SelectedLine));
                UpdateInputState();
            }

            ClearInputFields();
            BarcodeEntry.Focus();
        });

        if (lineInCollection.Packed)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await DisplayAlert("Packing Complete",
                    $"{lineInCollection.ItemDesc} has been fully packed ({lineInCollection.PackedQty}/{basisQty})\n\n" +
                    "Packing complete, needs checking", "OK");
            });
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

            await ProcessPackingItem(matchingLine);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to save progress: {ex.Message}", "OK");
        }
    }

    private async Task ResetSelectedLine()
    {
        // Capture BEFORE clearing. _soHeader.Packed alone is not enough on a device that has just
        // downloaded the order — the line's own Packed bit is the reliable signal.
        bool wasPackedOnServer = _soHeader.Packed || SelectedLine.Packed;

        // Reset the selected line
        SelectedLine.PackedQty = 0;
        SelectedLine.PackStarted = false;
        SelectedLine.PackStartDateTime = null;
        SelectedLine.PackCompleteDateTime = null;
        SelectedLine.PackedBy = null;
        SelectedLine.Packed = false;

        // Also update the line in the collection
        var lineInCollection = _soLines.FirstOrDefault(l => l.Id == SelectedLine.Id);
        if (lineInCollection != null)
        {
            lineInCollection.PackedQty = 0;
            lineInCollection.PackStarted = false;
            lineInCollection.PackStartDateTime = null;
            lineInCollection.PackCompleteDateTime = null;
            lineInCollection.PackedBy = null;
            lineInCollection.Packed = false;
        }

        // With no picking stage, packing mirrors itself into the pick columns — so a reset has to
        // clear those too, or the line still reads as picked with a stale PickedQty.
        if (!WorkflowConfig.UsePicking)
        {
            foreach (var target in new[] { SelectedLine, lineInCollection })
            {
                if (target == null) continue;
                target.PickedQty = 0;
                target.Picked = false;
                target.PickStarted = false;
                target.PickedBy = null;
                target.PickStartDateTime = null;
                target.PickCompleteDateTime = null;
            }
        }

        // Run database update on background thread
        await Task.Run(async () =>
        {
            await App.Db.UpdateSoLineAsync(SelectedLine);
        });

        // If packing had already been COMPLETED and posted, undo it on the server too, or the
        // header keeps Packed = 1 and a checker sails through WorkflowGate onto stale quantities.
        // Under Pack→Check packing also owns Picked, so clear that as well.
        if (wasPackedOnServer)
        {
            if (!await SendPackResetToApiAsync(_soHeader))
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Reset Not Sent",
                        $"{_soHeader.Reference} was reset on this device, but the server still has it " +
                        "as fully packed.\n\nRe-pack and complete the order to clear this, or the " +
                        "checker may work off the old quantities.", "OK");
                });
            }
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            PackingInputSection.IsVisible = false;
            StartPackingButton.IsVisible = true;
            SelectedItemFrame.IsVisible = true;

            SelectedItemFrame.BindingContext = null;
            SelectedItemFrame.BindingContext = SelectedLine;
            OnPropertyChanged(nameof(SelectedLine));

            UpdateInputState();
        });

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await DisplayAlert("Reset Complete", $"Successfully reset {SelectedLine.ItemDesc}", "OK");
        });
    }

    /// <summary>
    /// Sends the per-line packed quantities to api/UpdateSalesOrderStage. Before this, packing
    /// posted NOTHING (the method was a stub returning true), so the packed quantities never left
    /// the device and the server's Packed flag was never set — an order could not reach
    /// "Authorize Release". The server also stamps the header flags per the workflow matrix.
    /// </summary>
    private async Task<StageSyncService.StageSyncResult> SendToApiForCompletionAsync(string soNumber, List<SoLine> soLines)
    {
        return await StageSyncService.SendAsync(soNumber, StageSyncService.StagePacking, soLines);
    }

    /// <summary>
    /// Clears the server's completed-packing flags after a line was reset here, so the order stops
    /// satisfying the authorization filter and checking's gate blocks it again. Sends Picked too
    /// when packing owns that flag (no picking stage). Mirrors PickingPage.SendPickResetToApiAsync.
    /// </summary>
    private async Task<bool> SendPackResetToApiAsync(SoHeader soHeader)
    {
        try
        {
            using var client = new HttpClient();

            var payload = new SalesHeaderUpdateRequest
            {
                Reference = soHeader.Reference,
                Packed = false,
                Picked = WorkflowConfig.UsePicking ? (bool?)null : false
            };

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{AppConfig.ApiBaseUrl}UpdateSalesOrderHeader", content);

            if (response.IsSuccessStatusCode)
                return true;

            System.Diagnostics.Debug.WriteLine(
                $"SendPackResetToApiAsync failed: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SendPackResetToApiAsync error: {ex.Message}");
            return false;
        }
    }
    #endregion
}