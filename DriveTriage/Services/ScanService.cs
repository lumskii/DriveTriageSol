using System.Collections.Concurrent;
using System.IO;
using DriveTriage.ViewModels;

namespace DriveTriage.Services
{
    public class ScanService
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private const int DefaultTopN = 100;

        public bool IsScanning { get; private set; }

        public async Task ScanAsync(
            IProgress<double> progress,
            IProgress<string> statusUpdate,
            Action<List<FileSystemItem>> onFilesFound,
            Action<List<FileSystemItem>> onFoldersFound)
        {
            await ScanPathAsync(
                rootPath: null,
                topN: DefaultTopN,
                progress: progress,
                statusUpdate: statusUpdate,
                onFilesFound: onFilesFound,
                onFoldersFound: onFoldersFound);
        }

        public async Task ScanPathAsync(
            string? rootPath,
            int topN,
            IProgress<double> progress,
            IProgress<string> statusUpdate,
            Action<List<FileSystemItem>> onFilesFound,
            Action<List<FileSystemItem>> onFoldersFound)
        {
            IsScanning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await Task.Run(() =>
                {
                    var scanStats = new ScanStatistics();
                    var topFiles = new TopNTracker<FileMetadata>(topN);
                    var folderSizes = new ConcurrentDictionary<string, FolderInfo>();

                    if (string.IsNullOrEmpty(rootPath))
                    {
                        var drives = DriveInfo.GetDrives()
                            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                            .ToList();

                        int driveCount = 0;
                        foreach (var drive in drives)
                        {
                            _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                            statusUpdate.Report($"Scanning {drive.Name}...");

                            try
                            {
                                ScanDirectoryRecursive(
                                    drive.RootDirectory.FullName,
                                    topFiles,
                                    folderSizes,
                                    scanStats,
                                    statusUpdate,
                                    _cancellationTokenSource.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }

                            driveCount++;
                            progress.Report((double)driveCount / drives.Count * 100);
                        }
                    }
                    else
                    {
                        statusUpdate.Report($"Scanning {rootPath}...");
                        ScanDirectoryRecursive(
                            rootPath,
                            topFiles,
                            folderSizes,
                            scanStats,
                            statusUpdate,
                            _cancellationTokenSource.Token);
                        progress.Report(100);
                    }

                    statusUpdate.Report($"Processed {scanStats.FilesScanned:N0} files in {scanStats.FoldersScanned:N0} folders");

                    var largestFiles = topFiles.GetTop()
                        .Select(f => new FileSystemItem
                        {
                            Path = f.Path,
                            Size = FormatSize(f.Size),
                            LastModified = f.LastModified.ToString("yyyy-MM-dd HH:mm:ss")
                        })
                        .ToList();

                    var largestFolders = folderSizes.Values
                        .OrderByDescending(f => f.TotalSize)
                        .Take(topN)
                        .Select(f => new FileSystemItem
                        {
                            Path = f.Path,
                            Size = FormatSize(f.TotalSize),
                            LastModified = f.LastModified.ToString("yyyy-MM-dd HH:mm:ss")
                        })
                        .ToList();

                    onFilesFound(largestFiles);
                    onFoldersFound(largestFolders);

                }, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                statusUpdate.Report("Scan cancelled");
            }
            finally
            {
                IsScanning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private long ScanDirectoryRecursive(
            string path,
            TopNTracker<FileMetadata> topFiles,
            ConcurrentDictionary<string, FolderInfo> folderSizes,
            ScanStatistics stats,
            IProgress<string> statusUpdate,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            long totalSize = 0;
            DateTime lastModified = DateTime.MinValue;

            try
            {
                var dirInfo = new DirectoryInfo(path);

                if (IsReparsePoint(dirInfo))
                {
                    return 0;
                }

                FileInfo[] files;
                try
                {
                    files = dirInfo.GetFiles();
                }
                catch (UnauthorizedAccessException)
                {
                    return 0;
                }
                catch (DirectoryNotFoundException)
                {
                    return 0;
                }

                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        var fileSize = file.Length;
                        var fileModified = file.LastWriteTime;

                        topFiles.Add(new FileMetadata
                        {
                            Path = file.FullName,
                            Size = fileSize,
                            LastModified = fileModified
                        });

                        totalSize += fileSize;
                        if (fileModified > lastModified)
                        {
                            lastModified = fileModified;
                        }

                        stats.IncrementFiles();

                        if (stats.FilesScanned % 1000 == 0)
                        {
                            statusUpdate.Report($"Scanning... ({stats.FilesScanned:N0} files, {stats.FoldersScanned:N0} folders)");
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (FileNotFoundException) { }
                    catch (IOException) { }
                }

                DirectoryInfo[] subDirs;
                try
                {
                    subDirs = dirInfo.GetDirectories();
                }
                catch (UnauthorizedAccessException)
                {
                    return totalSize;
                }
                catch (DirectoryNotFoundException)
                {
                    return totalSize;
                }

                foreach (var subDir in subDirs)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        if (!IsReparsePoint(subDir))
                        {
                            var subDirSize = ScanDirectoryRecursive(
                                subDir.FullName,
                                topFiles,
                                folderSizes,
                                stats,
                                statusUpdate,
                                token);

                            totalSize += subDirSize;

                            var subDirModified = subDir.LastWriteTime;
                            if (subDirModified > lastModified)
                            {
                                lastModified = subDirModified;
                            }
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (DirectoryNotFoundException) { }
                    catch (IOException) { }
                }

                folderSizes[path] = new FolderInfo
                {
                    Path = path,
                    TotalSize = totalSize,
                    LastModified = lastModified > DateTime.MinValue ? lastModified : dirInfo.LastWriteTime
                };

                stats.IncrementFolders();
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }

            return totalSize;
        }

        private static bool IsReparsePoint(DirectoryInfo dirInfo)
        {
            return (dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
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

        public async Task CancelAsync()
        {
            _cancellationTokenSource?.Cancel();
            await Task.CompletedTask;
        }

        private class FileMetadata : IComparable<FileMetadata>
        {
            public required string Path { get; init; }
            public required long Size { get; init; }
            public required DateTime LastModified { get; init; }

            public int CompareTo(FileMetadata? other)
            {
                if (other == null) return 1;
                return Size.CompareTo(other.Size);
            }
        }

        private class FolderInfo
        {
            public required string Path { get; init; }
            public required long TotalSize { get; init; }
            public required DateTime LastModified { get; init; }
        }

        private class TopNTracker<T> where T : IComparable<T>
        {
            private readonly int _maxCount;
            private readonly SortedSet<T> _items;
            private readonly object _lock = new();

            public TopNTracker(int maxCount)
            {
                _maxCount = maxCount;
                _items = new SortedSet<T>();
            }

            public void Add(T item)
            {
                lock (_lock)
                {
                    if (_items.Count < _maxCount)
                    {
                        _items.Add(item);
                    }
                    else if (_items.Min != null && item.CompareTo(_items.Min) > 0)
                    {
                        _items.Remove(_items.Min);
                        _items.Add(item);
                    }
                }
            }

            public List<T> GetTop()
            {
                lock (_lock)
                {
                    return _items.Reverse().ToList();
                }
            }
        }

        private class ScanStatistics
        {
            private int _filesScanned;
            private int _foldersScanned;

            public int FilesScanned => _filesScanned;
            public int FoldersScanned => _foldersScanned;

            public void IncrementFiles() => Interlocked.Increment(ref _filesScanned);
            public void IncrementFolders() => Interlocked.Increment(ref _foldersScanned);
        }
    }
}
