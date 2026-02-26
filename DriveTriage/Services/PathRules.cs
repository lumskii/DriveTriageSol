using System.Text.RegularExpressions;

namespace DriveTriage.Services
{
    public class PathRules
    {
        private static readonly PathPattern[] BlockedPatterns = new[]
        {
            // Windows System Directories
            new PathPattern(@"^[A-Z]:\\Windows\\", "Windows system directory", PathSafetyLevel.Blocked),
            new PathPattern(@"^[A-Z]:\\Windows\\System32\\", "Critical system directory", PathSafetyLevel.Blocked),
            new PathPattern(@"^[A-Z]:\\Windows\\SysWOW64\\", "Critical system directory (32-bit)", PathSafetyLevel.Blocked),
            new PathPattern(@"^[A-Z]:\\Windows\\WinSxS\\", "Windows side-by-side assemblies", PathSafetyLevel.Blocked),
            new PathPattern(@"^[A-Z]:\\Program Files\\WindowsApps\\", "Windows Store apps", PathSafetyLevel.Blocked),
            new PathPattern(@"\\System Volume Information\\", "System volume metadata", PathSafetyLevel.Blocked),
            new PathPattern(@"\\\$Recycle\.Bin\\", "Recycle bin", PathSafetyLevel.Blocked),

            // Boot and Recovery
            new PathPattern(@"^[A-Z]:\\Boot\\", "Boot configuration", PathSafetyLevel.Blocked),
            new PathPattern(@"^[A-Z]:\\Recovery\\", "System recovery", PathSafetyLevel.Blocked),
            new PathPattern(@"^[A-Z]:\\PerfLogs\\", "Performance logs", PathSafetyLevel.Blocked),

            // User Critical
            new PathPattern(@"\\AppData\\Local\\Microsoft\\Windows\\", "Windows user data", PathSafetyLevel.Blocked),
            new PathPattern(@"\\NTUSER\.DAT", "User registry hive", PathSafetyLevel.Blocked),
        };

        private static readonly PathPattern[] CautionPatterns = new[]
        {
            // Program Files
            new PathPattern(@"^[A-Z]:\\Program Files\\", "Installed applications", PathSafetyLevel.Caution),
            new PathPattern(@"^[A-Z]:\\Program Files \(x86\)\\", "Installed applications (32-bit)", PathSafetyLevel.Caution),

            // User AppData
            new PathPattern(@"\\AppData\\Local\\", "Application data", PathSafetyLevel.Caution),
            new PathPattern(@"\\AppData\\LocalLow\\", "Application data (low integrity)", PathSafetyLevel.Caution),
            new PathPattern(@"\\AppData\\Roaming\\", "Roaming application data", PathSafetyLevel.Caution),

            // Development (source control and settings - keep as caution)
            new PathPattern(@"\\\.git\\", "Git repository", PathSafetyLevel.Caution),
            new PathPattern(@"\\\.svn\\", "SVN repository", PathSafetyLevel.Caution),
            new PathPattern(@"\\\.vs\\", "Visual Studio settings", PathSafetyLevel.Caution),

            // Databases
            new PathPattern(@"\.(mdf|ldf)$", "Database files", PathSafetyLevel.Caution),
        };

        private static readonly PathPattern[] SafePatterns = new[]
        {
            // Common safe locations
            new PathPattern(@"\\Downloads\\.*\.(exe|msi|zip|rar|7z|iso)$", "Downloaded installers", PathSafetyLevel.Safe),
            new PathPattern(@"\\Temp\\", "Temporary files", PathSafetyLevel.Safe),
            new PathPattern(@"\\Cache\\", "Cache files", PathSafetyLevel.Safe),
            new PathPattern(@"\\Logs\\", "Log files", PathSafetyLevel.Safe),
            new PathPattern(@"\\node_modules\\", "Node.js dependencies", PathSafetyLevel.Safe),
            new PathPattern(@"\\packages\\", "Package dependencies", PathSafetyLevel.Safe),
            new PathPattern(@"\\\.cache\\", "Cache directory", PathSafetyLevel.Safe),
            new PathPattern(@"\.(tmp|temp|bak|old)$", "Temporary/backup files", PathSafetyLevel.Safe),
            new PathPattern(@"\\Desktop\\.*\.(txt|log)$", "Log files on desktop", PathSafetyLevel.Safe),

            // Developer-specific safe patterns
            new PathPattern(@"\\\.nuget\\packages\\", "NuGet global package cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\bin\\(Debug|Release)\\", "Build output folder", PathSafetyLevel.Safe),
            new PathPattern(@"\\obj\\", "Build intermediate files", PathSafetyLevel.Safe),
            new PathPattern(@"\\ProgramData\\Microsoft\\VisualStudio\\Packages\\", "Visual Studio package cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\Package Cache\\", "Installer package cache", PathSafetyLevel.Safe),

            // Vendor cache patterns - NVIDIA
            new PathPattern(@"\\NVIDIA Corporation\\(Downloader|NV_Cache)\\", "NVIDIA download/shader cache", PathSafetyLevel.Safe),
            new PathPattern(@"^[A-Z]:\\NVIDIA\\", "NVIDIA driver cache root", PathSafetyLevel.Safe),
            new PathPattern(@"\\ProgramData\\NVIDIA Corporation\\", "NVIDIA program data cache", PathSafetyLevel.Safe),

            // Vendor cache patterns - Microsoft
            new PathPattern(@"\\ProgramData\\Microsoft\\Windows\\WER\\", "Windows Error Reporting cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\ProgramData\\Microsoft\\Diagnosis\\", "Windows diagnostics cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\ProgramData\\Microsoft\\NetFramework\\BreadcrumbStore\\", ".NET Framework cache", PathSafetyLevel.Safe),

            // Vendor cache patterns - Adobe
            new PathPattern(@"\\Adobe\\(Acrobat|Reader)\\Cache\\", "Adobe Reader cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\ProgramData\\Adobe\\ARM\\", "Adobe update cache", PathSafetyLevel.Safe),

            // Vendor cache patterns - Google
            new PathPattern(@"\\Google\\Chrome\\User Data\\.*\\Cache\\", "Chrome browser cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\Google\\Update\\", "Google Update cache", PathSafetyLevel.Safe),

            // Vendor cache patterns - Mozilla
            new PathPattern(@"\\Mozilla\\Firefox\\Profiles\\.*\\cache2?\\", "Firefox cache", PathSafetyLevel.Safe),

            // Vendor cache patterns - Microsoft Edge
            new PathPattern(@"\\Microsoft\\Edge\\User Data\\.*\\Cache\\", "Edge browser cache", PathSafetyLevel.Safe),

            // ProgramData common cache/log/temp patterns
            new PathPattern(@"\\ProgramData\\.*\\Logs?\\", "Application logs in ProgramData", PathSafetyLevel.Safe),
            new PathPattern(@"\\ProgramData\\.*\\Cache\\", "Application cache in ProgramData", PathSafetyLevel.Safe),
            new PathPattern(@"\\ProgramData\\.*\\Temp\\", "Application temp in ProgramData", PathSafetyLevel.Safe),
            new PathPattern(@"\\ProgramData\\.*\\Crash(es|Dumps?)\\", "Crash dumps in ProgramData", PathSafetyLevel.Safe),

            // Development tool caches
            new PathPattern(@"\\\.gradle\\caches\\", "Gradle build cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\\.m2\\repository\\", "Maven repository cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\\.npm\\", "npm package cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\\.yarn\\cache\\", "Yarn package cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\\.cargo\\registry\\", "Rust Cargo cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\go\\pkg\\mod\\", "Go module cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\\.docker\\", "Docker cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\\.vscode\\extensions\\", "VS Code extension cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\JetBrains\\.*\\caches\\", "JetBrains IDE cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\pip\\cache\\", "Python pip cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\__pycache__\\", "Python compiled bytecode", PathSafetyLevel.Safe),

            // Windows Update and component caches
            new PathPattern(@"\\Windows\\SoftwareDistribution\\Download\\", "Windows Update download cache", PathSafetyLevel.Safe),
            new PathPattern(@"\\Windows\\Logs\\CBS\\", "Component-Based Servicing logs", PathSafetyLevel.Safe),
        };

        public static PathClassification ClassifyPath(string path)
        {
            var normalizedPath = path.Replace('/', '\\');

            // Check blocked patterns first (highest priority)
            foreach (var pattern in BlockedPatterns)
            {
                if (pattern.Matches(normalizedPath))
                {
                    return new PathClassification
                    {
                        Level = PathSafetyLevel.Blocked,
                        Reason = pattern.Description,
                        MatchedPattern = pattern.Pattern
                    };
                }
            }

            // Check caution patterns
            foreach (var pattern in CautionPatterns)
            {
                if (pattern.Matches(normalizedPath))
                {
                    return new PathClassification
                    {
                        Level = PathSafetyLevel.Caution,
                        Reason = pattern.Description,
                        MatchedPattern = pattern.Pattern
                    };
                }
            }

            // Check safe patterns
            foreach (var pattern in SafePatterns)
            {
                if (pattern.Matches(normalizedPath))
                {
                    return new PathClassification
                    {
                        Level = PathSafetyLevel.Safe,
                        Reason = pattern.Description,
                        MatchedPattern = pattern.Pattern
                    };
                }
            }

            // Default: Caution for unknown paths
            return new PathClassification
            {
                Level = PathSafetyLevel.Caution,
                Reason = "Unknown path pattern",
                MatchedPattern = null
            };
        }

        public static bool IsSystemProtected(string path)
        {
            var classification = ClassifyPath(path);
            return classification.Level == PathSafetyLevel.Blocked;
        }

        public static bool IsSafeToDelete(string path)
        {
            var classification = ClassifyPath(path);
            return classification.Level == PathSafetyLevel.Safe;
        }

#if DEBUG
        /// <summary>
        /// DEBUG-only self-check method to verify path classification rules.
        /// Tests representative paths and asserts expected classifications.
        /// </summary>
        public static void RunSelfCheck()
        {
            Console.WriteLine("=== PathRules Self-Check ===");
            Console.WriteLine();

            var testCases = new[]
            {
                // Expected: Blocked (System directories)
                (@"C:\Windows\System32\kernel32.dll", PathSafetyLevel.Blocked),
                (@"C:\Windows\WinSxS\manifest.dat", PathSafetyLevel.Blocked),
                (@"C:\Program Files\WindowsApps\store.app", PathSafetyLevel.Blocked),
                (@"C:\Users\Test\AppData\Local\Microsoft\Windows\UsrClass.dat", PathSafetyLevel.Blocked),
                (@"D:\System Volume Information\indexer", PathSafetyLevel.Blocked),

                // Expected: Safe (Vendor caches)
                (@"C:\ProgramData\NVIDIA Corporation\Downloader\cache.bin", PathSafetyLevel.Safe),
                (@"C:\NVIDIA\shaderCache\file.cache", PathSafetyLevel.Safe),
                (@"C:\ProgramData\Microsoft\Windows\WER\report.wer", PathSafetyLevel.Safe),
                (@"C:\Users\Test\AppData\Local\Google\Chrome\User Data\Default\Cache\data_1", PathSafetyLevel.Safe),
                (@"C:\Users\Test\AppData\Local\Mozilla\Firefox\Profiles\abc123\cache2\entries\file", PathSafetyLevel.Safe),
                (@"C:\Users\Test\AppData\Local\Microsoft\Edge\User Data\Default\Cache\f_000001", PathSafetyLevel.Safe),

                // Expected: Safe (ProgramData caches/logs)
                (@"C:\ProgramData\SomeApp\Logs\app.log", PathSafetyLevel.Safe),
                (@"C:\ProgramData\AnotherApp\Cache\data.bin", PathSafetyLevel.Safe),
                (@"C:\ProgramData\MyProgram\Temp\tmp123.dat", PathSafetyLevel.Safe),
                (@"C:\ProgramData\Game\Crashes\crash_20240101.dmp", PathSafetyLevel.Safe),

                // Expected: Safe (Dev tool caches)
                (@"C:\Users\Test\.nuget\packages\newtonsoft.json\13.0.1\lib\net45\Newtonsoft.Json.dll", PathSafetyLevel.Safe),
                (@"D:\Projects\MyApp\bin\Debug\MyApp.exe", PathSafetyLevel.Safe),
                (@"D:\Projects\MyApp\obj\Debug\MyApp.pdb", PathSafetyLevel.Safe),
                (@"C:\Users\Test\.gradle\caches\modules-2\files-2.1\junit", PathSafetyLevel.Safe),
                (@"C:\Users\Test\.npm\_cacache\content-v2\sha512\ab\cd", PathSafetyLevel.Safe),
                (@"C:\Users\Test\.cargo\registry\cache\github.com", PathSafetyLevel.Safe),
                (@"D:\Projects\node_project\node_modules\express\lib\express.js", PathSafetyLevel.Safe),
                (@"C:\Users\Test\AppData\Local\JetBrains\Rider2023.1\caches\index.dat", PathSafetyLevel.Safe),
                (@"D:\Python\project\__pycache__\module.cpython-39.pyc", PathSafetyLevel.Safe),

                // Expected: Safe (Windows Update)
                (@"C:\Windows\SoftwareDistribution\Download\update.cab", PathSafetyLevel.Safe),
                (@"C:\Windows\Logs\CBS\CBS.log", PathSafetyLevel.Safe),

                // Expected: Caution (Program Files)
                (@"C:\Program Files\MyApp\myapp.exe", PathSafetyLevel.Caution),
                (@"C:\Program Files (x86)\OtherApp\data.dll", PathSafetyLevel.Caution),
                (@"C:\Users\Test\AppData\Local\MyApp\settings.json", PathSafetyLevel.Caution),
                (@"C:\Users\Test\AppData\Roaming\App\config.xml", PathSafetyLevel.Caution),

                // Expected: Caution (Dev repos)
                (@"D:\Projects\MyRepo\.git\config", PathSafetyLevel.Caution),
                (@"D:\Projects\MyRepo\.vs\Solution.sln.cache", PathSafetyLevel.Caution),

                // Expected: Safe (Temp files)
                (@"C:\Users\Test\AppData\Local\Temp\tmp12345.tmp", PathSafetyLevel.Safe),
                (@"D:\Downloads\installer.exe", PathSafetyLevel.Safe),
                (@"C:\Temp\backup.bak", PathSafetyLevel.Safe),
            };

            int passed = 0;
            int failed = 0;

            foreach (var (path, expectedLevel) in testCases)
            {
                var result = ClassifyPath(path);
                var status = result.Level == expectedLevel ? "✅ PASS" : "❌ FAIL";

                if (result.Level == expectedLevel)
                {
                    passed++;
                    Console.WriteLine($"{status}: {path}");
                    Console.WriteLine($"         Expected: {expectedLevel}, Got: {result.Level} - {result.Reason}");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"{status}: {path}");
                    Console.WriteLine($"         Expected: {expectedLevel}, Got: {result.Level} - {result.Reason}");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"         ⚠️ MISMATCH!");
                    Console.ResetColor();
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
#endif
    }

    public class PathPattern
    {
        public string Pattern { get; }
        public string Description { get; }
        public PathSafetyLevel Level { get; }
        private readonly Regex _regex;

        public PathPattern(string pattern, string description, PathSafetyLevel level)
        {
            Pattern = pattern;
            Description = description;
            Level = level;
            _regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        public bool Matches(string path) => _regex.IsMatch(path);
    }

    public enum PathSafetyLevel
    {
        Safe = 0,
        Caution = 1,
        Blocked = 2
    }

    public class PathClassification
    {
        public PathSafetyLevel Level { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string? MatchedPattern { get; init; }
    }
}
