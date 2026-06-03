using Newtonsoft.Json;

namespace AmaScan.Classes
{
    public class VersionInfo
    {
        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        [JsonProperty("apkFileName")]
        public string ApkFileName { get; set; } = string.Empty;
    }

    public static class UpdateService
    {
        /// <summary>
        /// Asks the API for the latest version (the endpoint reads scannerapk/version.json on the
        /// server) and compares it to the version installed on this device. Returns VersionInfo if
        /// the server's version is newer, otherwise null. Nothing server-side to maintain — the
        /// build drops a fresh version.json into scannerapk and the endpoint serves it.
        /// </summary>
        public static async Task<VersionInfo?> CheckForUpdateAsync()
        {
            try
            {
                var client = AppConfig.GetHttpClient();
                var response = await client.GetAsync("AppUpdate/LatestVersion");

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var info = JsonConvert.DeserializeObject<VersionInfo>(json);

                if (info is null)
                    return null;

                // AppInfo.VersionString reads ApplicationDisplayVersion from the .csproj automatically
                var currentVersion = AppInfo.VersionString;

                if (Version.TryParse(info.Version, out var serverVer) &&
                    Version.TryParse(currentVersion, out var currentVer) &&
                    serverVer > currentVer)
                {
                    return info;
                }

                return null;
            }
            catch
            {
                // Silently fail — update check should never block the user
                return null;
            }
        }

        /// <summary>
        /// Returns the root server URL (the API base with any trailing /api segment removed),
        /// e.g. http://175.25.97.2:8079 — the host the scannerapk folder lives under.
        /// </summary>
        private static string GetServerRootUrl()
        {
            var baseUrl = AppConfig.ApiBaseUrl.TrimEnd('/');
            if (baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
                baseUrl = baseUrl[..^4];
            return baseUrl;
        }

        /// <summary>
        /// Downloads the APK from the server's scannerapk folder and triggers the Android installer.
        /// </summary>
        public static async Task DownloadAndInstallAsync(VersionInfo info, IProgress<double>? progress = null)
        {
            var client = AppConfig.GetHttpClient();

            var apkUrl = $"{GetServerRootUrl()}/scannerapk/{info.ApkFileName}";

            var response = await client.GetAsync(apkUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var downloadPath = Path.Combine(FileSystem.CacheDirectory, info.ApkFileName);

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(downloadPath);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;

                if (totalBytes > 0)
                    progress?.Report((double)downloaded / totalBytes);
            }

            fileStream.Close();

            // Trigger the Android package installer
            await Launcher.OpenAsync(new OpenFileRequest
            {
                File = new ReadOnlyFile(downloadPath, "application/vnd.android.package-archive")
            });
        }
    }
}
