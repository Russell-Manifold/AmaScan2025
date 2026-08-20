using AmaScan.Classes;

namespace AmaScan;

public partial class DashboardPicking : ContentPage
{
    private readonly UserSession _userSession;

    public bool CanReceive => _userSession.CurrentUser?.CanReceive == true;
    public bool CanTransfer => _userSession.CurrentUser?.CanTransfer == true;
    public bool CanPick => _userSession.CurrentUser?.CanPick == true;
    public bool CanPack => _userSession.CurrentUser?.CanPack == true;
    public bool CanCheck => _userSession.CurrentUser?.CanCheck == true;
    public bool CanAuth => _userSession.CurrentUser?.CanAuthPicking == true;

    // Two separate gates: the company decides whether a stage runs at all (WorkflowConfig,
    // set on the web Company Config page), the role decides who is allowed to run it.
    public bool ShowPicking => CanPick && WorkflowConfig.UsePicking;
    public bool ShowPacking => CanPack && WorkflowConfig.UsePacking;
    public string WorkflowDescription => $"Workflow: {WorkflowConfig.Describe()}";

    public string UserName => _userSession.CurrentUser?.UserName ?? "Guest";
    public string RoleName => _userSession.CurrentUser?.RoleName ?? "No Role Assigned";
    public string UseNRole => $"User: {UserName} ({RoleName})";
    public UserSession UserSession => _userSession;
    public DashboardPicking()
    {
        InitializeComponent();
        _userSession = App.Services.GetRequiredService<UserSession>();
        BindingContext = this;
    }

    private async void OnPage3Clicked(object sender, EventArgs e)
    {
        var pickingMain = App.Services.GetRequiredService<PickingMain>();
        await Navigation.PushAsync(pickingMain);
    }

    private async void OnPage4Clicked(object sender, EventArgs e)
    {
        var packingMain = App.Services.GetRequiredService<PackingMain>();
        await Navigation.PushAsync(packingMain);
    }

    private async void OnPage5Clicked(object sender, EventArgs e)
    {
        var checkingMain = App.Services.GetRequiredService<CheckingMain>();
        await Navigation.PushAsync(checkingMain);
    }

    private async void OnPage6Clicked(object sender, EventArgs e)
    {
        var authorizationMain = App.Services.GetRequiredService<AuthorizationMain>();
        await Navigation.PushAsync(authorizationMain);
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert("Log Out", "Are you sure you want to log out?", "Yes", "No");
        if (!confirm) return;
        _userSession.CurrentUser = null;
        // Reset the Shell navigation stack back to the login page. PopToRootAsync did
        // nothing here because DashboardPicking is the root of the current Shell stack.
        await Shell.Current.GoToAsync("//MainPage");
    }
}