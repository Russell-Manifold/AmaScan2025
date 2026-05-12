namespace AmaScan.Classes
{
    public static class AppConfig
    {
        private const string OffSiteApiUrl = "http://192.168.0.121:8052/api/";
        private const string OnSiteApiUrl = "http://175.25.97.2:8079/api/";
        private const string ApiModeKey = "OnSite";
        private const string CustomApiUrlKey = "CustomApiUrl"; // NEW
        public static string ApiBaseUrl
        {
            get
            {
                var custom = Preferences.Get(CustomApiUrlKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(custom))
                    return custom;
                var mode = Preferences.Get(ApiModeKey, "OnSite");
                return mode == "OffSite" ? OffSiteApiUrl : OnSiteApiUrl;
            }
            set => Preferences.Set(CustomApiUrlKey, value); // NEW
        }
        public static bool IsOnSite
        {
            get => Preferences.Get(ApiModeKey, "OnSite") == "OnSite";
            set => Preferences.Set(ApiModeKey, value ? "OnSite" : "OffSite");
        }
        public static string DeviceName
        {
            get => Preferences.Get(nameof(DeviceName), string.Empty);
            set => Preferences.Set(nameof(DeviceName), value);
        }
        public static void Reset()
        {
            Preferences.Remove(ApiModeKey);
            Preferences.Remove(nameof(DeviceName));
            Preferences.Remove(CustomApiUrlKey); // NEW
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