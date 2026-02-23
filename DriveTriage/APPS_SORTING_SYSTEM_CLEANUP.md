# Apps Sorting & System Cleanup Features

## Overview
Added sorting functionality to the Apps tab and implemented Recycle Bin and Quarantine cleanup features in the Restore tab.

---

## Feature 1: Apps Sorting

### New Sorting Buttons in Apps Tab

Added three sorting buttons to quickly organize the applications list:

#### **📅 Sort by Date**
- Sorts applications by install date (newest first)
- Apps without install dates appear last
- Status: "Sorted by install date (newest first)"

#### **📊 Sort by Size**
- Sorts applications by estimated size (largest first)
- Helps identify space-consuming apps quickly
- Status: "Sorted by size (largest first)"

#### **🔤 Sort by Name**
- Sorts applications alphabetically (A-Z)
- Default/natural ordering
- Status: "Sorted alphabetically (A-Z)"

### Implementation Details

**MainViewModel.cs:**
```csharp
// New Commands
public AsyncRelayCommand SortAppsByDateCommand { get; }
public AsyncRelayCommand SortAppsBySizeCommand { get; }
public AsyncRelayCommand SortAppsByNameCommand { get; }

// Sort by Install Date (Newest First)
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
                InstalledApps.Add(app);
            FilterApps();
        });

        StatusText = "Sorted by install date (newest first)";
    });
}

// Sort by Size (Largest First)
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
                InstalledApps.Add(app);
            FilterApps();
        });

        StatusText = "Sorted by size (largest first)";
    });
}

// Sort by Name (A-Z)
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
                InstalledApps.Add(app);
            FilterApps();
        });

        StatusText = "Sorted alphabetically (A-Z)";
    });
}
```

**MainWindow.xaml:**
```xaml
<StackPanel Orientation="Horizontal">
    <Button Content="Scan Apps" .../>
    <Button Content="Cancel" .../>
    <Separator/>
    <TextBlock Text="Sort by:"/>
    <Button Content="📅 Date" Command="{Binding SortAppsByDateCommand}"
            ToolTip="Sort by Install Date (Newest First)"/>
    <Button Content="📊 Size" Command="{Binding SortAppsBySizeCommand}"
            ToolTip="Sort by Size (Largest First)"/>
    <Button Content="🔤 Name" Command="{Binding SortAppsByNameCommand}"
            ToolTip="Sort by Name (A-Z)"/>
</StackPanel>
```

### Key Features
✅ **One-click sorting** - No need to click column headers
✅ **Visual indicators** - Emoji icons for easy identification
✅ **Tooltips** - Hover descriptions for each sort option
✅ **Preserves search** - Sorting works with filtered results
✅ **Status feedback** - Shows which sort is active
✅ **Thread-safe** - Uses Dispatcher for UI updates

---

## Feature 2: System Cleanup (Recycle Bin & Quarantine)

### New System Cleanup Section in Restore Tab

Added a prominent cleanup section at the top of the Restore tab with two cleanup options:

#### **🗑️ Recycle Bin Cleanup**
- Shows item count and total size
- One-click empty with confirmation
- Uses native Windows API (`SHEmptyRecycleBin`)
- Cannot be undone (permanent deletion)

#### **📦 Quarantine Cleanup**
- Shows quarantine folder contents
- Permanently deletes all quarantined files
- Displays location path
- Cannot be undone (makes restore impossible)

### New Service: SystemCleanupService.cs

Complete service for system-level cleanup operations:

**Key Methods:**
1. `EmptyRecycleBinAsync` - Empties Windows Recycle Bin
2. `CleanQuarantineAsync` - Deletes all quarantined files
3. `GetSystemCleanupInfoAsync` - Gets current sizes/counts

**Features:**
- Progress reporting during cleanup
- Calculates sizes before deletion
- Shows reclaimed space after completion
- Handles access errors gracefully
- Cancellation support
- Uses Windows Shell32.dll API for Recycle Bin

### Implementation Details

**SystemCleanupService.cs:**
```csharp
public class SystemCleanupService
{
    private readonly string _quarantinePath;

    public async Task<CleanupResult> EmptyRecycleBinAsync(
        IProgress<string> statusUpdate,
        CancellationToken cancellationToken)
    {
        // Calculate size
        var recycleBinSize = GetRecycleBinSize();
        
        // Empty using Windows API
        EmptyRecycleBin();
        
        // Return results
        return new CleanupResult
        {
            SpaceReclaimed = recycleBinSize,
            Success = true,
            Message = $"Reclaimed {FormatSize(recycleBinSize)}"
        };
    }

    public async Task<CleanupResult> CleanQuarantineAsync(
        IProgress<string> statusUpdate,
        CancellationToken cancellationToken)
    {
        // Calculate size
        var quarantineSize = CalculateDirectorySize(_quarantinePath);
        
        // Delete all files and folders
        foreach (var file in Directory.GetFiles(_quarantinePath, "*", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }
        
        // Return results
        return new CleanupResult
        {
            SpaceReclaimed = quarantineSize,
            ItemsDeleted = itemCount,
            Success = true
        };
    }

    public async Task<SystemCleanupInfo> GetSystemCleanupInfoAsync()
    {
        return new SystemCleanupInfo
        {
            RecycleBinSize = GetRecycleBinSize(),
            RecycleBinItemCount = GetRecycleBinItemCount(),
            QuarantineSize = CalculateDirectorySize(_quarantinePath),
            QuarantineItemCount = GetFileCount(_quarantinePath),
            QuarantinePath = _quarantinePath
        };
    }

    // Uses Windows Shell32.dll API
    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private void EmptyRecycleBin()
    {
        SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
    }
}
```

**MainViewModel.cs:**
```csharp
private readonly SystemCleanupService _systemCleanupService;
private SystemCleanupInfo? _systemCleanupInfo;

public SystemCleanupInfo? SystemCleanupInfo { get; set; }
public AsyncRelayCommand LoadSystemCleanupInfoCommand { get; }
public AsyncRelayCommand EmptyRecycleBinCommand { get; }
public AsyncRelayCommand CleanQuarantineCommand { get; }

private async Task ExecuteLoadSystemCleanupInfoAsync()
{
    StatusText = "Loading system cleanup information...";
    SystemCleanupInfo = await _systemCleanupService.GetSystemCleanupInfoAsync();
    StatusText = "System cleanup information loaded";
}

private async Task ExecuteEmptyRecycleBinAsync()
{
    // Show confirmation
    var result = MessageBox.Show(
        $"Empty Recycle Bin?\n\n" +
        $"Items: {SystemCleanupInfo.RecycleBinItemCount}\n" +
        $"Size: {SystemCleanupInfo.FormattedRecycleBinSize}\n\n" +
        $"⚠️ WARNING: Cannot be undone!",
        "Confirm Empty Recycle Bin",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);

    if (result != MessageBoxResult.Yes) return;

    // Execute cleanup
    var cleanupResult = await _systemCleanupService.EmptyRecycleBinAsync(...);
    
    // Show results
    MessageBox.Show(
        $"Recycle Bin emptied!\n" +
        $"Reclaimed: {cleanupResult.FormattedSpaceReclaimed}",
        "Cleanup Complete");
    
    // Refresh info
    await ExecuteLoadSystemCleanupInfoAsync();
}

private async Task ExecuteCleanQuarantineAsync()
{
    // Similar to EmptyRecycleBinAsync but for quarantine
    // Permanently deletes all quarantined files
}
```

**MainWindow.xaml:**
```xaml
<!-- System Cleanup Section (Orange Border) -->
<Border BorderBrush="#FF9800" BorderThickness="2" Background="#FFF3E0">
    <Grid>
        <!-- Recycle Bin Card -->
        <Border Background="White">
            <StackPanel>
                <TextBlock Text="🗑️ Recycle Bin"/>
                <TextBlock Text="{Binding SystemCleanupInfo.RecycleBinItemCount}"/>
                <TextBlock Text="{Binding SystemCleanupInfo.FormattedRecycleBinSize}"/>
                <Button Content="Empty Recycle Bin" 
                        Command="{Binding EmptyRecycleBinCommand}"
                        Background="#FF5722"/>
                <TextBlock Text="⚠️ Cannot be undone!"/>
            </StackPanel>
        </Border>

        <!-- Quarantine Card -->
        <Border Background="White">
            <StackPanel>
                <TextBlock Text="📦 Quarantine Folder"/>
                <TextBlock Text="{Binding SystemCleanupInfo.QuarantineItemCount}"/>
                <TextBlock Text="{Binding SystemCleanupInfo.FormattedQuarantineSize}"/>
                <Button Content="Clean Quarantine" 
                        Command="{Binding CleanQuarantineCommand}"
                        Background="#FF9800"/>
                <TextBlock Text="⚠️ Permanently deletes files!"/>
            </StackPanel>
        </Border>
    </Grid>
</Border>
```

### Visual Design

**Restore Tab Layout:**
```
┌─────────────────────────────────────────────────────────┐
│ [Load Sessions] [Refresh Cleanup Info]  Status: Ready  │
├─────────────────────────────────────────────────────────┤
│ ┏━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓ │
│ ┃ 🗑️ Recycle Bin          📦 Quarantine Folder     ┃ │
│ ┃ Items: 145              Items: 23                 ┃ │
│ ┃ Size: 2.5 GB            Size: 1.2 GB              ┃ │
│ ┃ [Empty Recycle Bin]     [Clean Quarantine]       ┃ │
│ ┃ ⚠️ Cannot be undone!    ⚠️ Permanently deletes!  ┃ │
│ ┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛ │
├─────────────────────────────────────────────────────────┤
│ Restorable Cleanup Sessions                             │
│ ┌─────────────────────────────────────────────────┐   │
│ │ User Temp Files                    [Restore All]│   │
│ │ Date: 2024-01-15 14:30                          │   │
│ │ Restorable Items: 45  Original Size: 500 MB    │   │
│ └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

**Apps Tab with Sorting:**
```
┌─────────────────────────────────────────────────────────┐
│ ℹ️ Showing applications installed on: C:\              │
├─────────────────────────────────────────────────────────┤
│ [Scan Apps] [Cancel] | Sort by: [📅Date][📊Size][🔤Name]│
│ Search: [____________]                                  │
├─────────────────────────────────────────────────────────┤
│ Application Name     │ Publisher │ Size   │ Date │ ... │
│ Visual Studio 2024   │ Microsoft │ 5.2 GB │ ...  │ ... │
│ Chrome               │ Google    │ 1.1 GB │ ...  │ ... │
└─────────────────────────────────────────────────────────┘
```

### Color Scheme

**System Cleanup Section:**
- **Border**: Orange (#FF9800) - Indicates caution
- **Background**: Light orange (#FFF3E0) - Warning color
- **Recycle Bin Button**: Red (#FF5722) - Danger action
- **Quarantine Button**: Orange (#FF9800) - Caution action

### Usage Flow

#### Apps Sorting
```
1. Click "Scan Apps" → Apps load
2. Click "📊 Size" → Apps sorted by size
3. See largest apps first
4. Decide which to uninstall
```

#### Recycle Bin Cleanup
```
1. Navigate to Restore tab
2. Click "Refresh Cleanup Info"
3. See Recycle Bin size (e.g., 2.5 GB, 145 items)
4. Click "Empty Recycle Bin"
5. Confirm warning dialog
6. Recycle Bin emptied
7. Space reclaimed shown
```

#### Quarantine Cleanup
```
1. Navigate to Restore tab
2. Click "Refresh Cleanup Info"
3. See Quarantine size (e.g., 1.2 GB, 23 items)
4. Click "Clean Quarantine"
5. Confirm warning dialog (files cannot be restored!)
6. Quarantine deleted
7. Space reclaimed shown
```

## Benefits

### Apps Sorting
✅ **Quick identification** of newest/largest/specific apps
✅ **Better cleanup decisions** - see what's using most space
✅ **Faster navigation** - alphabetical sorting for known apps
✅ **Visual feedback** - emoji icons and tooltips
✅ **Maintains search** - sorting works with filtered results

### System Cleanup
✅ **Reclaim more space** - Empty Recycle Bin to free up hidden space
✅ **Clear quarantine** - Remove old quarantined files permanently
✅ **Transparency** - Shows exact sizes before deletion
✅ **Safety warnings** - Clear indication that actions are permanent
✅ **Visual separation** - Orange border indicates caution area
✅ **Progress feedback** - Status updates during cleanup

## Safety Features

### Recycle Bin
- ⚠️ Warning icon and text
- Confirmation dialog with item count and size
- "Cannot be undone" message
- Shows space that will be reclaimed

### Quarantine
- ⚠️ Warning icon and text
- Confirmation dialog with full path
- "Permanently deletes files" message
- Cannot restore after deletion
- Shows quarantine location

### Both Operations
- Try/catch error handling
- Progress reporting
- Success/failure messages
- Automatic info refresh after completion
- Cancellation support

## Example Scenarios

### Scenario 1: Find and Uninstall Large Apps
```
1. Select C:\
2. Click "Scan Apps"
3. Click "📊 Size" (sort by size)
4. See: Visual Studio (5.2 GB) at top
5. Click "Uninstall" on unused apps
```

### Scenario 2: Reclaim Hidden Space
```
User: "I cleaned up but still low on space"
1. Navigate to Restore tab
2. Click "Refresh Cleanup Info"
3. See: Recycle Bin has 2.5 GB
4. Click "Empty Recycle Bin"
5. Confirm → 2.5 GB reclaimed!
```

### Scenario 3: Clear Old Quarantine
```
After multiple cleanup sessions:
1. Navigate to Restore tab
2. See: Quarantine has 1.2 GB (old files)
3. Don't need to restore anymore
4. Click "Clean Quarantine"
5. Confirm → 1.2 GB reclaimed
```

## Technical Implementation

### Thread Safety
All operations use proper thread safety:
- Background calculations with `Task.Run()`
- UI updates via `Application.Current.Dispatcher.Invoke()`
- No cross-thread collection modifications

### Progress Reporting
- `IProgress<string>` for status updates
- Shows current operation
- Updates during long operations (every 100 items)
- Final summary with results

### Error Handling
- Try/catch blocks for each operation
- Graceful handling of access denied errors
- Continues even if some items fail
- Shows user-friendly error messages
- Logs error details

### Recycle Bin Access
Uses Windows API to:
- Get Recycle Bin size from all drives
- Count items in `$Recycle.Bin` folders
- Empty using `SHEmptyRecycleBin` API
- Handle per-user SID folders

### Quarantine Access
- Calculates recursive directory size
- Counts all files (all levels)
- Deletes files then directories
- Preserves quarantine root folder

## New Classes

### CleanupResult
```csharp
public class CleanupResult
{
    public bool Success { get; set; }
    public string OperationType { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public long SizeBefore { get; set; }
    public long SizeAfter { get; set; }
    public long SpaceReclaimed { get; set; }
    public int ItemsDeleted { get; set; }
    public string Message { get; set; }
    public string FormattedSpaceReclaimed { get; }
    public string Duration { get; }
}
```

### SystemCleanupInfo
```csharp
public class SystemCleanupInfo
{
    public long RecycleBinSize { get; set; }
    public int RecycleBinItemCount { get; set; }
    public long QuarantineSize { get; set; }
    public int QuarantineItemCount { get; set; }
    public string QuarantinePath { get; set; }
    public string FormattedRecycleBinSize { get; }
    public string FormattedQuarantineSize { get; }
}
```

## Testing Checklist

### Apps Sorting
- [x] Date sorting works (newest first)
- [x] Size sorting works (largest first)
- [x] Name sorting works (A-Z)
- [x] Search still works after sorting
- [x] Status text updates correctly
- [x] Tooltips show on hover

### Recycle Bin
- [x] Shows correct size and count
- [x] Confirmation dialog appears
- [x] Actually empties Recycle Bin
- [x] Shows reclaimed space
- [x] Refreshes info after completion
- [x] Handles errors gracefully

### Quarantine
- [x] Shows correct size and count
- [x] Shows quarantine path
- [x] Confirmation dialog with warnings
- [x] Deletes all files
- [x] Shows items deleted
- [x] Refreshes info after completion
- [x] Handles access errors

## User Benefits

### Before Implementation
- ❌ No way to sort apps by date or size
- ❌ Had to click column headers multiple times
- ❌ No Recycle Bin cleanup in app
- ❌ No way to clear old quarantine files
- ❌ Hidden space not easily reclaimable

### After Implementation
✅ One-click sorting by date, size, or name
✅ Quick identification of space-wasting apps
✅ Recycle Bin cleanup integrated
✅ Quarantine management built-in
✅ Clear warnings for permanent actions
✅ Progress feedback during operations
✅ Automatic info refresh
✅ More comprehensive cleanup solution

## Summary

**Apps Sorting**: Makes it easy to find apps by date (find old/new apps), size (find space hogs), or name (find specific apps quickly).

**System Cleanup**: Adds powerful cleanup options to reclaim space from Windows Recycle Bin and the app's own Quarantine folder, with clear warnings and progress feedback.

Both features enhance the app's usefulness as a comprehensive disk space management tool! 🎉
