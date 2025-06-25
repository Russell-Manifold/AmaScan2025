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
    public string UserName => _userSession.CurrentUser?.UserName ?? "Guest";
    public string RoleName => _userSession.CurrentUser?.RoleName ?? "No Role Assigned";
    public string UseNRole => $"User: {UserName} ({RoleName})";
    public UserSession UserSession => _userSession;
    public DashboardPicking(UserSession userSession)
    {
        InitializeComponent();
        _userSession = userSession;
        BindingContext = this;
    }

    private async void OnPage3Clicked(object sender, EventArgs e)
    {
        var PickingMain = App.Services.GetRequiredService<PickingMain>();
        await Navigation.PushAsync(PickingMain);
    }

    private async void OnPage4Clicked(object sender, EventArgs e)
    {
        var PackingMain = App.Services.GetRequiredService<PackingMain>();
        await Navigation.PushAsync(PackingMain);
    }

    private async void OnPage5Clicked(object sender, EventArgs e)
    {
        var CheckingMain = App.Services.GetRequiredService<CheckingMain>();
        await Navigation.PushAsync(CheckingMain);
    }

    private async void OnPage6Clicked(object sender, EventArgs e)
    {
        var AuthorizationMain = App.Services.GetRequiredService<AuthorizationMain>();
        await Navigation.PushAsync(AuthorizationMain);
    }
}