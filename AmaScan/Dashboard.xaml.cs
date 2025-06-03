using AmaScan.Classes;

namespace AmaScan;

public partial class Dashboard : ContentPage
{
    private readonly UserSession _userSession;

    public bool CanReceive => _userSession.CurrentUser?.CanReceive == true;
    public bool CanTransfer => _userSession.CurrentUser?.CanTransfer == true;

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
       // await Navigation.PushAsync(new Page3());
    }

    private async void OnPage4Clicked(object sender, EventArgs e)
    {
        //await Navigation.PushAsync(new Page4());
    }
}