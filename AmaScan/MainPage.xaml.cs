using AmaScan.Data;
using Data.Model;
using System.Text;
using AmaScan.Classes;
using Newtonsoft.Json;

namespace AmaScan
{
    public partial class MainPage : ContentPage
    {
        private readonly HttpClient _httpClient;
        private readonly UserSession _userSession;

        public MainPage(AmaScanDatabase amaScanDatabase)
        {
            InitializeComponent();
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
                var password = passwordEntry.Text?.Trim();

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    await DisplayAlert("Error", "Please enter both username and password.", "OK");
                    return;
                }

                // Run login on background thread to avoid blocking UI
                var user = await Task.Run(async () => await LoginAsync(username, password));
                
                if (user != null)
                {
                    await DbReset.ResetAsync();
                    _userSession.CurrentUser = user;
                    await DisplayAlert("", $"Welcome {user.UserName} ({user.RoleName})", "OK");
                    var dashboard = App.Services.GetRequiredService<Dashboard>();
                    await Navigation.PushAsync(dashboard);
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
            finally
            {
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;
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
                var response = await client.PostAsync($"{AppConfig.ApiBaseUrl}GetUser/GetUserAsync", content);
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

        private async void OnSettingsClicked(object sender, EventArgs e)
        {
            var settingsPage = App.Services.GetRequiredService<SettingsPage>();
            await Navigation.PushAsync(settingsPage);
        }

        private async void OnTestConnectionClicked(object sender, EventArgs e)
        {
            loadingIndicator.IsVisible = true;
            loadingIndicator.IsRunning = true;

//#if DEBUG
//            System.Net.ServicePointManager.ServerCertificateValidationCallback +=
//                (sender, cert, chain, sslPolicyErrors) => true;
//#endif

//            var client = new HttpClient
//            {
//                Timeout = TimeSpan.FromSeconds(10)
//            };

//            var resp = await client.GetAsync("http://192.168.0.100:8052/api/connection/check-connection");

            try
            {
                // Run connection test on background thread
                var result = await Task.Run(async () =>
                {
                    var client = new HttpClient();
                    var response = await client.GetAsync($"{AppConfig.ApiBaseUrl}connection/check-connection");
                    return new { response, content = await response.Content.ReadAsStringAsync() };
                });

                if (result.response.IsSuccessStatusCode)
                {
                    var user = JsonConvert.DeserializeObject<User>(result.content);
                    await DisplayAlert("Connection Test", "Connection Successful.", "OK");
                }
                else
                {
                    await DisplayAlert("Connection Test", $"Connection failed: {result.response.StatusCode} - {result.content}", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Connection Test", $"Connection failed: {ex.Message}", "OK");
            }
            finally
            {
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;
            }
        }
    }
}