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

            // Vendor cache patterns - NVIDIA
            if (path.Contains("\\NVIDIA Corporation\\Downloader\\", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("\\NVIDIA Corporation\\NV_Cache\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ NVIDIA driver/shader cache (regenerated automatically)");
            }
            else if (path.Contains("\\NVIDIA\\", StringComparison.OrdinalIgnoreCase) && 
                     path.Contains("\\ProgramData\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
                reasons.Add("✅ NVIDIA program data cache");
            }

            // Vendor cache patterns - Browsers
            if (path.Contains("\\Google\\Chrome\\User Data\\", StringComparison.OrdinalIgnoreCase) && 
                path.Contains("\\Cache\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ Chrome browser cache");
            }
            else if (path.Contains("\\Mozilla\\Firefox\\Profiles\\", StringComparison.OrdinalIgnoreCase) && 
                     path.Contains("\\cache", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ Firefox browser cache");
            }
            else if (path.Contains("\\Microsoft\\Edge\\User Data\\", StringComparison.OrdinalIgnoreCase) && 
                     path.Contains("\\Cache\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ Edge browser cache");
            }

            // Vendor cache patterns - Microsoft
            if (path.Contains("\\ProgramData\\Microsoft\\Windows\\WER\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
                reasons.Add("✅ Windows Error Reporting cache");
            }
            else if (path.Contains("\\ProgramData\\Microsoft\\Diagnosis\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
                reasons.Add("✅ Windows diagnostics cache");
            }
            else if (path.Contains("\\Windows\\SoftwareDistribution\\Download\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
                reasons.Add("✅ Windows Update download cache");
            }

            // Vendor cache patterns - Adobe
            if (path.Contains("\\Adobe\\", StringComparison.OrdinalIgnoreCase) && 
                (path.Contains("\\Cache\\", StringComparison.OrdinalIgnoreCase) || 
                 path.Contains("\\ARM\\", StringComparison.OrdinalIgnoreCase)))
            {
                score += 15;
                reasons.Add("✅ Adobe application cache");
            }

            // ProgramData common patterns
            if (path.Contains("\\ProgramData\\", StringComparison.OrdinalIgnoreCase))
            {
                if (path.Contains("\\Logs\\", StringComparison.OrdinalIgnoreCase) || 
                    path.Contains("\\Log\\", StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                    reasons.Add("✅ Application log files in ProgramData");
                }
                else if (path.Contains("\\Temp\\", StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                    reasons.Add("✅ Temporary files in ProgramData");
                }
                else if (path.Contains("\\Crash", StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                    reasons.Add("✅ Crash dumps in ProgramData");
                }
            }

            // Development tool caches
            if (path.Contains("\\.gradle\\caches\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ Gradle build cache (re-downloaded on build)");
            }
            else if (path.Contains("\\.m2\\repository\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ Maven repository cache (re-downloaded on build)");
            }
            else if (path.Contains("\\.npm\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ npm package cache (restored with npm install)");
            }
            else if (path.Contains("\\.yarn\\cache\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ Yarn package cache");
            }
            else if (path.Contains("\\.cargo\\registry\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ Rust Cargo cache");
            }
            else if (path.Contains("\\go\\pkg\\mod\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ Go module cache");
            }
            else if (path.Contains("\\__pycache__\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ Python compiled bytecode cache");
            }
            else if (path.Contains("\\pip\\cache\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ Python pip package cache");
            }
            else if (path.Contains("\\JetBrains\\", StringComparison.OrdinalIgnoreCase) && 
                     path.Contains("\\caches\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                reasons.Add("✅ JetBrains IDE cache (IntelliJ/Rider/PyCharm)");
            }
            else if (path.Contains("\\.vscode\\extensions\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
                reasons.Add("✅ VS Code extension cache");
            }
            else if (path.Contains("\\.docker\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
                reasons.Add("✅ Docker cache");
            }

            // Ensure executables/binaries are still penalized even in safe locations
            if (BlockedExtensions.Contains(extension) && score > 50)
            {
                // Already penalized above, but add additional context
                if (!reasons.Any(r => r.Contains("executable")))
                {
                    reasons.Add("⚠️ Executable/system file type requires extra caution");
                }
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

#if DEBUG
        /// <summary>
        /// DEBUG-only self-check method to verify file scoring logic.
        /// Tests representative file paths with different characteristics.
        /// </summary>
        public static void RunFileScoringCheck()
        {
            Console.WriteLine("=== ScoringService File Scoring Self-Check ===");
            Console.WriteLine();

            var service = new ScoringService();
            var testDate = DateTime.Now.AddDays(-400); // ~13 months old
            var recentDate = DateTime.Now.AddDays(-10);

            var testCases = new[]
            {
                // Expected: Blocked (System files)
                (Path: @"C:\Windows\System32\kernel32.dll", Size: 1024L * 1024, Date: testDate, Expected: SafetyClassification.Blocked),
                (Path: @"C:\Windows\explorer.exe", Size: 5 * 1024L * 1024, Date: testDate, Expected: SafetyClassification.Blocked),

                // Expected: Safe (Vendor caches - large, old)
                (Path: @"C:\ProgramData\NVIDIA Corporation\Downloader\cache123.bin", Size: 500L * 1024 * 1024, Date: testDate, Expected: SafetyClassification.Safe),
                (Path: @"C:\Users\Test\AppData\Local\Google\Chrome\User Data\Default\Cache\data_1", Size: 200L * 1024 * 1024, Date: testDate, Expected: SafetyClassification.Safe),
                (Path: @"C:\ProgramData\SomeApp\Logs\application.log", Size: 100L * 1024 * 1024, Date: testDate, Expected: SafetyClassification.Safe),
                (Path: @"C:\Windows\SoftwareDistribution\Download\update.cab", Size: 300L * 1024 * 1024, Date: testDate, Expected: SafetyClassification.Safe),

                // Expected: Safe (Dev caches)
                (Path: @"D:\Projects\MyApp\bin\Debug\MyApp.exe", Size: 50L * 1024 * 1024, Date: recentDate, Expected: SafetyClassification.Safe),
                (Path: @"C:\Users\Test\.nuget\packages\newtonsoft.json\13.0.1\lib\Newtonsoft.Json.dll", Size: 600L * 1024, Date: testDate, Expected: SafetyClassification.Safe),
                (Path: @"D:\Projects\webapp\node_modules\express\lib\express.js", Size: 150L * 1024, Date: testDate, Expected: SafetyClassification.Safe),
                (Path: @"C:\Users\Test\.gradle\caches\modules-2\files-2.1\junit.jar", Size: 2L * 1024 * 1024, Date: testDate, Expected: SafetyClassification.Safe),
                (Path: @"D:\Python\project\__pycache__\module.cpython-39.pyc", Size: 50L * 1024, Date: testDate, Expected: SafetyClassification.Safe),

                // Expected: Caution (Program Files - executables)
                (Path: @"C:\Program Files\MyApp\myapp.exe", Size: 10L * 1024 * 1024, Date: recentDate, Expected: SafetyClassification.Caution),
                (Path: @"C:\Program Files (x86)\Game\game.exe", Size: 100L * 1024 * 1024, Date: testDate, Expected: SafetyClassification.Caution),

                // Expected: Safe (Temp files - even if executable extension, should be safe due to location)
                (Path: @"C:\Users\Test\AppData\Local\Temp\installer.exe", Size: 50L * 1024 * 1024, Date: testDate, Expected: SafetyClassification.Safe),
                (Path: @"C:\Temp\backup.bak", Size: 200L * 1024 * 1024, Date: testDate, Expected: SafetyClassification.Safe),
            };

            int passed = 0;
            int failed = 0;

            foreach (var (path, size, date, expected) in testCases)
            {
                var result = service.ScoreFile(path, size, date);
                var status = result.Classification == expected ? "✅ PASS" : "❌ FAIL";

                if (result.Classification == expected)
                {
                    passed++;
                    Console.WriteLine($"{status}: {Path.GetFileName(path)}");
                    Console.WriteLine($"         Path: {path}");
                    Console.WriteLine($"         Expected: {expected}, Got: {result.Classification} (Score: {result.Score})");
                    Console.WriteLine($"         Reasons: {result.ReasonSummary.Split('\n')[0]}");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"{status}: {Path.GetFileName(path)}");
                    Console.WriteLine($"         Path: {path}");
                    Console.WriteLine($"         Expected: {expected}, Got: {result.Classification} (Score: {result.Score})");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"         ⚠️ MISMATCH!");
                    Console.ResetColor();
                    Console.WriteLine($"         Reasons: {result.ReasonSummary.Replace("\n", "\n         ")}");
                }
                Console.WriteLine();
            }

            Console.WriteLine("=== Summary ===");
            Console.WriteLine($"Total: {testCases.Length}");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Passed: {passed}");
            Console.ResetColor();
            if (failed > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed: {failed}");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        /// <summary>
        /// DEBUG-only self-check method to verify folder scoring logic.
        /// Tests representative folder paths with different characteristics.
        /// </summary>
        public static void RunFolderScoringCheck()
        {
            Console.WriteLine("=== ScoringService Folder Scoring Self-Check ===");
            Console.WriteLine();

            var service = new ScoringService();
            var oldDate = DateTime.Now.AddDays(-400);
            var recentDate = DateTime.Now.AddDays(-15);

            var testCases = new[]
            {
                // Expected: Blocked (System folders)
                (Path: @"C:\Windows\System32", Size: 5L * 1024 * 1024 * 1024, Date: oldDate, FileCount: 5000, Expected: SafetyClassification.Blocked),

                // Expected: Safe (Large dev cache folders)
                (Path: @"D:\Projects\BigApp\node_modules", Size: 2L * 1024 * 1024 * 1024, Date: oldDate, FileCount: 50000, Expected: SafetyClassification.Safe),
                (Path: @"C:\Users\Test\.nuget\packages", Size: 5L * 1024 * 1024 * 1024, Date: oldDate, FileCount: 10000, Expected: SafetyClassification.Safe),
                (Path: @"D:\Projects\MyApp\bin", Size: 500L * 1024 * 1024, Date: recentDate, FileCount: 200, Expected: SafetyClassification.Safe),
                (Path: @"C:\Users\Test\.gradle\caches", Size: 3L * 1024 * 1024 * 1024, Date: oldDate, FileCount: 20000, Expected: SafetyClassification.Safe),

                // Expected: Safe (Vendor cache folders)
                (Path: @"C:\ProgramData\NVIDIA Corporation\Downloader", Size: 800L * 1024 * 1024, Date: oldDate, FileCount: 100, Expected: SafetyClassification.Safe),
                (Path: @"C:\Users\Test\AppData\Local\Google\Chrome\User Data\Default\Cache", Size: 1L * 1024 * 1024 * 1024, Date: oldDate, FileCount: 5000, Expected: SafetyClassification.Safe),
                (Path: @"C:\ProgramData\MyApp\Logs", Size: 200L * 1024 * 1024, Date: oldDate, FileCount: 50, Expected: SafetyClassification.Safe),

                // Expected: Caution (Program Files)
                (Path: @"C:\Program Files\MyApp", Size: 500L * 1024 * 1024, Date: recentDate, FileCount: 100, Expected: SafetyClassification.Caution),
                (Path: @"C:\Users\Test\AppData\Local\MyApp", Size: 200L * 1024 * 1024, Date: recentDate, FileCount: 50, Expected: SafetyClassification.Caution),
            };

            int passed = 0;
            int failed = 0;

            foreach (var (path, size, date, fileCount, expected) in testCases)
            {
                var result = service.ScoreFolder(path, size, date, fileCount);
                var status = result.Classification == expected ? "✅ PASS" : "❌ FAIL";

                if (result.Classification == expected)
                {
                    passed++;
                    Console.WriteLine($"{status}: {Path.GetFileName(path)}");
                    Console.WriteLine($"         Path: {path}");
                    Console.WriteLine($"         Expected: {expected}, Got: {result.Classification} (Score: {result.Score})");
                    Console.WriteLine($"         Reasons: {result.ReasonSummary.Split('\n')[0]}");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"{status}: {Path.GetFileName(path)}");
                    Console.WriteLine($"         Path: {path}");
                    Console.WriteLine($"         Expected: {expected}, Got: {result.Classification} (Score: {result.Score})");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"         ⚠️ MISMATCH!");
                    Console.ResetColor();
                    Console.WriteLine($"         Reasons: {result.ReasonSummary.Replace("\n", "\n         ")}");
                }
                Console.WriteLine();
            }

            Console.WriteLine("=== Summary ===");
            Console.WriteLine($"Total: {testCases.Length}");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Passed: {passed}");
            Console.ResetColor();
            if (failed > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed: {failed}");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Run all self-checks.
        /// </summary>
        public static void RunAllSelfChecks()
        {
            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║  DriveTriage Safety Intelligence Self-Check  ║");
            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine();

            PathRules.RunSelfCheck();
            Console.WriteLine();
            Console.WriteLine("─────────────────────────────────────────────────");
            Console.WriteLine();
            RunFileScoringCheck();
            Console.WriteLine();
            Console.WriteLine("─────────────────────────────────────────────────");
            Console.WriteLine();
            RunFolderScoringCheck();
            Console.WriteLine();
            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║          All Self-Checks Complete             ║");
            Console.WriteLine("╚════════════════════════════════════════════════╝");
        }
#endif
    }
}

