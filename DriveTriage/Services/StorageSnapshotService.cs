using System.IO;
using System.Text.Json;
using DriveTriage.ViewModels;

namespace DriveTriage.Services
{
    public class StorageSnapshotService
    {
        private readonly string _snapshotsDirectory;
        private readonly string _ignoreListPath;
        private const string SnapshotFilePattern = "snapshot_*.json";
        private const string IgnoreListFileName = "ignore_list.json";
        private const int MaxSnapshots = 12;

        public StorageSnapshotService(string? snapshotsDirectory = null)
        {
            _snapshotsDirectory = snapshotsDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DriveTriage",
                "Snapshots");

            _ignoreListPath = Path.Combine(_snapshotsDirectory, IgnoreListFileName);

            if (!Directory.Exists(_snapshotsDirectory))
            {
                Directory.CreateDirectory(_snapshotsDirectory);
            }
        }

        public async Task<StorageSnapshot> TakeSnapshotAsync(
            IProgress<string>? statusUpdate = null,
            CancellationToken cancellationToken = default)
        {
            var snapshot = new StorageSnapshot
            {
                Timestamp = DateTime.Now,
                FolderSnapshots = new List<FolderSnapshot>()
            };

            // Define paths to monitor
            var pathsToMonitor = new List<string>
            {
                @"C:\Program Files",
                @"C:\Program Files (x86)",
                @"C:\ProgramData",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            };

            // Take snapshots of each path
            foreach (var path in pathsToMonitor)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(path))
                    continue;

                statusUpdate?.Report($"Analyzing {path}...");

                try
                {
                    var folderSnapshot = await Task.Run(() => 
                        AnalyzeTopLevelFolders(path, cancellationToken), cancellationToken);
                    
                    snapshot.FolderSnapshots.Add(folderSnapshot);
                }
                catch (Exception ex)
                {
                    statusUpdate?.Report($"Warning: Could not analyze {path}: {ex.Message}");
                }
            }

            // Save snapshot to disk
            await SaveSnapshotAsync(snapshot, cancellationToken);

            statusUpdate?.Report($"Snapshot completed: {snapshot.FolderSnapshots.Sum(f => f.SubfolderSizes.Count)} folders tracked");

            return snapshot;
        }

        private FolderSnapshot AnalyzeTopLevelFolders(string rootPath, CancellationToken cancellationToken)
        {
            var folderSnapshot = new FolderSnapshot
            {
                RootPath = rootPath,
                SubfolderSizes = new Dictionary<string, long>()
            };

            try
            {
                // Get all top-level subdirectories
                var topLevelDirs = Directory.GetDirectories(rootPath, "*", SearchOption.TopDirectoryOnly);

                foreach (var dir in topLevelDirs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var size = CalculateFolderSize(dir, cancellationToken);
                        folderSnapshot.SubfolderSizes[dir] = size;
                    }
                    catch
                    {
                        // Skip folders we can't access
                        folderSnapshot.SubfolderSizes[dir] = 0;
                    }
                }
            }
            catch
            {
                // Handle directory access errors
            }

            return folderSnapshot;
        }

        private long CalculateFolderSize(string folderPath, CancellationToken cancellationToken)
        {
            long totalSize = 0;

            try
            {
                // Get files in current directory
                foreach (var file in Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
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

                // Recursively process subdirectories
                foreach (var subDir in Directory.GetDirectories(folderPath, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    try
                    {
                        totalSize += CalculateFolderSize(subDir, cancellationToken);
                    }
                    catch
                    {
                        // Skip subdirectories we can't access
                    }
                }
            }
            catch
            {
                // Handle directory access errors
            }

            return totalSize;
        }

        private async Task SaveSnapshotAsync(StorageSnapshot snapshot, CancellationToken cancellationToken)
        {
            var timestamp = snapshot.Timestamp.ToString("yyyyMMdd_HHmmss");
            var fileName = $"snapshot_{timestamp}.json";
            var filePath = Path.Combine(_snapshotsDirectory, fileName);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(snapshot, options);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);

            // Maintain rolling window - delete old snapshots beyond MaxSnapshots
            await CleanupOldSnapshotsAsync(cancellationToken);
        }

        private async Task CleanupOldSnapshotsAsync(CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                var snapshotFiles = Directory.GetFiles(_snapshotsDirectory, SnapshotFilePattern)
                    .OrderByDescending(f => f)
                    .ToList();

                if (snapshotFiles.Count > MaxSnapshots)
                {
                    var filesToDelete = snapshotFiles.Skip(MaxSnapshots);
                    foreach (var file in filesToDelete)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            // Continue if we can't delete a file
                        }
                    }
                }
            }, cancellationToken);
        }

        public async Task<List<string>> LoadIgnoreListAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_ignoreListPath))
                return new List<string>();

            try
            {
                var json = await File.ReadAllTextAsync(_ignoreListPath, cancellationToken);
                var ignoreList = JsonSerializer.Deserialize<IgnoreList>(json);
                return ignoreList?.Paths ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        public async Task SaveIgnoreListAsync(List<string> paths, CancellationToken cancellationToken = default)
        {
            var ignoreList = new IgnoreList { Paths = paths };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(ignoreList, options);
            await File.WriteAllTextAsync(_ignoreListPath, json, cancellationToken);
        }

        public async Task AddToIgnoreListAsync(string path, CancellationToken cancellationToken = default)
        {
            var ignoreList = await LoadIgnoreListAsync(cancellationToken);

            if (!ignoreList.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                ignoreList.Add(path);
                await SaveIgnoreListAsync(ignoreList, cancellationToken);
            }
        }

        public async Task RemoveFromIgnoreListAsync(string path, CancellationToken cancellationToken = default)
        {
            var ignoreList = await LoadIgnoreListAsync(cancellationToken);
            ignoreList.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
            await SaveIgnoreListAsync(ignoreList, cancellationToken);
        }

        private bool IsPathIgnored(string path, List<string> ignoreList)
        {
            return ignoreList.Any(ignoredPath => 
                path.Equals(ignoredPath, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(ignoredPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<List<StorageSnapshot>> LoadSnapshotsAsync(CancellationToken cancellationToken = default)
        {
            var snapshots = new List<StorageSnapshot>();

            if (!Directory.Exists(_snapshotsDirectory))
                return snapshots;

            var snapshotFiles = Directory.GetFiles(_snapshotsDirectory, SnapshotFilePattern)
                .OrderByDescending(f => f) // Most recent first
                .ToList();

            foreach (var file in snapshotFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var snapshot = JsonSerializer.Deserialize<StorageSnapshot>(json);
                    
                    if (snapshot != null)
                    {
                        snapshots.Add(snapshot);
                    }
                }
                catch
                {
                    // Skip corrupted snapshot files
                }
            }

            return snapshots;
        }

        public async Task<List<GrowthAlert>> CompareLatestSnapshotsAsync(
            IProgress<string>? statusUpdate = null,
            CancellationToken cancellationToken = default)
        {
            var alerts = new List<GrowthAlert>();

            statusUpdate?.Report("Loading snapshots...");

            var snapshots = await LoadSnapshotsAsync(cancellationToken);
            var ignoreList = await LoadIgnoreListAsync(cancellationToken);

            if (snapshots.Count < 2)
            {
                statusUpdate?.Report("Need at least 2 snapshots to compare. Please take another snapshot.");
                return alerts;
            }

            var latest = snapshots[0];
            var previous = snapshots[1];

            statusUpdate?.Report($"Comparing snapshots from {previous.Timestamp:g} to {latest.Timestamp:g}...");

            // Compare each root path
            foreach (var latestFolder in latest.FolderSnapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var previousFolder = previous.FolderSnapshots
                    .FirstOrDefault(f => f.RootPath.Equals(latestFolder.RootPath, StringComparison.OrdinalIgnoreCase));

                if (previousFolder == null)
                    continue;

                // Compare each subfolder
                foreach (var (subfolderPath, currentSize) in latestFolder.SubfolderSizes)
                {
                    // Skip ignored paths
                    if (IsPathIgnored(subfolderPath, ignoreList))
                        continue;

                    var previousSize = previousFolder.SubfolderSizes.TryGetValue(subfolderPath, out var prevSize)
                        ? prevSize
                        : 0;

                    var delta = currentSize - previousSize;

                    // Only report if there's growth > 1 MB
                    if (delta > 1024 * 1024)
                    {
                        var alert = new GrowthAlert
                        {
                            Path = subfolderPath,
                            PreviousBytes = previousSize,
                            CurrentBytes = currentSize,
                            DeltaBytes = delta,
                            Timestamp = latest.Timestamp
                        };

                        // Classify using PathRules
                        var classification = PathRules.ClassifyPath(subfolderPath);
                        alert.Classification = (ViewModels.SafetyClassification)classification.Level;
                        alert.ClassificationReasons = new List<string> { classification.Reason };

                        alerts.Add(alert);
                    }
                }

                // Check for new folders
                foreach (var (subfolderPath, currentSize) in latestFolder.SubfolderSizes)
                {
                    if (!previousFolder.SubfolderSizes.ContainsKey(subfolderPath) && currentSize > 1024 * 1024)
                    {
                        // Already handled above as previousSize = 0
                    }
                }
            }

            // Sort by delta descending
            alerts = alerts.OrderByDescending(a => a.DeltaBytes).ToList();

            statusUpdate?.Report($"Found {alerts.Count} folders with significant growth");

            return alerts;
        }

        public async Task<List<GrowthAlert>> GetTopGrowersAsync(
            int daysBack,
            int topCount = 20,
            IProgress<string>? statusUpdate = null,
            CancellationToken cancellationToken = default)
        {
            var alerts = new List<GrowthAlert>();

            statusUpdate?.Report("Loading snapshots...");

            var snapshots = await LoadSnapshotsAsync(cancellationToken);
            var ignoreList = await LoadIgnoreListAsync(cancellationToken);

            if (snapshots.Count < 2)
            {
                statusUpdate?.Report("Need at least 2 snapshots to analyze growth.");
                return alerts;
            }

            var cutoffDate = DateTime.Now.AddDays(-daysBack);
            var latest = snapshots[0];

            // Find the oldest snapshot within the time window
            var oldest = snapshots
                .Where(s => s.Timestamp >= cutoffDate)
                .OrderBy(s => s.Timestamp)
                .FirstOrDefault();

            if (oldest == null || oldest.Timestamp == latest.Timestamp)
            {
                statusUpdate?.Report($"No snapshots found within {daysBack} days window.");
                return alerts;
            }

            statusUpdate?.Report($"Analyzing growth from {oldest.Timestamp:g} to {latest.Timestamp:g}...");

            var daysDifference = (latest.Timestamp - oldest.Timestamp).TotalDays;

            // Compare each root path
            foreach (var latestFolder in latest.FolderSnapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var oldestFolder = oldest.FolderSnapshots
                    .FirstOrDefault(f => f.RootPath.Equals(latestFolder.RootPath, StringComparison.OrdinalIgnoreCase));

                if (oldestFolder == null)
                    continue;

                // Compare each subfolder
                foreach (var (subfolderPath, currentSize) in latestFolder.SubfolderSizes)
                {
                    // Skip ignored paths
                    if (IsPathIgnored(subfolderPath, ignoreList))
                        continue;

                    var previousSize = oldestFolder.SubfolderSizes.TryGetValue(subfolderPath, out var prevSize)
                        ? prevSize
                        : 0;

                    var delta = currentSize - previousSize;

                    // Only report if there's growth > 1 MB
                    if (delta > 1024 * 1024)
                    {
                        var alert = new GrowthAlert
                        {
                            Path = subfolderPath,
                            PreviousBytes = previousSize,
                            CurrentBytes = currentSize,
                            DeltaBytes = delta,
                            Timestamp = latest.Timestamp
                        };

                        // Calculate growth rate (bytes per day)
                        alert.GrowthRateBytesPerDay = daysDifference > 0 
                            ? delta / daysDifference 
                            : 0;

                        // Classify using PathRules
                        var classification = PathRules.ClassifyPath(subfolderPath);
                        alert.Classification = (ViewModels.SafetyClassification)classification.Level;
                        alert.ClassificationReasons = new List<string> { classification.Reason };

                        alerts.Add(alert);
                    }
                }
            }

            // Sort by absolute growth first, then by growth rate
            alerts = alerts
                .OrderByDescending(a => a.DeltaBytes)
                .ThenByDescending(a => a.GrowthRateBytesPerDay)
                .Take(topCount)
                .ToList();

            statusUpdate?.Report($"Found {alerts.Count} top growers over {daysBack} days");

            return alerts;
        }

        public async Task<int> GetSnapshotCountAsync()
        {
            if (!Directory.Exists(_snapshotsDirectory))
                return 0;

            return await Task.Run(() => 
                Directory.GetFiles(_snapshotsDirectory, SnapshotFilePattern).Length);
        }

        public async Task<DateTime?> GetLatestSnapshotDateAsync()
        {
            var snapshots = await LoadSnapshotsAsync();
            return snapshots.FirstOrDefault()?.Timestamp;
        }
    }

    public class StorageSnapshot
    {
        public DateTime Timestamp { get; set; }
        public List<FolderSnapshot> FolderSnapshots { get; set; } = new();
    }

    public class FolderSnapshot
    {
        public required string RootPath { get; set; }
        public Dictionary<string, long> SubfolderSizes { get; set; } = new();
    }

    public class IgnoreList
    {
        public List<string> Paths { get; set; } = new();
    }
}
