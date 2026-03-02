# AI Quick Reference - AssetStudio Unity 6000 Project

**Last Updated**: November 19, 2025 (v2.3.2)  
**Status**: ✅ ALL UNITY 6000 ISSUES RESOLVED

---

## 🎯 Current State Summary

**v2.3.2 Released** - Unity 6000 texture loading fixed, all features working

### ModViewer (WASM) Notes
- Build ModViewer with [ModViewer.sln](ModViewer.sln) and `dotnet build ModViewer.sln -c Release /p:WasmBuild=true`
- WASM build excludes FBX/PInvoke; do not call FBX export or P/Invoke paths
- `ASSETSTUDIO_WASM` is defined in WASM builds for conditional code paths

### What's Working
- ✅ Unity 6000.0.58f2 texture loading and preview (tested with Marvel Snap)
- ✅ Multi-threaded parallel export (v2.2.0)
- ✅ Thread-safe stream operations (v2.2.1)
- ✅ Shader parsing bypass for Unity 6000+ (prevents format issues)

### Recent History
- **v2.3.1** broke texture loading with object-scoped bounds checking
- **v2.3.2** reverted breaking changes, added shader bypass
- Tested working with Korg_02 textures from Marvel Snap

---

## 📂 Critical File Locations

### Core Parsing (v2.2.1 - WORKING VERSIONS)
- `AssetStudio/EndianBinaryReader.cs` - Binary reading, `ReadAlignedString()` always calls `AlignStream()`
- `AssetStudio/ObjectReader.cs` - Object scope reader, uses base `Remaining` calculation
- `AssetStudio/AssetsManager.cs` - Asset loading orchestration

### Asset Classes (MODIFIED FOR UNITY 6000)
- `AssetStudio/Classes/Shader.cs` - **Unity 6000 bypass added at line 1084**
- `AssetStudio/Classes/Texture2D.cs` - Texture loading (working with v2.2.1 core)
- `AssetStudio/Classes/Sprite.cs` - Sprite handling
- `AssetStudio/Classes/Mesh.cs` - Mesh data

### Export/GUI
- `AssetStudio.GUI/Studio.cs` - **Line 546: `Parallel.ForEach` for multi-threaded export**
- `AssetStudio.GUI/MainForm.cs` - UI and preview logic

---

## 🔧 Key Technical Details

### Unity 6000 Shader Bypass (NEW in v2.3.2)
```csharp
// Location: AssetStudio/Classes/Shader.cs line 1084
if (version[0] >= 6000)
{
    Logger.Verbose($"Skipping Shader manual parsing for Unity {version[0]}.{version[1]}");
    var remaining = reader.byteSize - (reader.Position - reader.byteStart);
    if (remaining > 0)
    {
        reader.ReadBytes((int)remaining);
    }
    return;
}
```

### Stream Reading (v2.2.1 - WORKING)
```csharp
// EndianBinaryReader.ReadAlignedString() - ALWAYS calls AlignStream()
public string ReadAlignedString()
{
    var result = "";
    var length = ReadInt32();
    if (length > 0 && length <= Remaining)
    {
        var stringData = ReadBytes(length);
        result = Encoding.UTF8.GetString(stringData);
    }
    AlignStream();  // ← CRITICAL: Always called (v2.2.1 behavior)
    return result;
}
```

### ObjectReader (v2.2.1 - WORKING)
```csharp
// NO object-scoped Remaining override
// Uses base EndianBinaryReader.Remaining (stream-based)
public override int Read(byte[] buffer, int index, int count)
{
    var pos = Position - byteStart;
    if (pos + count > byteSize)
    {
        throw new EndOfStreamException("Unable to read beyond the end of the stream.");
    }
    return base.Read(buffer, index, count);
}
```

---

## 🚨 What NOT to Do

### ❌ DO NOT Change These (Working in v2.2.1)
- `EndianBinaryReader.ReadAlignedString()` - Must always call `AlignStream()`
- `ObjectReader.Remaining` - Must use base calculation (stream-based, not object-scoped)
- `ObjectReader.Read()` bounds checking - Keep v2.2.1 version

### ❌ DO NOT Break Multi-Threading
- `AssetStudio.GUI/Studio.cs` line 546: `Parallel.ForEach` is CRITICAL
- Thread-safety locks in `ObjectReader` must remain

### ❌ DO NOT Remove Unity 6000 Shader Bypass
- Shader parsing for Unity 6000+ is intentionally skipped
- Format is undocumented and not needed for texture extraction

---

## 📊 Version History Quick Reference

| Version | Date       | Changes                                         | Status          |
| ------- | ---------- | ----------------------------------------------- | --------------- |
| v2.2.0  | Nov 2025   | Added multi-threaded parallel export            | ✅ Working      |
| v2.2.1  | Nov 2025   | Thread-safety fixes                             | ✅ Working      |
| v2.3.1  | Nov 19     | TypeTree fixes, BUT broke texture loading       | ❌ Broken       |
| v2.3.2  | Nov 19     | Reverted breaking changes, added shader bypass  | ✅ Working      |

---

## 🎯 Quick Problem Solving

### "Texture won't load/preview"
1. Check if Unity 6000+ → ensure shader bypass is active
2. Verify `ReadAlignedString()` calls `AlignStream()` always
3. Check `ObjectReader.Remaining` doesn't have object-scope override

### "Stream position errors"
1. Ensure `AlignStream()` is called after variable-length reads
2. Check version-specific field reads are conditioned properly
3. Verify no object-scoped bounds checking interfering

### "Multi-threading issues"
1. Check all `Position` operations are locked in `ObjectReader`
2. Verify `Parallel.ForEach` in Studio.cs is intact
3. Ensure no shared state without proper synchronization

---

## 📝 Common Unity Version Checks

```csharp
// Unity 6 detection (version[0] is 6000 for Unity 6)
if (version[0] >= 6000)
{
    // Unity 6+ specific code
}

// Unity 2022.2+
if (version[0] > 2022 || (version[0] == 2022 && version[1] >= 2))
{
    // 2022.2+ code
}

// Unity 5.5+
if (version[0] == 5 && version[1] >= 5 || version[0] > 5)
{
    // 5.5+ code
}
```

---

## 🔍 Debugging Tips

### Enable Verbose Logging
```csharp
Logger.Verbose($"Debug info: {value}");
```

### Check Stream Position
```csharp
var pos = reader.Position;
var remaining = reader.byteSize - (reader.Position - reader.byteStart);
Logger.Verbose($"Position: 0x{pos:X8}, Remaining: {remaining} bytes");
```

### Inspect Raw Bytes
```csharp
var bytes = reader.ReadBytes(count);
Logger.Verbose($"Bytes: {BitConverter.ToString(bytes)}");
```

---

## ✅ Testing Checklist

When making changes, verify:
- [ ] Build succeeds without errors
- [ ] Unity 6000.0.58f2 (Marvel Snap) textures load
- [ ] Korg_02 textures preview correctly
- [ ] Multi-threaded export still works
- [ ] No "Unable to read beyond stream" errors in log
- [ ] No texture preview failures

---

**Remember**: v2.2.1 core (EndianBinaryReader, ObjectReader, AssetsManager) + Unity 6000 shader bypass = WORKING
