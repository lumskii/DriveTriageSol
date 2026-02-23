using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using DriveTriage.Services;
using DriveTriage.Utils;

namespace DriveTriage.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ScanService _scanService;
        private readonly BucketsService _bucketsService;
        private readonly ReportService _reportService;
        private readonly AppsService _appsService;
        private readonly SystemCleanupService _systemCleanupService;
        private CancellationTokenSource? _bucketsCancellationTokenSource;
        private CancellationTokenSource? _appsCancellationTokenSource;
        private double _progressValue;
        private string _statusText = "Ready to scan";
        private bool _isScanningBuckets;
        private bool _isScanningApps;
        private string _appsSearchText = string.Empty;
        private DriveInfo? _selectedDrive;
        private bool _isScanning;
        private double _progressPercent;
        private SystemCleanupInfo? _systemCleanupInfo;

        public MainViewModel()
        {
            _scanService = new ScanService();
            _bucketsService = new BucketsService();
            _reportService = new ReportService();
            _appsService = new AppsService();
            _systemCleanupService = new SystemCleanupService();

            LargestFiles = new ObservableCollection<FileSystemItem>();
            LargestFolders = new ObservableCollection<FileSystemItem>();
            CleanupBuckets = new ObservableCollection<CleanupBucket>();
            InstalledApps = new ObservableCollection<InstalledApp>();
            FilteredApps = new ObservableCollection<InstalledApp>();
            RestorableSessions = new ObservableCollection<CleanupSession>();
            AvailableDrives = new ObservableCollection<DriveInfo>();

            // Initialize commands BEFORE loading drives
            // This prevents NullReferenceException when SelectedDrive setter calls RaiseCanExecuteChanged
            ScanCommand = new AsyncRelayCommand(ExecuteScanAsync, CanExecuteScan);
            CancelCommand = new AsyncRelayCommand(ExecuteCancelAsync, CanExecuteCancel);
            ScanBucketsCommand = new AsyncRelayCommand(ExecuteScanBucketsAsync, CanExecuteScanBuckets);
            CleanBucketCommand = new AsyncRelayCommand<CleanupBucket>(ExecuteCleanBucketAsync, CanExecuteCleanBucket);
            CancelBucketsCommand = new AsyncRelayCommand(ExecuteCancelBucketsAsync, CanExecuteCancelBuckets);
            ScanAppsCommand = new AsyncRelayCommand(ExecuteScanAppsAsync, CanExecuteScanApps);
            CancelAppsCommand = new AsyncRelayCommand(ExecuteCancelAppsAsync, CanExecuteCancelApps);
            UninstallAppCommand = new AsyncRelayCommand<InstalledApp>(ExecuteUninstallAppAsync, CanExecuteUninstallApp);
            LoadRestorableSessionsCommand = new AsyncRelayCommand(ExecuteLoadRestorableSessionsAsync);
            RestoreSessionCommand = new AsyncRelayCommand<CleanupSession>(ExecuteRestoreSessionAsync, CanExecuteRestoreSession);

            // Load drives AFTER commands are initialized
            // This is safe because SelectedDrive setter now has ScanCommand initialized
            LoadAvailableDrives();
        }

        public ObservableCollection<FileSystemItem> LargestFiles { get; }
        public ObservableCollection<FileSystemItem> LargestFolders { get; }
        public ObservableCollection<CleanupBucket> CleanupBuckets { get; }
        public ObservableCollection<InstalledApp> InstalledApps { get; }
        public ObservableCollection<InstalledApp> FilteredApps { get; }
        public ObservableCollection<CleanupSession> RestorableSessions { get; }
        public ObservableCollection<DriveInfo> AvailableDrives { get; }

        public DriveInfo? SelectedDrive
        {
            get => _selectedDrive;
            set
            {
                _selectedDrive = value;
                OnPropertyChanged();
                ScanCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                OnPropertyChanged();
            }
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            set
            {
                _progressPercent = value;
                OnPropertyChanged();
            }
        }

        public double ProgressValue
        {
            get => _progressValue;
            set
            {
                _progressValue = value;
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public string AppsSearchText
        {
            get => _appsSearchText;
            set
            {
                _appsSearchText = value;
                OnPropertyChanged();
                FilterApps();
            }
        }

        public AsyncRelayCommand ScanCommand { get; }
        public AsyncRelayCommand CancelCommand { get; }
        public AsyncRelayCommand ScanBucketsCommand { get; }
        public AsyncRelayCommand<CleanupBucket> CleanBucketCommand { get; }
        public AsyncRelayCommand CancelBucketsCommand { get; }
        public AsyncRelayCommand ScanAppsCommand { get; }
        public AsyncRelayCommand CancelAppsCommand { get; }
        public AsyncRelayCommand<InstalledApp> UninstallAppCommand { get; }
        public AsyncRelayCommand LoadRestorableSessionsCommand { get; }
        public AsyncRelayCommand<CleanupSession> RestoreSessionCommand { get; }

        private bool CanExecuteScan()
        {
            return !_scanService.IsScanning && SelectedDrive != null;
        }

        private void LoadAvailableDrives()
        {
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                    .OrderBy(d => d.Name)
                    .ToList();

                foreach (var drive in drives)
                {
                    AvailableDrives.Add(drive);
                }

                // Auto-select first drive if available
                if (AvailableDrives.Any())
                {
                    SelectedDrive = AvailableDrives.First();
                }
            }
            catch
            {
                // Handle drive enumeration errors gracefully
            }
        }

        private bool CanExecuteCancel()
        {
            return _scanService.IsScanning;
        }

        private bool CanExecuteScanBuckets()
        {
            return !_isScanningBuckets;
        }

        private bool CanExecuteCleanBucket(CleanupBucket? bucket)
        {
            return bucket != null && 
                   bucket.Status == CleanupStatus.Scanned && 
                   bucket.ItemCount > 0;
        }

        private bool CanExecuteCancelBuckets()
        {
            return _isScanningBuckets;
        }

        private async Task ExecuteScanAsync()
        {
            if (SelectedDrive == null)
            {
                MessageBox.Show(
                    "Please select a drive to scan.",
                    "No Drive Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            IsScanning = true;
            ProgressPercent = 0;
            ProgressValue = 0;
            StatusText = $"Scanning {SelectedDrive.Name}...";
            ScanCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();

            List<FileSystemItem>? filesResult = null;
            List<FileSystemItem>? foldersResult = null;

            try
            {
                await _scanService.ScanPathAsync(
                    rootPath: SelectedDrive.RootDirectory.FullName,
                    topN: 100,
                    progress: new Progress<double>(p =>
                    {
                        ProgressPercent = p;
                        ProgressValue = p;
                    }),
                    statusUpdate: new Progress<string>(s => StatusText = s),
                    onFilesFound: files =>
                    {
                        filesResult = files;
                    },
                    onFoldersFound: folders =>
                    {
                        foldersResult = folders;
                    });

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    LargestFiles.Clear();
                    if (filesResult != null)
                    {
                        foreach (var file in filesResult)
                        {
                            LargestFiles.Add(file);
                        }
                    }

                    LargestFolders.Clear();
                    if (foldersResult != null)
                    {
                        foreach (var folder in foldersResult)
                        {
                            LargestFolders.Add(folder);
                        }
                    }
                });

                StatusText = "Scan completed";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Scan cancelled";
            }
            catch (Exception ex)
            {
                StatusText = $"Scan error: {ex.Message}";
                MessageBox.Show(
                    $"An error occurred during scanning:\n{ex.Message}",
                    "Scan Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsScanning = false;
                ScanCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task ExecuteCancelAsync()
        {
            await _scanService.CancelAsync();
            StatusText = "Scan cancelled";
            IsScanning = false;
            ScanCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }

        private async Task ExecuteScanBucketsAsync()
        {
            _isScanningBuckets = true;
            _bucketsCancellationTokenSource = new CancellationTokenSource();
            CleanupBuckets.Clear();

            ScanBucketsCommand.RaiseCanExecuteChanged();
            CancelBucketsCommand.RaiseCanExecuteChanged();

            try
            {
                var buckets = await _bucketsService.ScanBucketsAsync(
                    new Progress<string>(s => StatusText = s),
                    _bucketsCancellationTokenSource.Token);

                foreach (var bucket in buckets)
                {
                    CleanupBuckets.Add(bucket);
                }

                var totalSize = buckets.Sum(b => b.ReclaimableBytes);
                StatusText = $"Found {CleanupBuckets.Count} cleanup opportunities. Total reclaimable: {FormatSize(totalSize)}";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Bucket scan cancelled";
            }
            finally
            {
                _isScanningBuckets = false;
                _bucketsCancellationTokenSource?.Dispose();
                _bucketsCancellationTokenSource = null;

                ScanBucketsCommand.RaiseCanExecuteChanged();
                CancelBucketsCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task ExecuteCleanBucketAsync(CleanupBucket? bucket)
        {
            if (bucket == null) return;

            var result = MessageBox.Show(
                $"Clean {bucket.Name}?\n\n" +
                $"This will move {bucket.ItemCount} items ({bucket.ReclaimableSize}) to quarantine.\n" +
                $"Quarantine location: {_bucketsService.GetQuarantinePath()}\n\n" +
                $"You can restore items from quarantine if needed.",
                "Confirm Cleanup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                var actions = await _bucketsService.CleanBucketAsync(
                    bucket,
                    new Progress<string>(s => StatusText = s),
                    CancellationToken.None);

                await _reportService.LogCleanupActionsAsync(bucket.Name, actions);

                var successCount = actions.Count(a => a.Success);
                var totalSize = actions.Where(a => a.Success).Sum(a => a.Size);

                MessageBox.Show(
                    $"Cleanup complete!\n\n" +
                    $"Moved: {successCount} items\n" +
                    $"Size: {FormatSize(totalSize)}\n" +
                    $"Log: {_reportService.GetLogDirectory()}",
                    "Cleanup Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                StatusText = $"Cleaned {bucket.Name}: {successCount} items moved";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error during cleanup:\n{ex.Message}",
                    "Cleanup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task ExecuteCancelBucketsAsync()
        {
            _bucketsCancellationTokenSource?.Cancel();
            StatusText = "Cancelling bucket scan...";
            await Task.CompletedTask;
        }

        private bool CanExecuteScanApps()
        {
            return !_isScanningApps && SelectedDrive != null;
        }

        private bool CanExecuteCancelApps()
        {
            return _isScanningApps;
        }

        private bool CanExecuteUninstallApp(InstalledApp? app)
        {
            return app != null && !string.IsNullOrWhiteSpace(app.UninstallString);
        }

        private async Task ExecuteScanAppsAsync()
        {
            if (SelectedDrive == null)
            {
                MessageBox.Show(
                    "Please select a drive to scan for applications.",
                    "No Drive Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _isScanningApps = true;
            _appsCancellationTokenSource = new CancellationTokenSource();
            InstalledApps.Clear();
            FilteredApps.Clear();

            ScanAppsCommand.RaiseCanExecuteChanged();
            CancelAppsCommand.RaiseCanExecuteChanged();

            try
            {
                var apps = await _appsService.EnumerateInstalledAppsAsync(
                    new Progress<string>(s => StatusText = s),
                    _appsCancellationTokenSource.Token,
                    SelectedDrive.Name);

                foreach (var app in apps)
                {
                    InstalledApps.Add(app);
                    FilteredApps.Add(app);
                }

                var totalSize = apps.Sum(a => a.EstimatedSize);
                StatusText = $"Found {InstalledApps.Count} applications on {SelectedDrive.Name}. Total size: {FormatSize(totalSize)}";
            }
            catch (OperationCanceledException)
            {
                StatusText = "App scan cancelled";
            }
            finally
            {
                _isScanningApps = false;
                _appsCancellationTokenSource?.Dispose();
                _appsCancellationTokenSource = null;

                ScanAppsCommand.RaiseCanExecuteChanged();
                CancelAppsCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task ExecuteCancelAppsAsync()
        {
            _appsCancellationTokenSource?.Cancel();
            StatusText = "Cancelling app scan...";
            await Task.CompletedTask;
        }

        private async Task ExecuteUninstallAppAsync(InstalledApp? app)
        {
            if (app == null) return;

            var result = MessageBox.Show(
                $"Uninstall {app.DisplayName}?\n\n" +
                $"Publisher: {app.Publisher}\n" +
                $"Version: {app.DisplayVersion}\n" +
                $"Size: {app.FormattedSize}\n\n" +
                $"This will launch the application's uninstaller.\n" +
                $"You may need to provide administrator permission.",
                "Confirm Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                StatusText = $"Launching uninstaller for {app.DisplayName}...";

                var uninstallResult = await _appsService.UninstallApplicationAsync(app, silent: false);

                if (uninstallResult.Success)
                {
                    MessageBox.Show(
                        $"Uninstaller launched for {app.DisplayName}.\n\n" +
                        $"Please follow the uninstaller's instructions.\n" +
                        $"Refresh the app list after uninstall completes.",
                        "Uninstall Started",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    StatusText = $"Uninstaller launched for {app.DisplayName}";
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to launch uninstaller:\n{uninstallResult.ErrorMessage}",
                        "Uninstall Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    StatusText = "Uninstall failed";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error launching uninstaller:\n{ex.Message}",
                    "Uninstall Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void FilterApps()
        {
            FilteredApps.Clear();

            var filtered = string.IsNullOrWhiteSpace(AppsSearchText)
                ? InstalledApps.ToList()
                : _appsService.FilterApps(InstalledApps.ToList(), AppsSearchText);

            foreach (var app in filtered)
            {
                FilteredApps.Add(app);
            }
        }

        private async Task ExecuteSortAppsByDateAsync()
        {
            await Task.Run(() =>
            {
                var sortedApps = InstalledApps
                    .OrderByDescending(a => a.InstallDate ?? DateTime.MinValue)
                    .ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    InstalledApps.Clear();
                    FilteredApps.Clear();

                    foreach (var app in sortedApps)
                    {
                        InstalledApps.Add(app);
                    }

                    FilterApps();
                });

                StatusText = "Sorted by install date (newest first)";
            });
        }

        private async Task ExecuteSortAppsBySizeAsync()
        {
            await Task.Run(() =>
            {
                var sortedApps = InstalledApps
                    .OrderByDescending(a => a.EstimatedSize)
                    .ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    InstalledApps.Clear();
                    FilteredApps.Clear();

                    foreach (var app in sortedApps)
                    {
                        InstalledApps.Add(app);
                    }

                    FilterApps();
                });

                StatusText = "Sorted by size (largest first)";
            });
        }

        private async Task ExecuteSortAppsByNameAsync()
        {
            await Task.Run(() =>
            {
                var sortedApps = InstalledApps
                    .OrderBy(a => a.DisplayName)
                    .ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    InstalledApps.Clear();
                    FilteredApps.Clear();

                    foreach (var app in sortedApps)
                    {
                        InstalledApps.Add(app);
                    }

                    FilterApps();
                });

                StatusText = "Sorted alphabetically (A-Z)";
            });
        }

        private async Task ExecuteLoadRestorableSessionsAsync()
        {
            RestorableSessions.Clear();
            StatusText = "Loading restorable sessions...";

            try
            {
                var sessions = await _reportService.GetRestorableSessionsAsync();

                foreach (var session in sessions)
                {
                    RestorableSessions.Add(session);
                }

                StatusText = $"Found {RestorableSessions.Count} restorable cleanup sessions";
            }
            catch (Exception ex)
            {
                StatusText = $"Error loading sessions: {ex.Message}";
            }
        }

        private bool CanExecuteRestoreSession(CleanupSession? session)
        {
            return session != null && session.HasRestorableItems;
        }

        private async Task ExecuteRestoreSessionAsync(CleanupSession? session)
        {
            if (session == null) return;

            var result = MessageBox.Show(
                $"Restore cleanup session?\n\n" +
                $"Bucket: {session.BucketName}\n" +
                $"Date: {session.FormattedTimestamp}\n" +
                $"Items to restore: {session.RestorableCount}\n" +
                $"Original size: {session.FormattedSize}\n\n" +
                $"Files will be moved back to their original locations.",
                "Confirm Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                StatusText = "Restoring files...";

                var restoreResult = await _reportService.RestoreSessionAsync(
                    session.SessionId,
                    new Progress<string>(s => StatusText = s));

                if (restoreResult.Success)
                {
                    MessageBox.Show(
                        $"Restore complete!\n\n" +
                        $"Restored: {restoreResult.RestoredCount} items\n" +
                        $"All files have been moved back to their original locations.",
                        "Restore Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // Refresh the sessions list
                    await ExecuteLoadRestorableSessionsAsync();
                }
                else
                {
                    var errorDetails = restoreResult.Errors.Any()
                        ? string.Join("\n", restoreResult.Errors.Take(10))
                        : restoreResult.ErrorMessage ?? "Unknown error";

                    MessageBox.Show(
                        $"Restore completed with errors:\n\n" +
                        $"Restored: {restoreResult.RestoredCount} items\n" +
                        $"Failed: {restoreResult.FailedCount} items\n\n" +
                        $"Errors:\n{errorDetails}",
                        "Restore Errors",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    // Refresh even if there were errors
                    await ExecuteLoadRestorableSessionsAsync();
                }

                StatusText = restoreResult.Summary;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error during restore:\n{ex.Message}",
                    "Restore Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "Restore failed";
            }
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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
