# Scoring System Implementation

## Overview
Implemented a comprehensive scoring system that classifies files and folders into safety categories (Safe/Caution/Blocked) with a 0-100 score, using multiple criteria including path patterns, file extensions, size, and age. The system provides human-readable explanations and is extensible for future LLM integration.

## Architecture

### Core Components

#### 1. **ScoringService.cs**
The main scoring engine that evaluates files and folders.

##### Key Methods:
- `ScoreFile(path, size, lastModified)` - Scores individual files
- `ScoreFolder(path, totalSize, lastModified, fileCount)` - Scores folders
- `ScoreWithLLMAsync(baseResult, path)` - Future LLM integration point
- `ScoreBatch(items)` - Batch scoring for multiple items

#### 2. **PathRules.cs**
Pattern-based path classification system.

##### Features:
- **Blocked Patterns**: System-critical paths (Windows, System32, boot files)
- **Caution Patterns**: Important but potentially cleanable (Program Files, AppData)
- **Safe Patterns**: Generally safe to clean (temp, cache, node_modules)
- Regex-based pattern matching
- Priority-based evaluation (Blocked → Caution → Safe)

#### 3. **Models.cs**
Data structures for scoring results.

##### Key Classes:
- `ScoringResult` - Complete scoring information
- `SafetyClassification` - Enum: Safe/Caution/Blocked
- `ScoredFileSystemItem` - File/folder with score
- `PathClassification` - Path pattern match result

## Scoring Criteria

### 1. Path-Based Scoring (Highest Priority)

#### **Blocked (Score: 0)**
- Windows system directories
- Boot and recovery folders
- System Volume Information
- Critical user data (NTUSER.DAT)

**Impact**: Automatic score of 0, immediate return

#### **Safe (Score: +30)**
- Temp directories
- Cache folders
- node_modules
- Downloaded installers
- .tmp, .bak files

#### **Caution (Score: 0)**
- Program Files
- AppData
- Development folders (.git, bin, obj)
- Database files

### 2. File Extension Scoring

#### **Blocked Extensions** (Score: -40)
System/executable files that should never be deleted:
```
.sys, .dll, .exe, .drv, .ocx, .cpl, .scr, 
.msi, .cab, .inf, .cat, .mui
```

#### **Caution Extensions** (Score: -10)
Important files requiring careful consideration:
```
.db, .sqlite, .mdf, .ldf, .accdb, .config,
.ini, .reg, .pst, .ost, .vhd, .vhdx
```

#### **Safe Extensions** (Score: +20)
Temporary/backup files safe to clean:
```
.tmp, .temp, .bak, .old, .cache, .log,
.dmp, .etl, .bak~, .~
```

### 3. Size-Based Scoring

| Size Range | Score Bonus | Description |
|------------|-------------|-------------|
| ≥ 10 GB | +15 | Very large file - excellent cleanup candidate |
| ≥ 1 GB | +10 | Large file |
| ≥ 100 MB | +5 | Medium file |
| < 100 MB | 0 | Small file - size not a factor |

**Rationale**: Larger files provide more storage benefit

### 4. Age-Based Scoring

| Age | Score Modifier | Description |
|-----|----------------|-------------|
| ≥ 2 years | +15 | Very old - likely unnecessary |
| ≥ 1 year | +10 | Old - good cleanup candidate |
| ≤ 30 days | -5 | Recently used - be cautious |
| Other | 0 | Neutral |

**Rationale**: Older files are less likely to be needed

### 5. Special Pattern Bonuses

| Pattern | Score Bonus | Reason |
|---------|-------------|--------|
| Filename contains "cache" or "temp" | +10 | Naming suggests temporary data |
| Filename starts/ends with "~" | +15 | Backup/temp convention |
| Path contains "node_modules" | +20 | Restorable with npm install |

## Scoring Algorithm

### Base Score
All items start with a **base score of 50** (neutral).

### Score Calculation Flow
```
1. Check path pattern (Blocked → immediate return with score 0)
2. Apply path-based adjustment (+30 for safe, 0 for caution)
3. Apply extension-based adjustment (-40 to +20)
4. Apply size-based bonus (+0 to +15)
5. Apply age-based adjustment (-5 to +15)
6. Apply special pattern bonuses (+10 to +20)
7. Clamp final score to 0-100 range
8. Determine classification based on final score
```

### Classification Thresholds
```
Score ≥ 70  →  Safe (✅)
Score 30-69 →  Caution (⚠️)
Score < 30  →  Blocked (🚫)
```

## Example Scoring Results

### Example 1: node_modules Folder
```
Path: C:\Projects\MyApp\node_modules
Size: 500 MB
Age: 60 days

Score Calculation:
  Base:              50
  Path (Safe):      +30  (node_modules pattern matched)
  Size (Medium):    +5
  Age (neutral):    0
  Special:          +20  (node_modules restorable)
  TOTAL:            105 → clamped to 100

Classification: ✅ Safe
Reasons:
  • ✅ Recommended for cleanup
  • ✅ Safe location: Node.js dependencies
  • 💾 Medium folder: 500.00 MB
  • ✅ Node.js dependencies (fully restorable with npm install)
  • 📦 Contains 15,347 files
```

### Example 2: System DLL
```
Path: C:\Windows\System32\kernel32.dll
Size: 1.2 MB
Age: 800 days

Score Calculation:
  Path (Blocked):   0  → Immediate return

Classification: 🚫 Blocked
Reasons:
  • 🚫 Do not clean - critical or protected
  • 🚫 System protected: Critical system directory
```

### Example 3: Old Downloaded Installer
```
Path: C:\Users\John\Downloads\OldApp_Setup.exe
Size: 250 MB
Age: 400 days

Score Calculation:
  Base:              50
  Path (Safe):      +30  (Downloads installer pattern)
  Extension:        -40  (.exe is blocked extension)
  Size (Medium):    +5
  Age (Old):        +10
  TOTAL:            55

Classification: ⚠️ Caution
Reasons:
  • ⚠️ Review before cleanup - may be important
  • ✅ Safe location: Downloaded installers
  • 🚫 System/executable file type: .exe
  • 💾 Medium file: 250.00 MB
  • 📅 Old file: Last modified 400 days ago (2023-12-01)
```

### Example 4: Cache File
```
Path: C:\Users\John\AppData\Local\Temp\cache_data.tmp
Size: 85 MB
Age: 15 days

Score Calculation:
  Base:              50
  Path (Safe):      +30  (Temp directory)
  Extension:        +20  (.tmp is safe extension)
  Size (neutral):   0
  Age (Recent):     -5
  Special:          +10  (filename contains "cache")
  TOTAL:            105 → clamped to 100

Classification: ✅ Safe
Reasons:
  • ✅ Recommended for cleanup
  • ✅ Safe location: Temporary files
  • ✅ Temporary/backup file type: .tmp
  • 📅 Recently modified: 15 days ago (2025-01-05)
  • ✅ Filename suggests temporary data
```

## Human-Readable Reasons

The system generates detailed, emoji-enhanced reasons:

### Reason Categories:
- 🚫 **Blocked/Critical**: System protection, executable files
- ⚠️ **Caution**: Important locations, potential data files
- ✅ **Safe**: Temporary data, restorable files
- 💾 **Size**: File/folder size information
- 📅 **Age**: Last modified date and age
- 🤖 **LLM**: Future AI explanations (placeholder)
- 📦 **Details**: Additional context (file counts, etc.)

### Reason Format:
```csharp
ScoringResult.Reasons          // List<string> of individual reasons
ScoringResult.ReasonSummary    // Newline-joined summary
ScoringResult.GetReasonText()  // Bullet-point formatted text
```

## Future LLM Integration

### Placeholder Method: `ScoreWithLLMAsync`

**Current Status**: Implemented structure, awaiting LLM API integration

**Planned Features**:
1. Send file/folder context to LLM (OpenAI, Azure OpenAI, local model)
2. Request detailed explanation of why item is safe/unsafe
3. Get suggestions for alternatives
4. Explain technical context in plain language
5. Provide restoration instructions if deleted

**Example Future LLM Output**:
```
🤖 AI Analysis:

This is a node_modules folder containing JavaScript dependencies for a Node.js 
project. It's completely safe to delete because:

1. All dependencies are listed in package.json
2. They can be restored instantly with 'npm install'
3. This is a standard practice for JavaScript developers
4. The folder often grows to 500MB-2GB unnecessarily

If you delete this and need the project again:
  $ cd C:\Projects\MyApp
  $ npm install

This will recreate the entire folder from the package.json specification.
```

### Integration Points:
```csharp
// Basic usage
var baseScore = scoringService.ScoreFile(path, size, date);
var enhancedScore = await scoringService.ScoreWithLLMAsync(
    baseScore, 
    path, 
    cancellationToken
);

// Result includes LLM explanation
Console.WriteLine(enhancedScore.LLMExplanation);
```

## API Usage Examples

### Score a Single File
```csharp
var scoringService = new ScoringService();
var result = scoringService.ScoreFile(
    path: @"C:\Users\John\Downloads\installer.exe",
    size: 150 * 1024 * 1024,  // 150 MB
    lastModified: DateTime.Now.AddDays(-45)
);

Console.WriteLine($"Classification: {result.ClassificationText}");
Console.WriteLine($"Score: {result.ScoreDisplay}");
Console.WriteLine($"Reasons:\n{result.ReasonSummary}");
```

### Score a Folder
```csharp
var result = scoringService.ScoreFolder(
    path: @"C:\Projects\MyApp\node_modules",
    totalSize: 500L * 1024 * 1024,  // 500 MB
    lastModified: DateTime.Now.AddDays(-30),
    fileCount: 15347
);
```

### Batch Scoring
```csharp
List<FileSystemItem> items = GetFilesFromScan();
List<ScoringResult> results = scoringService.ScoreBatch(items);

var safeItems = results.Where(r => r.Classification == SafetyClassification.Safe);
var blockedItems = results.Where(r => r.Classification == SafetyClassification.Blocked);
```

### Check Path Safety
```csharp
// Quick path checks
bool isProtected = PathRules.IsSystemProtected(@"C:\Windows\System32\ntdll.dll");
bool isSafe = PathRules.IsSafeToDelete(@"C:\Temp\cache.tmp");

// Detailed classification
var classification = PathRules.ClassifyPath(@"C:\Program Files\MyApp\data.db");
Console.WriteLine($"Level: {classification.Level}");
Console.WriteLine($"Reason: {classification.Reason}");
Console.WriteLine($"Pattern: {classification.MatchedPattern}");
```

## Extension Points

### Adding New Path Patterns
Edit `PathRules.cs` arrays:
```csharp
private static readonly PathPattern[] SafePatterns = new[]
{
    // Add your pattern
    new PathPattern(@"\\YourFolder\\", "Your description", PathSafetyLevel.Safe),
    // Existing patterns...
};
```

### Custom Scoring Rules
Extend `ScoringService`:
```csharp
public class CustomScoringService : ScoringService
{
    public override ScoringResult ScoreFile(string path, long size, DateTime lastModified)
    {
        var baseResult = base.ScoreFile(path, size, lastModified);
        
        // Add custom logic
        if (path.EndsWith(".custom"))
        {
            baseResult.Score += 20;
            baseResult.Reasons.Add("✅ Custom file type bonus");
        }
        
        return baseResult;
    }
}
```

### LLM Provider Implementation
```csharp
public async Task<ScoringResult> ScoreWithLLMAsync(
    ScoringResult baseResult,
    string path,
    CancellationToken cancellationToken)
{
    // Example OpenAI integration
    var openAIClient = new OpenAIClient("your-api-key");
    
    var prompt = $@"
        Analyze this file for cleanup safety:
        Path: {path}
        Current Score: {baseResult.Score}
        Classification: {baseResult.Classification}
        Reasons: {string.Join(", ", baseResult.Reasons)}
        
        Provide a detailed explanation in plain language.
    ";
    
    var response = await openAIClient.CompleteChatAsync(prompt, cancellationToken);
    
    return new ScoringResult
    {
        // Copy base result
        Classification = baseResult.Classification,
        Score = baseResult.Score,
        Reasons = baseResult.Reasons,
        PathClassification = baseResult.PathClassification,
        
        // Add LLM enhancement
        LLMExplanation = response.Content
    };
}
```

## Testing Strategy

### Unit Test Examples
```csharp
[Fact]
public void SystemFiles_ShouldBeBlocked()
{
    var result = scoringService.ScoreFile(
        @"C:\Windows\System32\kernel32.dll",
        1000000,
        DateTime.Now
    );
    
    Assert.Equal(SafetyClassification.Blocked, result.Classification);
    Assert.Equal(0, result.Score);
}

[Fact]
public void NodeModules_ShouldBeSafe()
{
    var result = scoringService.ScoreFolder(
        @"C:\Projects\App\node_modules",
        500000000,
        DateTime.Now.AddDays(-30),
        10000
    );
    
    Assert.Equal(SafetyClassification.Safe, result.Classification);
    Assert.True(result.Score >= 70);
}

[Fact]
public void RecentCacheFile_ShouldBeSafe()
{
    var result = scoringService.ScoreFile(
        @"C:\Users\Test\AppData\Local\Temp\cache.tmp",
        10000000,
        DateTime.Now.AddDays(-5)
    );
    
    Assert.Equal(SafetyClassification.Safe, result.Classification);
}
```

## Performance Considerations

- **Pattern Matching**: Compiled regex for efficiency
- **Priority Evaluation**: Short-circuit on Blocked patterns
- **Batch Processing**: Single enumeration for multiple items
- **Memory**: Minimal allocations, reusable structures

## File Structure
```
Services/
  ├── ScoringService.cs    - Main scoring engine
  └── PathRules.cs         - Path pattern definitions

ViewModels/
  └── Models.cs            - ScoringResult, SafetyClassification, etc.
```

## Summary

This scoring system provides:
✅ **Multi-criteria** evaluation (path, extension, size, age)
✅ **Safety-first** approach (system protection built-in)
✅ **Transparent** decision-making (detailed reasons)
✅ **Extensible** architecture (easy to add rules)
✅ **LLM-ready** for future AI enhancements
✅ **Production-ready** error handling and edge cases
