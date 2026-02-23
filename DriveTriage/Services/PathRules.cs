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

            // Development
            new PathPattern(@"\\\.git\\", "Git repository", PathSafetyLevel.Caution),
            new PathPattern(@"\\\.svn\\", "SVN repository", PathSafetyLevel.Caution),
            new PathPattern(@"\\bin\\Debug\\", "Build output (debug)", PathSafetyLevel.Caution),
            new PathPattern(@"\\bin\\Release\\", "Build output (release)", PathSafetyLevel.Caution),
            new PathPattern(@"\\obj\\", "Build intermediate files", PathSafetyLevel.Caution),

            // Databases
            new PathPattern(@"\\\.vs\\", "Visual Studio settings", PathSafetyLevel.Caution),
            new PathPattern(@"\.(mdf|ldf)$", "Database files", PathSafetyLevel.Caution),
        };

        private static readonly PathPattern[] SafePatterns = new[]
        {
            // Common safe locations
            new PathPattern(@"\\Downloads\\.*\.(exe|msi|zip|rar|7z|iso)$", "Downloaded installers", PathSafetyLevel.Safe),
            new PathPattern(@"\\Temp\\", "Temporary files", PathSafetyLevel.Safe),
            new PathPattern(@"\\Cache\\", "Cache files", PathSafetyLevel.Safe),
            new PathPattern(@"\\node_modules\\", "Node.js dependencies", PathSafetyLevel.Safe),
            new PathPattern(@"\\packages\\", "Package dependencies", PathSafetyLevel.Safe),
            new PathPattern(@"\\\.cache\\", "Cache directory", PathSafetyLevel.Safe),
            new PathPattern(@"\.(tmp|temp|bak|old)$", "Temporary/backup files", PathSafetyLevel.Safe),
            new PathPattern(@"\\Desktop\\.*\.(txt|log)$", "Log files on desktop", PathSafetyLevel.Safe),
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
