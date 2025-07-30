using AmaScan.sqliteModels;
using AmaScan.Classes;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Linq;
using SQLite;
using Microsoft.Maui.Controls;

namespace AmaScan;

[QueryProperty(nameof(StockCodeQuery), "stockCode")]
public partial class StockCountPage : ContentPage, INotifyPropertyChanged
{
    private readonly DatabaseHelper _dbHelper;
    private readonly UserSession _userSession;
    private StockCountItem _currentItem;
    private string _stockCodeQuery;
    private string _currentUser;
    private CountPhase _currentPhase;
    private bool _isPackMode = true; // Default to pack mode

    public event PropertyChangedEventHandler PropertyChanged;

    #region Properties
    public string StockCodeQuery
    {
        get => _stockCodeQuery;
        set
        {
            if (_stockCodeQuery != value)
            {
                _stockCodeQuery = Uri.UnescapeDataString(value);
                _ = LoadStockCountItemAsync(_stockCodeQuery);
            }
        }
    }

    public string StockCode => _currentItem?.StockCode ?? "";
    public string StockDescription => _currentItem?.StockDescription ?? "";
    public decimal Level => _currentItem?.Level ?? 0;
    public decimal Count1Qty => _currentItem?.Count1Qty ?? 0;
    public decimal Count2Qty => _currentItem?.Count2Qty ?? 0;
    public decimal ConfirmCountQty => _currentItem?.ConfirmCountQty ?? 0;
    public string CountBy => _currentItem?.CountBy ?? "";
    public string ConfirmBy => _currentItem?.ConfirmBy ?? "";
    public bool IsCountComplete => _currentItem?.CountComplete ?? false;


    public string StatusText => GetStatusText();
    public Color StatusTextColor => GetStatusTextColor();
    #endregion

    #region Constructor and Lifecycle
    public StockCountPage()
    {
        InitializeComponent();
        BindingContext = this;
        _dbHelper = new DatabaseHelper(new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags));
        _userSession = App.Services.GetRequiredService<UserSession>();
        _currentUser = GetCurrentUserName();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        InitializeUI();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Clear references to help garbage collection
        _currentItem = null;
        ClearInputFields();
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    #endregion

    #region Initialization
    private void InitializeUI()
    {
        ManualInputSwitch.IsToggled = false;
        BarcodeEntry.IsReadOnly = true;
        
        // Initialize scan mode buttons (Pack is default)
        UpdateScanModeButtons(true);
        
        // Show start counting button initially, hide input section
        StockCountInputSection.IsVisible = false;
        StartCountingButton.IsVisible = true;
        
        ClearInputFields();
    }

    private string GetCurrentUserName() => _userSession.CurrentUser?.UserName ?? "Unknown User";

    private async Task LoadStockCountItemAsync(string stockCode)
    {
        try
        {
            _currentItem = await _dbHelper.GetStockCountItemByCodeAsync(stockCode);
            if (_currentItem == null)
            {
                await DisplayAlert("Error", $"Stock count item not found: {stockCode}", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            DetermineCurrentPhase();
            UpdateUI();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load stock count item: {ex.Message}", "OK");
        }
    }

    private void UpdateUI()
    {
        OnPropertyChanged(nameof(StockCode));
        OnPropertyChanged(nameof(StockDescription));
        OnPropertyChanged(nameof(Count1Qty));
        OnPropertyChanged(nameof(Count2Qty));
        OnPropertyChanged(nameof(ConfirmCountQty));
        OnPropertyChanged(nameof(CountBy));
        OnPropertyChanged(nameof(ConfirmBy));
        OnPropertyChanged(nameof(IsCountComplete));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTextColor));
    }
    #endregion

    #region Phase Management
    private enum CountPhase
    {
        Phase1,  // First count
        Phase2,  // Second count (same user)
        Phase3,  // Confirm count (different user)
        Complete
    }

    private void DetermineCurrentPhase()
    {
        if (_currentItem == null)
        {
            _currentPhase = CountPhase.Phase1;
            return;
        }

        if (_currentItem.CountComplete)
        {
            _currentPhase = CountPhase.Complete;
            return;
        }

        // Phase 1: No phase completion yet
        if (!_currentItem.Phase1Complete)
        {
            _currentPhase = CountPhase.Phase1;
            return;
        }

        // Phase 2: Phase 1 complete, Phase 2 not complete yet (same user)
        if (_currentItem.Phase1Complete && !_currentItem.Phase2Complete && _currentItem.CountBy == _currentUser)
        {
            _currentPhase = CountPhase.Phase2;
            return;
        }

        // Phase 3: Phase 2 complete, confirm count in progress (different user)
        if (_currentItem.Phase2Complete && _currentItem.CountBy != _currentUser)
        {
            _currentPhase = CountPhase.Phase3;
            return;
        }

        // Default to Phase 1
        _currentPhase = CountPhase.Phase1;
    }

    private bool CanCurrentUserCount()
    {
        if (_currentItem == null) return false;

        return _currentPhase switch
        {
            CountPhase.Phase1 => true, // Anyone can start
            CountPhase.Phase2 => _currentItem.CountBy == _currentUser, // Same user as Phase 1
            CountPhase.Phase3 => _currentItem.CountBy != _currentUser, // Different user required
            _ => false
        };
    }
    #endregion

    #region Status and Validation
    private string GetStatusText()
    {
        if (_currentItem == null) return "";

        if (_currentItem.CountComplete)
            return _currentItem.CountString ? "Needs Accounting Review" : "Count Complete";

        var discrepancyStatus = GetCurrentPhaseDiscrepancyStatus();
        var (phaseName, currentQty) = _currentPhase switch
        {
            CountPhase.Phase1 => ("First Count", _currentItem.Count1Qty),
            CountPhase.Phase2 => ("Second Count", _currentItem.Count2Qty),
            CountPhase.Phase3 => ("Confirm Count", _currentItem.ConfirmCountQty),
            _ => ("", 0)
        };

        return currentQty > 0 
            ? $"{phaseName} in Progress: {currentQty} - {discrepancyStatus}"
            : _currentPhase == CountPhase.Phase3 
                ? "Ready for Confirm Count (Different User Required)"
                : $"Ready for {phaseName}";
    }

    private string GetCurrentPhaseDiscrepancyStatus()
    {
        if (_currentItem == null) return "";

        decimal currentCount = _currentPhase switch
        {
            CountPhase.Phase1 => _currentItem.Count1Qty,
            CountPhase.Phase2 => _currentItem.Count2Qty,
            CountPhase.Phase3 => _currentItem.ConfirmCountQty,
            _ => 0
        };

        if (currentCount == 0) return "";

        if (currentCount == _currentItem.Level)
        {
            return "Matches Expected Level";
        }
        else if (currentCount > _currentItem.Level)
        {
            return $"Over Expected Level";
        }
        else
        {
            return "Under Expected Level";
        }
    }

    private Color GetStatusTextColor()
    {
        if (_currentItem == null) return Colors.Black;

        if (_currentItem.CountComplete)
            return _currentItem.CountString ? Colors.Red : Colors.Green;

        return Colors.Blue;
    }


    #endregion

    #region Event Handlers
    private async void OnBarcodeEntered(object sender, EventArgs e)
    {
        if (!CanCurrentUserCount())
        {
            string message = _currentPhase switch
            {
                CountPhase.Phase3 => "You cannot do the confirm count. A different user is required.",
                _ => "You cannot count this item at this time."
            };
            await DisplayAlert("Access Denied", message, "OK");
            return;
        }

        string scannedBarcode = BarcodeEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(scannedBarcode))
        {
            await DisplayAlert("Error", "Please scan a barcode.", "OK");
            return;
        }

        await ProcessBarcode(scannedBarcode);
        BarcodeEntry.Text = string.Empty;
    }

    private async void OnRestartClicked(object sender, EventArgs e)
    {
        if (_currentItem == null)
        {
            await DisplayAlert("No Item Selected", "Please select an item to reset.", "OK");
            return;
        }

        if (_currentItem.CountComplete)
        {
            await DisplayAlert(
                "Reset Not Allowed",
                "This count has been completed and cannot be reset. Please contact your administrator if you need to modify a completed count.",
                "OK"
            );
            return;
        }

        bool confirm = await DisplayAlert(
            "Reset Count",
            $"Reset {_currentItem.StockDescription}?\n\nThis will clear the items counted.",
            "Yes", "No"
        );

        if (!confirm) return;

        await ResetCurrentItem();
    }

    private async void OnCompleteClicked(object sender, EventArgs e)
    {
        try
        {
            if (_currentItem == null)
            {
                await DisplayAlert("No Item Selected", "Please select an item to complete.", "OK");
                return;
            }

            if (!CanCurrentUserCount())
            {
                await DisplayAlert("Access Denied", "You cannot complete this phase.", "OK");
                return;
            }

            await CompleteCurrentPhase();
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

    private async void OnStartCountingClicked(object sender, EventArgs e)
    {
        if (!CanCurrentUserCount())
        {
            string message = _currentPhase switch
            {
                CountPhase.Phase3 => "You cannot do the confirm count. A different user is required.",
                _ => "You cannot count this item at this time."
            };
            await DisplayAlert("Access Denied", message, "OK");
            return;
        }

        StockCountInputSection.IsVisible = true;
        StartCountingButton.IsVisible = false;
        ClearInputFields();
        BarcodeEntry.Focus();
    }

    private void UpdateScanModeButtons(bool isPackMode)
    {
        _isPackMode = isPackMode;
        var selectedColor = Color.FromHex("#007AFF");
        var unselectedColor = Colors.LightGray;
        
        SingleButton.BackgroundColor = isPackMode ? unselectedColor : selectedColor;
        PackButton.BackgroundColor = isPackMode ? selectedColor : unselectedColor;
        SingleButton.TextColor = isPackMode ? Colors.Black : Colors.White;
        PackButton.TextColor = isPackMode ? Colors.White : Colors.Black;
    }

    private void OnSingleClicked(object sender, EventArgs e) => UpdateScanModeButtons(false);
    private void OnPackClicked(object sender, EventArgs e) => UpdateScanModeButtons(true);

    private void QuantityEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.NewTextValue)) return;

        if (!decimal.TryParse(e.NewTextValue, out decimal _))
        {
            ((Entry)sender).Text = e.OldTextValue;
        }
    }

    private async void QuantityEntry_Completed(object sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(BarcodeEntry.Text) && !string.IsNullOrEmpty(QuantityEntry.Text))
        {
            await ProcessBarcode(BarcodeEntry.Text);
        }
    }

    private async void OnSaveCountClicked(object sender, EventArgs e)
    {
        try
        {
            if (_currentItem == null)
            {
                await DisplayAlert("Error", "No item selected to save.", "OK");
                return;
            }

            if (!CanCurrentUserCount())
            {
                string message = _currentPhase switch
                {
                    CountPhase.Phase3 => "You cannot do the confirm count. A different user is required.",
                    _ => "You cannot count this item at this time."
                };
                await DisplayAlert("Access Denied", message, "OK");
                return;
            }

            string scannedBarcode = BarcodeEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(scannedBarcode))
            {
                await DisplayAlert("Error", "Please scan a barcode first.", "OK");
                return;
            }

            bool success = await ProcessBarcode(scannedBarcode);
            if (success)
            {
                BarcodeEntry.Text = string.Empty;
                QuantityEntry.Text = string.Empty;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to save count: {ex.Message}", "OK");
        }
    }
    #endregion

    #region Business Logic
    private async Task<bool> ProcessBarcode(string scannedBarcode)
    {
        if (_currentItem == null) return false;

        try
        {
            var stockItem = await _dbHelper.ResolveStockItemByBarcodeAsync(scannedBarcode);
            
            if (stockItem != null && stockItem.stock_code == _currentItem.StockCode)
            {
                // Check if it's a pack barcode
                // If barcode_lmmp exists and matches the scanned barcode, it's a pack barcode
                // If barcode_lmmp is the same as bar_code, then scanning bar_code should be treated as pack barcode
                bool isPackBarcode = !string.IsNullOrWhiteSpace(stockItem.barcode_lmmp) && 
                    (stockItem.barcode_lmmp.Equals(scannedBarcode, StringComparison.OrdinalIgnoreCase) ||
                     (stockItem.bar_code?.Equals(scannedBarcode, StringComparison.OrdinalIgnoreCase) == true && 
                      stockItem.barcode_lmmp.Equals(stockItem.bar_code, StringComparison.OrdinalIgnoreCase)));
                
                // If barcodes are the same (ambiguous), use the scan mode setting
                if (stockItem.bar_code?.Equals(stockItem.barcode_lmmp, StringComparison.OrdinalIgnoreCase) == true)
                {
                    isPackBarcode = _isPackMode;
                }
                
                // Get quantity from entry field
                decimal quantity = decimal.TryParse(QuantityEntry.Text, out decimal qty) ? qty : 1;
                
                // Calculate total to add - multiply by pack size for pack barcodes
                decimal quantityToAdd = isPackBarcode ? 
                    quantity * stockItem.pack.GetValueOrDefault(1) : 
                    quantity;
                
                // Add to the current phase count
                switch (_currentPhase)
                {
                    case CountPhase.Phase1:
                        _currentItem.Count1Qty += quantityToAdd;
                        if (string.IsNullOrEmpty(_currentItem.CountBy))
                            _currentItem.CountBy = _currentUser;
                        break;
                    case CountPhase.Phase2:
                        _currentItem.Count2Qty += quantityToAdd;
                        break;
                    case CountPhase.Phase3:
                        _currentItem.ConfirmCountQty += quantityToAdd;
                        if (string.IsNullOrEmpty(_currentItem.ConfirmBy))
                            _currentItem.ConfirmBy = _currentUser;
                        break;
                }
                
                await _dbHelper.UpdateStockCountItemAsync(_currentItem);
                UpdateUI();
                
                ClearInputFields();
                return true;
            }
            else
            {
                await DisplayAlert("Error", "Barcode does not match current item.", "OK");
                return false;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to process barcode: {ex.Message}", "OK");
            return false;
        }
    }

    private async Task CompleteCurrentPhase()
    {
        switch (_currentPhase)
        {
            case CountPhase.Phase1:
                await CompletePhase1();
                break;
            case CountPhase.Phase2:
                await CompletePhase2();
                break;
            case CountPhase.Phase3:
                await CompletePhase3();
                break;
            default:
                await DisplayAlert("Error", "Invalid phase state.", "OK");
                break;
        }
    }

    private async Task CompletePhase1()
    {
        // Check if first count matches expected level
        if (_currentItem.Count1Qty == _currentItem.Level)
        {
            await DisplayAlert(
                "First Count Correct",
                $"First count: {_currentItem.Count1Qty}\n\n" +
                "First count matches expected level. Setting confirm count to first count and completing item.",
                "OK"
            );

            // Set confirm count to first count and complete the item
            _currentItem.ConfirmCountQty = _currentItem.Count1Qty;
            _currentItem.ConfirmBy = _currentUser;
            _currentItem.CountComplete = true;
            _currentItem.CountString = false; // No discrepancy
            await _dbHelper.UpdateStockCountItemAsync(_currentItem);
            _currentPhase = CountPhase.Complete;
            UpdateUI();

            // Navigate back to stock count list
            await Shell.Current.GoToAsync("..");
        }
        else
        {
            await DisplayAlert(
                "First Count Incorrect",
                $"First count: {_currentItem.Count1Qty}\n\n" +
                "First count differs from expected level. You must count again for verification.",
                "OK"
            );

            // Mark Phase 1 as complete and save to database
            _currentItem.Phase1Complete = true;
            await _dbHelper.UpdateStockCountItemAsync(_currentItem);

            // Move to Phase 2 for second count
            _currentPhase = CountPhase.Phase2;
            StockCountInputSection.IsVisible = false;
            UpdateUI();
        }
    }

    private async Task CompletePhase2()
    {
        // Check if second count matches expected level
        if (_currentItem.Count2Qty == _currentItem.Level)
        {
            await DisplayAlert(
                "Second Count Correct",
                $"Second count: {_currentItem.Count2Qty}\n\n" +
                "Second count matches expected level. Setting confirm count to second count and completing item.",
                "OK"
            );

            // Set confirm count to second count and complete the item
            _currentItem.ConfirmCountQty = _currentItem.Count2Qty;
            _currentItem.ConfirmBy = _currentUser;
            _currentItem.CountComplete = true;
            _currentItem.CountString = false; // No discrepancy
            await _dbHelper.UpdateStockCountItemAsync(_currentItem);
            _currentPhase = CountPhase.Complete;
            UpdateUI();

            // Navigate back to stock count list
            await Shell.Current.GoToAsync("..");
        }
        else
        {
            await DisplayAlert(
                "Second Count Incorrect",
                $"Second count: {_currentItem.Count2Qty}\n\n" +
                "Second count differs from expected level. A different user must do the confirm count.",
                "OK"
            );

            // Mark Phase 2 as complete and save to database
            _currentItem.Phase2Complete = true;
            await _dbHelper.UpdateStockCountItemAsync(_currentItem);

            // Move to Phase 3 (confirm count by different user)
            _currentPhase = CountPhase.Phase3;
            StockCountInputSection.IsVisible = false;
            UpdateUI();
        }
    }

    private async Task CompletePhase3()
    {
        // Check if confirm count matches expected level
        if (_currentItem.ConfirmCountQty == _currentItem.Level)
        {
            await DisplayAlert(
                "Confirm Count Correct",
                $"Confirm count: {_currentItem.ConfirmCountQty}\n\n" +
                "Confirm count matches expected level. Count is accurate!",
                "OK"
            );
            _currentItem.CountString = false; // No discrepancy
        }
        else
        {
            // Require supervisor password for incorrect confirm count
            bool supervisorAuthorized = await PromptSupervisorAuthorizationAsync();
            
            if (!supervisorAuthorized)
            {
                await DisplayAlert("Access Denied", "Supervisor authorization required to complete count with discrepancies.", "OK");
                return;
            }

            await DisplayAlert(
                "Confirm Count Incorrect - Supervisor Authorized",
                $"Confirm count: {_currentItem.ConfirmCountQty}\n\n" +
                "All three counts differ from expected level.\n\n" +
                "Item has been marked for accounting review.",
                "OK"
            );
            _currentItem.CountString = true; // Mark for accounting review
        }

        // Set item as complete regardless of accuracy
        _currentItem.CountComplete = true;
        await _dbHelper.UpdateStockCountItemAsync(_currentItem);
        _currentPhase = CountPhase.Complete;
        UpdateUI();

        // Navigate back to stock count list
        await Shell.Current.GoToAsync("..");
    }

    private async Task<bool> PromptSupervisorAuthorizationAsync()
    {
        string password = await DisplayPromptAsync("Supervisor Authorization", "Enter supervisor password:", "OK", "Cancel", "Password", -1, Keyboard.Text);

        if (string.IsNullOrWhiteSpace(password))
            return false;

        // TODO: Implement actual supervisor password validation
        // For now, accept any non-empty password
        return true;
    }

    private async Task ResetCurrentItem()
    {
        // Get the phase name BEFORE we change it
        var resetPhaseName = _currentPhase switch
        {
            CountPhase.Phase1 => "First Count",
            CountPhase.Phase2 => "Second Count", 
            CountPhase.Phase3 => "Confirm Count",
            _ => "Current Phase"
        };

        // Reset only the current phase
        switch (_currentPhase)
        {
            case CountPhase.Phase1:
                _currentItem.Count1Qty = 0;
                _currentItem.CountBy = null;
                break;
            case CountPhase.Phase2:
                _currentItem.Count2Qty = 0;
                break;
            case CountPhase.Phase3:
                _currentItem.ConfirmCountQty = 0;
                _currentItem.ConfirmBy = null;
                break;
        }

        await _dbHelper.UpdateStockCountItemAsync(_currentItem);
        DetermineCurrentPhase(); // Recalculate phase
        UpdateUI();

        await DisplayAlert("Reset Complete", $"Successfully reset {resetPhaseName} for {_currentItem.StockDescription}", "OK");
    }

    private void ClearInputFields()
    {
        BarcodeEntry.Text = string.Empty;
        QuantityEntry.Text = string.Empty;
    }
    #endregion
} 