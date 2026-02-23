# Drive-Filtered Apps Feature

## Overview
Modified the Apps tab to only show applications installed on the currently selected drive, instead of showing all system-wide applications.

## Changes Made

### 1. **AppsService.cs** - Drive Filtering Logic

#### Enhanced EnumerateInstalledAppsAsync Method
```csharp
public async Task<List<InstalledApp>> EnumerateInstalledAppsAsync(
    IProgress<string> statusUpdate,
    CancellationToken cancellationToken,
    string? filterDriveLetter = null)  // ✅ New parameter
```

**Drive Filter Logic:**
- Accepts optional `filterDriveLetter` parameter (e.g., "C:\")
- Normalizes to format: `"C:\\"`
- Filters apps by `InstallLocation` registry value
- Only includes apps whose `InstallLocation` starts with the drive letter

**Implementation:**
```csharp
var driveFilter = !string.IsNullOrEmpty(filterDriveLetter) 
    ? filterDriveLetter.TrimEnd('\\', ':') + ":\\" 
    : null;

// During registry scan:
if (driveFilter != null)
{
    if (string.IsNullOrWhiteSpace(installLocation))
        continue; // Skip apps without install location
    
    if (!installLocation.StartsWith(driveFilter, StringComparison.OrdinalIgnoreCase))
        continue; // Skip apps not on the specified drive
}
```

#### Updated ScanRegistryKey Method
```csharp
private void ScanRegistryKey(
    RegistryKey rootKey,
    string keyPath,
    List<InstalledApp> apps,
    CancellationToken cancellationToken,
    string? driveFilter)  // ✅ New parameter
```

**Filter Behavior:**
- **If `driveFilter` is null**: Shows all apps (backward compatible)
- **If `driveFilter` is provided**: Only shows apps on that drive
- **Apps without InstallLocation**: Skipped when filtering by drive

### 2. **MainViewModel.cs** - Drive Selection Integration

#### Enhanced ExecuteScanAppsAsync Method
```csharp
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

    // Pass selected drive to filter apps
    var apps = await _appsService.EnumerateInstalledAppsAsync(
        new Progress<string>(s => StatusText = s),
        _appsCancellationTokenSource.Token,
        SelectedDrive.Name);  // ✅ Now passes drive letter

    StatusText = $"Found {InstalledApps.Count} applications on {SelectedDrive.Name}. Total size: {FormatSize(totalSize)}";
}
```

#### Updated CanExecuteScanApps
```csharp
private bool CanExecuteScanApps()
{
    return !_isScanningApps && SelectedDrive != null;  // ✅ Requires drive selection
}
```

### 3. **MainWindow.xaml** - UI Updates

#### Added Info Banner
Shows which drive is being scanned:
```xaml
<!-- Info Banner -->
<Border Background="#E3F2FD" 
        BorderBrush="#2196F3" 
        BorderThickness="1" 
        CornerRadius="3" 
        Padding="10,5" 
        Margin="0,0,0,10">
    <StackPanel Orientation="Horizontal">
        <TextBlock Text="ℹ️" FontSize="14" Margin="0,0,10,0"/>
        <TextBlock>
            <Run Text="Showing applications installed on:"/>
            <Run Text="{Binding SelectedDrive.Name, Mode=OneWay}" FontWeight="Bold"/>
        </TextBlock>
    </StackPanel>
</Border>
```

#### Updated Empty State
Dynamic message showing the selected drive:
```xaml
<TextBlock.Style>
    <Style TargetType="TextBlock">
        <Setter Property="Visibility" Value="Collapsed"/>
        <Style.Triggers>
            <DataTrigger Binding="{Binding FilteredApps.Count}" Value="0">
                <Setter Property="Text" 
                        Value="{Binding SelectedDrive, StringFormat='Click &quot;Scan Apps&quot; to find applications installed on {0}'}"/>
                <Setter Property="Visibility" Value="Visible"/>
            </DataTrigger>
        </Style.Triggers>
    </Style>
</TextBlock.Style>
```

#### Adjusted Grid Rows
Changed from 3 rows to 4 rows to accommodate the info banner:
- Row 0: Info banner (NEW)
- Row 1: Control panel (Scan Apps / Cancel buttons)
- Row 2: Search box
- Row 3: Apps DataGrid / Empty state

## How It Works

### Registry Filtering Process
```
1. Read all apps from Windows Registry
   ↓
2. For each app, check InstallLocation value
   ↓
3. If InstallLocation starts with "C:\":
   ✅ Include in results
   ↓
4. If InstallLocation starts with "D:\":
   ❌ Skip (user selected C:\)
   ↓
5. If no InstallLocation:
   ❌ Skip when filtering by drive
   ↓
6. Return filtered list
```

### Example Filtering

**User selects: C:\**

| App Name | Install Location | Result |
|----------|------------------|--------|
| Visual Studio Code | `C:\Program Files\...` | ✅ Show |
| Chrome | `C:\Program Files (x86)\...` | ✅ Show |
| Steam Game | `D:\Games\...` | ❌ Hide |
| Dropbox | `C:\Users\...\Dropbox` | ✅ Show |
| Windows Feature | *(empty)* | ❌ Hide |

## Benefits

### Before Change
- ❌ Shows all 200+ apps from entire system
- ❌ No way to see which drive apps are on
- ❌ Harder to find drive-specific apps
- ❌ Not consistent with Scan tab behavior

### After Change
- ✅ Shows only apps on selected drive
- ✅ Clear visual indicator of which drive
- ✅ Consistent with Scan tab (drive-specific)
- ✅ Easier to identify what's taking space on specific drives
- ✅ More focused uninstall candidates
- ✅ Better for multi-drive systems

## Visual Changes

### Apps Tab Header Area
```
┌─────────────────────────────────────────────┐
│ ℹ️ Showing applications installed on: C:\  │ ← NEW info banner
├─────────────────────────────────────────────┤
│ [Scan Apps]  [Cancel]  Status: Ready       │
│ Search: [____________]                      │
├─────────────────────────────────────────────┤
│ Application Name │ Publisher │ Size │ ...  │
│ Visual Studio    │ Microsoft │ 5 GB │ ...  │
│ Chrome           │ Google    │ 200 MB│ ...  │
└─────────────────────────────────────────────┘
```

### Empty State (Before Scan)
```
Click "Scan Apps" to find applications installed on C:\
```

### Status Messages
- **Before**: "Found 215 installed applications"
- **After**: "Found 47 applications on C:\. Total size: 15.2 GB"

## Technical Details

### Drive Letter Normalization
```csharp
var driveFilter = filterDriveLetter.TrimEnd('\\', ':') + ":\\";

// Input: "C:\"  → Output: "C:\"
// Input: "C:"   → Output: "C:\"
// Input: "C"    → Output: "C:\"
```

### Case-Insensitive Matching
```csharp
installLocation.StartsWith(driveFilter, StringComparison.OrdinalIgnoreCase)

// Matches:
// - "C:\Program Files\..."
// - "c:\program files\..."
// - "C:\PROGRAM FILES\..."
```

### Apps Without InstallLocation
Many apps don't have an `InstallLocation` registry value:
- Windows Store apps
- System components
- Portable apps
- Some older installers

**Behavior**: These are **excluded** when filtering by drive (conservative approach - only show apps we can definitively place on the drive).

## Testing Scenarios

### Scenario 1: Single Drive System (C:\ only)
- Select C:\
- Scan Apps
- Shows ~50-150 apps (typical)
- All apps have C:\ as InstallLocation

### Scenario 2: Multi-Drive System
- Select C:\
- Scan Apps → Shows ~50 system apps
- Select D:\
- Scan Apps → Shows ~10 games/programs
- Clear difference in app lists

### Scenario 3: No Apps on Drive
- Select D:\ (data-only drive)
- Scan Apps → "Found 0 applications on D:\"
- Empty state shows appropriate message

### Scenario 4: Search After Filtering
- Select C:\
- Scan Apps (shows 50 apps)
- Search for "Visual" → Filters within those 50 apps
- ✅ Search still works as expected

## Limitations & Considerations

### Registry Limitations
Some apps may not report accurate `InstallLocation`:
- Microsoft Store apps (UWP) - Not in these registry keys
- Portable apps - No registry entry
- System apps - Filtered out anyway
- Older apps - May have empty or incorrect paths

### Multi-Location Apps
Some apps install to multiple drives:
- Main app on C:\
- Data/cache on D:\
- **Behavior**: Shows on C:\ only (based on primary InstallLocation)

### Drive Selection Required
- Apps tab now requires drive selection (like Scan tab)
- Consistent user experience across tabs
- Scan Apps button disabled until drive selected

## Future Enhancements

Possible improvements:
- [ ] Option to show all drives (checkbox toggle)
- [ ] Show drive letter in app list column
- [ ] Detect apps using space on drive (even if installed elsewhere)
- [ ] Multi-drive installation detection
- [ ] Filter by app type (system, user, games)
- [ ] Show app folder sizes (not just registry estimates)

## Summary

✅ Apps tab now filters by selected drive  
✅ Only shows apps with InstallLocation on that drive  
✅ Clear visual indicator of which drive is active  
✅ Consistent behavior with Scan tab  
✅ Better for users with multiple drives  
✅ More targeted cleanup/uninstall decisions  

The Apps tab now provides drive-specific application information, making it easier to understand what's using space on each drive!
