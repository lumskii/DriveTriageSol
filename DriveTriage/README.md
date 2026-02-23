# DriveTriage - Complete Feature Set

## Overview
DriveTriage is a comprehensive Windows disk space management tool with intelligent cleanup recommendations, application management, and AI-ready scoring system.

## ✅ Completed Features

### 1. **Filesystem Scanning** (`ScanService.cs`)
- ✅ Cancellable async filesystem enumeration
- ✅ Recursive directory traversal with reparse point detection
- ✅ Metadata collection (path, size, last modified)
- ✅ Top-N file/folder tracking
- ✅ Progress reporting and status updates
- ✅ Thread-safe statistics tracking
- ✅ Handles UnauthorizedAccessException gracefully

**UI**: Scan tab with progress bar, status, and top files/folders lists

### 2. **Cleanup Buckets** (`BucketsService.cs`)
- ✅ User Temp Files bucket
- ✅ Old Installers bucket (Downloads, 30+ days)
- ✅ node_modules Folders bucket
- ✅ Reclaimable space calculation
- ✅ Quarantine system (move, not delete)
- ✅ Directory structure preservation
- ✅ Action logging (text + JSON)
- ✅ IBucketRule interface for extensibility

**UI**: Recommendations tab with bucket cards showing size and item counts

### 3. **Application Management** (`AppsService.cs`)
- ✅ Registry enumeration (HKLM, WOW6432Node, HKCU)
- ✅ Complete metadata extraction:
  - DisplayName, Publisher, Version
  - InstallDate, EstimatedSize, InstallLocation
  - UninstallString, QuietUninstallString
- ✅ Intelligent filtering (system components, updates)
- ✅ Duplicate detection
- ✅ Safe uninstall execution with UAC elevation
- ✅ Search and filter capabilities
- ✅ Publisher statistics

**UI**: Apps tab with searchable DataGrid and uninstall buttons

### 4. **Intelligent Scoring System** (`ScoringService.cs`, `PathRules.cs`)
- ✅ Multi-criteria evaluation:
  - Path pattern matching (Blocked/Caution/Safe)
  - File extension classification
  - Size-based scoring
  - Age-based scoring
  - Special pattern bonuses
- ✅ 0-100 scoring with safety classifications
- ✅ Human-readable explanations with emojis
- ✅ Regex-based pattern matching
- ✅ Batch scoring support
- ✅ LLM integration placeholder for future AI explanations

**Path Rules**:
- 🚫 Blocked: Windows system directories, boot files, critical user data
- ⚠️ Caution: Program Files, AppData, databases, development folders
- ✅ Safe: Temp, cache, node_modules, backups

### 5. **Reporting & Logging** (`ReportService.cs`)
- ✅ Detailed cleanup action logs
- ✅ Human-readable text format
- ✅ Machine-readable JSON format
- ✅ Timestamped log files
- ✅ Summary statistics
- ✅ Error tracking

**Location**: `%LocalAppData%\DriveTriage\Logs`

### 6. **User Interface** (WPF)
- ✅ Modern tabbed interface
- ✅ Real-time progress updates
- ✅ Confirmation dialogs
- ✅ Status notifications
- ✅ Search and filtering
- ✅ Sortable data grids
- ✅ Empty state handling
- ✅ Visual feedback (colors, emojis)

## 🏗️ Architecture

```
DriveTriage/
├── App.xaml                        # Application entry point
├── MainWindow.xaml                 # Main UI
├── ViewModels/
│   ├── MainViewModel.cs            # UI logic coordinator
│   └── Models.cs                   # Data models
├── Services/
│   ├── ScanService.cs              # Filesystem scanning
│   ├── BucketsService.cs           # Cleanup buckets
│   ├── AppsService.cs              # Application management
│   ├── ScoringService.cs           # Safety scoring
│   ├── PathRules.cs                # Path classification
│   ├── ReportService.cs            # Logging
│   ├── ExecuteService.cs           # (Placeholder)
│   └── ScoringService.cs           # (Placeholder)
└── Utils/
    ├── AsyncRelayCommand.cs        # MVVM commands
    └── StringNotEmptyConverter.cs  # XAML converters
```

## 📊 Data Flow

```
User Action → ViewModel → Service → Results → UI Update

Example: Cleanup Bucket Scan
1. User clicks "Scan for Cleanup"
2. MainViewModel.ExecuteScanBucketsAsync()
3. BucketsService.ScanBucketsAsync()
4. IBucketRule implementations scan filesystem
5. Results populate CleanupBuckets collection
6. UI updates automatically via bindings
```

## 🎯 Key Design Patterns

- **MVVM**: Clean separation of UI and logic
- **Async/Await**: Non-blocking operations
- **Interface-based**: Extensible bucket rules
- **Observer**: INotifyPropertyChanged for UI binding
- **Strategy**: Different scoring strategies per file type
- **Factory**: Bucket rule creation
- **Repository**: Centralized data access

## 🔒 Safety Features

1. **System Protection**
   - Blocks Windows system directories
   - Prevents deletion of critical files
   - UAC elevation for uninstalls

2. **Quarantine System**
   - Move, not delete
   - Preserves directory structure
   - Can be restored manually

3. **Confirmation Dialogs**
   - User must confirm cleanup
   - Shows what will be affected
   - Displays size and count

4. **Comprehensive Logging**
   - Every action recorded
   - Timestamps and sizes tracked
   - Error messages captured

5. **Error Handling**
   - Access denied → skip gracefully
   - File not found → continue
   - Cancellation supported

## 📈 Scoring System Summary

### Classification Thresholds
- **Safe (70-100)**: Recommended for cleanup
- **Caution (30-69)**: Review before cleanup
- **Blocked (0-29)**: Do not clean

### Scoring Factors
| Factor | Impact | Examples |
|--------|--------|----------|
| Path Pattern | -50 to +30 | System dirs (-50), Temp (+30) |
| Extension | -40 to +20 | .dll (-40), .tmp (+20) |
| Size | 0 to +15 | 10GB+ (+15), < 100MB (0) |
| Age | -5 to +15 | 2+ years (+15), Recent (-5) |
| Special Patterns | 0 to +20 | node_modules (+20) |

### Human-Readable Output
```
✅ Safe - Score: 95/100
  • ✅ Recommended for cleanup
  • ✅ Safe location: Node.js dependencies
  • 💾 Very large folder: 1.50 GB
  • 📅 Old folder: 120 days old
  • ✅ Node.js dependencies (fully restorable with npm install)
  • 📦 Contains 15,347 files
```

## 🚀 Usage Examples

### Basic Scan
```csharp
var scanService = new ScanService();
await scanService.ScanAsync(
    progress: new Progress<double>(p => ProgressBar.Value = p),
    statusUpdate: new Progress<string>(s => StatusText.Text = s),
    onFilesFound: files => LargestFiles.ItemsSource = files,
    onFoldersFound: folders => LargestFolders.ItemsSource = folders
);
```

### Cleanup Workflow
```csharp
var bucketsService = new BucketsService();
var buckets = await bucketsService.ScanBucketsAsync(statusUpdate, cancellationToken);

// User selects bucket to clean
var actions = await bucketsService.CleanBucketAsync(selectedBucket, statusUpdate, cancellationToken);

// Log results
var reportService = new ReportService();
await reportService.LogCleanupActionsAsync(bucket.Name, actions);
```

### Application Enumeration
```csharp
var appsService = new AppsService();
var apps = await appsService.EnumerateInstalledAppsAsync(statusUpdate, cancellationToken);

// Filter and display
var largeApps = appsService.GetLargestApps(apps, count: 20);
var filtered = appsService.FilterApps(apps, searchText: "Microsoft");
```

### Safety Scoring
```csharp
var scoringService = new ScoringService();
var result = scoringService.ScoreFile(
    path: @"C:\Temp\cache.tmp",
    size: 100 * 1024 * 1024,
    lastModified: DateTime.Now.AddDays(-60)
);

Console.WriteLine($"{result.ClassificationText}: {result.ScoreDisplay}");
Console.WriteLine(result.ReasonSummary);
```

## 🔮 Future Enhancements (Placeholders Ready)

### 1. **LLM Integration** (`ScoreWithLLMAsync`)
- OpenAI/Azure OpenAI integration
- Natural language explanations
- Context-aware recommendations
- Plain language restoration instructions

### 2. **ExecuteService**
- Automated cleanup execution
- Scheduled maintenance
- Batch operations
- Rollback capability

### 3. **Advanced Scoring**
- File content analysis
- Usage pattern tracking
- Duplicate detection
- Compression recommendations

### 4. **Additional Buckets**
- Browser caches
- Windows update cache
- Visual Studio build artifacts
- Docker images/containers
- Game save backups

## 📁 Data Locations

| Type | Location |
|------|----------|
| Quarantine | `%LocalAppData%\DriveTriage\Quarantine` |
| Logs | `%LocalAppData%\DriveTriage\Logs` |
| Config | (Future) `%AppData%\DriveTriage` |

## 🎨 UI Features

### Scan Tab
- Drive scanning with progress bar
- Top 100 largest files/folders
- Real-time status updates
- Sortable columns (Path, Size, Date)

### Recommendations Tab
- Cleanup bucket cards
- Reclaimable space calculations
- One-click cleanup
- Status indicators
- Empty state guidance

### Apps Tab
- Searchable application list
- Sortable DataGrid
- Size and install date display
- One-click uninstall
- Publisher grouping

## 🛠️ Development Setup

### Requirements
- .NET 10
- Windows 10/11
- Visual Studio 2022+

### Build
```bash
dotnet build
```

### Run
```bash
dotnet run --project DriveTriage/DriveTriage.csproj
```

## 📝 Documentation Files

- `BUCKETS_IMPLEMENTATION.md` - Cleanup buckets details
- `APPS_IMPLEMENTATION.md` - Application management
- `SCORING_IMPLEMENTATION.md` - Scoring system deep dive
- `SCORING_EXAMPLES.cs` - Code examples
- `README.md` - This file

## 🎯 Quick Start Checklist

For new users:
1. ✅ Click "Scan" to discover large files/folders
2. ✅ Switch to "Recommendations" → "Scan for Cleanup"
3. ✅ Review cleanup buckets and reclaimable space
4. ✅ Click "Clean" on desired buckets
5. ✅ Check "Apps" tab to uninstall unused programs
6. ✅ Review logs in `%LocalAppData%\DriveTriage\Logs`

## 💡 Pro Tips

- **Quarantine**: Files aren't deleted, restore from quarantine if needed
- **node_modules**: Always safe to delete, run `npm install` to restore
- **Old Installers**: Check before cleaning, may need for reinstall
- **Scoring**: 70+ score = safe, 30-69 = review, < 30 = don't touch
- **Logs**: Keep logs for audit trail and troubleshooting

## 🐛 Troubleshooting

| Issue | Solution |
|-------|----------|
| Access Denied | Run as Administrator |
| Slow Scan | Large drives take time, use Cancel if needed |
| Missing Apps | Some apps don't register in standard locations |
| Can't Uninstall | Use Control Panel as fallback |
| Quarantine Full | Manually clean quarantine folder |

## 📄 License

(Add your license here)

## 👥 Contributing

(Add contribution guidelines here)

---

**Built with ❤️ for helping users reclaim disk space safely and intelligently.**
