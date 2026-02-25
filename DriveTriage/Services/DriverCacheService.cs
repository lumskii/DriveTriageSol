using System.IO;

namespace DriveTriage.Services
{
    public class DriverCacheService
    {
        private static readonly string[] NvidiaCachePaths = 
        {
            @"C:\ProgramData\NVIDIA Corporation\Downloader",
            @"C:\ProgramData\NVIDIA Corporation\NV_Cache",
            @"C:\NVIDIA"
        };

        public async Task<long> GetNvidiaCacheSizeAsync(CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                long totalSize = 0;

                foreach (var path in NvidiaCachePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!Directory.Exists(path))
                        continue;

                    try
                    {
                        totalSize += CalculateDirectorySize(path, cancellationToken);
                    }
                    catch
                    {
                        // Skip paths we can't access
                    }
                }

                return totalSize;
            }, cancellationToken);
        }

        public async Task<CleanupResult> PurgeNvidiaCachesAsync(
            IProgress<string> statusUpdate,
            CancellationToken cancellationToken)
        {
            var result = new CleanupResult
            {
                StartTime = DateTime.Now,
                OperationType = "Purge NVIDIA Cache"
            };

            await Task.Run(() =>
            {
                try
                {
                    statusUpdate.Report("Calculating NVIDIA cache size...");
                    
                    result.SizeBefore = GetNvidiaCacheSizeAsync(cancellationToken).Result;

                    if (result.SizeBefore == 0)
                    {
                        result.Success = true;
                        result.Message = "NVIDIA cache is already empty or not found";
                        statusUpdate.Report(result.Message);
                        return;
                    }

                    statusUpdate.Report($"Purging NVIDIA cache ({FormatSize(result.SizeBefore)})...");

                    var itemsDeleted = 0;
                    var failedItems = 0;

                    foreach (var cachePath in NvidiaCachePaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!Directory.Exists(cachePath))
                            continue;

                        // Check if this is a Windows path (should never happen, but extra safety)
                        if (IsWindowsPath(cachePath))
                        {
                            statusUpdate.Report($"Skipping protected Windows path: {cachePath}");
                            continue;
                        }

                        try
                        {
                            statusUpdate.Report($"Cleaning {Path.GetFileName(cachePath)}...");
                            
                            var stats = DeleteDirectoryContents(cachePath, statusUpdate, cancellationToken);
                            itemsDeleted += stats.deleted;
                            failedItems += stats.failed;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            statusUpdate.Report($"Error cleaning {cachePath}: {ex.Message}");
                            failedItems++;
                        }
                    }

                    // Calculate size after cleanup
                    result.SizeAfter = GetNvidiaCacheSizeAsync(cancellationToken).Result;
                    result.SpaceReclaimed = result.SizeBefore - result.SizeAfter;
                    result.ItemsDeleted = itemsDeleted;
                    result.Success = true;

                    if (failedItems > 0)
                    {
                        result.Message = $"NVIDIA cache purged. Deleted {itemsDeleted} items, reclaimed {FormatSize(result.SpaceReclaimed)}. {failedItems} items could not be deleted (in use or access denied).";
                    }
                    else
                    {
                        result.Message = $"NVIDIA cache purged successfully. Deleted {itemsDeleted} items, reclaimed {FormatSize(result.SpaceReclaimed)}";
                    }
                    
                    statusUpdate.Report(result.Message);
                }
                catch (OperationCanceledException)
                {
                    result.Success = false;
                    result.Message = "NVIDIA cache purge cancelled";
                    throw;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Error purging NVIDIA cache: {ex.Message}";
                    result.ErrorDetails = ex.ToString();
                }
                finally
                {
                    result.EndTime = DateTime.Now;
                }
            }, cancellationToken);

            return result;
        }

        private (int deleted, int failed) DeleteDirectoryContents(
            string directoryPath,
            IProgress<string> statusUpdate,
            CancellationToken cancellationToken)
        {
            int deleted = 0;
            int failed = 0;

            try
            {
                // Delete all files in the directory and subdirectories
                foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        File.Delete(file);
                        deleted++;

                        if (deleted % 100 == 0)
                        {
                            statusUpdate.Report($"Deleted {deleted} items...");
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        failed++;
                        // File is protected or we don't have access
                    }
                    catch (IOException)
                    {
                        failed++;
                        // File is in use
                    }
                    catch
                    {
                        failed++;
                        // Other errors
                    }
                }

                // Delete all subdirectories (but not the root directory itself)
                foreach (var dir in Directory.GetDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        failed++;
                    }
                    catch (IOException)
                    {
                        failed++;
                    }
                    catch
                    {
                        failed++;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Can't access the directory itself
                failed++;
            }
            catch (DirectoryNotFoundException)
            {
                // Directory doesn't exist, nothing to delete
            }

            return (deleted, failed);
        }

        private long CalculateDirectorySize(string directoryPath, CancellationToken cancellationToken)
        {
            long size = 0;

            try
            {
                if (!Directory.Exists(directoryPath))
                    return 0;

                foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var fileInfo = new FileInfo(file);
                        size += fileInfo.Length;
                    }
                    catch
                    {
                        // Skip files we can't access
                    }
                }
            }
            catch
            {
                // If we can't access the directory, return 0
            }

            return size;
        }

        private bool IsWindowsPath(string path)
        {
            var normalizedPath = path.Replace('/', '\\').ToUpperInvariant();
            
            // Check if path starts with C:\Windows
            if (normalizedPath.StartsWith(@"C:\WINDOWS\"))
                return true;

            // Check other protected Windows paths
            var protectedPaths = new[]
            {
                @"C:\WINDOWS\SYSTEM32",
                @"C:\WINDOWS\SYSWOW64",
                @"C:\WINDOWS\WINSXS",
                @"C:\PROGRAM FILES\WINDOWSAPPS"
            };

            foreach (var protectedPath in protectedPaths)
            {
                if (normalizedPath.StartsWith(protectedPath))
                    return true;
            }

            return false;
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
}
