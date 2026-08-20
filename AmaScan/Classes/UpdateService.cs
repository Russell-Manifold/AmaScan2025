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
        /// Why the last check found no update. Empty when the check ran cleanly and the device is
        /// simply current. Surfaced on demand so a silent failure can be diagnosed without a site visit.
        /// </summary>
        public static string LastCheckError { get; private set; } = string.Empty;

        /// <summary>
        /// Android 8+ requires the user to allow this app to install apps ("Install unknown apps"),
        /// on top of the REQUEST_INSTALL_PACKAGES manifest permission. Without it the installer never
        /// appears and the update silently does nothing.
        /// </summary>
        public static bool CanInstallPackages()
        {
#if ANDROID
            if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.O)
                return true;

            return Android.App.Application.Context.PackageManager?.CanRequestPackageInstalls() ?? false;
#else
            return true;
#endif
        }

        /// <summary>Opens the Android settings screen where that permission is granted.</summary>
        public static void OpenInstallPermissionSettings()
        {
#if ANDROID
            var context = Android.App.Application.Context;
            var intent = new Android.Content.Intent(
                Android.Provider.Settings.ActionManageUnknownAppSources,
                Android.Net.Uri.Parse("package:" + context.PackageName));
            intent.SetFlags(Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
#endif
        }

        /// <summary>
        /// Asks the API for the latest version (the endpoint reads scannerapk/version.json on the
        /// server) and compares it to the version installed on this device. Returns VersionInfo if
        /// the server's version is newer, otherwise null. Nothing server-side to maintain — the
        /// build drops a fresh version.json into scannerapk and the endpoint serves it.
        /// </summary>
        public static async Task<VersionInfo?> CheckForUpdateAsync()
        {
            LastCheckError = string.Empty;
            var currentVersion = AppInfo.VersionString;

            try
            {
                var client = AppConfig.GetHttpClient();
                var response = await client.GetAsync("AppUpdate/LatestVersion");

                if (!response.IsSuccessStatusCode)
                {
                    LastCheckError = $"Server returned {(int)response.StatusCode} for AppUpdate/LatestVersion. " +
                                     "Check version.json is in the server's scannerapk folder.";
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var info = JsonConvert.DeserializeObject<VersionInfo>(json);

                if (info is null || string.IsNullOrWhiteSpace(info.Version))
                {
                    LastCheckError = "version.json on the server could not be read.";
                    return null;
                }

                if (!Version.TryParse(info.Version, out var serverVer))
                {
                    LastCheckError = $"Server version '{info.Version}' is not a valid version number.";
                    return null;
                }

                // AppInfo.VersionString reads ApplicationDisplayVersion from the .csproj automatically
                if (!Version.TryParse(currentVersion, out var currentVer))
                {
                    LastCheckError = $"Installed version '{currentVersion}' is not a valid version number.";
                    return null;
                }

                if (serverVer > currentVer)
                    return info;

                LastCheckError = $"Up to date (installed {currentVersion}, server {info.Version}).";
                return null;
            }
            catch (Exception ex)
            {
                // Never block the user on a failed check — but record why, so a device that never
                // updates can be diagnosed without a trip to site.
                LastCheckError = $"Update check failed: {ex.Message}";
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
            // A plain client on purpose: AppConfig.GetHttpClient() sends "Accept: application/json",
            // and the server can only offer the APK as a binary package, so it answered 406.
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("*/*"));

            if (string.IsNullOrWhiteSpace(info.ApkFileName))
                throw new InvalidOperationException(
                    "version.json on the server has no apkFileName, so there is nothing to download.");

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
