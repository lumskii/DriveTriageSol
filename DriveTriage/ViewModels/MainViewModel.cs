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
        private readonly DriverCacheService _driverCacheService;
        private readonly StorageSnapshotService _storageSnapshotService;
        private readonly PlanService _planService;
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
        private SystemMaintenanceInfo? _systemMaintenanceInfo;
        private bool _showSafe = true;
        private bool _showCaution = true;
        private bool _showBlocked = true;
        private int _daysBackFilter = 7;
        private long _targetFreeSpaceGB = 100;
        private RecoveryPlan? _currentPlan;

        public MainViewModel()
        {
            _scanService = new ScanService();
            _bucketsService = new BucketsService();
            _reportService = new ReportService();
            _appsService = new AppsService();
            _systemCleanupService = new SystemCleanupService();
            _driverCacheService = new DriverCacheService();
            _storageSnapshotService = new StorageSnapshotService();
            _planService = new PlanService(_bucketsService, _appsService, _systemCleanupService, _driverCacheService);

            LargestFiles = new ObservableCollection<FileSystemItem>();
            LargestFolders = new ObservableCollection<FileSystemItem>();
            LargestProgramDataFolders = new ObservableCollection<FileSystemItem>();
            CleanupBuckets = new ObservableCollection<CleanupBucket>();
            InstalledApps = new ObservableCollection<InstalledApp>();
            FilteredApps = new ObservableCollection<InstalledApp>();
            RestorableSessions = new ObservableCollection<CleanupSession>();
            AvailableDrives = new ObservableCollection<DriveInfo>();
            GrowthAlerts = new ObservableCollection<GrowthAlert>();
            RecoveryCandidates = new ObservableCollection<RecoveryCandidate>();
            FindingGroups = new ObservableCollection<FindingGroup>();

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
            ScanProgramDataCommand = new AsyncRelayCommand(ExecuteScanProgramDataAsync, CanExecuteScanProgramData);
            RefreshSystemCleanupInfoCommand = new AsyncRelayCommand(ExecuteRefreshSystemCleanupInfoAsync);
            PurgeNvidiaCacheCommand = new AsyncRelayCommand(ExecutePurgeNvidiaCacheAsync);
            EmptyRecycleBinCommand = new AsyncRelayCommand(ExecuteEmptyRecycleBinAsync);
            CleanQuarantineCommand = new AsyncRelayCommand(ExecuteCleanQuarantineAsync);
            RunDismComponentCleanupCommand = new AsyncRelayCommand(ExecuteRunDismComponentCleanupAsync);
            ClearWindowsUpdateCacheCommand = new AsyncRelayCommand(ExecuteClearWindowsUpdateCacheAsync);
            RefreshSystemMaintenanceInfoCommand = new AsyncRelayCommand(ExecuteRefreshSystemMaintenanceInfoAsync);
            TakeSnapshotCommand = new AsyncRelayCommand(ExecuteTakeSnapshotAsync, CanExecuteTakeSnapshot);
            CompareSnapshotsCommand = new AsyncRelayCommand(ExecuteCompareSnapshotsAsync, CanExecuteCompareSnapshots);
            AnalyzeWeeklyGrowthCommand = new AsyncRelayCommand(ExecuteAnalyzeWeeklyGrowthAsync, CanExecuteAnalyzeWeeklyGrowth);
            IgnoreGrowthAlertCommand = new AsyncRelayCommand<GrowthAlert>(ExecuteIgnoreGrowthAlertAsync, CanExecuteIgnoreGrowthAlert);
            GeneratePlanCommand = new AsyncRelayCommand(ExecuteGeneratePlanAsync, CanExecuteGeneratePlan);
            ExecuteSafePlanCommand = new AsyncRelayCommand(ExecuteSafePlanAsync, CanExecuteSafePlan);
            CleanGroupSafeCommand = new AsyncRelayCommand<FindingGroup>(ExecuteCleanGroupSafeAsync, CanExecuteCleanGroup);
            CleanGroupAllCommand = new AsyncRelayCommand<FindingGroup>(ExecuteCleanGroupAllAsync, CanExecuteCleanGroup);
            CleanSubgroupCommand = new AsyncRelayCommand<FindingSubgroup>(ExecuteCleanSubgroupAsync, CanExecuteCleanSubgroup);

            // Load drives AFTER commands are initialized
            // This is safe because SelectedDrive setter now has ScanCommand initialized
            LoadAvailableDrives();

            // Load system cleanup info
            _ = ExecuteRefreshSystemCleanupInfoAsync();

            // Load system maintenance info
            _ = ExecuteRefreshSystemMaintenanceInfoAsync();
        }

        public ObservableCollection<FileSystemItem> LargestFiles { get; }
        public ObservableCollection<FileSystemItem> LargestFolders { get; }
        public ObservableCollection<FileSystemItem> LargestProgramDataFolders { get; }
        public ObservableCollection<CleanupBucket> CleanupBuckets { get; }
        public ObservableCollection<InstalledApp> InstalledApps { get; }
        public ObservableCollection<InstalledApp> FilteredApps { get; }
        public ObservableCollection<CleanupSession> RestorableSessions { get; }
        public ObservableCollection<DriveInfo> AvailableDrives { get; }

        private ObservableCollection<GrowthAlert> _allGrowthAlerts = new();
        public ObservableCollection<GrowthAlert> GrowthAlerts { get; }
        public ObservableCollection<RecoveryCandidate> RecoveryCandidates { get; }
        public ObservableCollection<FindingGroup> FindingGroups { get; }

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

        public SystemCleanupInfo? SystemCleanupInfo
        {
            get => _systemCleanupInfo;
            set
            {
                _systemCleanupInfo = value;
                OnPropertyChanged();
            }
        }

        public SystemMaintenanceInfo? SystemMaintenanceInfo
        {
            get => _systemMaintenanceInfo;
            set
            {
                _systemMaintenanceInfo = value;
                OnPropertyChanged();
            }
        }

        public bool ShowSafe
        {
            get => _showSafe;
            set
            {
                _showSafe = value;
                OnPropertyChanged();
                FilterGrowthAlerts();
            }
        }

        public bool ShowCaution
        {
            get => _showCaution;
            set
            {
                _showCaution = value;
                OnPropertyChanged();
                FilterGrowthAlerts();
            }
        }

        public bool ShowBlocked
        {
            get => _showBlocked;
            set
            {
                _showBlocked = value;
                OnPropertyChanged();
                FilterGrowthAlerts();
            }
        }

        public int DaysBackFilter
        {
            get => _daysBackFilter;
            set
            {
                _daysBackFilter = value;
                OnPropertyChanged();
            }
        }

        public long TargetFreeSpaceGB
        {
            get => _targetFreeSpaceGB;
            set
            {
                _targetFreeSpaceGB = value;
                OnPropertyChanged();
            }
        }

        public RecoveryPlan? CurrentPlan
        {
            get => _currentPlan;
            set
            {
                _currentPlan = value;
                OnPropertyChanged();
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
        public AsyncRelayCommand ScanProgramDataCommand { get; }
        public AsyncRelayCommand RefreshSystemCleanupInfoCommand { get; }
        public AsyncRelayCommand PurgeNvidiaCacheCommand { get; }
        public AsyncRelayCommand EmptyRecycleBinCommand { get; }
        public AsyncRelayCommand CleanQuarantineCommand { get; }
        public AsyncRelayCommand RunDismComponentCleanupCommand { get; }
        public AsyncRelayCommand ClearWindowsUpdateCacheCommand { get; }
        public AsyncRelayCommand RefreshSystemMaintenanceInfoCommand { get; }
        public AsyncRelayCommand TakeSnapshotCommand { get; }
        public AsyncRelayCommand CompareSnapshotsCommand { get; }
        public AsyncRelayCommand AnalyzeWeeklyGrowthCommand { get; }
        public AsyncRelayCommand<GrowthAlert> IgnoreGrowthAlertCommand { get; }
        public AsyncRelayCommand GeneratePlanCommand { get; }
        public AsyncRelayCommand ExecuteSafePlanCommand { get; }
        public AsyncRelayCommand<FindingGroup> CleanGroupSafeCommand { get; }
        public AsyncRelayCommand<FindingGroup> CleanGroupAllCommand { get; }
        public AsyncRelayCommand<FindingSubgroup> CleanSubgroupCommand { get; }

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
            FindingGroups.Clear();

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

                // Build finding groups
                await BuildFindingGroupsAsync(buckets);

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

        private bool CanExecuteScanProgramData()
        {
            return !_scanService.IsScanning;
        }

        private async Task ExecuteScanProgramDataAsync()
        {
            IsScanning = true;
            ProgressPercent = 0;
            ProgressValue = 0;
            StatusText = "Preparing to scan C:\\ProgramData...";
            ScanProgramDataCommand.RaiseCanExecuteChanged();

            List<FileSystemItem>? foldersResult = null;
            var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                foldersResult = await _scanService.ScanProgramDataAsync(
                    topN: 50,
                    progress: new Progress<double>(p =>
                    {
                        ProgressPercent = p;
                        ProgressValue = p;
                    }),
                    statusUpdate: new Progress<string>(s => StatusText = s),
                    cancellationToken: cancellationTokenSource.Token);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    LargestProgramDataFolders.Clear();
                    if (foldersResult != null)
                    {
                        foreach (var folder in foldersResult)
                        {
                            LargestProgramDataFolders.Add(folder);
                        }
                    }
                });

                StatusText = foldersResult != null && foldersResult.Any()
                    ? $"Found {foldersResult.Count} largest ProgramData folders"
                    : "ProgramData scan completed";
            }
            catch (OperationCanceledException)
            {
                StatusText = "ProgramData scan cancelled";
            }
            catch (Exception ex)
            {
                StatusText = $"ProgramData scan error: {ex.Message}";
                MessageBox.Show(
                    $"Error scanning ProgramData:\n{ex.Message}",
                    "Scan Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsScanning = false;
                ProgressPercent = 0;
                ProgressValue = 0;
                cancellationTokenSource.Dispose();
                ScanProgramDataCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task ExecuteRefreshSystemCleanupInfoAsync()
        {
            try
            {
                StatusText = "Refreshing system cleanup info...";
                var info = await _systemCleanupService.GetSystemCleanupInfoAsync();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SystemCleanupInfo = info;
                });

                StatusText = "System cleanup info updated";
            }
            catch (Exception ex)
            {
                StatusText = $"Error refreshing info: {ex.Message}";
            }
        }

        private async Task ExecutePurgeNvidiaCacheAsync()
        {
            var result = MessageBox.Show(
                "Purge NVIDIA Cache?\n\n" +
                $"This will delete all files from:\n" +
                "• C:\\ProgramData\\NVIDIA Corporation\\Downloader\n" +
                "• C:\\ProgramData\\NVIDIA Corporation\\NV_Cache\n" +
                "• C:\\NVIDIA (if present)\n\n" +
                "NVIDIA drivers will re-download needed files automatically.\n" +
                "This operation is safe and reversible.",
                "Confirm NVIDIA Cache Purge",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                StatusText = "Purging NVIDIA cache...";

                var cleanupResult = await _driverCacheService.PurgeNvidiaCachesAsync(
                    new Progress<string>(s => StatusText = s),
                    CancellationToken.None);

                if (cleanupResult.Success)
                {
                    MessageBox.Show(
                        $"NVIDIA Cache Purged!\n\n" +
                        $"Items deleted: {cleanupResult.ItemsDeleted}\n" +
                        $"Space reclaimed: {cleanupResult.FormattedSpaceReclaimed}\n" +
                        $"Duration: {cleanupResult.Duration}\n\n" +
                        cleanupResult.Message,
                        "Purge Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // Refresh the system cleanup info
                    await ExecuteRefreshSystemCleanupInfoAsync();
                }
                else
                {
                    MessageBox.Show(
                        $"NVIDIA cache purge completed with errors:\n\n{cleanupResult.Message}",
                        "Purge Errors",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                StatusText = cleanupResult.Message;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error purging NVIDIA cache:\n{ex.Message}",
                    "Purge Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "NVIDIA cache purge failed";
            }
        }

        private async Task ExecuteEmptyRecycleBinAsync()
        {
            var result = MessageBox.Show(
                "Empty Recycle Bin?\n\n" +
                "This will permanently delete all files in the Recycle Bin.\n" +
                "This operation cannot be undone.",
                "Confirm Empty Recycle Bin",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                StatusText = "Emptying Recycle Bin...";

                var cleanupResult = await _systemCleanupService.EmptyRecycleBinAsync(
                    new Progress<string>(s => StatusText = s),
                    CancellationToken.None);

                if (cleanupResult.Success)
                {
                    MessageBox.Show(
                        $"Recycle Bin Emptied!\n\n" +
                        $"Space reclaimed: {cleanupResult.FormattedSpaceReclaimed}\n" +
                        $"Duration: {cleanupResult.Duration}\n\n" +
                        cleanupResult.Message,
                        "Empty Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    await ExecuteRefreshSystemCleanupInfoAsync();
                }
                else
                {
                    MessageBox.Show(
                        $"Error emptying Recycle Bin:\n\n{cleanupResult.Message}",
                        "Empty Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                StatusText = cleanupResult.Message;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error emptying Recycle Bin:\n{ex.Message}",
                    "Empty Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "Recycle Bin empty failed";
            }
        }

        private async Task ExecuteCleanQuarantineAsync()
        {
            var result = MessageBox.Show(
                "Clean Quarantine?\n\n" +
                "This will permanently delete all files in the quarantine folder.\n" +
                "After cleaning, you will no longer be able to restore these items.\n\n" +
                "This operation cannot be undone.",
                "Confirm Clean Quarantine",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                StatusText = "Cleaning quarantine...";

                var cleanupResult = await _systemCleanupService.CleanQuarantineAsync(
                    new Progress<string>(s => StatusText = s),
                    CancellationToken.None);

                if (cleanupResult.Success)
                {
                    MessageBox.Show(
                        $"Quarantine Cleaned!\n\n" +
                        $"Items deleted: {cleanupResult.ItemsDeleted}\n" +
                        $"Space reclaimed: {cleanupResult.FormattedSpaceReclaimed}\n" +
                        $"Duration: {cleanupResult.Duration}\n\n" +
                        cleanupResult.Message,
                        "Clean Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    await ExecuteRefreshSystemCleanupInfoAsync();
                }
                else
                {
                    MessageBox.Show(
                        $"Error cleaning quarantine:\n\n{cleanupResult.Message}",
                        "Clean Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                StatusText = cleanupResult.Message;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error cleaning quarantine:\n{ex.Message}",
                    "Clean Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "Quarantine clean failed";
            }
        }

        private async Task ExecuteRunDismComponentCleanupAsync()
        {
            var result = MessageBox.Show(
                "Run DISM Component Cleanup?\n\n" +
                "This will clean up Windows component store and can free significant disk space.\n\n" +
                "⚠️ Important:\n" +
                "• This operation may take 10-30 minutes\n" +
                "• Requires Administrator privileges\n" +
                "• Safe to run, but cannot be cancelled once started\n" +
                "• Your computer will remain usable during cleanup\n\n" +
                "Continue?",
                "Confirm DISM Component Cleanup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                StatusText = "Running DISM component cleanup...";

                var cleanupResult = await _systemCleanupService.RunDismComponentCleanupAsync(
                    new Progress<string>(s => StatusText = s),
                    CancellationToken.None);

                if (cleanupResult.Success)
                {
                    MessageBox.Show(
                        $"DISM Component Cleanup Complete!\n\n" +
                        $"Duration: {cleanupResult.Duration}\n\n" +
                        cleanupResult.Message,
                        "Cleanup Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"DISM component cleanup completed with errors:\n\n{cleanupResult.Message}\n\n" +
                        "Note: You may need to run this application as Administrator.",
                        "Cleanup Errors",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                StatusText = cleanupResult.Message;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error running DISM component cleanup:\n{ex.Message}\n\n" +
                    "Note: This operation requires Administrator privileges.",
                    "Cleanup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "DISM component cleanup failed";
            }
        }

        private async Task ExecuteClearWindowsUpdateCacheAsync()
        {
            var result = MessageBox.Show(
                "Clear Windows Update Download Cache?\n\n" +
                "This will:\n" +
                "1. Stop the Windows Update service (wuauserv)\n" +
                "2. Delete all files in C:\\Windows\\SoftwareDistribution\\Download\n" +
                "3. Restart the Windows Update service\n\n" +
                "⚠️ Important:\n" +
                "• Requires Administrator privileges\n" +
                "• In-progress Windows Updates will be interrupted\n" +
                "• Updates will re-download when needed\n" +
                "• Safe operation - Windows Update will work normally after\n\n" +
                "Continue?",
                "Confirm Clear Windows Update Cache",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                StatusText = "Clearing Windows Update cache...";

                var cleanupResult = await _systemCleanupService.ClearWindowsUpdateDownloadCacheAsync(
                    new Progress<string>(s => StatusText = s),
                    CancellationToken.None);

                if (cleanupResult.Success)
                {
                    MessageBox.Show(
                        $"Windows Update Cache Cleared!\n\n" +
                        $"Items deleted: {cleanupResult.ItemsDeleted}\n" +
                        $"Space reclaimed: {cleanupResult.FormattedSpaceReclaimed}\n" +
                        $"Duration: {cleanupResult.Duration}\n\n" +
                        cleanupResult.Message,
                        "Cache Cleared",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // Refresh maintenance info
                    await ExecuteRefreshSystemMaintenanceInfoAsync();
                }
                else
                {
                    MessageBox.Show(
                        $"Windows Update cache clear completed with errors:\n\n{cleanupResult.Message}\n\n" +
                        "Note: You may need to run this application as Administrator.",
                        "Clear Errors",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                StatusText = cleanupResult.Message;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error clearing Windows Update cache:\n{ex.Message}\n\n" +
                    "Note: This operation requires Administrator privileges.",
                    "Clear Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "Windows Update cache clear failed";
            }
        }

        private async Task ExecuteRefreshSystemMaintenanceInfoAsync()
        {
            try
            {
                StatusText = "Refreshing system maintenance info...";

                var info = new SystemMaintenanceInfo
                {
                    DismAvailable = true,
                    WindowsUpdateCacheAvailable = Directory.Exists(@"C:\Windows\SoftwareDistribution\Download")
                };

                // Calculate Windows Update cache size
                if (info.WindowsUpdateCacheAvailable)
                {
                    try
                    {
                        var downloadPath = @"C:\Windows\SoftwareDistribution\Download";
                        info.WindowsUpdateCacheSize = await Task.Run(() => 
                            CalculateDirectorySizeHelper(downloadPath));
                    }
                    catch
                    {
                        info.WindowsUpdateCacheSize = 0;
                    }
                }

                // Get shadow storage info
                try
                {
                    info.ShadowStorageInfo = await _systemCleanupService.GetShadowStorageInfoAsync();
                }
                catch
                {
                    info.ShadowStorageInfo = new ShadowStorageInfo 
                    { 
                        ErrorMessage = "Unable to retrieve shadow storage information" 
                    };
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SystemMaintenanceInfo = info;
                });

                StatusText = "System maintenance info updated";
            }
            catch (Exception ex)
            {
                StatusText = $"Error refreshing maintenance info: {ex.Message}";
            }
        }

        private async Task ExecuteTakeSnapshotAsync()
        {
            try
            {
                StatusText = "Taking storage snapshot...";
                IsScanning = true;
                ProgressPercent = 0;

                var snapshot = await _storageSnapshotService.TakeSnapshotAsync(
                    new Progress<string>(s => StatusText = s),
                    CancellationToken.None);

                var snapshotCount = await _storageSnapshotService.GetSnapshotCountAsync();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        $"Storage snapshot completed!\n\n" +
                        $"Tracked folders: {snapshot.FolderSnapshots.Sum(f => f.SubfolderSizes.Count)}\n" +
                        $"Total snapshots: {snapshotCount}\n\n" +
                        $"You can now compare snapshots to detect install drift.",
                        "Snapshot Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                });

                StatusText = $"Snapshot saved - {snapshotCount} total snapshots";

                // Enable compare button if we have enough snapshots
                CompareSnapshotsCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error taking snapshot:\n{ex.Message}",
                    "Snapshot Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "Snapshot failed";
            }
            finally
            {
                IsScanning = false;
                ProgressPercent = 0;
                TakeSnapshotCommand.RaiseCanExecuteChanged();
            }
        }

        private bool CanExecuteTakeSnapshot()
        {
            return !IsScanning;
        }

        private async Task ExecuteCompareSnapshotsAsync()
        {
            try
            {
                StatusText = "Comparing snapshots...";
                IsScanning = true;

                var alerts = await _storageSnapshotService.CompareLatestSnapshotsAsync(
                    new Progress<string>(s => StatusText = s),
                    CancellationToken.None);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _allGrowthAlerts.Clear();
                    foreach (var alert in alerts)
                    {
                        _allGrowthAlerts.Add(alert);
                    }
                    FilterGrowthAlerts();
                });

                if (alerts.Count == 0)
                {
                    var snapshotCount = await _storageSnapshotService.GetSnapshotCountAsync();

                    if (snapshotCount < 2)
                    {
                        MessageBox.Show(
                            $"Need at least 2 snapshots to compare growth.\n\n" +
                            $"Current snapshots: {snapshotCount}\n\n" +
                            "Please take another snapshot and try again.",
                            "Insufficient Snapshots",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(
                            "No significant growth detected!\n\n" +
                            "No folders have grown by more than 1 MB since the last snapshot.",
                            "No Growth Detected",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    var latestDate = await _storageSnapshotService.GetLatestSnapshotDateAsync();
                    var totalGrowth = alerts.Sum(a => a.DeltaBytes);

                    MessageBox.Show(
                        $"Found {alerts.Count} folders with significant growth!\n\n" +
                        $"Total growth detected: {FormatSize(totalGrowth)}\n" +
                        $"Latest snapshot: {latestDate:g}\n\n" +
                        "Results are displayed in the 'Install Drift Monitor' section below.",
                        "Growth Detected",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                StatusText = alerts.Count > 0 
                    ? $"Found {alerts.Count} folders with growth" 
                    : "No significant growth detected";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error comparing snapshots:\n{ex.Message}",
                    "Comparison Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "Comparison failed";
            }
            finally
            {
                IsScanning = false;
                CompareSnapshotsCommand.RaiseCanExecuteChanged();
            }
        }

        private bool CanExecuteCompareSnapshots()
        {
            return !IsScanning;
        }

        private async Task ExecuteAnalyzeWeeklyGrowthAsync()
        {
            try
            {
                StatusText = $"Analyzing growth over {DaysBackFilter} days...";
                IsScanning = true;

                var alerts = await _storageSnapshotService.GetTopGrowersAsync(
                    DaysBackFilter,
                    50, // Top 50
                    new Progress<string>(s => StatusText = s),
                    CancellationToken.None);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _allGrowthAlerts.Clear();
                    foreach (var alert in alerts)
                    {
                        _allGrowthAlerts.Add(alert);
                    }
                    FilterGrowthAlerts();
                });

                if (alerts.Count == 0)
                {
                    MessageBox.Show(
                        $"No significant growth detected in the last {DaysBackFilter} days.\n\n" +
                        "Try adjusting the time period or take more snapshots.",
                        "No Growth Detected",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    var totalGrowth = alerts.Sum(a => a.DeltaBytes);

                    MessageBox.Show(
                        $"Found {alerts.Count} top growers over {DaysBackFilter} days!\n\n" +
                        $"Total growth: {FormatSize(totalGrowth)}\n\n" +
                        "Results are displayed in the 'Install Drift Monitor' section.",
                        "Growth Analysis Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                StatusText = alerts.Count > 0
                    ? $"Found {alerts.Count} top growers over {DaysBackFilter} days"
                    : "No significant growth detected";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error analyzing weekly growth:\n{ex.Message}",
                    "Analysis Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "Analysis failed";
            }
            finally
            {
                IsScanning = false;
                AnalyzeWeeklyGrowthCommand.RaiseCanExecuteChanged();
            }
        }

        private bool CanExecuteAnalyzeWeeklyGrowth()
        {
            return !IsScanning;
        }

        private async Task ExecuteIgnoreGrowthAlertAsync(GrowthAlert? alert)
        {
            if (alert == null)
                return;

            var result = MessageBox.Show(
                $"Mark this path as expected growth?\n\n" +
                $"Path: {alert.Path}\n\n" +
                "This will add it to the ignore list and it won't appear in future comparisons.",
                "Mark as Expected",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                await _storageSnapshotService.AddToIgnoreListAsync(alert.Path);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _allGrowthAlerts.Remove(alert);
                    GrowthAlerts.Remove(alert);
                });

                StatusText = $"Added to ignore list: {Path.GetFileName(alert.Path)}";

                MessageBox.Show(
                    "Path added to ignore list.\n\n" +
                    "It will be excluded from future growth comparisons.",
                    "Added to Ignore List",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error adding to ignore list:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool CanExecuteIgnoreGrowthAlert(GrowthAlert? alert)
        {
            return alert != null && !IsScanning;
        }

        private void FilterGrowthAlerts()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                GrowthAlerts.Clear();

                var filtered = _allGrowthAlerts.Where(alert =>
                {
                    return alert.Classification switch
                    {
                        SafetyClassification.Safe => ShowSafe,
                        SafetyClassification.Caution => ShowCaution,
                        SafetyClassification.Blocked => ShowBlocked,
                        _ => true
                    };
                });

                foreach (var alert in filtered)
                {
                    GrowthAlerts.Add(alert);
                }
            });
        }

        private long CalculateDirectorySizeHelper(string path)
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

        private async Task ExecuteGeneratePlanAsync()
        {
            if (SelectedDrive == null)
            {
                MessageBox.Show(
                    "Please select a drive first.",
                    "No Drive Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                StatusText = "Generating recovery plan...";
                IsScanning = true;

                var targetFreeBytes = TargetFreeSpaceGB * 1024L * 1024L * 1024L;

                var plan = await _planService.GeneratePlanAsync(
                    SelectedDrive,
                    targetFreeBytes,
                    new Progress<string>(s => StatusText = s),
                    CancellationToken.None);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CurrentPlan = plan;
                    RecoveryCandidates.Clear();
                    foreach (var candidate in plan.SelectedCandidates)
                    {
                        RecoveryCandidates.Add(candidate);
                    }
                });

                if (plan.GoalAchievable)
                {
                    MessageBox.Show(
                        $"Recovery Plan Generated!\n\n" +
                        $"Current Free: {plan.FormattedCurrentFree}\n" +
                        $"Target Free: {plan.FormattedTargetFree}\n" +
                        $"Total Reclaimable: {plan.FormattedTotalReclaimable}\n" +
                        $"Projected Free: {plan.FormattedProjectedFree}\n\n" +
                        $"Safe Actions: {plan.SafeCandidatesCount}\n" +
                        $"Caution Actions: {plan.CautionCandidatesCount}\n\n" +
                        "Review the plan below and click 'Execute Safe Plan' to proceed.",
                        "Plan Generated",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"Goal Cannot Be Fully Achieved\n\n" +
                        $"Current Free: {plan.FormattedCurrentFree}\n" +
                        $"Target Free: {plan.FormattedTargetFree}\n" +
                        $"Available Reclaimable: {plan.FormattedTotalReclaimable}\n" +
                        $"Remaining Gap: {plan.FormattedRemainingGap}\n\n" +
                        $"The plan includes all available recovery candidates.\n" +
                        "Consider:\n" +
                        "• Moving files to another drive\n" +
                        "• Uninstalling more applications\n" +
                        "• Deleting personal files",
                        "Goal Not Achievable",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                StatusText = $"Plan ready: {RecoveryCandidates.Count} actions";
                ExecuteSafePlanCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error generating plan:\n{ex.Message}",
                    "Plan Generation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "Plan generation failed";
            }
            finally
            {
                IsScanning = false;
                GeneratePlanCommand.RaiseCanExecuteChanged();
            }
        }

        private bool CanExecuteGeneratePlan()
        {
            return !IsScanning && SelectedDrive != null;
        }

        private async Task ExecuteSafePlanAsync()
        {
            if (CurrentPlan == null || !CurrentPlan.SelectedCandidates.Any())
            {
                MessageBox.Show(
                    "No plan available. Generate a plan first.",
                    "No Plan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var safeCandidates = CurrentPlan.SelectedCandidates
                .Where(c => c.Risk == SafetyClassification.Safe)
                .ToList();

            if (!safeCandidates.Any())
            {
                MessageBox.Show(
                    "No safe actions in plan.\n\n" +
                    "All candidates require caution. Please review them individually.",
                    "No Safe Actions",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Execute Safe Plan?\n\n" +
                $"This will execute {safeCandidates.Count} safe recovery actions:\n\n" +
                string.Join("\n", safeCandidates.Select(c => $"• {c.Name} ({c.FormattedSize})")) +
                $"\n\nEstimated recovery: {FormatSize(safeCandidates.Sum(c => c.EstimatedReclaimableBytes))}\n\n" +
                "Caution items will be skipped and require manual review.",
                "Confirm Safe Plan Execution",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                StatusText = "Executing safe plan...";
                IsScanning = true;

                var executionResult = await _planService.ExecuteSafePlanAsync(
                    CurrentPlan,
                    new Progress<string>(s => StatusText = s),
                    CancellationToken.None);

                if (executionResult.Success)
                {
                    MessageBox.Show(
                        $"Safe Plan Executed Successfully!\n\n" +
                        $"Actions completed: {executionResult.SuccessfulActions}\n" +
                        $"Space reclaimed: {executionResult.FormattedTotalReclaimed}\n" +
                        $"Duration: {executionResult.Duration}\n\n" +
                        "Check the Restore tab to review cleanup sessions.",
                        "Plan Execution Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // Refresh drive info
                    if (SelectedDrive != null)
                    {
                        var refreshedDrive = DriveInfo.GetDrives()
                            .FirstOrDefault(d => d.Name == SelectedDrive.Name);
                        if (refreshedDrive != null)
                        {
                            SelectedDrive = refreshedDrive;
                        }
                    }

                    // Clear plan
                    CurrentPlan = null;
                    RecoveryCandidates.Clear();
                }
                else
                {
                    var errors = string.Join("\n", executionResult.Errors.Take(5));
                    MessageBox.Show(
                        $"Plan Execution Completed with Errors\n\n" +
                        $"Successful: {executionResult.SuccessfulActions}\n" +
                        $"Failed: {executionResult.FailedActions}\n" +
                        $"Reclaimed: {executionResult.FormattedTotalReclaimed}\n\n" +
                        $"Errors:\n{errors}",
                        "Execution Errors",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                StatusText = "Plan execution complete";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error executing plan:\n{ex.Message}",
                    "Execution Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "Plan execution failed";
            }
            finally
            {
                IsScanning = false;
                ExecuteSafePlanCommand.RaiseCanExecuteChanged();
                GeneratePlanCommand.RaiseCanExecuteChanged();
            }
        }

        private bool CanExecuteSafePlan()
        {
            return !IsScanning && CurrentPlan != null && 
                   CurrentPlan.SelectedCandidates.Any(c => c.Risk == SafetyClassification.Safe);
        }

        private async Task BuildFindingGroupsAsync(List<CleanupBucket> buckets)
        {
            await Task.Run(() =>
            {
                var systemCoreGroup = new FindingGroup
                {
                    Name = "🚫 System Core (Protected)",
                    Classification = SafetyClassification.Blocked
                };

                var optionalGroup = new FindingGroup
                {
                    Name = "✅ Optional / Reclaimable",
                    Classification = SafetyClassification.Safe
                };

                // Categorize buckets and their items
                var vendorCacheSubgroup = new FindingSubgroup { CategoryName = "Vendor Caches" };
                var devCacheSubgroup = new FindingSubgroup { CategoryName = "Development Caches" };
                var tempFilesSubgroup = new FindingSubgroup { CategoryName = "Temporary Files" };
                var installersSubgroup = new FindingSubgroup { CategoryName = "Installers & Downloads" };
                var logsSubgroup = new FindingSubgroup { CategoryName = "Logs & Diagnostics" };
                var systemActionsSubgroup = new FindingSubgroup { CategoryName = "System Actions" };
                var otherSubgroup = new FindingSubgroup { CategoryName = "Other" };
                var blockedSubgroup = new FindingSubgroup { CategoryName = "System Protected" };

                var scoringService = new ScoringService();

                foreach (var bucket in buckets)
                {
                    // Classify each item in the bucket
                    var bucketClassifications = new Dictionary<SafetyClassification, int>();

                    foreach (var item in bucket.Items)
                    {
                        var pathClassification = PathRules.ClassifyPath(item.Path);
                        var scoringResult = scoringService.ScoreFile(item.Path, item.Size, item.LastModified);

                        var findingItem = new FindingItem
                        {
                            Name = Path.GetFileName(item.Path) ?? item.Path,
                            Path = item.Path,
                            SizeBytes = item.Size,
                            Classification = scoringResult.Classification,
                            Reasons = scoringResult.Reasons,
                            SourceReference = bucket
                        };

                        // Count classifications
                        if (!bucketClassifications.ContainsKey(scoringResult.Classification))
                            bucketClassifications[scoringResult.Classification] = 0;
                        bucketClassifications[scoringResult.Classification]++;

                        // Add to appropriate subgroup based on classification and category
                        if (scoringResult.Classification == SafetyClassification.Blocked)
                        {
                            blockedSubgroup.Items.Add(findingItem);
                        }
                        else
                        {
                            // Categorize by type
                            var category = DetermineCategoryFromPath(item.Path, pathClassification, scoringResult);
                            switch (category)
                            {
                                case "VendorCache":
                                    vendorCacheSubgroup.Items.Add(findingItem);
                                    break;
                                case "DevCache":
                                    devCacheSubgroup.Items.Add(findingItem);
                                    break;
                                case "TempFiles":
                                    tempFilesSubgroup.Items.Add(findingItem);
                                    break;
                                case "Installers":
                                    installersSubgroup.Items.Add(findingItem);
                                    break;
                                case "Logs":
                                    logsSubgroup.Items.Add(findingItem);
                                    break;
                                case "SystemActions":
                                    systemActionsSubgroup.Items.Add(findingItem);
                                    break;
                                default:
                                    otherSubgroup.Items.Add(findingItem);
                                    break;
                            }
                        }
                    }
                }

                // Add non-empty subgroups to appropriate groups
                if (blockedSubgroup.Items.Any())
                    systemCoreGroup.Subgroups.Add(blockedSubgroup);

                if (vendorCacheSubgroup.Items.Any())
                    optionalGroup.Subgroups.Add(vendorCacheSubgroup);
                if (devCacheSubgroup.Items.Any())
                    optionalGroup.Subgroups.Add(devCacheSubgroup);
                if (tempFilesSubgroup.Items.Any())
                    optionalGroup.Subgroups.Add(tempFilesSubgroup);
                if (installersSubgroup.Items.Any())
                    optionalGroup.Subgroups.Add(installersSubgroup);
                if (logsSubgroup.Items.Any())
                    optionalGroup.Subgroups.Add(logsSubgroup);
                if (systemActionsSubgroup.Items.Any())
                    optionalGroup.Subgroups.Add(systemActionsSubgroup);
                if (otherSubgroup.Items.Any())
                    optionalGroup.Subgroups.Add(otherSubgroup);

                // Update UI on dispatcher thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    FindingGroups.Clear();
                    if (systemCoreGroup.Subgroups.Any())
                        FindingGroups.Add(systemCoreGroup);
                    if (optionalGroup.Subgroups.Any())
                        FindingGroups.Add(optionalGroup);
                });
            });
        }

        private string DetermineCategoryFromPath(string path, PathClassification pathClassification, ScoringResult scoringResult)
        {
            var pathLower = path.ToLowerInvariant();
            var reasonsLower = string.Join(" ", scoringResult.Reasons).ToLowerInvariant();

            // Vendor caches
            if (reasonsLower.Contains("nvidia") || reasonsLower.Contains("chrome") || 
                reasonsLower.Contains("firefox") || reasonsLower.Contains("edge") ||
                reasonsLower.Contains("adobe") || reasonsLower.Contains("browser cache"))
                return "VendorCache";

            // Dev tool caches
            if (reasonsLower.Contains("nuget") || reasonsLower.Contains("npm") ||
                reasonsLower.Contains("gradle") || reasonsLower.Contains("maven") ||
                reasonsLower.Contains("cargo") || reasonsLower.Contains("pip") ||
                reasonsLower.Contains("node_modules") || reasonsLower.Contains("build artifact") ||
                pathLower.Contains("\\.nuget\\") || pathLower.Contains("\\node_modules\\") ||
                pathLower.Contains("\\.gradle\\") || pathLower.Contains("\\.m2\\"))
                return "DevCache";

            // Temp files
            if (reasonsLower.Contains("temp") || reasonsLower.Contains("cache") ||
                pathLower.Contains("\\temp\\") || pathLower.Contains("\\cache\\") ||
                Path.GetExtension(path).ToLowerInvariant() is ".tmp" or ".temp" or ".bak")
                return "TempFiles";

            // Installers
            if (reasonsLower.Contains("installer") || reasonsLower.Contains("download") ||
                pathLower.Contains("\\downloads\\") ||
                Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".msi" or ".zip" or ".rar")
                return "Installers";

            // Logs
            if (reasonsLower.Contains("log") || pathLower.Contains("\\logs\\") ||
                Path.GetExtension(path).ToLowerInvariant() is ".log" or ".etl")
                return "Logs";

            // System actions (Windows Update, Error Reporting, etc.)
            if (reasonsLower.Contains("windows update") || reasonsLower.Contains("error reporting") ||
                reasonsLower.Contains("diagnostics") || pathLower.Contains("\\wer\\") ||
                pathLower.Contains("\\diagnosis\\"))
                return "SystemActions";

            return "Other";
        }

        private bool CanExecuteCleanGroup(FindingGroup? group)
        {
            return group != null && !group.IsProtected && group.TotalItems > 0;
        }

        private bool CanExecuteCleanSubgroup(FindingSubgroup? subgroup)
        {
            return subgroup != null && subgroup.Items.Any();
        }

        private async Task ExecuteCleanGroupSafeAsync(FindingGroup? group)
        {
            if (group == null) return;

            var safeItems = group.Subgroups
                .SelectMany(s => s.Items)
                .Where(i => i.Classification == SafetyClassification.Safe)
                .ToList();

            if (!safeItems.Any())
            {
                MessageBox.Show(
                    "No safe items found in this group.",
                    "No Safe Items",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var totalSize = safeItems.Sum(i => i.SizeBytes);
            var result = MessageBox.Show(
                $"Clean Safe Items from {group.Name}?\n\n" +
                $"This will clean {safeItems.Count} safe items ({FormatSize(totalSize)}).\n\n" +
                $"Breakdown by category:\n" +
                string.Join("\n", group.Subgroups
                    .Where(s => s.Items.Any(i => i.Classification == SafetyClassification.Safe))
                    .Select(s => $"• {s.CategoryName}: {s.Items.Count(i => i.Classification == SafetyClassification.Safe)} items")) +
                $"\n\nAll items will be moved to quarantine and can be restored if needed.",
                "Confirm Safe Cleanup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            await CleanItemsAsync(safeItems, $"{group.Name} (Safe Only)");
        }

        private async Task ExecuteCleanGroupAllAsync(FindingGroup? group)
        {
            if (group == null) return;

            var allItems = group.Subgroups.SelectMany(s => s.Items).ToList();
            var safeCount = allItems.Count(i => i.Classification == SafetyClassification.Safe);
            var cautionCount = allItems.Count(i => i.Classification == SafetyClassification.Caution);

            var result = MessageBox.Show(
                $"⚠️ Clean ALL Items from {group.Name}?\n\n" +
                $"Total items: {allItems.Count}\n" +
                $"• Safe: {safeCount}\n" +
                $"• Caution: {cautionCount}\n" +
                $"Total size: {FormatSize(allItems.Sum(i => i.SizeBytes))}\n\n" +
                $"Caution items may include important data!\n" +
                $"All items will be moved to quarantine.\n\n" +
                "Are you sure you want to proceed?",
                "⚠️ Confirm All Items Cleanup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            // Second confirmation for caution items
            if (cautionCount > 0)
            {
                var confirm2 = MessageBox.Show(
                    $"Final Confirmation:\n\n" +
                    $"You are about to clean {cautionCount} CAUTION items.\n" +
                    $"These items may be important and could affect applications.\n\n" +
                    "Continue?",
                    "⚠️ Final Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm2 != MessageBoxResult.Yes)
                    return;
            }

            await CleanItemsAsync(allItems, $"{group.Name} (All Items)");
        }

        private async Task ExecuteCleanSubgroupAsync(FindingSubgroup? subgroup)
        {
            if (subgroup == null) return;

            var safeCount = subgroup.Items.Count(i => i.Classification == SafetyClassification.Safe);
            var cautionCount = subgroup.Items.Count(i => i.Classification == SafetyClassification.Caution);
            var totalSize = subgroup.Items.Sum(i => i.SizeBytes);

            var message = cautionCount > 0
                ? $"⚠️ Clean {subgroup.CategoryName}?\n\n" +
                  $"Total items: {subgroup.Items.Count}\n" +
                  $"• Safe: {safeCount}\n" +
                  $"• Caution: {cautionCount}\n" +
                  $"Total size: {FormatSize(totalSize)}\n\n" +
                  $"This includes {cautionCount} caution items that may be important.\n" +
                  "Continue?"
                : $"Clean {subgroup.CategoryName}?\n\n" +
                  $"Items: {subgroup.Items.Count} (all safe)\n" +
                  $"Size: {FormatSize(totalSize)}\n\n" +
                  "All items will be moved to quarantine.";

            var result = MessageBox.Show(
                message,
                cautionCount > 0 ? "⚠️ Confirm Cleanup" : "Confirm Cleanup",
                MessageBoxButton.YesNo,
                cautionCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            await CleanItemsAsync(subgroup.Items.ToList(), subgroup.CategoryName);
        }

        private async Task CleanItemsAsync(List<FindingItem> items, string groupName)
        {
            try
            {
                StatusText = $"Cleaning {items.Count} items from {groupName}...";

                // Group items by their source bucket
                var bucketGroups = items.GroupBy(i => i.SourceReference as CleanupBucket);

                int totalSuccess = 0;
                long totalReclaimed = 0;
                var errors = new List<string>();

                foreach (var bucketGroup in bucketGroups)
                {
                    var bucket = bucketGroup.Key;
                    if (bucket == null) continue;

                    // Get only the items from this bucket that we want to clean
                    var itemsToClean = bucketGroup.Select(i => 
                        bucket.Items.FirstOrDefault(bi => bi.Path == i.Path))
                        .Where(bi => bi != null)
                        .ToList();

                    if (!itemsToClean.Any()) continue;

                    // Create a temporary bucket with only these items
                    var tempBucket = new CleanupBucket
                    {
                        Name = bucket.Name,
                        Description = bucket.Description,
                        Items = itemsToClean!,
                        Status = bucket.Status
                    };

                    try
                    {
                        var actions = await _bucketsService.CleanBucketAsync(
                            tempBucket,
                            new Progress<string>(s => StatusText = s),
                            CancellationToken.None);

                        await _reportService.LogCleanupActionsAsync($"{groupName} - {bucket.Name}", actions);

                        var successCount = actions.Count(a => a.Success);
                        var reclaimedSize = actions.Where(a => a.Success).Sum(a => a.Size);

                        totalSuccess += successCount;
                        totalReclaimed += reclaimedSize;

                        var failed = actions.Where(a => !a.Success).ToList();
                        if (failed.Any())
                        {
                            errors.AddRange(failed.Select(f => $"{f.SourcePath}: {f.ErrorMessage}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{bucket.Name}: {ex.Message}");
                    }
                }

                // Show results
                if (errors.Any())
                {
                    var errorSummary = string.Join("\n", errors.Take(5));
                    MessageBox.Show(
                        $"Cleanup completed with some errors!\n\n" +
                        $"Successfully cleaned: {totalSuccess} items\n" +
                        $"Space reclaimed: {FormatSize(totalReclaimed)}\n" +
                        $"Failed: {errors.Count} items\n\n" +
                        $"First errors:\n{errorSummary}" +
                        (errors.Count > 5 ? $"\n...and {errors.Count - 5} more" : ""),
                        "Cleanup Completed with Errors",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show(
                        $"Cleanup Complete!\n\n" +
                        $"Cleaned: {totalSuccess} items\n" +
                        $"Space reclaimed: {FormatSize(totalReclaimed)}\n\n" +
                        "All items moved to quarantine.",
                        "Cleanup Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                // Refresh buckets and groups
                await ExecuteScanBucketsAsync();

                StatusText = $"Cleaned {totalSuccess} items from {groupName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error during cleanup:\n{ex.Message}",
                    "Cleanup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText = "Cleanup failed";
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
