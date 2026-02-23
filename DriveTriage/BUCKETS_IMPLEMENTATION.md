# Cleanup Buckets Implementation

## Overview
Implemented a comprehensive cleanup bucket system that identifies reclaimable disk space and provides safe cleanup actions by moving files/folders to a quarantine location.

## Features Implemented

### 1. Cleanup Buckets (BucketsService.cs)
Three intelligent cleanup buckets:

#### **User Temp Files**
- Scans the user's temp folder (%TEMP%)
- Identifies unlocked files and folders that can be safely removed
- Calculates total reclaimable space

#### **Old Installers**
- Scans the Downloads folder
- Finds installer files (.exe, .msi, .msix, .zip, .7z, .rar, .tar, .gz, .iso)
- Only includes files older than 30 days
- Helps remove installers no longer needed

#### **node_modules Folders**
- Intelligently searches common development directories:
  - User profile root
  - source/, repos/, projects/, dev/, Documents/
- Finds all node_modules folders (max depth: 5)
- These can be restored with `npm install`
- Typically contains hundreds of MB or even GB per project

### 2. Quarantine System
- **Preserves directory structure**: Files are moved maintaining their relative paths
- **Safe operation**: Move instead of delete, allows restoration
- **Quarantine location**: `%LocalAppData%\DriveTriage\Quarantine`
- **Logging**: All actions are logged for audit trail

### 3. Logging & Reporting (ReportService.cs)
- Creates detailed logs for each cleanup operation
- **Text logs (.log)**: Human-readable format with summary and details
- **JSON logs (.json)**: Machine-readable for automation
- **Log location**: `%LocalAppData%\DriveTriage\Logs`
- Tracks:
  - Source paths
  - Quarantine destination
  - File sizes
  - Timestamps
  - Success/failure status
  - Error messages

### 4. User Interface (MainWindow.xaml)
Enhanced Recommendations tab with:
- **Scan for Cleanup** button to discover reclaimable space
- Visual cards for each bucket showing:
  - Name and description
  - Number of items found
  - Total reclaimable size (color-coded green)
  - Status messages
  - **Clean** button for each bucket
- Empty state when no scan has been performed
- Confirmation dialogs before cleanup
- Success/error notifications

### 5. View Model Integration (MainViewModel.cs)
- `ScanBucketsCommand`: Initiates bucket scanning
- `CleanBucketCommand`: Executes cleanup for a specific bucket
- `CancelBucketsCommand`: Cancels ongoing bucket scan
- Observable collection of buckets with real-time updates
- Progress reporting and status messages

### 6. Data Models (Models.cs)
- `CleanupBucket`: Represents a cleanup category
- `CleanupItem`: Individual file/folder to clean
- `CleanupAction`: Log entry for each cleanup operation
- Status enums for tracking progress

## Safety Features
1. **Confirmation dialogs**: User must confirm before cleanup
2. **Quarantine instead of delete**: Files can be restored
3. **Comprehensive logging**: Full audit trail of all actions
4. **Error handling**: Gracefully handles access denied, file not found, etc.
5. **Locked file detection**: Skips files currently in use
6. **Reparse point detection**: Avoids symbolic links to prevent issues

## Usage Flow
1. Switch to **Recommendations** tab
2. Click **Scan for Cleanup** button
3. Wait for scan to complete (shows progress)
4. Review discovered buckets with reclaimable space
5. Click **Clean** on desired bucket
6. Confirm the action
7. Items are moved to quarantine
8. View log files for detailed records

## Extensibility
The bucket system uses an interface-based design (`IBucketRule`), making it easy to add new cleanup rules:
- Create a new class implementing `IBucketRule`
- Add to `_bucketRules` list in `BucketsService` constructor
- Automatically appears in the UI

## File Structure
```
Services/
  ├── BucketsService.cs      - Core bucket logic & rules
  ├── ReportService.cs       - Logging & reporting
  
ViewModels/
  ├── Models.cs              - Data models & enums
  ├── MainViewModel.cs       - UI integration
  
Utils/
  └── AsyncRelayCommand.cs   - Generic command support
  
MainWindow.xaml              - UI layout
```
