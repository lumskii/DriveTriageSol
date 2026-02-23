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
                new NodeModulesBucketRule()
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
}
