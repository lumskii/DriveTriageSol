using System.IO;
using DriveTriage.ViewModels;

namespace DriveTriage.Services
{
    public class PlanService
    {
        private readonly BucketsService _bucketsService;
        private readonly AppsService _appsService;
        private readonly SystemCleanupService _systemCleanupService;
        private readonly DriverCacheService _driverCacheService;

        public PlanService(
            BucketsService bucketsService,
            AppsService appsService,
            SystemCleanupService systemCleanupService,
            DriverCacheService driverCacheService)
        {
            _bucketsService = bucketsService;
            _appsService = appsService;
            _systemCleanupService = systemCleanupService;
            _driverCacheService = driverCacheService;
        }

        public async Task<RecoveryPlan> GeneratePlanAsync(
            DriveInfo targetDrive,
            long targetFreeBytes,
            IProgress<string>? statusUpdate = null,
            CancellationToken cancellationToken = default)
        {
            var plan = new RecoveryPlan
            {
                TargetFreeBytes = targetFreeBytes,
                CurrentFreeBytes = targetDrive.AvailableFreeSpace
            };

            var gapBytes = targetFreeBytes - targetDrive.AvailableFreeSpace;
            plan.RemainingGapBytes = gapBytes;

            if (gapBytes <= 0)
            {
                statusUpdate?.Report("Target free space already achieved!");
                return plan;
            }

            statusUpdate?.Report("Gathering recovery candidates...");

            // Collect all candidates
            var candidates = new List<RecoveryCandidate>();

            // 1. System cleanup candidates (always safe)
            await AddSystemCleanupCandidatesAsync(candidates, statusUpdate, cancellationToken);

            // 2. Cleanup buckets
            await AddBucketCandidatesAsync(candidates, targetDrive, statusUpdate, cancellationToken);

            // 3. Apps on target drive
            await AddAppCandidatesAsync(candidates, targetDrive, statusUpdate, cancellationToken);

            statusUpdate?.Report($"Found {candidates.Count} recovery candidates");

            // Sort candidates: Safe first (by size desc), then Caution (by size desc)
            var sortedCandidates = candidates
                .OrderBy(c => c.Risk)
                .ThenByDescending(c => c.EstimatedReclaimableBytes)
                .ToList();

            // Select candidates to meet goal
            long accumulated = 0;
            foreach (var candidate in sortedCandidates)
            {
                if (accumulated >= gapBytes)
                    break;

                candidate.IsSelected = true;
                plan.SelectedCandidates.Add(candidate);
                accumulated += candidate.EstimatedReclaimableBytes;
            }

            plan.RemainingGapBytes = Math.Max(0, gapBytes - accumulated);

            statusUpdate?.Report($"Plan generated: {plan.SelectedCandidates.Count} actions selected");

            return plan;
        }

        private async Task AddSystemCleanupCandidatesAsync(
            List<RecoveryCandidate> candidates,
            IProgress<string>? statusUpdate,
            CancellationToken cancellationToken)
        {
            statusUpdate?.Report("Analyzing system cleanup opportunities...");

            try
            {
                var systemInfo = await _systemCleanupService.GetSystemCleanupInfoAsync();

                if (systemInfo.RecycleBinSize > 0)
                {
                    candidates.Add(new RecoveryCandidate
                    {
                        Name = "Empty Recycle Bin",
                        Category = "System Cleanup",
                        EstimatedReclaimableBytes = systemInfo.RecycleBinSize,
                        Risk = SafetyClassification.Safe,
                        ActionKind = ActionKind.SystemAction,
                        ReferenceId = "RecycleBin"
                    });
                }

                if (systemInfo.QuarantineSize > 0)
                {
                    candidates.Add(new RecoveryCandidate
                    {
                        Name = "Clean Quarantine Folder",
                        Category = "System Cleanup",
                        EstimatedReclaimableBytes = systemInfo.QuarantineSize,
                        Risk = SafetyClassification.Safe,
                        ActionKind = ActionKind.SystemAction,
                        ReferenceId = "Quarantine"
                    });
                }

                if (systemInfo.NvidiaCacheSize > 0)
                {
                    candidates.Add(new RecoveryCandidate
                    {
                        Name = "Purge NVIDIA Cache",
                        Category = "System Cleanup",
                        EstimatedReclaimableBytes = systemInfo.NvidiaCacheSize,
                        Risk = SafetyClassification.Safe,
                        ActionKind = ActionKind.SystemAction,
                        ReferenceId = "NvidiaCache"
                    });
                }
            }
            catch (Exception ex)
            {
                statusUpdate?.Report($"Warning: Could not analyze system cleanup: {ex.Message}");
            }

            // Windows Update cache
            try
            {
                var updateCachePath = @"C:\Windows\SoftwareDistribution\Download";
                if (Directory.Exists(updateCachePath))
                {
                    var size = await Task.Run(() => CalculateDirectorySize(updateCachePath), cancellationToken);
                    if (size > 0)
                    {
                        candidates.Add(new RecoveryCandidate
                        {
                            Name = "Clear Windows Update Cache",
                            Category = "System Cleanup",
                            EstimatedReclaimableBytes = size,
                            Risk = SafetyClassification.Caution,
                            ActionKind = ActionKind.SystemAction,
                            ReferenceId = "WindowsUpdate"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                statusUpdate?.Report($"Warning: Could not analyze Windows Update cache: {ex.Message}");
            }
        }

        private async Task AddBucketCandidatesAsync(
            List<RecoveryCandidate> candidates,
            DriveInfo targetDrive,
            IProgress<string>? statusUpdate,
            CancellationToken cancellationToken)
        {
            statusUpdate?.Report("Scanning cleanup buckets...");

            try
            {
                var buckets = await _bucketsService.ScanBucketsAsync(
                    new Progress<string>(),
                    cancellationToken);

                foreach (var bucket in buckets)
                {
                    // Filter buckets to target drive if they have path-based items
                    var relevantSize = bucket.ReclaimableBytes;

                    // Check if bucket items are on target drive
                    if (bucket.Items.Any())
                    {
                        var driveItems = bucket.Items
                            .Where(item => item.Path.StartsWith(targetDrive.Name, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (driveItems.Any())
                        {
                            relevantSize = driveItems.Sum(i => i.Size);
                        }
                        else
                        {
                            // Skip buckets with no items on target drive
                            continue;
                        }
                    }

                    if (relevantSize > 0)
                    {
                        // Classify bucket based on top reasons
                        var risk = ClassifyBucketRisk(bucket);

                        candidates.Add(new RecoveryCandidate
                        {
                            Name = $"Clean: {bucket.Name}",
                            Category = "Cleanup Bucket",
                            EstimatedReclaimableBytes = relevantSize,
                            Risk = risk,
                            ActionKind = ActionKind.CleanBucket,
                            ReferenceId = $"Bucket:{bucket.Name}"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                statusUpdate?.Report($"Warning: Could not scan buckets: {ex.Message}");
            }
        }

        private async Task AddAppCandidatesAsync(
            List<RecoveryCandidate> candidates,
            DriveInfo targetDrive,
            IProgress<string>? statusUpdate,
            CancellationToken cancellationToken)
        {
            statusUpdate?.Report("Analyzing installed applications...");

            try
            {
                var apps = await _appsService.EnumerateInstalledAppsAsync(
                    new Progress<string>(),
                    cancellationToken,
                    targetDrive.Name.Substring(0, 1));

                // Filter apps on target drive
                var relevantApps = apps
                    .Where(app => !string.IsNullOrEmpty(app.InstallLocation) &&
                                  app.InstallLocation.StartsWith(targetDrive.Name, StringComparison.OrdinalIgnoreCase) &&
                                  app.EstimatedSize > 0 &&
                                  !string.IsNullOrEmpty(app.UninstallString))
                    .OrderByDescending(app => app.EstimatedSize)
                    .Take(20) // Top 20 largest apps
                    .ToList();

                foreach (var app in relevantApps)
                {
                    candidates.Add(new RecoveryCandidate
                    {
                        Name = $"Uninstall: {app.DisplayName}",
                        Category = "Application",
                        EstimatedReclaimableBytes = app.EstimatedSize,
                        Risk = SafetyClassification.Caution,
                        ActionKind = ActionKind.UninstallApp,
                        ReferenceId = $"App:{app.RegistryKeyPath}"
                    });
                }
            }
            catch (Exception ex)
            {
                statusUpdate?.Report($"Warning: Could not analyze apps: {ex.Message}");
            }
        }

        private SafetyClassification ClassifyBucketRisk(CleanupBucket bucket)
        {
            // Check if bucket has any blocked reasons
            if (bucket.TopReasons.Any(r => r.Contains("system", StringComparison.OrdinalIgnoreCase) ||
                                            r.Contains("critical", StringComparison.OrdinalIgnoreCase)))
            {
                return SafetyClassification.Blocked;
            }

            // Check for common safe bucket types
            var safeBuckets = new[] { "Browser Caches", "Temp Files", "Logs", "Download Folders" };
            if (safeBuckets.Any(s => bucket.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                return SafetyClassification.Safe;
            }

            // Default to caution
            return SafetyClassification.Caution;
        }

        private long CalculateDirectorySize(string path)
        {
            long size = 0;
            try
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        size += fileInfo.Length;
                    }
                    catch { }
                }
            }
            catch { }
            return size;
        }

        public async Task<PlanExecutionResult> ExecuteSafePlanAsync(
            RecoveryPlan plan,
            IProgress<string>? statusUpdate,
            CancellationToken cancellationToken)
        {
            var result = new PlanExecutionResult
            {
                StartTime = DateTime.Now
            };

            var safeCandidates = plan.SelectedCandidates
                .Where(c => c.Risk == SafetyClassification.Safe)
                .ToList();

            statusUpdate?.Report($"Executing {safeCandidates.Count} safe recovery actions...");

            foreach (var candidate in safeCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    statusUpdate?.Report($"Processing: {candidate.Name}...");

                    switch (candidate.ActionKind)
                    {
                        case ActionKind.SystemAction:
                            await ExecuteSystemActionAsync(candidate, statusUpdate, cancellationToken);
                            break;

                        case ActionKind.CleanBucket:
                            // Note: This would need to be coordinated with MainViewModel
                            // to access the actual bucket and trigger cleanup
                            statusUpdate?.Report($"Skipping bucket cleanup: {candidate.Name} (requires UI coordination)");
                            break;

                        case ActionKind.UninstallApp:
                            // Skip apps in safe execution
                            break;
                    }

                    result.SuccessfulActions++;
                    result.TotalReclaimed += candidate.EstimatedReclaimableBytes;
                }
                catch (Exception ex)
                {
                    result.FailedActions++;
                    result.Errors.Add($"{candidate.Name}: {ex.Message}");
                    statusUpdate?.Report($"Error: {candidate.Name} - {ex.Message}");
                }
            }

            result.EndTime = DateTime.Now;
            result.Success = result.FailedActions == 0;

            return result;
        }

        private async Task ExecuteSystemActionAsync(
            RecoveryCandidate candidate,
            IProgress<string>? statusUpdate,
            CancellationToken cancellationToken)
        {
            switch (candidate.ReferenceId)
            {
                case "RecycleBin":
                    await _systemCleanupService.EmptyRecycleBinAsync(
                        new Progress<string>(s => statusUpdate?.Report(s)),
                        cancellationToken);
                    break;

                case "Quarantine":
                    await _systemCleanupService.CleanQuarantineAsync(
                        new Progress<string>(s => statusUpdate?.Report(s)),
                        cancellationToken);
                    break;

                case "NvidiaCache":
                    var nvResult = await _driverCacheService.PurgeNvidiaCachesAsync(
                        new Progress<string>(s => statusUpdate?.Report(s)),
                        cancellationToken);
                    break;

                case "WindowsUpdate":
                    await _systemCleanupService.ClearWindowsUpdateDownloadCacheAsync(
                        new Progress<string>(s => statusUpdate?.Report(s)),
                        cancellationToken);
                    break;
            }
        }
    }

    public class PlanExecutionResult
    {
        public bool Success { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int SuccessfulActions { get; set; }
        public int FailedActions { get; set; }
        public long TotalReclaimed { get; set; }
        public List<string> Errors { get; set; } = new();

        public string FormattedTotalReclaimed => FormatSize(TotalReclaimed);
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
}
