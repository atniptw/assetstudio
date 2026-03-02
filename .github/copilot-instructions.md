# AI Coding Agent Instructions - AssetStudio

**Project**: Unity Asset Extraction Tool  
**Language**: C# (.NET 10) | **Scope**: Multi-threaded asset parsing & export  
**Last Updated**: March 2026

---

## Core Architecture

### Data Flow Pipeline
```
LoadFiles() → BundleFile decompress (parallel) → SerializedFile.Parse()
→ TypeTree read → ObjectReader deserialize → Classes/* (Assets)
→ AssetBrowser display / Exporter.Export() (parallel)
```

### Critical Components

| **Component** | **Purpose** | **Key Files** |
|---|---|---|
| **AssetsManager** | Orchestrator: parallel file loading with wave-based batching | `AssetsManager.cs` (1092 lines) |
| **ObjectReader** | Scoped binary stream reader for single objects | `ObjectReader.cs` bounds-checks reads |
| **EndianBinaryReader** | Binary data reader with alignment support | `ReadAlignedString()` ALWAYS calls `AlignStream()` |
| **Classes/** | Deserializers for Texture2D, Mesh, Shader, Sprite, etc. | Unity version-conditional parsing required |
| **BundleFile** | Decompresses LZMA/Brotli/LZ4 Unity bundles | Returns decompressed streams to SerializedFile |

---

## Patterns & Conventions

### 1. Binary Reading: Stream Alignment Critical
```csharp
// ALWAYS align after variable-length reads:
var length = reader.ReadInt32();
var data = reader.ReadBytes(length);
reader.AlignStream();  // ← ESSENTIAL (4-byte default)

// EndianBinaryReader handles big-endian/little-endian via BinaryPrimitives
```

### 2. Unity Version-Conditional Parsing
```csharp
// Pattern: Check version array for feature availability
if (version[0] >= 6000)  // Unity 6
    // v6-specific parsing (shader format changed)
else if (version[0] >= 2022 && version[1] >= 2)
    // 2022.2+ changes
    
// For Unity 6: Shader bypass is intentional (undocumented format)
// See AssetStudio/Classes/Shader.cs:1084
```

### 3. Thread-Safe Parallel Loading
**AssetsManager uses wave-based batching**: Load+parse files in parallel batches to handle dynamic dependencies.
```csharp
// Synchronization pattern (AssetsManager.cs):
- private readonly object assetsFileListLock = new object();
- lock (assetsFileListLock) { assetsFileList.Add(...); }
- Parallel.For() with MaxDegreeOfParallelism = ProcessorCount
```

### 4. Game-Specific Asset Mapping
```csharp
// Game enum: BH3, CB1, CB2, CB3, GI, SR, TOT, ZZZ (miHoYo games)
// Used for: XOR key decryption (MhyFile), asset_index lookups
public enum Game { BH3, CB1, ... }
```

---

## Essential Knowledge for Common Tasks

### Adding Export Format Support
1. Create converter in `AssetStudio.Utility/` (e.g., `TextureConverter.cs`)
2. Register in [Exporter.cs](AssetStudio.GUI/Exporter.cs) export switch
3. Add `ExportType` enum entry for CLI visibility
4. Handle version-conditional behavior (Unity 5.5 vs 2022+)

### Fixing Deserialization Issues
1. **Stream misalignment**: Check `AlignStream()` calls in read sequence
2. **Version mismatch**: Add debug logging with `version[0]`, `version[1]` checks
3. **TypeTree failures**: See `AI_QUICK_REFERENCE.md` for string length exceeds patterns
4. **Unity 6 shader failures**: Add graceful error handler, skip if undocumented

### Debugging Binary Reads
**Enable verbose logging** (see [CURRENT_STATE.md](CURRENT_STATE.md#how-to-use-debug-logging)):
```csharp
Logger.Verbose($"Position: 0x{reader.Position:X8}, Remaining: {reader.Remaining}");
var bytes = reader.ReadBytes(16);
Logger.Verbose($"Hex: {BitConverter.ToString(bytes)}");
```

---

## ⚠️ Critical Constraints

### DO NOT
- ❌ Change `EndianBinaryReader.ReadAlignedString()` alignment behavior (breaks all string parsing)
- ❌ Override `ObjectReader.Remaining` with object-scoped bounds (breaks stream positioning)
- ❌ Remove Unity 6 shader bypass (format undocumented; causes cascading parse failures)
- ❌ Use unbounded `ReadBytes(int count)` - always validate length first

### DO
- ✅ Lock `assetsFileList` modifications in parallel contexts
- ✅ Check `version[0] >= 6000` before accessing Unity 6-specific fields
- ✅ Call `AlignStream()` after every variable-length field
- ✅ Use `ConcurrentDictionary` for cross-thread resource maps

---

## Build & Release

**Build**: `dotnet build AssetStudio.sln`  
**Release**: Create git tag → GitHub Actions auto-builds + publishes ZIPs  
Example: `git tag v2.4.0 && git push origin v2.4.0`

---

## Key References

### Current Status (v2.4.0 - November 2025)
- ✅ Multi-threaded loading/export fully working
- ✅ Unity 6 support (shader parsing gracefully skipped)
- ⚠️ 1,082 Shader parse failures on Unity 6000.0.58f2 (format undocumented)

### Critical Files for Common Issues
- **Texture loading fails**: [Classes/Texture2D.cs](AssetStudio/Classes/Texture2D.cs) + version checks
- **Bundle decompress fails**: [BundleFile.cs](AssetStudio/BundleFile.cs) + crypto (MhyFile, XORStream)
- **Thread safety issues**: [AssetsManager.cs](AssetStudio/AssetsManager.cs) lines 35-38 (locks)
- **Stream position errors**: [EndianBinaryReader.cs](AssetStudio/EndianBinaryReader.cs) + AlignStream pattern

### Guides
- **Deep dive**: [AI_ONBOARDING.md](AI_ONBOARDING.md) (847 lines, comprehensive)
- **Quick reference**: [AI_QUICK_REFERENCE.md](AI_QUICK_REFERENCE.md) (v-specific patterns)
- **Current issues**: [CURRENT_STATE.md](CURRENT_STATE.md) (active development status)
- **Testing**: See "Testing Checklist" in [AI_QUICK_REFERENCE.md](AI_QUICK_REFERENCE.md#-testing-checklist)

---

## Integration Points

## Browser Validation Skills (ModViewer)

When validating ModViewer in-browser behavior, use these skills in order:

1. **Playwright mechanics only**: [.github/skills/playwright/SKILL.md](.github/skills/playwright/SKILL.md)
2. **ModViewer page assertions/checklist**: [.github/skills/modviewer-page-checks/SKILL.md](.github/skills/modviewer-page-checks/SKILL.md)

Keep responsibilities separated:
- Playwright skill: how to run browser automation commands and collect artifacts
- ModViewer checks skill: what to verify on page (console health, diagnostics, status, troubleshooting sections)

**External Dependencies**:
- **FBX Export**: `AssetStudio.FBXWrapper` → C++ native DLL (pinvoke)
- **Compression**: 7zip, Brotli, LZ4 for bundle decompression
- **Crypto**: MiHoYo XOR (Keys.json)
- **Asset Indices**: Downloaded JSON for container recovery

**CLI vs GUI**:
- CLI uses `AssetStudio.CLI/` with `Settings.cs` for option parsing
- GUI uses Windows Forms in `AssetStudio.GUI/` with format preview via `DirectBitmap`
