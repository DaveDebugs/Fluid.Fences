using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;

namespace DesktopFences
{
    public static class Updater
    {

        public static string CurrentVersion { get; } = ResolveCurrentVersion();

        private static string ResolveCurrentVersion()
        {
            try
            {
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch { return "0.0.0"; }
        }
        private const string GitHubApiUrl = "https://api.github.com/repos/DaveDebugs/Fluid.Fences/releases/latest";

        public class GitHubRelease
        {
            [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
            [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
        }

        public class GitHubAsset
        {
            [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
        }

        public static async Task CheckAndApplyUpdateAsync(bool silentCheck = true)
        {
            try
            {
                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("User-Agent", "FluidFences-Updater");

                var release = await client.GetFromJsonAsync<GitHubRelease>(GitHubApiUrl);
                if (release == null || string.IsNullOrEmpty(release.TagName)) return;

                string latestTag = release.TagName.TrimStart('v', 'V');

                if (Version.TryParse(latestTag, out Version? latestVersion) && Version.TryParse(CurrentVersion, out Version? currentVersion))
                {
                    if (latestVersion > currentVersion)
                    {

                        GitHubAsset? exeAsset =
                            release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                                            && a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                            ?? release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                        GitHubAsset? checksumAsset = release.Assets.FirstOrDefault(a => a.Name.Equals("checksum.txt", StringComparison.OrdinalIgnoreCase));

                        if (exeAsset != null && checksumAsset != null)
                        {
                            if (!silentCheck || MessageBox.Show($"A secure update to Fluid Fences (v{latestTag}) is available!\n\nWould you like to download and install it now?", "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                            {
                                await ApplySecureUpdateAsync(exeAsset.DownloadUrl, checksumAsset.DownloadUrl);
                            }
                        }
                        else if (!silentCheck)
                        {
                            MessageBox.Show("An update was found, but the security checksum is missing from the server. Update aborted to ensure your safety.", "Security Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else if (!silentCheck)
                    {
                        MessageBox.Show("You are running the latest version of Fluid Fences!", "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!silentCheck) MessageBox.Show($"Could not check for updates.\n{ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static async Task ApplySecureUpdateAsync(string installerUrl, string checksumUrl)
        {
            string installerPath = Path.Combine(
                Path.GetTempPath(), $"FluidFences_Update_{Guid.NewGuid():N}.exe");

            try
            {
                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("User-Agent", "FluidFences-Updater");

                string expectedHashRaw = await client.GetStringAsync(checksumUrl);
                string expectedHash = expectedHashRaw.Trim().ToUpperInvariant().Split(' ', '\t')[0];

                var response = await client.GetAsync(installerUrl);
                response.EnsureSuccessStatusCode();
                using (var fs = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }

                string actualHash = CalculateSHA256(installerPath);
                if (!expectedHash.Equals(actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(installerPath);
                    MessageBox.Show(
                        "Security Alert: the downloaded update was corrupted or tampered with. The update has been aborted.",
                        "Verification Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                ProcessStartInfo psi = new(installerPath)
                {
                    Arguments = "/SILENT /NORESTART",
                    UseShellExecute = true
                };

                try
                {
                    Process.Start(psi);

                    Application.Current.Shutdown();
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {

                    TryDelete(installerPath);
                    MessageBox.Show(
                        "The update was cancelled because Administrator permission was not granted.",
                        "Update Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                TryDelete(installerPath);
                MessageBox.Show($"Failed to download or start the update:\n{ex.Message}",
                                "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string CalculateSHA256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }

        public static void CleanupOldUpdates()
        {
            try
            {
                string? backupPath = Environment.ProcessPath + ".old";
                if (backupPath is not null && File.Exists(backupPath)) File.Delete(backupPath);
            }
            catch { }

            try
            {
                foreach (string leftover in Directory.GetFiles(Path.GetTempPath(), "FluidFences_Update_*.exe"))
                    TryDelete(leftover);
            }
            catch { }
        }
    }
}
