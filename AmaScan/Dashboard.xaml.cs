using AmaScan.Classes;

namespace AmaScan;
public partial class Dashboard : ContentPage
{
    private readonly UserSession _userSession;
    public bool CanReceive => _userSession.CurrentUser?.CanReceive == true;
    public bool CanTransfer => _userSession.CurrentUser?.CanTransfer == true;
    public bool CanPick => _userSession.CurrentUser?.CanPick == true;
    public bool CanPack => _userSession.CurrentUser?.CanPack == true;
    public bool CanCheck => _userSession.CurrentUser?.CanCheck == true;
    public bool CanAuth => _userSession.CurrentUser?.CanAuthPicking == true;

    // New property to show picking workflow access if user has any picking-related permissions
    public bool CanAccessPickingWorkflow => CanPick || CanPack || CanCheck || CanAuth;

    public string UserName => _userSession.CurrentUser?.UserName ?? "Guest";
    public string RoleName => _userSession.CurrentUser?.RoleName ?? "No Role Assigned";
    public string UseNRole => $"User: {UserName} ({RoleName})";
    public UserSession UserSession => _userSession;
    public Dashboard(UserSession userSession)
    {
        InitializeComponent();
        _userSession = userSession;
        BindingContext = this;
    }

    private async void OnPage1Clicked(object sender, EventArgs e)
    {
        var ReceivingMain = App.Services.GetRequiredService<ReceivingMain>();
        await Navigation.PushAsync(ReceivingMain);
        //await Shell.Current.GoToAsync(nameof(ReceivingMain));
    }

    private async void OnPage2Clicked(object sender, EventArgs e)
    {
        var TransferMain = App.Services.GetRequiredService<TransferMainPage>();
        await Navigation.PushAsync(TransferMain);
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        var SettingsPage = App.Services.GetRequiredService<SettingsPage>();
        await Navigation.PushAsync(SettingsPage);
        //await Shell.Current.GoToAsync(nameof(SettingsPage));
    }

    private async void OnPage3Clicked(object sender, EventArgs e)
    {
        var DashboardPicking = App.Services.GetRequiredService<DashboardPicking>();
        await Navigation.PushAsync(DashboardPicking);
    }

    private async void OnPage4Clicked(object sender, EventArgs e)
    {
        var ReturnsPage = App.Services.GetRequiredService<ReturnsPage>();
        await Navigation.PushAsync(ReturnsPage);
    }
}