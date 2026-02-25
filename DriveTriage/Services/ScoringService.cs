using System.IO;
using DriveTriage.ViewModels;
using PathClassification = DriveTriage.Services.PathClassification;

namespace DriveTriage.Services
{
    public class ScoringService
    {
        // Extension categories
        private static readonly string[] BlockedExtensions = 
        { 
            ".sys", ".dll", ".exe", ".drv", ".ocx", ".cpl", ".scr",
            ".msi", ".cab", ".inf", ".cat", ".mui"
        };

        private static readonly string[] CautionExtensions = 
        { 
            ".db", ".sqlite", ".mdf", ".ldf", ".accdb", ".config",
            ".ini", ".reg", ".pst", ".ost", ".vhd", ".vhdx"
        };

        private static readonly string[] SafeExtensions = 
        { 
            ".tmp", ".temp", ".bak", ".old", ".cache", ".log",
            ".dmp", ".etl", ".bak~", ".~"
        };

        // Size thresholds (in bytes)
        private const long VeryLargeFileSize = 10L * 1024 * 1024 * 1024; // 10 GB
        private const long LargeFileSize = 1L * 1024 * 1024 * 1024; // 1 GB
        private const long MediumFileSize = 100L * 1024 * 1024; // 100 MB

        // Age thresholds (in days)
        private const int VeryOldDays = 365 * 2; // 2 years
        private const int OldDays = 365; // 1 year
        private const int RecentDays = 30;

        public ScoringResult ScoreFile(string path, long size, DateTime lastModified)
        {
            var reasons = new List<string>();
            var pathClassification = PathRules.ClassifyPath(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var ageInDays = (DateTime.Now - lastModified).Days;

            // Base score starts at 50 (neutral)
            int score = 50;

            // Path-based scoring (most important)
            switch (pathClassification.Level)
            {
                case PathSafetyLevel.Blocked:
                    score = 0;
                    reasons.Add($"🚫 System protected: {pathClassification.Reason}");
                    return CreateResult(SafetyClassification.Blocked, score, reasons, pathClassification);

                case PathSafetyLevel.Safe:
                    score += 30;
                    reasons.Add($"✅ Safe location: {pathClassification.Reason}");
                    break;

                case PathSafetyLevel.Caution:
                    score += 0; // Neutral
                    reasons.Add($"⚠️ Caution location: {pathClassification.Reason}");
                    break;
            }

            // Extension-based scoring
            if (BlockedExtensions.Contains(extension))
            {
                score -= 40;
                reasons.Add($"🚫 System/executable file type: {extension}");
            }
            else if (CautionExtensions.Contains(extension))
            {
                score -= 10;
                reasons.Add($"⚠️ Important file type: {extension}");
            }
            else if (SafeExtensions.Contains(extension))
            {
                score += 20;
                reasons.Add($"✅ Temporary/backup file type: {extension}");
            }

            // Size-based scoring
            if (size >= VeryLargeFileSize)
            {
                score += 15;
                reasons.Add($"💾 Very large file: {FormatSize(size)} (good cleanup candidate)");
            }
            else if (size >= LargeFileSize)
            {
                score += 10;
                reasons.Add($"💾 Large file: {FormatSize(size)}");
            }
            else if (size >= MediumFileSize)
            {
                score += 5;
                reasons.Add($"💾 Medium file: {FormatSize(size)}");
            }

            // Age-based scoring
            if (ageInDays >= VeryOldDays)
            {
                score += 15;
                reasons.Add($"📅 Very old file: Last modified {ageInDays} days ago ({lastModified:yyyy-MM-dd})");
            }
            else if (ageInDays >= OldDays)
            {
                score += 10;
                reasons.Add($"📅 Old file: Last modified {ageInDays} days ago ({lastModified:yyyy-MM-dd})");
            }
            else if (ageInDays <= RecentDays)
            {
                score -= 5;
                reasons.Add($"📅 Recently modified: {ageInDays} days ago ({lastModified:yyyy-MM-dd})");
            }

            // Specific patterns that boost safety
            var fileName = Path.GetFileName(path).ToLowerInvariant();
            if (fileName.Contains("cache") || fileName.Contains("temp"))
            {
                score += 10;
                reasons.Add("✅ Filename suggests temporary data");
            }
            if (fileName.StartsWith("~") || fileName.EndsWith("~"))
            {
                score += 15;
                reasons.Add("✅ Backup/temp file naming convention");
            }
            if (path.Contains("\\node_modules\\"))
            {
                score += 20;
                reasons.Add("✅ Node.js dependency (restorable with npm install)");
            }

            // Developer-specific patterns
            if (path.Contains("\\.nuget\\packages\\"))
            {
                score += 20;
                reasons.Add("✅ NuGet package cache (restored on build/restore)");
            }
            if (path.Contains("\\bin\\Debug\\") || path.Contains("\\bin\\Release\\") || path.Contains("\\obj\\"))
            {
                score += 20;
                reasons.Add("✅ Build artifact (regenerated on build)");
            }
            if (path.Contains("\\Package Cache\\") || path.Contains("\\VisualStudio\\Packages\\"))
            {
                score += 15;
                reasons.Add("✅ Installer cache (re-downloaded when needed)");
            }

            // Clamp score to 0-100
            score = Math.Clamp(score, 0, 100);

            // Determine classification
            var classification = score switch
            {
                >= 70 => SafetyClassification.Safe,
                >= 30 => SafetyClassification.Caution,
                _ => SafetyClassification.Blocked
            };

            // Add summary
            if (classification == SafetyClassification.Safe)
            {
                reasons.Insert(0, "✅ Recommended for cleanup");
            }
            else if (classification == SafetyClassification.Caution)
            {
                reasons.Insert(0, "⚠️ Review before cleanup - may be important");
            }
            else
            {
                reasons.Insert(0, "🚫 Do not clean - critical or protected");
            }

            return CreateResult(classification, score, reasons, pathClassification);
        }

        public ScoringResult ScoreFolder(string path, long totalSize, DateTime lastModified, int fileCount)
        {
            var reasons = new List<string>();
            var pathClassification = PathRules.ClassifyPath(path);
            var folderName = Path.GetFileName(path)?.ToLowerInvariant() ?? "";
            var ageInDays = (DateTime.Now - lastModified).Days;

            int score = 50;

            // Path-based scoring
            switch (pathClassification.Level)
            {
                case PathSafetyLevel.Blocked:
                    score = 0;
                    reasons.Add($"🚫 System protected: {pathClassification.Reason}");
                    return CreateResult(SafetyClassification.Blocked, score, reasons, pathClassification);

                case PathSafetyLevel.Safe:
                    score += 30;
                    reasons.Add($"✅ Safe location: {pathClassification.Reason}");
                    break;

                case PathSafetyLevel.Caution:
                    reasons.Add($"⚠️ Caution location: {pathClassification.Reason}");
                    break;
            }

            // Size-based scoring (folders)
            if (totalSize >= VeryLargeFileSize)
            {
                score += 20;
                reasons.Add($"💾 Very large folder: {FormatSize(totalSize)} (excellent cleanup candidate)");
            }
            else if (totalSize >= LargeFileSize)
            {
                score += 15;
                reasons.Add($"💾 Large folder: {FormatSize(totalSize)}");
            }
            else if (totalSize >= MediumFileSize)
            {
                score += 10;
                reasons.Add($"💾 Medium folder: {FormatSize(totalSize)}");
            }

            // Age-based scoring
            if (ageInDays >= VeryOldDays)
            {
                score += 10;
                reasons.Add($"📅 Very old folder: {ageInDays} days old");
            }
            else if (ageInDays >= OldDays)
            {
                score += 5;
                reasons.Add($"📅 Old folder: {ageInDays} days old");
            }

            // Specific folder patterns
            if (folderName == "node_modules")
            {
                score += 25;
                reasons.Add("✅ Node.js dependencies (fully restorable with npm install)");
                reasons.Add($"📦 Contains {fileCount} files");
            }
            else if (folderName == "packages" || folderName == ".nuget")
            {
                score += 20;
                reasons.Add("✅ NuGet packages (restorable with restore)");
            }
            else if (folderName == "bin" || folderName == "obj")
            {
                score += 20;
                reasons.Add("✅ Build output (regenerated on build)");
            }
            else if (folderName == ".cache" || folderName == "cache")
            {
                score += 15;
                reasons.Add("✅ Cache directory");
            }
            else if (folderName == "temp" || folderName == "tmp")
            {
                score += 15;
                reasons.Add("✅ Temporary files directory");
            }

            score = Math.Clamp(score, 0, 100);

            var classification = score switch
            {
                >= 70 => SafetyClassification.Safe,
                >= 30 => SafetyClassification.Caution,
                _ => SafetyClassification.Blocked
            };

            if (classification == SafetyClassification.Safe)
            {
                reasons.Insert(0, "✅ Recommended for cleanup");
            }
            else if (classification == SafetyClassification.Caution)
            {
                reasons.Insert(0, "⚠️ Review before cleanup");
            }
            else
            {
                reasons.Insert(0, "🚫 Do not clean");
            }

            return CreateResult(classification, score, reasons, pathClassification);
        }

        public async Task<ScoringResult> ScoreWithLLMAsync(
            ScoringResult baseResult,
            string path,
            CancellationToken cancellationToken = default)
        {
            // Placeholder for future LLM integration
            // This will be implemented to call an LLM API (OpenAI, Azure OpenAI, etc.)
            // to generate more detailed explanations

            await Task.CompletedTask;

            var enhancedReasons = new List<string>(baseResult.Reasons)
            {
                "🤖 LLM explanation: (Not yet implemented - will provide AI-powered insights)"
            };

            return new ScoringResult
            {
                Classification = baseResult.Classification,
                Score = baseResult.Score,
                Reasons = enhancedReasons,
                PathClassification = baseResult.PathClassification,
                LLMExplanation = "(Future: AI-powered detailed analysis will appear here)"
            };
        }

        public List<ScoringResult> ScoreBatch(List<FileSystemItem> items)
        {
            var results = new List<ScoringResult>();

            foreach (var item in items)
            {
                try
                {
                    var size = ParseSize(item.Size);
                    var lastModified = DateTime.TryParse(item.LastModified, out var date) 
                        ? date 
                        : DateTime.Now;

                    var result = ScoreFile(item.Path, size, lastModified);
                    results.Add(result);
                }
                catch
                {
                    // Skip items we can't score
                }
            }

            return results;
        }

        private static ScoringResult CreateResult(
            SafetyClassification classification,
            int score,
            List<string> reasons,
            PathClassification pathClassification)
        {
            return new ScoringResult
            {
                Classification = classification,
                Score = score,
                Reasons = reasons,
                PathClassification = pathClassification,
                ReasonSummary = string.Join("\n", reasons)
            };
        }

        private static long ParseSize(string sizeString)
        {
            // Parse sizes like "10.5 GB", "500 MB", etc.
            var parts = sizeString.Trim().Split(' ');
            if (parts.Length != 2 || !double.TryParse(parts[0], out var value))
                return 0;

            return parts[1].ToUpperInvariant() switch
            {
                "B" => (long)value,
                "KB" => (long)(value * 1024),
                "MB" => (long)(value * 1024 * 1024),
                "GB" => (long)(value * 1024 * 1024 * 1024),
                "TB" => (long)(value * 1024 * 1024 * 1024 * 1024),
                _ => 0
            };
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
