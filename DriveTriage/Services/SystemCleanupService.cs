using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DriveTriage.Services
{
    public class SystemCleanupService
    {
        private readonly string _quarantinePath;

        public SystemCleanupService(string? quarantinePath = null)
        {
            _quarantinePath = quarantinePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DriveTriage",
                "Quarantine");
        }

        public async Task<CleanupResult> EmptyRecycleBinAsync(
            IProgress<string> statusUpdate,
            CancellationToken cancellationToken)
        {
            var result = new CleanupResult
            {
                StartTime = DateTime.Now,
                OperationType = "Empty Recycle Bin"
            };

            await Task.Run(() =>
            {
                try
                {
                    statusUpdate.Report("Calculating Recycle Bin size...");
                    
                    // Get Recycle Bin size before emptying
                    var recycleBinSize = GetRecycleBinSize();
                    result.SizeBefore = recycleBinSize;

                    statusUpdate.Report($"Emptying Recycle Bin ({FormatSize(recycleBinSize)})...");

                    // Empty the Recycle Bin
                    EmptyRecycleBin();

                    cancellationToken.ThrowIfCancellationRequested();

                    result.SizeAfter = 0;
                    result.SpaceReclaimed = recycleBinSize;
                    result.Success = true;
                    result.Message = $"Recycle Bin emptied successfully. Reclaimed {FormatSize(recycleBinSize)}";
                    
                    statusUpdate.Report(result.Message);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Error emptying Recycle Bin: {ex.Message}";
                    result.ErrorDetails = ex.ToString();
                }
                finally
                {
                    result.EndTime = DateTime.Now;
                }
            }, cancellationToken);

            return result;
        }

        public async Task<CleanupResult> CleanQuarantineAsync(
            IProgress<string> statusUpdate,
            CancellationToken cancellationToken)
        {
            var result = new CleanupResult
            {
                StartTime = DateTime.Now,
                OperationType = "Clean Quarantine"
            };

            await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(_quarantinePath))
                    {
                        result.Success = true;
                        result.Message = "Quarantine folder is already empty or does not exist";
                        statusUpdate.Report(result.Message);
                        return;
                    }

                    statusUpdate.Report("Calculating quarantine size...");
                    
                    var quarantineSize = CalculateDirectorySize(_quarantinePath, statusUpdate, cancellationToken);
                    result.SizeBefore = quarantineSize;

                    if (quarantineSize == 0)
                    {
                        result.Success = true;
                        result.Message = "Quarantine folder is empty";
                        statusUpdate.Report(result.Message);
                        return;
                    }

                    statusUpdate.Report($"Deleting quarantine contents ({FormatSize(quarantineSize)})...");

                    // Delete all files and folders in quarantine
                    var itemsDeleted = 0;
                    foreach (var file in Directory.GetFiles(_quarantinePath, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            File.Delete(file);
                            itemsDeleted++;
                            
                            if (itemsDeleted % 100 == 0)
                            {
                                statusUpdate.Report($"Deleted {itemsDeleted} items...");
                            }
                        }
                        catch
                        {
                            // Continue even if some files fail
                        }
                    }

                    // Delete directories
                    foreach (var dir in Directory.GetDirectories(_quarantinePath, "*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            Directory.Delete(dir, recursive: true);
                        }
                        catch
                        {
                            // Continue even if some directories fail
                        }
                    }

                    result.SizeAfter = CalculateDirectorySize(_quarantinePath, statusUpdate, cancellationToken);
                    result.SpaceReclaimed = result.SizeBefore - result.SizeAfter;
                    result.ItemsDeleted = itemsDeleted;
                    result.Success = true;
                    result.Message = $"Quarantine cleaned successfully. Deleted {itemsDeleted} items, reclaimed {FormatSize(result.SpaceReclaimed)}";
                    
                    statusUpdate.Report(result.Message);
                }
                catch (OperationCanceledException)
                {
                    result.Success = false;
                    result.Message = "Quarantine cleanup cancelled";
                    throw;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Error cleaning quarantine: {ex.Message}";
                    result.ErrorDetails = ex.ToString();
                }
                finally
                {
                    result.EndTime = DateTime.Now;
                }
            }, cancellationToken);

            return result;
        }

        public async Task<SystemCleanupInfo> GetSystemCleanupInfoAsync()
        {
            return await Task.Run(() =>
            {
                var info = new SystemCleanupInfo();

                try
                {
                    info.RecycleBinSize = GetRecycleBinSize();
                    info.RecycleBinItemCount = GetRecycleBinItemCount();
                }
                catch
                {
                    info.RecycleBinSize = 0;
                    info.RecycleBinItemCount = 0;
                }

                try
                {
                    if (Directory.Exists(_quarantinePath))
                    {
                        info.QuarantineSize = CalculateDirectorySize(_quarantinePath, null, CancellationToken.None);
                        info.QuarantineItemCount = Directory.GetFiles(_quarantinePath, "*", SearchOption.AllDirectories).Length;
                    }
                }
                catch
                {
                    info.QuarantineSize = 0;
                    info.QuarantineItemCount = 0;
                }

                info.QuarantinePath = _quarantinePath;

                try
                {
                    var driverCacheService = new DriverCacheService();
                    info.NvidiaCacheSize = driverCacheService.GetNvidiaCacheSizeAsync(CancellationToken.None).Result;
                }
                catch
                {
                    info.NvidiaCacheSize = 0;
                }

                return info;
            });
        }

        private long CalculateDirectorySize(string path, IProgress<string>? statusUpdate, CancellationToken cancellationToken)
        {
            long totalSize = 0;

            try
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        totalSize += fileInfo.Length;
                    }
                    catch
                    {
                        // Skip files we can't access
                    }
                }
            }
            catch
            {
                // Handle directory access errors
            }

            return totalSize;
        }

        private long GetRecycleBinSize()
        {
            long totalSize = 0;

            try
            {
                // Get Recycle Bin path for each drive
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed);

                foreach (var drive in drives)
                {
                    var recycleBinPath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                    
                    if (Directory.Exists(recycleBinPath))
                    {
                        foreach (var userFolder in Directory.GetDirectories(recycleBinPath))
                        {
                            try
                            {
                                foreach (var file in Directory.GetFiles(userFolder, "*", SearchOption.AllDirectories))
                                {
                                    try
                                    {
                                        var fileInfo = new FileInfo(file);
                                        totalSize += fileInfo.Length;
                                    }
                                    catch
                                    {
                                        // Skip files we can't access
                                    }
                                }
                            }
                            catch
                            {
                                // Skip folders we can't access
                            }
                        }
                    }
                }
            }
            catch
            {
                // Return what we calculated so far
            }

            return totalSize;
        }

        private int GetRecycleBinItemCount()
        {
            int itemCount = 0;

            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed);

                foreach (var drive in drives)
                {
                    var recycleBinPath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                    
                    if (Directory.Exists(recycleBinPath))
                    {
                        foreach (var userFolder in Directory.GetDirectories(recycleBinPath))
                        {
                            try
                            {
                                itemCount += Directory.GetFiles(userFolder, "*", SearchOption.AllDirectories).Length;
                            }
                            catch
                            {
                                // Skip folders we can't access
                            }
                        }
                    }
                }
            }
            catch
            {
                // Return what we counted so far
            }

            return itemCount;
        }

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        private void EmptyRecycleBin()
        {
            SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        }

        public async Task<CleanupResult> RunDismComponentCleanupAsync(
            IProgress<string> statusUpdate,
            CancellationToken cancellationToken)
        {
            var result = new CleanupResult
            {
                StartTime = DateTime.Now,
                OperationType = "DISM Component Cleanup"
            };

            await Task.Run(() =>
            {
                try
                {
                    statusUpdate.Report("Starting DISM component cleanup...");
                    statusUpdate.Report("This may take several minutes. Please wait...");

                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "Dism.exe",
                        Arguments = "/Online /Cleanup-Image /StartComponentCleanup",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        Verb = "runas" // Request admin privileges
                    };

                    using var process = new Process { StartInfo = processInfo };

                    var output = new System.Text.StringBuilder();
                    var errorOutput = new System.Text.StringBuilder();

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            output.AppendLine(e.Data);
                            statusUpdate.Report(e.Data);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            errorOutput.AppendLine(e.Data);
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Wait for completion or cancellation
                    while (!process.HasExited)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Thread.Sleep(500);
                    }

                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        result.Success = true;
                        result.Message = "DISM component cleanup completed successfully";
                        statusUpdate.Report(result.Message);
                    }
                    else
                    {
                        result.Success = false;
                        result.Message = $"DISM component cleanup failed with exit code: {process.ExitCode}";
                        result.ErrorDetails = errorOutput.ToString();
                    }
                }
                catch (OperationCanceledException)
                {
                    result.Success = false;
                    result.Message = "DISM component cleanup cancelled";
                    throw;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Error running DISM component cleanup: {ex.Message}";
                    result.ErrorDetails = ex.ToString();
                }
                finally
                {
                    result.EndTime = DateTime.Now;
                }
            }, cancellationToken);

            return result;
        }

        public async Task<CleanupResult> ClearWindowsUpdateDownloadCacheAsync(
            IProgress<string> statusUpdate,
            CancellationToken cancellationToken)
        {
            var result = new CleanupResult
            {
                StartTime = DateTime.Now,
                OperationType = "Clear Windows Update Cache"
            };

            await Task.Run(() =>
            {
                try
                {
                    var downloadPath = @"C:\Windows\SoftwareDistribution\Download";

                    if (!Directory.Exists(downloadPath))
                    {
                        result.Success = true;
                        result.Message = "Windows Update download cache does not exist or is already empty";
                        statusUpdate.Report(result.Message);
                        return;
                    }

                    // Calculate size before
                    statusUpdate.Report("Calculating Windows Update cache size...");
                    result.SizeBefore = CalculateDirectorySize(downloadPath, statusUpdate, cancellationToken);

                    if (result.SizeBefore == 0)
                    {
                        result.Success = true;
                        result.Message = "Windows Update download cache is already empty";
                        statusUpdate.Report(result.Message);
                        return;
                    }

                    statusUpdate.Report($"Found {FormatSize(result.SizeBefore)} in Windows Update cache");

                    // Stop Windows Update service
                    statusUpdate.Report("Stopping Windows Update service (wuauserv)...");
                    if (!StopService("wuauserv", statusUpdate))
                    {
                        result.Success = false;
                        result.Message = "Failed to stop Windows Update service. Try running as Administrator.";
                        return;
                    }

                    Thread.Sleep(2000); // Give service time to stop

                    // Delete cache contents
                    statusUpdate.Report("Deleting Windows Update download cache...");
                    var itemsDeleted = 0;

                    try
                    {
                        foreach (var file in Directory.GetFiles(downloadPath, "*", SearchOption.AllDirectories))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            try
                            {
                                File.Delete(file);
                                itemsDeleted++;

                                if (itemsDeleted % 50 == 0)
                                {
                                    statusUpdate.Report($"Deleted {itemsDeleted} files...");
                                }
                            }
                            catch
                            {
                                // Skip files we can't delete
                            }
                        }

                        // Delete subdirectories
                        foreach (var dir in Directory.GetDirectories(downloadPath, "*", SearchOption.TopDirectoryOnly))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            try
                            {
                                Directory.Delete(dir, recursive: true);
                            }
                            catch
                            {
                                // Skip directories we can't delete
                            }
                        }
                    }
                    finally
                    {
                        // Always restart the service
                        statusUpdate.Report("Restarting Windows Update service (wuauserv)...");
                        StartService("wuauserv", statusUpdate);
                    }

                    result.SizeAfter = CalculateDirectorySize(downloadPath, statusUpdate, cancellationToken);
                    result.SpaceReclaimed = result.SizeBefore - result.SizeAfter;
                    result.ItemsDeleted = itemsDeleted;
                    result.Success = true;
                    result.Message = $"Windows Update cache cleared. Deleted {itemsDeleted} items, reclaimed {FormatSize(result.SpaceReclaimed)}";

                    statusUpdate.Report(result.Message);
                }
                catch (OperationCanceledException)
                {
                    result.Success = false;
                    result.Message = "Windows Update cache cleanup cancelled";

                    // Ensure service is restarted
                    try
                    {
                        StartService("wuauserv", statusUpdate);
                    }
                    catch { }

                    throw;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Error clearing Windows Update cache: {ex.Message}";
                    result.ErrorDetails = ex.ToString();

                    // Ensure service is restarted
                    try
                    {
                        StartService("wuauserv", statusUpdate);
                    }
                    catch { }
                }
                finally
                {
                    result.EndTime = DateTime.Now;
                }
            }, cancellationToken);

            return result;
        }

        public async Task<ShadowStorageInfo> GetShadowStorageInfoAsync()
        {
            return await Task.Run(() =>
            {
                var info = new ShadowStorageInfo();

                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "vssadmin.exe",
                        Arguments = "list shadowstorage",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(processInfo);
                    if (process == null)
                    {
                        info.ErrorMessage = "Failed to start vssadmin process";
                        return info;
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        info.ErrorMessage = $"vssadmin exited with code {process.ExitCode}: {error}";
                        return info;
                    }

                    // Parse vssadmin output
                    ParseShadowStorageOutput(output, info);
                }
                catch (Exception ex)
                {
                    info.ErrorMessage = $"Error reading shadow storage info: {ex.Message}";
                }

                return info;
            });
        }

        private void ParseShadowStorageOutput(string output, ShadowStorageInfo info)
        {
            var lines = output.Split('\n');
            var currentStorage = new ShadowStorageItem();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("For volume:"))
                {
                    currentStorage = new ShadowStorageItem();
                    currentStorage.ForVolume = ExtractValue(trimmed);
                }
                else if (trimmed.StartsWith("Shadow Copy Storage volume:"))
                {
                    currentStorage.StorageVolume = ExtractValue(trimmed);
                }
                else if (trimmed.StartsWith("Used Shadow Copy Storage space:"))
                {
                    currentStorage.UsedSpace = ExtractValue(trimmed);
                    currentStorage.UsedSpaceBytes = ParseSizeToBytes(currentStorage.UsedSpace);
                }
                else if (trimmed.StartsWith("Allocated Shadow Copy Storage space:"))
                {
                    currentStorage.AllocatedSpace = ExtractValue(trimmed);
                    currentStorage.AllocatedSpaceBytes = ParseSizeToBytes(currentStorage.AllocatedSpace);
                }
                else if (trimmed.StartsWith("Maximum Shadow Copy Storage space:"))
                {
                    currentStorage.MaximumSpace = ExtractValue(trimmed);

                    if (!string.IsNullOrEmpty(currentStorage.ForVolume))
                    {
                        info.StorageItems.Add(currentStorage);
                        info.TotalUsedBytes += currentStorage.UsedSpaceBytes;
                        info.TotalAllocatedBytes += currentStorage.AllocatedSpaceBytes;
                    }
                }
            }

            info.HasData = info.StorageItems.Count > 0;
        }

        private string ExtractValue(string line)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex >= 0 && colonIndex < line.Length - 1)
            {
                return line.Substring(colonIndex + 1).Trim();
            }
            return string.Empty;
        }

        private long ParseSizeToBytes(string sizeString)
        {
            try
            {
                sizeString = sizeString.Replace(",", "").Trim();

                if (sizeString.Contains("UNBOUNDED", StringComparison.OrdinalIgnoreCase))
                    return -1;

                var parts = sizeString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    return 0;

                if (!double.TryParse(parts[0], out double value))
                    return 0;

                var unit = parts[1].ToUpperInvariant();
                return unit switch
                {
                    "BYTES" or "B" => (long)value,
                    "KB" => (long)(value * 1024),
                    "MB" => (long)(value * 1024 * 1024),
                    "GB" => (long)(value * 1024 * 1024 * 1024),
                    "TB" => (long)(value * 1024L * 1024 * 1024 * 1024),
                    _ => 0
                };
            }
            catch
            {
                return 0;
            }
        }

        private bool StopService(string serviceName, IProgress<string> statusUpdate)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "net.exe",
                    Arguments = $"stop {serviceName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                    return false;

                process.WaitForExit(10000); // 10 second timeout
                return process.ExitCode == 0 || process.ExitCode == 2; // 2 = already stopped
            }
            catch (Exception ex)
            {
                statusUpdate.Report($"Error stopping service: {ex.Message}");
                return false;
            }
        }

        private bool StartService(string serviceName, IProgress<string> statusUpdate)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "net.exe",
                    Arguments = $"start {serviceName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                    return false;

                process.WaitForExit(10000);
                return process.ExitCode == 0 || process.ExitCode == 2; // 2 = already started
            }
            catch (Exception ex)
            {
                statusUpdate.Report($"Error starting service: {ex.Message}");
                return false;
            }
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class CleanupResult
    {
        public bool Success { get; set; }
        public string OperationType { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public long SizeBefore { get; set; }
        public long SizeAfter { get; set; }
        public long SpaceReclaimed { get; set; }
        public int ItemsDeleted { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ErrorDetails { get; set; }
        
        public string FormattedSpaceReclaimed => FormatSize(SpaceReclaimed);
        public string Duration => (EndTime - StartTime).TotalSeconds.ToString("F1") + "s";
        
        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class SystemCleanupInfo
    {
        public long RecycleBinSize { get; set; }
        public int RecycleBinItemCount { get; set; }
        public long QuarantineSize { get; set; }
        public int QuarantineItemCount { get; set; }
        public string QuarantinePath { get; set; } = string.Empty;
        public long NvidiaCacheSize { get; set; }

        public string FormattedRecycleBinSize => FormatSize(RecycleBinSize);
        public string FormattedQuarantineSize => FormatSize(QuarantineSize);
        public string FormattedNvidiaCacheSize => FormatSize(NvidiaCacheSize);
        
        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class ShadowStorageInfo
    {
        public List<ShadowStorageItem> StorageItems { get; set; } = new();
        public long TotalUsedBytes { get; set; }
        public long TotalAllocatedBytes { get; set; }
        public bool HasData { get; set; }
        public string? ErrorMessage { get; set; }

        public string FormattedTotalUsed => FormatSize(TotalUsedBytes);
        public string FormattedTotalAllocated => FormatSize(TotalAllocatedBytes);

        private static string FormatSize(long bytes)
        {
            if (bytes < 0)
                return "UNBOUNDED";

            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class ShadowStorageItem
    {
        public string ForVolume { get; set; } = string.Empty;
        public string StorageVolume { get; set; } = string.Empty;
        public string UsedSpace { get; set; } = string.Empty;
        public long UsedSpaceBytes { get; set; }
        public string AllocatedSpace { get; set; } = string.Empty;
        public long AllocatedSpaceBytes { get; set; }
        public string MaximumSpace { get; set; } = string.Empty;
    }
}
