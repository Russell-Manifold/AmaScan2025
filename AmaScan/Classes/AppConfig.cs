namespace AmaScan.Classes
{
    class AppConfig
    {
        private const string DefaultApiUrl = "http://192.168.0.132:8052/api/";//
        //private const string DefaultApiUrl = "http://175.25.97.2:8079/api/";
        //private const string DefaultApiUrl = "http://192.168.18.111:8084/api/";

        public static string ApiBaseUrl
        {
            get => Preferences.Get(nameof(ApiBaseUrl), DefaultApiUrl);
            set => Preferences.Set(nameof(ApiBaseUrl), value);
        }

        // You can add other device-specific settings the same way:
        public static string DeviceName
        {
            get => Preferences.Get(nameof(DeviceName), string.Empty);
            set => Preferences.Set(nameof(DeviceName), value);
        }

        public static void Reset()
        {
            Preferences.Remove(nameof(ApiBaseUrl));
            Preferences.Remove(nameof(DeviceName));
        }

        internal static HttpClient GetHttpClient()
        {
            var baseAddress = new Uri(ApiBaseUrl);
            var client = new HttpClient
            {
                BaseAddress = baseAddress,
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            return client;
        }
    }
}
