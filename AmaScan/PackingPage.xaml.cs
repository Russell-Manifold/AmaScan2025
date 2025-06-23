using AmaScan.sqliteModels;
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
    private readonly DatabaseHelper _dbHelper;
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
    public PackingPage()
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
        OnPropertyChanged(nameof(PackerName));
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
                StartPackingForSelectedLine(freshLine, false);
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
                await DisplayAlert("No Item Selected", "Please select an item to pack.", "OK");
                await Shell.Current.GoToAsync("..");
            });
        }
    }
    #endregion

    private string GetCurrentUserName() => _userSession.CurrentUser?.UserName ?? "Unknown User";

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

        bool isFullyPacked = SelectedLine.PackedQty >= SelectedLine.PickedQty;

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
    private async void OnStartPackingClicked(object sender, EventArgs e)
    {
        string currentUserName = GetCurrentUserName();

        // Set the header-level user for packing
        await _dbHelper.SetPhaseUserAsync(_soHeader.Reference, currentUserName, "packing");

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

            var matchingLine = await _dbHelper.GetSoLineByBarcodeAsync(_soHeader.Reference, scannedBarcode);

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

            if (!SelectedLine.Packed)
            {
                var currentLineOutstanding = SelectedLine.PackingOutstandingQty;
                await DisplayAlert("Incomplete",
                    $"The selected item must be packed before completing.\n\n" +
                    $"{currentLineOutstanding} items still need packing", "OK");
                return;
            }

            bool confirmed = await DisplayAlert("Confirm Completion", "Item packed. Proceed to complete packing?", "Yes", "No");
            if (!confirmed) return;

            // Check if all lines are packed
            if (await _dbHelper.AreAllLinesPackedAsync(_soHeader.Reference))
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
            var soLines = await _dbHelper.GetSoLinesByOrderNoAsync(_soHeader.Reference);
            bool success = await SendToApiForCompletionAsync(_soHeader.Reference, soLines);

            if (success)
            {
                // Set header flags to indicate packing is complete
                _soHeader.Packed = true;
                await _dbHelper.UpdateSoHeaderAsync(_soHeader);

                PickingWorkflowSession.Clear();
                await DisplayAlert("Packing Phase Complete",
                    $"Packing successfully completed for entire SO!\n\n" +
                    $"Next phase: Checking\n\n" +
                    $"Order: {_soHeader.Reference}", "OK");

                var dashboardPage = App.Services.GetRequiredService<Dashboard>();
                await Navigation.PushAsync(dashboardPage);
            }
            else
            {
                await DisplayAlert("Error", "Failed to complete packing. Please try again.", "OK");
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
            // Check if all lines are packed
            if (!await _dbHelper.AreAllLinesPackedAsync(_soHeader.Reference))
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

        if (lineInCollection.PackedQty >= lineInCollection.PickedQty)
        {
            await DisplayAlert("Already Packed",
                $"{lineInCollection.ItemDesc} has already been fully packed ({lineInCollection.PackedQty}/{lineInCollection.PickedQty}).\n\n" +
                "No further packing allowed.", "OK");
            return;
        }

        if ((lineInCollection.PackedQty + quantity) > lineInCollection.PickedQty)
        {
            await DisplayAlert("Over-Packing Not Allowed",
                $"Cannot pack {quantity} more items. This would exceed the picked quantity of {lineInCollection.PickedQty}.\n\n" +
                $"Already packed: {lineInCollection.PackedQty}\n" +
                $"Remaining: {lineInCollection.PickedQty - lineInCollection.PackedQty}", "OK");
            return;
        }

        await UpdatePackingQuantity(lineInCollection, quantity);
    }

    private async Task UpdatePackingQuantity(SoLine lineInCollection, int quantity)
    {
        lineInCollection.PackedQty += quantity;

        lineInCollection.PackCompleteDateTime = lineInCollection.PackedQty == lineInCollection.PickedQty ? DateTime.Now : null;

        // Set Packed flag when quantities match exactly
        lineInCollection.Packed = lineInCollection.PackedQty == lineInCollection.PickedQty;

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

        if (lineInCollection.Packed)
        {
            await DisplayAlert("Packing Complete",
                $"{lineInCollection.ItemDesc} has been fully packed ({lineInCollection.PackedQty}/{lineInCollection.PickedQty})\n\n" +
                "Packing complete, needs checking", "OK");
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

            await ProcessPackingItem(matchingLine);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to save progress: {ex.Message}", "OK");
        }
    }

    private async Task ResetSelectedLine()
    {
        // Reset the selected line
        SelectedLine.PackedQty = 0;
        SelectedLine.PackStartDateTime = null;
        SelectedLine.PackCompleteDateTime = null;
        SelectedLine.PackedBy = null;
        SelectedLine.Packed = false;

        // Also update the line in the collection
        var lineInCollection = _soLines.FirstOrDefault(l => l.Id == SelectedLine.Id);
        if (lineInCollection != null)
        {
            lineInCollection.PackedQty = 0;
            lineInCollection.PackStartDateTime = null;
            lineInCollection.PackCompleteDateTime = null;
            lineInCollection.PackedBy = null;
            lineInCollection.Packed = false;
        }

        await _dbHelper.UpdateSoLineAsync(SelectedLine);

        PackingInputSection.IsVisible = false;
        StartPackingButton.IsVisible = true;
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