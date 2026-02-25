using System.IO;
using DriveTriage.ViewModels;

namespace DriveTriage.Services
{
    public class BucketsService
    {
        private readonly string _quarantinePath;
        private readonly List<IBucketRule> _bucketRules;

        public BucketsService(string? quarantinePath = null)
        {
            _quarantinePath = quarantinePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DriveTriage",
                "Quarantine");

            _bucketRules = new List<IBucketRule>
            {
                new UserTempBucketRule(),
                new DownloadsInstallersBucketRule(),
                new NodeModulesBucketRule(),
                new NvidiaCacheBucketRule(),
                new AsusGpuTweakCacheBucketRule(),
                new VendorCachesBucketRule(),
                new NuGetGlobalPackagesBucketRule(),
                new VisualStudioCacheBucketRule(),
                new BuildArtifactsBucketRule()
            };
        }

        public async Task<List<CleanupBucket>> ScanBucketsAsync(
            IProgress<string> statusUpdate,
            CancellationToken cancellationToken)
        {
            var buckets = new List<CleanupBucket>();

            foreach (var rule in _bucketRules)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bucket = new CleanupBucket
                {
                    Name = rule.Name,
                    Description = rule.Description,
                    Status = CleanupStatus.Scanning
                };
                buckets.Add(bucket);

                statusUpdate.Report($"Scanning {rule.Name}...");

                await Task.Run(() =>
                {
                    try
                    {
                        bucket.Items = rule.ScanForItems(cancellationToken);
                        bucket.ItemCount = bucket.Items.Count;
                        bucket.ReclaimableBytes = bucket.Items.Sum(i => i.Size);
                        bucket.Status = CleanupStatus.Scanned;
                        bucket.StatusMessage = $"Found {bucket.ItemCount} items ({bucket.ReclaimableSize})";

                        // Generate top safety reasons for the bucket
                        bucket.TopReasons = GenerateTopReasons(bucket.Items);
                    }
                    catch (OperationCanceledException)
                    {
                        bucket.Status = CleanupStatus.Error;
                        bucket.StatusMessage = "Scan cancelled";
                        throw;
                    }
                    catch (Exception ex)
                    {
                        bucket.Status = CleanupStatus.Error;
                        bucket.StatusMessage = $"Error: {ex.Message}";
                    }
                }, cancellationToken);
            }

            return buckets;
        }

        public async Task<List<CleanupAction>> CleanBucketAsync(
            CleanupBucket bucket,
            IProgress<string> statusUpdate,
            CancellationToken cancellationToken)
        {
            var actions = new List<CleanupAction>();
            bucket.Status = CleanupStatus.Cleaning;

            await Task.Run(() =>
            {
                foreach (var item in bucket.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var action = MoveToQuarantine(item);
                        actions.Add(action);

                        if (action.Success)
                        {
                            statusUpdate.Report($"Moved: {Path.GetFileName(item.Path)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        actions.Add(new CleanupAction
                        {
                            SourcePath = item.Path,
                            QuarantinePath = string.Empty,
                            ActionTime = DateTime.Now,
                            ActionType = CleanupActionType.Skipped,
                            Size = item.Size,
                            Success = false,
                            ErrorMessage = ex.Message
                        });
                    }
                }

                bucket.Status = CleanupStatus.Cleaned;
                bucket.StatusMessage = $"Cleaned {actions.Count(a => a.Success)} items";
            }, cancellationToken);

            return actions;
        }

        private CleanupAction MoveToQuarantine(CleanupItem item)
        {
            var relativePath = GetRelativePath(item.Path);
            var quarantinePath = Path.Combine(_quarantinePath, relativePath);

            try
            {
                var quarantineDir = Path.GetDirectoryName(quarantinePath);
                if (quarantineDir != null && !Directory.Exists(quarantineDir))
                {
                    Directory.CreateDirectory(quarantineDir);
                }

                if (item.Type == CleanupItemType.File)
                {
                    if (File.Exists(item.Path))
                    {
                        File.Move(item.Path, quarantinePath, overwrite: true);
                    }
                }
                else
                {
                    if (Directory.Exists(item.Path))
                    {
                        Directory.Move(item.Path, quarantinePath);
                    }
                }

                return new CleanupAction
                {
                    SourcePath = item.Path,
                    QuarantinePath = quarantinePath,
                    ActionTime = DateTime.Now,
                    ActionType = CleanupActionType.MovedToQuarantine,
                    Size = item.Size,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                return new CleanupAction
                {
                    SourcePath = item.Path,
                    QuarantinePath = quarantinePath,
                    ActionTime = DateTime.Now,
                    ActionType = CleanupActionType.Skipped,
                    Size = item.Size,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private string GetRelativePath(string fullPath)
        {
            var pathRoot = Path.GetPathRoot(fullPath);
            if (pathRoot != null)
            {
                var relativePath = fullPath.Substring(pathRoot.Length);
                return relativePath;
            }
            return fullPath;
        }

        private List<string> GenerateTopReasons(List<CleanupItem> items)
        {
            var reasons = new List<string>();

            if (items.Count == 0)
                return reasons;

            var scoringService = new ScoringService();
            var reasonCounts = new Dictionary<string, int>();

            // Sample up to 10 items to get common reasons
            var samplesToCheck = Math.Min(items.Count, 10);
            foreach (var item in items.Take(samplesToCheck))
            {
                try
                {
                    var result = item.Type == CleanupItemType.File
                        ? scoringService.ScoreFile(item.Path, item.Size, item.LastModified)
                        : scoringService.ScoreFolder(item.Path, item.Size, item.LastModified, 0);

                    foreach (var reason in result.Reasons)
                    {
                        if (!reasonCounts.ContainsKey(reason))
                            reasonCounts[reason] = 0;
                        reasonCounts[reason]++;
                    }
                }
                catch { }
            }

            // Return top 3-5 most common reasons
            reasons = reasonCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(5)
                .Select(kvp => kvp.Key)
                .ToList();

            return reasons;
        }

        public string GetQuarantinePath() => _quarantinePath;
    }

    public interface IBucketRule
    {
        string Name { get; }
        string Description { get; }
        List<CleanupItem> ScanForItems(CancellationToken cancellationToken);
    }

    public class UserTempBucketRule : IBucketRule
    {
        public string Name => "User Temp Files";
        public string Description => "Temporary files in your user temp folder";

        public List<CleanupItem> ScanForItems(CancellationToken cancellationToken)
        {
            var items = new List<CleanupItem>();
            var tempPath = Path.GetTempPath();

            try
            {
                var dirInfo = new DirectoryInfo(tempPath);
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (!IsFileLocked(file))
                        {
                            items.Add(new CleanupItem
                            {
                                Path = file.FullName,
                                Size = file.Length,
                                LastModified = file.LastWriteTime,
                                Type = CleanupItemType.File
                            });
                        }
                    }
                    catch { }
                }

                foreach (var dir in dirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var size = GetDirectorySize(dir);
                        items.Add(new CleanupItem
                        {
                            Path = dir.FullName,
                            Size = size,
                            LastModified = dir.LastWriteTime,
                            Type = CleanupItemType.Folder
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return items;
        }

        private bool IsFileLocked(FileInfo file)
        {
            try
            {
                using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private long GetDirectorySize(DirectoryInfo dir)
        {
            long size = 0;
            try
            {
                foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
                {
                    try { size += file.Length; } catch { }
                }
            }
            catch { }
            return size;
        }
    }

    public class DownloadsInstallersBucketRule : IBucketRule
    {
        public string Name => "Old Installers";
        public string Description => "Installer files in Downloads older than 30 days";

        private static readonly string[] InstallerExtensions = 
        { 
            ".exe", ".msi", ".msix", ".appx", ".appxbundle", 
            ".zip", ".7z", ".rar", ".tar", ".gz", ".iso" 
        };

        public List<CleanupItem> ScanForItems(CancellationToken cancellationToken)
        {
            var items = new List<CleanupItem>();
            var downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            downloadsPath = Path.Combine(downloadsPath, "Downloads");

            if (!Directory.Exists(downloadsPath))
                return items;

            var cutoffDate = DateTime.Now.AddDays(-30);

            try
            {
                var dirInfo = new DirectoryInfo(downloadsPath);
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var extension = file.Extension.ToLowerInvariant();
                        if (InstallerExtensions.Contains(extension) && file.LastWriteTime < cutoffDate)
                        {
                            items.Add(new CleanupItem
                            {
                                Path = file.FullName,
                                Size = file.Length,
                                LastModified = file.LastWriteTime,
                                Type = CleanupItemType.File
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return items;
        }
    }

    public class NodeModulesBucketRule : IBucketRule
    {
        public string Name => "node_modules Folders";
        public string Description => "Node.js dependencies folders (can be restored with npm install)";

        public List<CleanupItem> ScanForItems(CancellationToken cancellationToken)
        {
            var items = new List<CleanupItem>();
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var searchPaths = new[]
            {
                userProfile,
                Path.Combine(userProfile, "source"),
                Path.Combine(userProfile, "repos"),
                Path.Combine(userProfile, "projects"),
                Path.Combine(userProfile, "dev"),
                Path.Combine(userProfile, "Documents")
            };

            foreach (var searchPath in searchPaths)
            {
                if (!Directory.Exists(searchPath))
                    continue;

                try
                {
                    FindNodeModules(searchPath, items, cancellationToken, maxDepth: 5);
                }
                catch { }
            }

            return items;
        }

        private void FindNodeModules(string path, List<CleanupItem> items, CancellationToken cancellationToken, int maxDepth, int currentDepth = 0)
        {
            if (currentDepth >= maxDepth)
                return;

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var dirInfo = new DirectoryInfo(path);
                foreach (var subDir in dirInfo.GetDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (subDir.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
                        {
                            var size = GetDirectorySize(subDir);
                            items.Add(new CleanupItem
                            {
                                Path = subDir.FullName,
                                Size = size,
                                LastModified = subDir.LastWriteTime,
                                Type = CleanupItemType.Folder
                            });
                        }
                        else if (!subDir.Name.StartsWith('.') && 
                                 (subDir.Attributes & FileAttributes.ReparsePoint) != FileAttributes.ReparsePoint)
                        {
                            FindNodeModules(subDir.FullName, items, cancellationToken, maxDepth, currentDepth + 1);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private long GetDirectorySize(DirectoryInfo dir)
        {
            long size = 0;
            try
            {
                foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
                {
                    try { size += file.Length; } catch { }
                }
            }
            catch { }
            return size;
        }
    }

    public class NvidiaCacheBucketRule : IBucketRule
    {
        public string Name => "NVIDIA Cache";
        public string Description => "NVIDIA driver cache and downloader files (safe to delete, will be recreated as needed)";

        private static readonly string[] NvidiaPaths = 
        {
            @"C:\ProgramData\NVIDIA Corporation\Downloader",
            @"C:\ProgramData\NVIDIA Corporation\NV_Cache"
        };

        public List<CleanupItem> ScanForItems(CancellationToken cancellationToken)
        {
            var items = new List<CleanupItem>();
            var scoringService = new ScoringService();

            foreach (var basePath in NvidiaPaths)
            {
                if (!Directory.Exists(basePath))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var dirInfo = new DirectoryInfo(basePath);

                    foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var scoringResult = scoringService.ScoreFile(file.FullName, file.Length, file.LastWriteTime);

                            if (scoringResult.Classification != SafetyClassification.Blocked)
                            {
                                items.Add(new CleanupItem
                                {
                                    Path = file.FullName,
                                    Size = file.Length,
                                    LastModified = file.LastWriteTime,
                                    Type = CleanupItemType.File
                                });
                            }
                        }
                        catch { }
                    }

                    foreach (var dir in dirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var size = GetDirectorySize(dir, cancellationToken);
                            var scoringResult = scoringService.ScoreFolder(dir.FullName, size, dir.LastWriteTime, 0);

                            if (scoringResult.Classification != SafetyClassification.Blocked)
                            {
                                items.Add(new CleanupItem
                                {
                                    Path = dir.FullName,
                                    Size = size,
                                    LastModified = dir.LastWriteTime,
                                    Type = CleanupItemType.Folder
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return items;
        }

        private long GetDirectorySize(DirectoryInfo dir, CancellationToken cancellationToken)
        {
            long size = 0;
            try
            {
                foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { size += file.Length; } catch { }
                }
            }
            catch { }
            return size;
        }
    }

    public class AsusGpuTweakCacheBucketRule : IBucketRule
    {
        public string Name => "ASUS GPU Tweak Cache";
        public string Description => "ASUS GPU Tweak temporary files (logs, cache, updates - program files are never touched)";

        private static readonly string BaseAsusPath = @"C:\Program Files (x86)\ASUS\GPU TweakII";
        private static readonly string[] SafeSubfolders = { "logs", "cache", "temp", "update", "Logs", "Cache", "Temp", "Update" };

        public List<CleanupItem> ScanForItems(CancellationToken cancellationToken)
        {
            var items = new List<CleanupItem>();
            var scoringService = new ScoringService();

            if (!Directory.Exists(BaseAsusPath))
                return items;

            try
            {
                var baseDir = new DirectoryInfo(BaseAsusPath);

                foreach (var subDir in baseDir.GetDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!SafeSubfolders.Contains(subDir.Name, StringComparer.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        foreach (var file in subDir.GetFiles("*", SearchOption.AllDirectories))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            try
                            {
                                var scoringResult = scoringService.ScoreFile(file.FullName, file.Length, file.LastWriteTime);

                                if (scoringResult.Classification != SafetyClassification.Blocked)
                                {
                                    items.Add(new CleanupItem
                                    {
                                        Path = file.FullName,
                                        Size = file.Length,
                                        LastModified = file.LastWriteTime,
                                        Type = CleanupItemType.File
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return items;
        }
    }

    public class VendorCachesBucketRule : IBucketRule
    {
        public string Name => "Vendor Cache Folders";
        public string Description => "Generic vendor temporary files (Logs, Cache, Temp, Downloader subfolders under ProgramData and Program Files)";

        private static readonly string[] VendorBasePaths = 
        {
            @"C:\ProgramData",
            @"C:\Program Files (x86)"
        };

        private static readonly string[] CacheFolderNames = { "Logs", "Cache", "Temp", "Downloader", "logs", "cache", "temp", "downloader" };

        public List<CleanupItem> ScanForItems(CancellationToken cancellationToken)
        {
            var items = new List<CleanupItem>();
            var scoringService = new ScoringService();

            foreach (var basePath in VendorBasePaths)
            {
                if (!Directory.Exists(basePath))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var baseDir = new DirectoryInfo(basePath);

                    foreach (var vendorDir in baseDir.GetDirectories("*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (vendorDir.Name.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                            vendorDir.Name.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            ScanVendorDirectory(vendorDir, items, scoringService, cancellationToken);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return items;
        }

        private void ScanVendorDirectory(DirectoryInfo vendorDir, List<CleanupItem> items, ScoringService scoringService, CancellationToken cancellationToken)
        {
            try
            {
                foreach (var subDir in vendorDir.GetDirectories("*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!CacheFolderNames.Contains(subDir.Name, StringComparer.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        foreach (var file in subDir.GetFiles("*", SearchOption.AllDirectories))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            try
                            {
                                var scoringResult = scoringService.ScoreFile(file.FullName, file.Length, file.LastWriteTime);

                                if (scoringResult.Classification != SafetyClassification.Blocked)
                                {
                                    items.Add(new CleanupItem
                                    {
                                        Path = file.FullName,
                                        Size = file.Length,
                                        LastModified = file.LastWriteTime,
                                        Type = CleanupItemType.File
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    public class NuGetGlobalPackagesBucketRule : IBucketRule
    {
        public string Name => "NuGet Global Packages";
        public string Description => "NuGet package cache (safe to delete, restored on next build/restore)";

        public List<CleanupItem> ScanForItems(CancellationToken cancellationToken)
        {
            var items = new List<CleanupItem>();
            var scoringService = new ScoringService();

            // Check NUGET_PACKAGES environment variable first, then fall back to default location
            var nugetPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (string.IsNullOrEmpty(nugetPath))
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                nugetPath = Path.Combine(userProfile, ".nuget", "packages");
            }

            if (!Directory.Exists(nugetPath))
                return items;

            try
            {
                var dirInfo = new DirectoryInfo(nugetPath);

                // Scan package folders (each package/version is a folder)
                foreach (var packageDir in dirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // Get all version folders for this package
                        foreach (var versionDir in packageDir.GetDirectories("*", SearchOption.TopDirectoryOnly))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            try
                            {
                                var size = GetDirectorySize(versionDir, cancellationToken);
                                var scoringResult = scoringService.ScoreFolder(versionDir.FullName, size, versionDir.LastWriteTime, 0);

                                if (scoringResult.Classification != SafetyClassification.Blocked)
                                {
                                    items.Add(new CleanupItem
                                    {
                                        Path = versionDir.FullName,
                                        Size = size,
                                        LastModified = versionDir.LastWriteTime,
                                        Type = CleanupItemType.Folder
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return items;
        }

        private long GetDirectorySize(DirectoryInfo dir, CancellationToken cancellationToken)
        {
            long size = 0;
            try
            {
                foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { size += file.Length; } catch { }
                }
            }
            catch { }
            return size;
        }
    }

    public class VisualStudioCacheBucketRule : IBucketRule
    {
        public string Name => "Visual Studio Installer Cache";
        public string Description => "Visual Studio installer package cache (safe to delete, re-downloaded when needed)";

        private static readonly string[] CachePaths = 
        {
            @"C:\ProgramData\Microsoft\VisualStudio\Packages",
            @"C:\ProgramData\Package Cache",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "VisualStudio", "Packages")
        };

        public List<CleanupItem> ScanForItems(CancellationToken cancellationToken)
        {
            var items = new List<CleanupItem>();
            var scoringService = new ScoringService();

            foreach (var basePath in CachePaths)
            {
                if (!Directory.Exists(basePath))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var dirInfo = new DirectoryInfo(basePath);

                    // Scan files directly in cache directory
                    foreach (var file in dirInfo.GetFiles("*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var scoringResult = scoringService.ScoreFile(file.FullName, file.Length, file.LastWriteTime);

                            if (scoringResult.Classification != SafetyClassification.Blocked)
                            {
                                items.Add(new CleanupItem
                                {
                                    Path = file.FullName,
                                    Size = file.Length,
                                    LastModified = file.LastWriteTime,
                                    Type = CleanupItemType.File
                                });
                            }
                        }
                        catch { }
                    }

                    // Scan cache subdirectories
                    foreach (var dir in dirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var size = GetDirectorySize(dir, cancellationToken);
                            var scoringResult = scoringService.ScoreFolder(dir.FullName, size, dir.LastWriteTime, 0);

                            if (scoringResult.Classification != SafetyClassification.Blocked)
                            {
                                items.Add(new CleanupItem
                                {
                                    Path = dir.FullName,
                                    Size = size,
                                    LastModified = dir.LastWriteTime,
                                    Type = CleanupItemType.Folder
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return items;
        }

        private long GetDirectorySize(DirectoryInfo dir, CancellationToken cancellationToken)
        {
            long size = 0;
            try
            {
                foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { size += file.Length; } catch { }
                }
            }
            catch { }
            return size;
        }
    }

    public class BuildArtifactsBucketRule : IBucketRule
    {
        public string Name => "Build Artifacts (bin/obj)";
        public string Description => "Build output folders (safe to delete, regenerated on next build)";

        private static readonly string[] CommonProjectRoots = 
        {
            @"D:\Projects",
            @"D:\Source",
            @"D:\Dev",
            @"D:\repos",
            @"C:\Projects",
            @"C:\Source",
            @"C:\Dev",
            @"C:\repos",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "repos"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "projects"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "dev"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Visual Studio 2022", "Projects")
        };

        public List<CleanupItem> ScanForItems(CancellationToken cancellationToken)
        {
            var items = new List<CleanupItem>();
            var scoringService = new ScoringService();

            foreach (var searchPath in CommonProjectRoots)
            {
                if (!Directory.Exists(searchPath))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    FindBuildArtifacts(searchPath, items, scoringService, cancellationToken, maxDepth: 8);
                }
                catch { }
            }

            return items;
        }

        private void FindBuildArtifacts(string path, List<CleanupItem> items, ScoringService scoringService, CancellationToken cancellationToken, int maxDepth, int currentDepth = 0)
        {
            if (currentDepth >= maxDepth)
                return;

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var dirInfo = new DirectoryInfo(path);

                foreach (var subDir in dirInfo.GetDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var dirName = subDir.Name.ToLowerInvariant();

                        // Check if this is a bin or obj folder
                        if (dirName == "bin" || dirName == "obj")
                        {
                            var size = GetDirectorySize(subDir, cancellationToken);
                            var scoringResult = scoringService.ScoreFolder(subDir.FullName, size, subDir.LastWriteTime, 0);

                            if (scoringResult.Classification != SafetyClassification.Blocked)
                            {
                                items.Add(new CleanupItem
                                {
                                    Path = subDir.FullName,
                                    Size = size,
                                    LastModified = subDir.LastWriteTime,
                                    Type = CleanupItemType.Folder
                                });
                            }
                        }
                        // Skip common non-project folders to speed up scanning
                        else if (!ShouldSkipDirectory(subDir.Name))
                        {
                            FindBuildArtifacts(subDir.FullName, items, scoringService, cancellationToken, maxDepth, currentDepth + 1);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private bool ShouldSkipDirectory(string dirName)
        {
            var lowerName = dirName.ToLowerInvariant();
            return lowerName == "node_modules" ||
                   lowerName == ".git" ||
                   lowerName == ".svn" ||
                   lowerName == "packages" ||
                   lowerName == ".nuget" ||
                   dirName.StartsWith('$') ||
                   dirName.StartsWith('.');
        }

        private long GetDirectorySize(DirectoryInfo dir, CancellationToken cancellationToken)
        {
            long size = 0;
            try
            {
                foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { size += file.Length; } catch { }
                }
            }
            catch { }
            return size;
        }
    }
}
