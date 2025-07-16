namespace AmaScan.Classes
{
    public static class AppConfig
    {
        private const string OffSiteApiUrl = "http://192.168.0.132:8052/api/";
        private const string OnSiteApiUrl = "http://175.25.97.2:8079/api/";
        private const string ApiModeKey = "ApiMode"; // "OnSite" or "OffSite"

        public static string ApiBaseUrl
        {
            get
            {
                var mode = Preferences.Get(ApiModeKey, "OnSite");
                return mode == "OffSite" ? OffSiteApiUrl : OnSiteApiUrl;
            }
        }

        public static bool IsOnSite
        {
            get => Preferences.Get(ApiModeKey, "OnSite") == "OnSite";
            set => Preferences.Set(ApiModeKey, value ? "OnSite" : "OffSite");
        }

        // You can add other device-specific settings the same way:
        public static string DeviceName
        {
            get => Preferences.Get(nameof(DeviceName), string.Empty);
            set => Preferences.Set(nameof(DeviceName), value);
        }

        public static void Reset()
        {
            Preferences.Remove(ApiModeKey);
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
