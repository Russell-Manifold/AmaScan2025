using AmaScan.Data;
using Data.Model;
using System.Text;
using AmaScan.Classes;
using Newtonsoft.Json;

namespace AmaScan
{
    public partial class MainPage : ContentPage
    {
        AmaScanDatabase database;
        private readonly HttpClient _httpClient;
        private readonly UserSession _userSession;

        public MainPage(AmaScanDatabase amaScanDatabase)
        {
            InitializeComponent();
            database = amaScanDatabase;
            _userSession = App.Services.GetRequiredService<UserSession>();
            BindingContext = this;
        }

        private async void OnLogin_Clicked(object sender, EventArgs e)
        {
            loadingIndicator.IsVisible = true;
            loadingIndicator.IsRunning = true;
            try
            {
                var username = usernameEntry.Text?.Trim();
                var password = passwordEntry.Text;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    await DisplayAlert("Error", "Please enter both username and password.", "OK");
                    return;
                }

                var user = await LoginAsync(username, password);
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;

                if (user != null)
                {
                    _userSession.CurrentUser = user;
                    await DisplayAlert("", $"Welcome {user.UserName} ({user.RoleName})", "OK");

                    var dashboardPage = App.Services.GetRequiredService<Dashboard>();
                    await Navigation.PushAsync(dashboardPage);
                }
                else
                {
                    await DisplayAlert("Login Failed", "Invalid credentials or server error.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Exception: {ex.Message}", "OK");
            }
        }

        public async Task<User?> LoginAsync(string username, string password)
        {
            var client = new HttpClient();

            var loginPayload = new
            {
                Username = username,
                Password = password
            };

            string json = JsonConvert.SerializeObject(loginPayload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync($"{AppConfig.ApiBaseUrl}/GetUser/GetUserAsync", content);
                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    var user = JsonConvert.DeserializeObject<User>(responseContent);
                    return user;
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Login failed: {response.StatusCode} - {error}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login error: {ex.Message}");
                return null;
            }
        }
    }
}