using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace DriveTriage.Utils
{
    public class DriveInfoConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DriveInfo drive)
            {
                var totalSize = FormatSize(drive.TotalSize);
                var freeSpace = FormatSize(drive.AvailableFreeSpace);
                var usedPercent = drive.TotalSize > 0 
                    ? ((drive.TotalSize - drive.AvailableFreeSpace) * 100.0 / drive.TotalSize)
                    : 0;

                return $"{drive.Name} - {totalSize} ({usedPercent:F1}% used, {freeSpace} free)";
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
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
