using System.IO;

namespace DriveTriage.ViewModels
{
    public class FileSystemItem
    {
        public string Path { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;
    }

    public class CleanupBucket
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public long ReclaimableBytes { get; set; }
        public string ReclaimableSize => FormatSize(ReclaimableBytes);
        public int ItemCount { get; set; }
        public List<CleanupItem> Items { get; set; } = new();
        public CleanupStatus Status { get; set; } = CleanupStatus.Ready;
        public string StatusMessage { get; set; } = string.Empty;
        public List<string> TopReasons { get; set; } = new();

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

    public class CleanupItem
    {
        public required string Path { get; init; }
        public long Size { get; init; }
        public DateTime LastModified { get; init; }
        public CleanupItemType Type { get; init; }
    }

    public enum CleanupItemType
    {
        File,
        Folder
    }

    public enum CleanupStatus
    {
        Ready,
        Scanning,
        Scanned,
        Cleaning,
        Cleaned,
        Error
    }

    public class CleanupAction
    {
        public required string SourcePath { get; init; }
        public required string QuarantinePath { get; init; }
        public required DateTime ActionTime { get; init; }
        public required CleanupActionType ActionType { get; init; }
        public long Size { get; init; }
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public enum CleanupActionType
    {
        MovedToQuarantine,
        Deleted,
        Skipped
    }

    public class ScoringResult
    {
        public SafetyClassification Classification { get; init; }
        public int Score { get; init; }
        public List<string> Reasons { get; init; } = new();
        public string ReasonSummary { get; init; } = string.Empty;
        public Services.PathClassification? PathClassification { get; init; }
        public string? LLMExplanation { get; init; }

        public string ClassificationText => Classification switch
        {
            SafetyClassification.Safe => "✅ Safe",
            SafetyClassification.Caution => "⚠️ Caution",
            SafetyClassification.Blocked => "🚫 Blocked",
            _ => "Unknown"
        };

        public string ScoreDisplay => $"{Score}/100";

        public string GetReasonText() => string.Join("\n• ", new[] { "" }.Concat(Reasons));
    }

    public enum SafetyClassification
    {
        Safe = 0,
        Caution = 1,
        Blocked = 2
    }

    public class ScoredFileSystemItem
    {
        public required string Path { get; init; }
        public required string Size { get; init; }
        public required string LastModified { get; init; }
        public required ScoringResult ScoringResult { get; init; }

        public string Classification => ScoringResult.ClassificationText;
        public int Score => ScoringResult.Score;
        public string ReasonSummary => ScoringResult.ReasonSummary;
    }

    public class InstalledApp
    {
        public required string DisplayName { get; init; }
        public required string Publisher { get; init; }
        public string DisplayVersion { get; init; } = string.Empty;
        public DateTime? InstallDate { get; init; }
        public long EstimatedSize { get; init; }
        public string InstallLocation { get; init; } = string.Empty;
        public string UninstallString { get; init; } = string.Empty;
        public string QuietUninstallString { get; init; } = string.Empty;
        public string RegistryKeyPath { get; init; } = string.Empty;

        public string FormattedSize => FormatSize(EstimatedSize);
        public string FormattedInstallDate => InstallDate?.ToString("yyyy-MM-dd") ?? "Unknown";

        private static string FormatSize(long bytes)
        {
            if (bytes == 0) return "Unknown";

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

    public class UninstallResult
    {
        public bool Success { get; init; }
        public string? Message { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public class CleanupSession
    {
        public required string SessionId { get; set; }
        public required string BucketName { get; set; }
        public DateTime Timestamp { get; set; }
        public int TotalActions { get; set; }
        public int SuccessfulActions { get; set; }
        public int FailedActions { get; set; }
        public long TotalSize { get; set; }
        public List<ActionRecord> Actions { get; set; } = new();

        public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        public string FormattedSize => FormatSize(TotalSize);
        public int RestorableCount => Actions.Count(a => a.Success && !a.IsRestored && a.Operation == "MovedToQuarantine");
        public bool HasRestorableItems => RestorableCount > 0;

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

    public class ActionRecord
    {
        public DateTime Timestamp { get; set; }
        public required string Operation { get; set; }
        public required string OriginalPath { get; set; }
        public required string NewPath { get; set; }
        public long SizeBytes { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsRestored { get; set; }

        public string FormattedSize => FormatSize(SizeBytes);
        public string FileName => Path.GetFileName(OriginalPath);

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

    public class RestoreResult
    {
        public bool Success { get; init; }
        public int RestoredCount { get; init; }
        public int FailedCount { get; init; }
        public List<string> Errors { get; init; } = new();
        public string? ErrorMessage { get; init; }

        public string Summary => Success
            ? $"Successfully restored {RestoredCount} items"
            : $"Restored {RestoredCount} items, {FailedCount} failed";
    }
}
