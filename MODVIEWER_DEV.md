# AssetStudio.ModViewer – Developer Guide

**Audience:** Solo developer + AI agent  
**Scope:** Setup, building, testing, debugging

---

## Quick Start

### Prerequisites
- **.NET 9 SDK** (for WASM compilation)
- **Node.js 18+** (for three.js package mgmt, optional)
- **Visual Studio Code** with C# Dev Kit
- **Git** (already installed)

### 1. Create Project Structure

```bash
cd /workspaces/assetstudio

# Create new Blazor WASM project
dotnet new blazorwasm -n AssetStudio.ModViewer -o AssetStudio.ModViewer

# Create folders for services, components
mkdir -p AssetStudio.ModViewer/Services
mkdir -p AssetStudio.ModViewer/Components
mkdir -p AssetStudio.ModViewer/Models
```

### 2. Create ModViewer Solution

Create a separate solution for WASM-only builds at [ModViewer.sln](ModViewer.sln).

### 3. Configure Project References

**AssetStudio.ModViewer.csproj:**

```xml
<ItemGroup>
    <ProjectReference Include="../AssetStudio/AssetStudio.csproj" />
    <ProjectReference Include="../AssetStudio.Utility/AssetStudio.Utility.csproj" />
</ItemGroup>

<ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="9.0.0" />
    <PackageReference Include="IndexedDB.Blazor" Version="2.2.0" />
</ItemGroup>
```

### 4. Build & Run

```bash
# Build WASM assemblies
dotnet build ModViewer.sln -c Release /p:WasmBuild=true

# Run development server
cd AssetStudio.ModViewer
dotnet run
```

Open `https://localhost:5001` in browser.

---

## Key Files & Responsibilities

### Core Services

| File | Purpose | Owner |
|------|---------|-------|
| `Services/ModLoader.cs` | Orchestrates asset extraction from .unityFS | You |
| `Services/IFileSystem.cs` | File I/O abstraction (File API + IndexedDB) | AI |
| `Services/IndexedDBCache.cs` | Mod persistence layer | You |
| `Services/FormatConverter.cs` | Converts C# objects → JSON for JS | You |
| `wwwroot/js/AssetConverter.js` | three.js bridge (texture, mesh, skeletal binding) | You |

### Blazor Components

| File | Purpose |
|------|---------|
| `Components/ModUploadComponent.razor` | File input + loading state |
| `Components/PreviewCanvas.razor` | three.js canvas container + camera controls |
| `Components/ModManagerPanel.razor` | List saved mods, delete, reorder |
| `Pages/Index.razor` | Main layout |

---

## Testing Strategy

### Browser Automation Skills

- Playwright command usage: [.github/skills/playwright/SKILL.md](.github/skills/playwright/SKILL.md)
- ModViewer page verification checklist: [.github/skills/modviewer-page-checks/SKILL.md](.github/skills/modviewer-page-checks/SKILL.md)

Use both skills together:
1. Use the Playwright skill to open the app and capture screenshot/console/network artifacts.
2. Use the ModViewer page-checks skill to decide pass/fail from diagnostics, status, and troubleshooting signals.

### Unit Tests

Create `AssetStudio.ModViewer.Tests/AssetStudio.ModViewer.Tests.csproj`:

```csharp
// Tests/ModLoaderTests.cs
[TestClass]
public class ModLoaderTests
{
    [TestMethod]
    public async Task ParseMod_WithValidBundle_ExtractsTextures()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        var loader = new ModLoader(fileSystem);
        var bundleStream = File.OpenRead("TestData/sample_mod.unityfs");

        // Act
        var assets = await loader.ParseModAsync(bundleStream);

        // Assert
        Assert.IsNotNull(assets.Textures);
        Assert.IsTrue(assets.Textures.Count > 0);
    }
}
```

### Integration Tests

```bash
# Create test mods in TestData/
TestData/
├── small_mod.unityfs         (< 1 MB, simple geometry)
├── medium_mod.unityfs        (5 MB, textured mesh)
└── complex_mod.unityfs       (20 MB, skeletal + animations)

# Run end-to-end in browser
dotnet run
# Manually upload TestData mods, verify preview
```

### Performance Profiling

```javascript
// AssetConverter.js
console.time('TextureDecoding');
const texture = AssetConverter.CreateTextureFromPixels(...);
console.timeEnd('TextureDecoding');

// three.js scene stats
const stats = new THREE.Stats();
scene.add(stats.dom);
```

---

## Debugging Workflow

### Enable Verbose Logging

**Program.cs:**

```csharp
var logger = new BrowserLogger(isDevelopment: true);
Logger.Instance = logger;

class BrowserLogger : ILogger
{
    public void VerboseLogging(string message)
    {
        Console.WriteLine($"[VERBOSE] {message}");
    }
}
```

### Common Issues & Fixes

#### Issue: "System.IO.FileNotFoundException: file 'Keys.json'"

**Cause:** UnityCNManager.cs tries to load Keys.json from disk at runtime.

**Fix:** Embed Keys.json as a static asset or provide via dependency injection.

```csharp
// In Startup
services.AddSingleton<IGameManager>(provider =>
{
    var keys = JsonSerializer.Deserialize<Dictionary<string, string>>(
        File.ReadAllText("Keys.json")
    );
    return new GameManager(keys);
});
```

#### Issue: "Cannot decompress bundle: format not recognized"

**Cause:** .unityFS file is corrupted or wrong version.

**Fix:** Log bundle header before decompression:

```csharp
var reader = new EndianBinaryReader(stream);
var signature = reader.ReadString(8); // "UnityFS\0"
var version = reader.ReadInt32();
Logger.Verbose($"Bundle signature: {signature}, version: {version}");
```

#### Issue: "Mesh has no vertices" after extraction

**Cause:** VertexData decompression failed (wrong stride or compression codec).

**Fix:** Inspect Mesh.m_Vertices in debugger:

```csharp
Logger.Verbose($"Mesh vertices: count={mesh.m_Vertices?.Length ?? 0}");
if (mesh.m_VertexCount > 0 && mesh.m_Vertices == null)
{
    Logger.Error("VertexData decompression failed!");
}
```

---

## Development Checklist

### Phase 1: Setup & WASM Compilation

- [ ] AssetStudio.ModViewer project created
- [ ] Project references configured
- [ ] `dotnet build` produces valid WASM binary
- [ ] Base R.E.P.O avatar embedded as static asset
- [ ] IFileSystem abstraction implemented
- [ ] ILogger Blazor implementation created

### Phase 2: Asset Pipeline

- [ ] ModLoader service compiles and loads bundles
- [ ] Texture2D deserialization tested against sample mods
- [ ] Mesh vertex extraction works (validate with 3D viewer)
- [ ] Avatar skeleton hierarchy extracted
- [ ] JSON serialization works correctly
- [ ] IndexedDB cache functional (save/load cycle)

### Phase 3: three.js Integration

- [ ] AssetConverter.js created with all functions
- [ ] CreateTextureFromPixels produces correct colors
- [ ] CreateMeshGeometry renders correctly in three.js
- [ ] Skeletal binding preserves attachment points
- [ ] CompositeAvatarWithMod combines base + mod without gaps
- [ ] Orbit controls responsive and smooth

### Phase 4: UI & Polish

- [ ] ModUploadComponent file input works
- [ ] Error handling graceful (no white screens)
- [ ] three.js canvas responsive to window resize
- [ ] ModManagerPanel lists and deletes saved mods
- [ ] Performance acceptable (< 2 sec parse per mod)

---

## Deployment

### Local Build

```bash
cd AssetStudio.ModViewer
dotnet publish -c Release -o bin/Release/publish
```

Output: `bin/Release/publish/wwwroot/` (static Blazor WASM app)

### GitHub Pages

```bash
# Build with base path
dotnet publish -c Release /p:PublishSite=GitHub /p:BaseHref=/assetstudio-modviewer/

# Deploy wwwroot/ to gh-pages branch
```

### Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/nightly:9-sdk AS build
WORKDIR /src
COPY . .
RUN dotnet publish AssetStudio.ModViewer -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/nightly:9-aspnet
COPY --from=build /app/publish /app
EXPOSE 80
ENTRYPOINT ["dotnet", "AssetStudio.ModViewer.dll"]
```

---

## Useful Commands

```bash
# Clean build
dotnet clean && dotnet build

# Run tests
dotnet test AssetStudio.ModViewer.Tests

# Check WASM bundle size
ls -lh bin/Release/net9.0/publish/_framework/*.wasm

# Profile startup time
dotnet run --configuration Release 2>&1 | grep -i time

# Format code
dotnet format

# Add NuGet package
dotnet add package IndexedDB.Blazor
```

---

## Resources

- **three.js Docs:** https://threejs.org/docs/
- **Blazor WASM:** https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly/
- **IndexedDB guide:** https://developer.mozilla.org/en-US/docs/Web/API/IndexedDB_API
- **AssetStudio Architecture:** See [MODVIEWER_ARCHITECTURE.md](MODVIEWER_ARCHITECTURE.md) and [AI_ONBOARDING.md](AI_ONBOARDING.md)
