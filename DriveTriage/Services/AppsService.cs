using System.Diagnostics;
using Microsoft.Win32;
using DriveTriage.ViewModels;

namespace DriveTriage.Services
{
    public class AppsService
    {
        private static readonly string[] UninstallRegistryKeys = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        public async Task<List<InstalledApp>> EnumerateInstalledAppsAsync(
            IProgress<string> statusUpdate,
            CancellationToken cancellationToken,
            string? filterDriveLetter = null)
        {
            var apps = new List<InstalledApp>();

            await Task.Run(() =>
            {
                var driveFilter = !string.IsNullOrEmpty(filterDriveLetter) 
                    ? filterDriveLetter.TrimEnd('\\', ':') + ":\\" 
                    : null;

                statusUpdate.Report(driveFilter != null 
                    ? $"Scanning applications on {driveFilter}..." 
                    : "Scanning installed applications...");

                // Scan HKEY_LOCAL_MACHINE
                foreach (var keyPath in UninstallRegistryKeys)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ScanRegistryKey(Registry.LocalMachine, keyPath, apps, cancellationToken, driveFilter);
                }

                // Scan HKEY_CURRENT_USER
                cancellationToken.ThrowIfCancellationRequested();
                ScanRegistryKey(
                    Registry.CurrentUser,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    apps,
                    cancellationToken,
                    driveFilter);

                statusUpdate.Report($"Found {apps.Count} installed applications{(driveFilter != null ? $" on {driveFilter}" : "")}");

                // Remove duplicates based on DisplayName and Publisher
                apps = apps
                    .GroupBy(a => new { a.DisplayName, a.Publisher })
                    .Select(g => g.First())
                    .OrderBy(a => a.DisplayName)
                    .ToList();

            }, cancellationToken);

            return apps;
        }

        private void ScanRegistryKey(
            RegistryKey rootKey,
            string keyPath,
            List<InstalledApp> apps,
            CancellationToken cancellationToken,
            string? driveFilter)
        {
            try
            {
                using var key = rootKey.OpenSubKey(keyPath);
                if (key == null) return;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        var displayName = GetRegistryValue(subKey, "DisplayName");
                        if (string.IsNullOrWhiteSpace(displayName))
                            continue;

                        // Filter out system components and updates
                        if (IsSystemComponent(subKey) || IsUpdate(displayName))
                            continue;

                        var installLocation = GetRegistryValue(subKey, "InstallLocation") ?? "";

                        // Filter by drive if specified
                        if (driveFilter != null)
                        {
                            if (string.IsNullOrWhiteSpace(installLocation))
                                continue; // Skip apps without install location when filtering

                            if (!installLocation.StartsWith(driveFilter, StringComparison.OrdinalIgnoreCase))
                                continue; // Skip apps not on the specified drive
                        }

                        var app = new InstalledApp
                        {
                            DisplayName = displayName,
                            Publisher = GetRegistryValue(subKey, "Publisher") ?? "Unknown",
                            DisplayVersion = GetRegistryValue(subKey, "DisplayVersion") ?? "",
                            InstallDate = ParseInstallDate(GetRegistryValue(subKey, "InstallDate")),
                            EstimatedSize = GetEstimatedSize(subKey),
                            InstallLocation = installLocation,
                            UninstallString = GetRegistryValue(subKey, "UninstallString") ?? "",
                            QuietUninstallString = GetRegistryValue(subKey, "QuietUninstallString") ?? "",
                            RegistryKeyPath = $"{rootKey.Name}\\{keyPath}\\{subKeyName}"
                        };

                        apps.Add(app);
                    }
                    catch
                    {
                        // Skip apps we can't read
                    }
                }
            }
            catch
            {
                // Skip registry keys we can't access
            }
        }

        private static string? GetRegistryValue(RegistryKey key, string valueName)
        {
            try
            {
                return key.GetValue(valueName)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSystemComponent(RegistryKey key)
        {
            try
            {
                var systemComponent = key.GetValue("SystemComponent");
                if (systemComponent is int intValue && intValue == 1)
                    return true;

                var parentKeyName = key.GetValue("ParentKeyName");
                if (parentKeyName != null)
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUpdate(string displayName)
        {
            var lowerName = displayName.ToLowerInvariant();
            return lowerName.Contains("update for") ||
                   lowerName.Contains("hotfix for") ||
                   lowerName.Contains("security update") ||
                   lowerName.StartsWith("kb");
        }

        private static DateTime? ParseInstallDate(string? installDate)
        {
            if (string.IsNullOrWhiteSpace(installDate) || installDate.Length != 8)
                return null;

            try
            {
                var year = int.Parse(installDate.Substring(0, 4));
                var month = int.Parse(installDate.Substring(4, 2));
                var day = int.Parse(installDate.Substring(6, 2));
                return new DateTime(year, month, day);
            }
            catch
            {
                return null;
            }
        }

        private static long GetEstimatedSize(RegistryKey key)
        {
            try
            {
                var sizeValue = key.GetValue("EstimatedSize");
                if (sizeValue is int intSize)
                    return (long)intSize * 1024; // Registry stores in KB

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        public async Task<UninstallResult> UninstallApplicationAsync(
            InstalledApp app,
            bool silent = false)
        {
            if (string.IsNullOrWhiteSpace(app.UninstallString))
            {
                return new UninstallResult
                {
                    Success = false,
                    ErrorMessage = "No uninstall command available"
                };
            }

            try
            {
                var uninstallCommand = silent && !string.IsNullOrWhiteSpace(app.QuietUninstallString)
                    ? app.QuietUninstallString
                    : app.UninstallString;

                var (fileName, arguments) = ParseUninstallCommand(uninstallCommand);

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas" // Request admin elevation
                };

                await Task.Run(() =>
                {
                    using var process = Process.Start(processStartInfo);
                    process?.WaitForExit();
                });

                return new UninstallResult
                {
                    Success = true,
                    Message = "Uninstall command executed successfully"
                };
            }
            catch (Exception ex)
            {
                return new UninstallResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private (string fileName, string arguments) ParseUninstallCommand(string uninstallString)
        {
            uninstallString = uninstallString.Trim();

            // Handle quoted executable
            if (uninstallString.StartsWith("\""))
            {
                var endQuoteIndex = uninstallString.IndexOf("\"", 1);
                if (endQuoteIndex > 0)
                {
                    var fileName = uninstallString.Substring(1, endQuoteIndex - 1);
                    var arguments = uninstallString.Length > endQuoteIndex + 1
                        ? uninstallString.Substring(endQuoteIndex + 1).Trim()
                        : string.Empty;
                    return (fileName, arguments);
                }
            }

            // Handle msiexec commands
            if (uninstallString.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                return ("msiexec.exe", uninstallString.Substring(7).Trim());
            }

            // Handle space-separated
            var spaceIndex = uninstallString.IndexOf(' ');
            if (spaceIndex > 0)
            {
                return (
                    uninstallString.Substring(0, spaceIndex),
                    uninstallString.Substring(spaceIndex + 1).Trim()
                );
            }

            return (uninstallString, string.Empty);
        }

        public List<InstalledApp> FilterApps(
            List<InstalledApp> apps,
            string searchText,
            long? minSize = null,
            DateTime? installedBefore = null,
            DateTime? installedAfter = null)
        {
            var filtered = apps.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var lowerSearch = searchText.ToLowerInvariant();
                filtered = filtered.Where(a =>
                    a.DisplayName.ToLowerInvariant().Contains(lowerSearch) ||
                    (a.Publisher?.ToLowerInvariant().Contains(lowerSearch) ?? false));
            }

            if (minSize.HasValue)
            {
                filtered = filtered.Where(a => a.EstimatedSize >= minSize.Value);
            }

            if (installedBefore.HasValue)
            {
                filtered = filtered.Where(a =>
                    a.InstallDate.HasValue && a.InstallDate.Value <= installedBefore.Value);
            }

            if (installedAfter.HasValue)
            {
                filtered = filtered.Where(a =>
                    a.InstallDate.HasValue && a.InstallDate.Value >= installedAfter.Value);
            }

            return filtered.ToList();
        }

        public List<InstalledApp> GetLargestApps(List<InstalledApp> apps, int count = 20)
        {
            return apps
                .Where(a => a.EstimatedSize > 0)
                .OrderByDescending(a => a.EstimatedSize)
                .Take(count)
                .ToList();
        }

        public Dictionary<string, int> GetAppsByPublisher(List<InstalledApp> apps)
        {
            return apps
                .Where(a => !string.IsNullOrWhiteSpace(a.Publisher))
                .GroupBy(a => a.Publisher)
                .ToDictionary(g => g.Key!, g => g.Count())
                .OrderByDescending(kvp => kvp.Value)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }
}
