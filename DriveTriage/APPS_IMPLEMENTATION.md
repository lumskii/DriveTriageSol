# Installed Applications Enumerator - Implementation

## Overview
Implemented a comprehensive installed applications enumerator that reads Windows registry to discover all installed programs and provides safe uninstall functionality.

## Features

### 1. Registry Enumeration (AppsService.cs)
Scans multiple registry locations to find installed applications:

#### **Registry Keys Scanned**
- **HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall**
  - 64-bit applications on 64-bit systems
  
- **HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall**
  - 32-bit applications on 64-bit systems (WOW64)
  
- **HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall**
  - User-specific installations

#### **Data Extracted**
For each installed application:
- `DisplayName` - Application name
- `Publisher` - Software vendor/publisher
- `DisplayVersion` - Version number
- `InstallDate` - Installation date (yyyyMMdd format)
- `EstimatedSize` - Size in bytes (converted from KB)
- `InstallLocation` - Installation directory
- `UninstallString` - Command to uninstall
- `QuietUninstallString` - Silent uninstall command (if available)
- `RegistryKeyPath` - Full registry path

### 2. Intelligent Filtering
Automatically filters out:
- **System Components**: Hidden OS components (`SystemComponent=1`)
- **Windows Updates**: KB articles and security patches
- **Parent Components**: Sub-entries of other applications
- **Duplicates**: Same app from different registry locations

### 3. Safe Uninstall Execution
- **Command Parsing**: Intelligently handles quoted paths and arguments
- **Special Handling**: Recognizes msiexec commands
- **Admin Elevation**: Requests UAC elevation when needed (`runas` verb)
- **Silent Option**: Uses QuietUninstallString if available
- **Error Handling**: Catches and reports all errors

### 4. Search and Filter Capabilities
- **Text Search**: Filter by DisplayName or Publisher
- **Size Filter**: Find apps larger than specified size
- **Date Filters**: Filter by installation date range
- **Publisher Grouping**: See apps grouped by publisher
- **Largest Apps**: Quick view of space hogs

### 5. User Interface (MainWindow.xaml - Apps Tab)
Enhanced Apps tab featuring:
- **Scan Apps Button**: Discovers all installed applications
- **Search Box**: Real-time filtering as you type
- **DataGrid Display**: Sortable columns with key information
  - Application Name
  - Publisher
  - Version
  - Size (human-readable format)
  - Install Date
  - Uninstall Button (enabled only if uninstall command exists)
- **Alternating Row Colors**: Better readability
- **Empty State**: Helpful message when no apps scanned

### 6. View Model Integration (MainViewModel.cs)
- `ScanAppsCommand`: Initiates registry scan
- `CancelAppsCommand`: Cancels ongoing scan
- `UninstallAppCommand`: Launches uninstaller for selected app
- `AppsSearchText`: Real-time search binding
- Observable collections for apps with automatic UI updates
- Confirmation dialogs before uninstall
- Status notifications after operations

### 7. Data Models (Models.cs)
- **InstalledApp**: Complete application information
  - Formatted properties for display (size, date)
  - All registry data preserved
- **UninstallResult**: Return value for uninstall operations
  - Success/failure status
  - Messages and error details

## Safety Features

1. **Read-Only Registry Access**: Never modifies registry directly
2. **Confirmation Dialogs**: User must confirm before uninstall
3. **UAC Elevation**: Proper admin permission requests
4. **Error Handling**: Graceful handling of:
   - Access denied to registry keys
   - Missing or corrupt registry values
   - Invalid uninstall commands
   - Process execution failures
5. **Non-Destructive**: Only launches official uninstallers
6. **Duplicate Detection**: Prevents showing same app multiple times

## Usage Flow

1. Switch to **Apps** tab
2. Click **Scan Apps** button
3. Wait for registry enumeration (usually < 5 seconds)
4. Browse installed applications in the grid
5. Use search box to filter by name or publisher
6. Sort by any column (click column header)
7. Click **Uninstall** on desired application
8. Confirm the uninstall action
9. Follow the application's uninstaller prompts

## Advanced Features

### FilterApps Method
```csharp
var filtered = FilterApps(
    apps, 
    searchText: "microsoft",
    minSize: 100 * 1024 * 1024,  // 100 MB
    installedBefore: DateTime.Now.AddYears(-1)
);
```

### GetLargestApps Method
```csharp
var bigApps = GetLargestApps(apps, count: 20);
```

### GetAppsByPublisher Method
```csharp
var publisherStats = GetAppsByPublisher(apps);
// Dictionary<string, int> - Publisher name → count
```

## Technical Details

### Registry Value Parsing
- **EstimatedSize**: Stored as DWORD in KB, converted to bytes
- **InstallDate**: Stored as string "yyyyMMdd", parsed to DateTime
- **UninstallString**: May be quoted, parsed to separate executable and arguments

### Command Parsing Logic
Handles various uninstall command formats:
- Quoted paths: `"C:\Program Files\App\uninstall.exe" /S`
- MsiExec: `msiexec.exe /x {GUID}`
- Simple paths: `C:\App\uninst.exe`
- With arguments: `uninstaller.exe /quiet /norestart`

### System Component Detection
Filters out based on:
- `SystemComponent` registry value = 1
- `ParentKeyName` existence (child entries)
- Common update patterns in DisplayName

## Performance
- **Scan Speed**: ~2-5 seconds for typical systems
- **Memory Efficient**: Streaming enumeration, no large data sets in memory
- **Cancellable**: Can interrupt long scans
- **Asynchronous**: Non-blocking UI during scan

## Error Handling
All operations gracefully handle:
- Registry access denied
- Missing registry keys
- Null or invalid values
- Process execution failures
- User cancellation

## File Structure
```
Services/
  └── AppsService.cs        - Core registry enumeration & uninstall logic

ViewModels/
  ├── Models.cs             - InstalledApp & UninstallResult models
  └── MainViewModel.cs      - Apps UI integration

Utils/
  └── StringNotEmptyConverter.cs  - XAML converter for button enable state

MainWindow.xaml             - Apps tab UI (DataGrid)
```

## Future Enhancements
Possible additions:
- Export app list to CSV/JSON
- Batch uninstall
- App usage statistics
- Bloatware detection
- Package manager integration (winget, chocolatey)
- Install date validation
- File size verification vs registry size
