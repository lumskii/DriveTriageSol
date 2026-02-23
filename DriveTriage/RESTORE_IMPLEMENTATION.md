# JSON Report Writer & Restore Functionality

## Overview
Implemented comprehensive JSON-based action logging and file/folder restore capabilities. Every cleanup operation is logged with complete details, and users can restore files from quarantine back to their original locations.

## Features Implemented

### 1. JSON Report Writer (ReportService.cs)

#### Enhanced Logging Structure
Each cleanup action creates:
- **Text Log** (`.log`) - Human-readable summary
- **JSON Log** (`.json`) - Machine-readable structured data
- **Master History** (`actions_history.json`) - Centralized history of all sessions

#### JSON Log Schema
```json
{
  "sessionId": "unique-guid",
  "bucketName": "User Temp Files",
  "timestamp": "2025-01-20T14:30:45",
  "totalActions": 150,
  "successfulActions": 148,
  "failedActions": 2,
  "totalSize": 524288000,
  "actions": [
    {
      "timestamp": "2025-01-20T14:30:46",
      "operation": "MovedToQuarantine",
      "originalPath": "C:\\Temp\\cache.tmp",
      "newPath": "C:\\Users\\...\\DriveTriage\\Quarantine\\Temp\\cache.tmp",
      "sizeBytes": 10485760,
      "success": true,
      "errorMessage": null,
      "isRestored": false
    }
  ]
}
```

#### Data Captured Per Action
| Field | Type | Description |
|-------|------|-------------|
| `timestamp` | DateTime | When the action occurred |
| `operation` | string | Type of action (MovedToQuarantine, Deleted, Skipped) |
| `originalPath` | string | Original file/folder location |
| `newPath` | string | Quarantine location |
| `sizeBytes` | long | Size in bytes |
| `success` | boolean | Whether action succeeded |
| `errorMessage` | string? | Error details if failed |
| `isRestored` | boolean | Whether file has been restored |

### 2. Restore Functionality

#### Core Methods

##### **GetRestorableSessionsAsync()**
- Reads `actions_history.json`
- Filters sessions with restorable items
- Returns only successful moves not yet restored
- Orders by most recent first

##### **RestoreSessionAsync(sessionId, statusUpdate)**
- Restores all items from a specific cleanup session
- Recreates original directory structure
- Moves files/folders back from quarantine
- Updates JSON to mark items as restored
- Reports progress via IProgress<string>
- Returns RestoreResult with success/failure details

##### **RestoreSelectedItemsAsync(sessionId, paths, statusUpdate)**
- Restores specific items from a session
- Allows selective restore (not all-or-nothing)
- Useful for cherry-picking files to restore

#### Restore Process Flow
```
1. User clicks "Load Sessions" button
2. GetRestorableSessionsAsync() reads action history
3. UI displays restorable sessions with counts
4. User selects session and clicks "Restore All"
5. Confirmation dialog shows details
6. RestoreSessionAsync() executes:
   - For each unrestored successful action:
     a. Check if file exists in quarantine
     b. Recreate original directory structure
     c. Move file back to original location
     d. Mark as restored in JSON
   - Save updated action history
7. Success/error summary shown to user
8. Sessions list refreshed
```

### 3. User Interface (Restore Tab)

#### UI Components
- **Load Sessions Button** - Fetches restorable cleanup sessions
- **Session Cards** - Display for each restorable session:
  - Bucket name (cleanup type)
  - Timestamp (when cleanup occurred)
  - Restorable item count
  - Original total size
  - Session ID (for reference)
  - "Restore All" button
- **Empty State** - Guidance when no sessions available
- **Status Updates** - Real-time progress during restore

#### Visual Design
```
┌─────────────────────────────────────────────────┐
│ [Load Sessions]  Status: Ready                  │
├─────────────────────────────────────────────────┤
│ ┌───────────────────────────────────────────┐   │
│ │ User Temp Files        [Restore All]      │   │
│ │ Date: 2025-01-20 14:30:45                 │   │
│ │ Restorable Items: 148  Original Size: 500 MB │
│ │ Session ID: abc-123-def                   │   │
│ └───────────────────────────────────────────┘   │
│                                                  │
│ ┌───────────────────────────────────────────┐   │
│ │ node_modules Folders   [Restore All]      │   │
│ │ Date: 2025-01-19 10:15:30                 │   │
│ │ Restorable Items: 3    Original Size: 1.5 GB │
│ │ Session ID: xyz-789-ghi                   │   │
│ └───────────────────────────────────────────┘   │
└─────────────────────────────────────────────────┘
```

### 4. Data Models (Models.cs)

#### CleanupSession
```csharp
public class CleanupSession
{
    public string SessionId { get; set; }           // Unique identifier
    public string BucketName { get; set; }          // Cleanup type
    public DateTime Timestamp { get; set; }         // When cleanup occurred
    public int TotalActions { get; set; }           // Total items processed
    public int SuccessfulActions { get; set; }      // Successfully moved
    public int FailedActions { get; set; }          // Failed moves
    public long TotalSize { get; set; }             // Total bytes processed
    public List<ActionRecord> Actions { get; set; } // Detailed action list
    
    // Computed properties
    public string FormattedTimestamp { get; }       // Display format
    public string FormattedSize { get; }            // Human-readable size
    public int RestorableCount { get; }             // Count of restorable items
    public bool HasRestorableItems { get; }         // Any items to restore
}
```

#### ActionRecord
```csharp
public class ActionRecord
{
    public DateTime Timestamp { get; set; }         // Action time
    public string Operation { get; set; }           // Action type
    public string OriginalPath { get; set; }        // Source location
    public string NewPath { get; set; }             // Quarantine location
    public long SizeBytes { get; set; }             // File/folder size
    public bool Success { get; set; }               // Success flag
    public string? ErrorMessage { get; set; }       // Error details
    public bool IsRestored { get; set; }            // Restoration flag
    
    // Computed properties
    public string FormattedSize { get; }            // Display size
    public string FileName { get; }                 // Just filename
}
```

#### RestoreResult
```csharp
public class RestoreResult
{
    public bool Success { get; init; }              // Overall success
    public int RestoredCount { get; init; }         // Items restored
    public int FailedCount { get; init; }           // Items failed
    public List<string> Errors { get; init; }       // Error messages
    public string? ErrorMessage { get; init; }      // General error
    
    public string Summary { get; }                  // User-friendly summary
}
```

## Safety Features

### 1. Directory Structure Preservation
- Original folder hierarchy maintained in quarantine
- Restore recreates directory structure
- Example:
  ```
  Original:    C:\Projects\MyApp\src\components\Header.jsx
  Quarantine:  %LocalAppData%\DriveTriage\Quarantine\Projects\MyApp\src\components\Header.jsx
  Restored to: C:\Projects\MyApp\src\components\Header.jsx
  ```

### 2. Conflict Handling
- Checks if original location already has file
- Uses `overwrite: false` to prevent accidental overwrites
- Fails safely if conflict detected
- User notified in error summary

### 3. Verification
- Confirms file exists in quarantine before restore
- Handles both files and folders
- Validates directory creation permissions
- Comprehensive error reporting

### 4. State Tracking
- `isRestored` flag prevents double-restoration
- JSON history preserved permanently
- Can restore same session multiple times (won't re-restore marked items)

### 5. Progress Reporting
- Real-time status updates via IProgress<string>
- Shows current file being restored
- Updates UI continuously during operation

## Usage Examples

### Basic Restore Workflow
```csharp
var reportService = new ReportService();

// 1. Get restorable sessions
var sessions = await reportService.GetRestorableSessionsAsync();

// 2. Display to user (done in UI)
foreach (var session in sessions)
{
    Console.WriteLine($"{session.BucketName}: {session.RestorableCount} items");
}

// 3. Restore selected session
var sessionId = sessions.First().SessionId;
var result = await reportService.RestoreSessionAsync(
    sessionId,
    new Progress<string>(s => Console.WriteLine(s))
);

// 4. Check result
if (result.Success)
{
    Console.WriteLine($"Restored {result.RestoredCount} items");
}
else
{
    Console.WriteLine($"Errors: {string.Join(", ", result.Errors)}");
}
```

### Selective Restore
```csharp
// Restore only specific files
var pathsToRestore = new List<string>
{
    @"C:\Temp\important_file.tmp",
    @"C:\Temp\another_file.tmp"
};

var result = await reportService.RestoreSelectedItemsAsync(
    sessionId,
    pathsToRestore,
    new Progress<string>(s => Console.WriteLine(s))
);
```

### Check Restorable Items
```csharp
var sessions = await reportService.GetRestorableSessionsAsync();

foreach (var session in sessions)
{
    Console.WriteLine($"\nSession: {session.BucketName}");
    Console.WriteLine($"Date: {session.FormattedTimestamp}");
    Console.WriteLine($"Restorable: {session.RestorableCount} items");
    
    foreach (var action in session.Actions.Where(a => !a.IsRestored))
    {
        Console.WriteLine($"  - {action.FileName} ({action.FormattedSize})");
    }
}
```

## File Locations

| Type | Location |
|------|----------|
| Master History | `%LocalAppData%\DriveTriage\Logs\actions_history.json` |
| Session Logs (JSON) | `%LocalAppData%\DriveTriage\Logs\cleanup_*.json` |
| Session Logs (Text) | `%LocalAppData%\DriveTriage\Logs\cleanup_*.log` |
| Quarantine | `%LocalAppData%\DriveTriage\Quarantine\*` |

## JSON History Management

### History Retention
- Keeps last 100 cleanup sessions
- Automatically prunes older sessions
- Ordered by timestamp (newest first)
- Each session ~1-100KB depending on item count

### History Size Estimates
| Items per Session | JSON Size | 100 Sessions Total |
|-------------------|-----------|-------------------|
| 10 items | ~2 KB | ~200 KB |
| 100 items | ~15 KB | ~1.5 MB |
| 1000 items | ~150 KB | ~15 MB |

### Manual Cleanup
If history file grows too large:
```csharp
// Delete old history (user must do manually)
File.Delete(Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DriveTriage", "Logs", "actions_history.json"
));
```

## Error Handling

### Common Scenarios

#### 1. File Already Exists at Original Location
```
Error: File already exists: C:\Temp\file.tmp
Action: Skip restore, report in errors list
```

#### 2. Quarantine File Missing
```
Error: Not found in quarantine: %LocalAppData%\...\file.tmp
Cause: User manually deleted from quarantine
Action: Report as failed, continue with next
```

#### 3. Permission Denied
```
Error: Access denied creating directory: C:\Protected\Folder
Action: Report error, continue with next
```

#### 4. Disk Full
```
Error: IOException - disk full
Action: Stop restore, report partial success
```

### Error Reporting Format
```csharp
RestoreResult
{
    Success = false,
    RestoredCount = 45,
    FailedCount = 3,
    Errors = [
        "File1.tmp: File already exists at original location",
        "File2.tmp: Not found in quarantine",
        "Folder1: Access denied creating directory"
    ]
}
```

## Integration with Cleanup Buckets

### Enhanced BucketsService.CleanBucketAsync()
```csharp
var actions = await bucketsService.CleanBucketAsync(bucket, ...);

// Returns session ID for tracking
var sessionId = await reportService.LogCleanupActionsAsync(
    bucket.Name, 
    actions
);

// User can later restore using this session ID
```

### Cleanup → Restore Cycle
```
1. User performs cleanup (BucketsService)
2. Actions logged with session ID (ReportService)
3. Files moved to quarantine with structure preserved
4. JSON history updated
5. Later: User loads restorable sessions
6. User selects session and restores
7. Files moved back to original locations
8. JSON updated with isRestored = true
```

## Best Practices

### For Users
1. **Review Before Restore**: Check session details before restoring
2. **Verify Disk Space**: Ensure space available at original locations
3. **Close Applications**: Close apps using files before restore
4. **Backup First**: Consider backup before bulk restore
5. **Check Conflicts**: Verify no conflicts at original paths

### For Developers
1. **Atomic Operations**: Each restore is independent
2. **Idempotent**: Can run restore multiple times safely
3. **Fail-Safe**: One failed item doesn't stop others
4. **Logging**: Comprehensive error messages
5. **Progress**: Always report progress for long operations

## Future Enhancements

### Planned Features
- [ ] Selective item restore (checkbox list)
- [ ] Preview restore (show what will happen)
- [ ] Restore to alternate location
- [ ] Scheduled auto-cleanup of old quarantine
- [ ] Restore search/filter
- [ ] Bulk session restore
- [ ] Export/import restore sessions
- [ ] Restore confirmation with file preview

### Advanced Ideas
- [ ] Incremental restore (resume failed restore)
- [ ] Restore undo (re-quarantine restored files)
- [ ] Restore rules (auto-restore certain patterns)
- [ ] Cloud sync for quarantine
- [ ] Compression in quarantine to save space
- [ ] Duplicate detection before restore

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Sessions not appearing | Click "Load Sessions" button |
| "File already exists" error | Original location has new file with same name |
| No restorable items | All items already restored or failed originally |
| Restore button disabled | Session has no restorable items |
| Partial restore | Some files succeeded, check error list |
| JSON corrupt | Delete actions_history.json, lose history |

## Technical Notes

### Thread Safety
- File operations are sequential (not parallel)
- JSON updates atomic per session
- Progress reporting thread-safe

### Performance
- Restore speed: ~10-50 MB/s (disk speed limited)
- Large folders may take minutes
- Progress updates every file/folder

### Compatibility
- Works with .NET 10
- Uses System.Text.Json (built-in)
- Cross-platform JSON format
- Windows path handling

## Summary

✅ **Complete JSON logging** of all cleanup actions  
✅ **Detailed tracking**: timestamp, operation, paths, sizes  
✅ **Safe restore** with error handling  
✅ **UI integration** with dedicated Restore tab  
✅ **Session management** with 100-session history  
✅ **Progress reporting** for user feedback  
✅ **Directory structure preservation**  
✅ **Conflict detection** and error reporting  

Users can now confidently clean up space knowing they can restore files if needed!
