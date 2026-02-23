using System.IO;
using System.Text;
using System.Text.Json;
using DriveTriage.ViewModels;

namespace DriveTriage.Services
{
    public class ReportService
    {
        private readonly string _logDirectory;
        private const string ActionsFileName = "actions_history.json";

        public ReportService(string? logDirectory = null)
        {
            _logDirectory = logDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DriveTriage",
                "Logs");

            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }

        public async Task<string> LogCleanupActionsAsync(string bucketName, List<CleanupAction> actions)
        {
            var sessionId = Guid.NewGuid().ToString();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var logFileName = $"cleanup_{SanitizeFileName(bucketName)}_{timestamp}.log";
            var logFilePath = Path.Combine(_logDirectory, logFileName);

            // Text log for human readability
            var sb = new StringBuilder();
            sb.AppendLine($"=== Cleanup Report for {bucketName} ===");
            sb.AppendLine($"Session ID: {sessionId}");
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total Actions: {actions.Count}");
            sb.AppendLine($"Successful: {actions.Count(a => a.Success)}");
            sb.AppendLine($"Failed: {actions.Count(a => !a.Success)}");
            sb.AppendLine($"Total Size: {FormatSize(actions.Where(a => a.Success).Sum(a => a.Size))}");
            sb.AppendLine();
            sb.AppendLine("=== Actions ===");

            foreach (var action in actions)
            {
                sb.AppendLine($"[{action.ActionTime:HH:mm:ss}] {action.ActionType}");
                sb.AppendLine($"  Source: {action.SourcePath}");
                if (!string.IsNullOrEmpty(action.QuarantinePath))
                {
                    sb.AppendLine($"  Quarantine: {action.QuarantinePath}");
                }
                sb.AppendLine($"  Size: {FormatSize(action.Size)}");
                sb.AppendLine($"  Success: {action.Success}");
                if (!string.IsNullOrEmpty(action.ErrorMessage))
                {
                    sb.AppendLine($"  Error: {action.ErrorMessage}");
                }
                sb.AppendLine();
            }

            await File.WriteAllTextAsync(logFilePath, sb.ToString());

            // JSON log for restore functionality
            var jsonFileName = $"cleanup_{SanitizeFileName(bucketName)}_{timestamp}.json";
            var jsonFilePath = Path.Combine(_logDirectory, jsonFileName);

            var cleanupSession = new CleanupSession
            {
                SessionId = sessionId,
                BucketName = bucketName,
                Timestamp = DateTime.Now,
                TotalActions = actions.Count,
                SuccessfulActions = actions.Count(a => a.Success),
                FailedActions = actions.Count(a => !a.Success),
                TotalSize = actions.Where(a => a.Success).Sum(a => a.Size),
                Actions = actions.Select(a => new ActionRecord
                {
                    Timestamp = a.ActionTime,
                    Operation = a.ActionType.ToString(),
                    OriginalPath = a.SourcePath,
                    NewPath = a.QuarantinePath,
                    SizeBytes = a.Size,
                    Success = a.Success,
                    ErrorMessage = a.ErrorMessage,
                    IsRestored = false
                }).ToList()
            };

            var jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            await File.WriteAllTextAsync(jsonFilePath, JsonSerializer.Serialize(cleanupSession, jsonOptions));

            // Append to master actions history
            await AppendToActionsHistoryAsync(cleanupSession);

            return sessionId;
        }

        private async Task AppendToActionsHistoryAsync(CleanupSession session)
        {
            var historyPath = Path.Combine(_logDirectory, ActionsFileName);
            List<CleanupSession> history;

            if (File.Exists(historyPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(historyPath);
                    history = JsonSerializer.Deserialize<List<CleanupSession>>(json) ?? new List<CleanupSession>();
                }
                catch
                {
                    history = new List<CleanupSession>();
                }
            }
            else
            {
                history = new List<CleanupSession>();
            }

            history.Add(session);

            // Keep only last 100 sessions
            if (history.Count > 100)
            {
                history = history.OrderByDescending(s => s.Timestamp).Take(100).ToList();
            }

            var jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            await File.WriteAllTextAsync(historyPath, JsonSerializer.Serialize(history, jsonOptions));
        }

        public async Task<List<CleanupSession>> GetRestorableSessionsAsync()
        {
            var historyPath = Path.Combine(_logDirectory, ActionsFileName);

            if (!File.Exists(historyPath))
                return new List<CleanupSession>();

            try
            {
                var json = await File.ReadAllTextAsync(historyPath);
                var sessions = JsonSerializer.Deserialize<List<CleanupSession>>(json) ?? new List<CleanupSession>();

                // Return only sessions with restorable items (successful moves not yet restored)
                return sessions
                    .Where(s => s.Actions.Any(a => a.Success && !a.IsRestored && a.Operation == "MovedToQuarantine"))
                    .OrderByDescending(s => s.Timestamp)
                    .ToList();
            }
            catch
            {
                return new List<CleanupSession>();
            }
        }

        public async Task<RestoreResult> RestoreSessionAsync(string sessionId, IProgress<string> statusUpdate)
        {
            var historyPath = Path.Combine(_logDirectory, ActionsFileName);

            if (!File.Exists(historyPath))
            {
                return new RestoreResult
                {
                    Success = false,
                    ErrorMessage = "No action history found"
                };
            }

            try
            {
                var json = await File.ReadAllTextAsync(historyPath);
                var sessions = JsonSerializer.Deserialize<List<CleanupSession>>(json) ?? new List<CleanupSession>();

                var session = sessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session == null)
                {
                    return new RestoreResult
                    {
                        Success = false,
                        ErrorMessage = "Session not found"
                    };
                }

                var restoredCount = 0;
                var failedCount = 0;
                var errors = new List<string>();

                foreach (var action in session.Actions.Where(a => a.Success && !a.IsRestored && a.Operation == "MovedToQuarantine"))
                {
                    statusUpdate.Report($"Restoring: {Path.GetFileName(action.OriginalPath)}");

                    try
                    {
                        if (File.Exists(action.NewPath))
                        {
                            var originalDir = Path.GetDirectoryName(action.OriginalPath);
                            if (originalDir != null && !Directory.Exists(originalDir))
                            {
                                Directory.CreateDirectory(originalDir);
                            }

                            File.Move(action.NewPath, action.OriginalPath, overwrite: false);
                            action.IsRestored = true;
                            restoredCount++;
                        }
                        else if (Directory.Exists(action.NewPath))
                        {
                            var originalDir = Path.GetDirectoryName(action.OriginalPath);
                            if (originalDir != null && !Directory.Exists(originalDir))
                            {
                                Directory.CreateDirectory(originalDir);
                            }

                            Directory.Move(action.NewPath, action.OriginalPath);
                            action.IsRestored = true;
                            restoredCount++;
                        }
                        else
                        {
                            errors.Add($"Not found in quarantine: {action.NewPath}");
                            failedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(action.OriginalPath)}: {ex.Message}");
                        failedCount++;
                    }
                }

                // Save updated history
                var jsonOptions = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                await File.WriteAllTextAsync(historyPath, JsonSerializer.Serialize(sessions, jsonOptions));

                return new RestoreResult
                {
                    Success = failedCount == 0,
                    RestoredCount = restoredCount,
                    FailedCount = failedCount,
                    Errors = errors
                };
            }
            catch (Exception ex)
            {
                return new RestoreResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<RestoreResult> RestoreSelectedItemsAsync(
            string sessionId, 
            List<string> originalPaths, 
            IProgress<string> statusUpdate)
        {
            var historyPath = Path.Combine(_logDirectory, ActionsFileName);

            if (!File.Exists(historyPath))
            {
                return new RestoreResult
                {
                    Success = false,
                    ErrorMessage = "No action history found"
                };
            }

            try
            {
                var json = await File.ReadAllTextAsync(historyPath);
                var sessions = JsonSerializer.Deserialize<List<CleanupSession>>(json) ?? new List<CleanupSession>();

                var session = sessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session == null)
                {
                    return new RestoreResult
                    {
                        Success = false,
                        ErrorMessage = "Session not found"
                    };
                }

                var restoredCount = 0;
                var failedCount = 0;
                var errors = new List<string>();

                var actionsToRestore = session.Actions
                    .Where(a => originalPaths.Contains(a.OriginalPath) && a.Success && !a.IsRestored)
                    .ToList();

                foreach (var action in actionsToRestore)
                {
                    statusUpdate.Report($"Restoring: {Path.GetFileName(action.OriginalPath)}");

                    try
                    {
                        if (File.Exists(action.NewPath))
                        {
                            var originalDir = Path.GetDirectoryName(action.OriginalPath);
                            if (originalDir != null && !Directory.Exists(originalDir))
                            {
                                Directory.CreateDirectory(originalDir);
                            }

                            File.Move(action.NewPath, action.OriginalPath, overwrite: false);
                            action.IsRestored = true;
                            restoredCount++;
                        }
                        else if (Directory.Exists(action.NewPath))
                        {
                            Directory.Move(action.NewPath, action.OriginalPath);
                            action.IsRestored = true;
                            restoredCount++;
                        }
                        else
                        {
                            errors.Add($"Not found in quarantine: {action.NewPath}");
                            failedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(action.OriginalPath)}: {ex.Message}");
                        failedCount++;
                    }
                }

                // Save updated history
                var jsonOptions = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                await File.WriteAllTextAsync(historyPath, JsonSerializer.Serialize(sessions, jsonOptions));

                return new RestoreResult
                {
                    Success = failedCount == 0,
                    RestoredCount = restoredCount,
                    FailedCount = failedCount,
                    Errors = errors
                };
            }
            catch (Exception ex)
            {
                return new RestoreResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<List<string>> GetRecentLogsAsync(int count = 10)
        {
            var logFiles = Directory.GetFiles(_logDirectory, "*.log")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Take(count)
                .ToList();

            var logs = new List<string>();
            foreach (var logFile in logFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(logFile);
                    logs.Add($"=== {Path.GetFileName(logFile)} ===\n{content}\n");
                }
                catch { }
            }

            return logs;
        }

        public string GetLogDirectory() => _logDirectory;

        private static string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
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
