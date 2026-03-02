# AssetStudio.ModViewer – Architecture & Design

**Status:** Draft (for Phase 1 planning)  
**Last Updated:** March 2, 2026

---

## System Overview

```
┌────────────────────────────────────────────────────────────┐
│           Browser Environment (WASM + JavaScript)          │
├────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  Blazor WASM Component Layer                         │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │  AssetStudio WASM Core (C#)                          │  │
│  │  ├─ AssetsManager (parallel file loading)            │  │
│  │  ├─ BundleFile (LZMA/LZ4/Brotli decompression)       │  │
│  │  ├─ Classes/* (Texture2D, Mesh, Avatar)              │  │
│  │  └─ Converters (texture decode, mesh extraction)     │  │
│  │                                                       │  │
│  │  ModViewer Services (C#)                             │  │
│  │  ├─ ModLoader (orchestrates asset extraction)        │  │
│  │  ├─ IFileSystem, ILogger implementations             │  │
│  │  └─ IndexedDBCache (mod persistence)                 │  │
│  │                                                       │  │
│  │  UI Components (Blazor .razor)                       │  │
│  │  ├─ ModUploadComponent (file input)                  │  │
│  │  ├─ PreviewCanvas (three.js container)               │  │
│  │  ├─ ModManagerPanel (saved mods list)                │  │
│  │  └─ ControlsPanel (camera, visibility toggles)       │  │
│  └──────────────────────────────────────────────────────┘  │
│                          │                                  │
│                          ▼                                  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  JavaScript Bridge Layer                             │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │  JSInterop ← → C# ModLoader (JSON serialization)     │  │
│  │  AssetConverter.js (three.js bridge)                 │  │
│  │  ├─ CreateTextureFromPixels(...)                    │  │
│  │  ├─ CreateMeshGeometry(...)                         │  │
│  │  ├─ CompositeAvatarWithMod(...)                     │  │
│  │  └─ UpdateSkeletonBinding(...)                      │  │
│  │                                                       │  │
│  │  IndexedDB Driver (async mod persistence)            │  │
│  └──────────────────────────────────────────────────────┘  │
│                          │                                  │
│                          ▼                                  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  Rendering Layer (three.js + WebGL)                  │  │
│  ├──────────────────────────────────────────────────────┤  │
│  │  THREE.Scene (base avatar + mod geometries)          │  │
│  │  THREE.Camera (orbit controls)                       │  │
│  │  THREE.Renderer (WebGL2 if supported)                │  │
│  │  THREE.Skeleton (attachment point bindings)          │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                              │
│  Browser Storage Layer                                       │
│  ├─ IndexedDB (mod cache)                                   │
│  └─ localStorage (UI preferences)                           │
│                                                              │
└────────────────────────────────────────────────────────────┘
```

---

## Data Flow: Upload → Preview

### 1. User Uploads Mod (.unityFS)

```
User selects file
     ↓
FileInput.OnChange event (Blazor)
     ↓
ModUploadComponent.HandleFileSelected (C#)
     ↓
ModLoader.ParseModAsync(fileStream)
     ├─ AssetsManager.LoadFiles → reads bundle header
     ├─ BundleFile.Decompress → decompresses LZMA/LZ4
     ├─ SerializedFile.Parse → reads TypeTree + objects
     ├─ ObjectReader.Deserialize → instantiates Texture2D, Mesh, Avatar
     ├─ Texture2DConverter.DecodeTexture2D → BGRA32 pixels
     └─ Returns: ModAssets { textures: Texture2D[], meshes: Mesh[], avatar: Avatar }
     ↓
Serialize ModAssets to JSON (C#)
     ↓
JSInterop.InvokeAsync("buildModScene", jsonAssets)
     ↓
AssetConverter.js builds THREE.Scene
     ├─ CreateTextureFromPixels (RGBA WebGLTexture)
     ├─ CreateMeshGeometry (THREE.BufferGeometry)
     └─ AttachToSkeleton (bind to Avatar bones)
     ↓
Render preview in canvas
     ↓
ModUploadComponent.OnModConfirmed
     ├─ Cache in IndexedDB
     └─ Add to "Saved Mods" list
```

---

## Component Details

### C# Components

#### **ModLoader** (new service)
**File:** `AssetStudio.ModViewer/Services/ModLoader.cs`

```csharp
public class ModLoader
{
    private readonly AssetsManager assetsManager;
    private readonly IFileSystem fileSystem;
    private readonly ILogger logger;

    public async Task<ModAssets> ParseModAsync(Stream unityFsStream)
    {
        // 1. Load bundle
        var bundleFile = new BundleFile(unityFsStream);
        await bundleFile.Decompress();

        // 2. Parse serialized file
        var serializedFile = new SerializedFile(bundleFile.stream);
        serializedFile.ReadSerializedFileAndAssets();

        // 3. Extract target classes
        var textures = serializedFile.Objects
            .OfType<Texture2D>()
            .ToList();
        
        var meshes = serializedFile.Objects
            .OfType<Mesh>()
            .ToList();
        
        var avatar = serializedFile.Objects
            .OfType<Avatar>()
            .FirstOrDefault();

        var renderers = serializedFile.Objects
            .OfType<SkinnedMeshRenderer>()
            .ToList();

        return new ModAssets
        {
            Textures = textures,
            Meshes = meshes,
            Avatar = avatar,
            Renderers = renderers
        };
    }
}

public class ModAssets
{
    public List<Texture2D> Textures { get; set; }
    public List<Mesh> Meshes { get; set; }
    public Avatar Avatar { get; set; }
    public List<SkinnedMeshRenderer> Renderers { get; set; }
}
```

#### **IFileSystem** (abstraction)
**File:** `AssetStudio.ModViewer/Services/IFileSystem.cs`

Adapts `File.Open()`, `Directory.GetFiles()`, `Path.Combine()` to WASM environment (File API for uploads, IndexedDB for caching).

```csharp
public interface IFileSystem
{
    Task<Stream> OpenReadAsync(string path);
    Task<string[]> GetFilesAsync(string directory, string pattern = "*");
    string Combine(params string[] parts);
}
```

#### **IndexedDBCache** (persistence)
**File:** `AssetStudio.ModViewer/Services/IndexedDBCache.cs`

Wraps IndexedDB.Net Nuget package for storing parsed mods.

```csharp
public class IndexedDBCache
{
    public async Task SaveModAsync(string modId, ModAssets assets);
    public async Task<ModAssets> LoadModAsync(string modId);
    public async Task<string[]> ListModsAsync();
    public async Task DeleteModAsync(string modId);
}
```

---

### JavaScript/TypeScript Components

#### **AssetConverter.js** (three.js bridge)
**File:** `AssetStudio.ModViewer/wwwroot/js/AssetConverter.js`

```javascript
export class AssetConverter {
    /**
     * Create a THREE.Texture from raw RGBA pixels
     * @param {number} width
     * @param {number} height
     * @param {Uint8Array} rgbaBytes - BGRA32 byte array from C#
     * @returns {THREE.Texture}
     */
    static CreateTextureFromPixels(width, height, rgbaBytes) {
        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const ctx = canvas.getContext('2d');
        const imageData = ctx.createImageData(width, height);
        
        // Convert BGRA → RGBA
        for (let i = 0; i < rgbaBytes.length; i += 4) {
            imageData.data[i] = rgbaBytes[i + 2];     // R
            imageData.data[i + 1] = rgbaBytes[i + 1]; // G
            imageData.data[i + 2] = rgbaBytes[i];     // B
            imageData.data[i + 3] = rgbaBytes[i + 3]; // A
        }
        
        ctx.putImageData(imageData, 0, 0);
        const texture = new THREE.CanvasTexture(canvas);
        texture.magFilter = THREE.LinearFilter;
        texture.minFilter = THREE.LinearMipMapLinearFilter;
        return texture;
    }

    /**
     * Create THREE.BufferGeometry from vertex data
     * @param {Float32Array} vertices - flat array [x,y,z, x,y,z, ...]
     * @param {Float32Array} normals
     * @param {Float32Array} uvs
     * @param {Uint32Array} indices
     * @returns {THREE.BufferGeometry}
     */
    static CreateMeshGeometry(vertices, normals, uvs, indices) {
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.BufferAttribute(vertices, 3));
        geometry.setAttribute('normal', new THREE.BufferAttribute(normals, 3));
        geometry.setAttribute('uv', new THREE.BufferAttribute(uvs, 2));
        geometry.setIndex(new THREE.BufferAttribute(indices, 1));
        return geometry;
    }

    /**
     * Composite base avatar + mod
     * @param {Object} baseAssets - parsed base avatar JSON
     * @param {Object} modAssets - parsed mod JSON
     * @returns {THREE.Scene}
     */
    static CompositeAvatarWithMod(baseAssets, modAssets) {
        const scene = new THREE.Scene();
        
        // Load base avatar meshes
        baseAssets.meshes.forEach(mesh => {
            const geometry = this.CreateMeshGeometry(
                new Float32Array(mesh.vertices),
                new Float32Array(mesh.normals),
                new Float32Array(mesh.uvs),
                new Uint32Array(mesh.indices)
            );
            const material = new THREE.MeshStandardMaterial({
                map: new THREE.CanvasTexture(...), // from baseAssets.textures
                side: THREE.DoubleSide
            });
            const meshObj = new THREE.Mesh(geometry, material);
            scene.add(meshObj);
        });
        
        // Load mod meshes, positioned via skeleton
        if (modAssets.avatar && modAssets.avatar.skeleton) {
            this.BindModToSkeleton(scene, modAssets, baseAssets.avatar.skeleton);
        }
        
        return scene;
    }

    /**
     * Attach mod geometry to base skeleton (preserves attachment points)
     */
    static BindModToSkeleton(scene, modAssets, baseSkeleton) {
        // Map mod mesh to bone (e.g., "Hat" → "Head", "Outfit" → "Chest")
        modAssets.meshes.forEach(modMesh => {
            const attachmentBone = this.FindAttachmentBone(modMesh.name, baseSkeleton);
            if (attachmentBone) {
                // Create THREE mesh
                const geom = this.CreateMeshGeometry(...);
                const mat = new THREE.MeshStandardMaterial({...});
                const mesh = new THREE.Mesh(geom, mat);
                
                // Position at bone
                mesh.position.copy(attachmentBone.position);
                mesh.quaternion.copy(attachmentBone.quaternion);
                
                scene.add(mesh);
            }
        });
    }

    static FindAttachmentBone(modMeshName, skeleton) {
        // Heuristic: match "Hat" → "Head", "Outfit" → "Chest", etc.
        const mapping = {
            'hat': 'Head',
            'outfit': 'Chest',
            'shoe': 'LeftFoot',
            // ...
        };
        const boneName = mapping[modMeshName.toLowerCase()] || modMeshName;
        return skeleton.bones.find(b => b.name === boneName);
    }
}
```

---

### Blazor Components

#### **ModUploadComponent.razor**

```razor
@page "/upload-mod"
@inject IJSRuntime JS
@using AssetStudio.ModViewer.Services

<div class="mod-upload-container">
    <h2>Upload Avatar Mod</h2>
    
    <InputFile @onchange="HandleFileSelected" accept=".unityfs" />
    
    @if (isLoading)
    {
        <div class="progress">
            <p>Parsing mod... @loadingMessage</p>
        </div>
    }
    
    @if (error != null)
    {
        <div class="alert alert-danger">@error</div>
    }
    
    @if (currentMod != null)
    {
        <div class="preview-section">
            <div id="canvas-container" style="width: 100%; height: 600px;"></div>
            <button @onclick="ConfirmMod" class="btn btn-primary">Add to My Mods</button>
        </div>
    }
</div>

@code {
    private ModLoader modLoader;
    private bool isLoading = false;
    private string loadingMessage = "";
    private ModAssets currentMod = null;
    private string error = null;

    private async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        isLoading = true;
        error = null;
        
        try
        {
            var file = e.File;
            using var stream = file.OpenReadStream(maxAllowedSize: 500_000_000); // 500 MB
            
            modLoader = new ModLoader(/* deps */);
            currentMod = await modLoader.ParseModAsync(stream);
            
            // Serialize and send to JS
            var json = JsonSerializer.Serialize(currentMod);
            await JS.InvokeVoidAsync("AssetConverter.buildScene", json);
        }
        catch (Exception ex)
        {
            error = $"Failed to parse mod: {ex.Message}";
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task ConfirmMod()
    {
        // Save to IndexedDB
        var cache = new IndexedDBCache(JS);
        await cache.SaveModAsync(Guid.NewGuid().ToString(), currentMod);
    }
}
```

---

## Key Design Decisions

| Decision | Rationale | Alternative Rejected |
|----------|-----------|----------------------|
| **three.js for rendering** | Battle-tested, large ecosystem, good WebGL2 support | babylon.js (also viable, slightly heavier) |
| **IndexedDB for caching** | Structured storage, 50+ MB quota per origin, persistent | localStorage (too small), Service Worker cache (unstructured) |
| **Serialize to JSON for JS bridge** | Avoids complex C#↔JS object marshaling; easier debugging | Direct WASM interop via shared memory (complex, unsafe) |
| **Skeletal binding via heuristics** | Simple, fast; attachment points preserved without animation | Full skeleton rigging (saves for Phase 5) |
| **Static base avatar** | Avoids complexity of loading avatar at runtime; faster startup | Dynamic download (adds CDN + versioning complexity) |
| **Separate ModViewer project** | Cleaner dependency isolation; allows WASM build separate from CLI/GUI | Single AssetStudio.sln (couples WASM target) |

---

## Integration Points with AssetStudio

| Component | Usage | Required Changes |
|-----------|-------|-----------------|
| **AssetsManager.cs** | Core file loading + parsing | Add IFileSystem parameter to constructor |
| **BundleFile.cs** | Bundle decompression | None (pure managed code) |
| **Texture2DConverter.cs** | RGBA decode | Expose DecodeTexture2D as public static |
| **Mesh.cs** | Geometry extraction | Add ToJSON() serialization method |
| **Avatar.cs** | Skeleton hierarchy | Add ToJSON() for bone data |
| **ILogger.cs** | Logging abstraction | Already an interface ✓ |

---

## Performance Considerations

### Memory Budget
- Base avatar (uncompressed): ~30 MB
- Per mod (typical): ~5–15 MB
- three.js scene graph: ~2–3 MB per 100K vertices
- **Target:** Support 10 mods simultaneously on 2GB WASM heap

### Optimization Strategies
1. **Lazy load textures:** Only decode textures as they're displayed
2. **Web Workers:** Offload Texture2D decoding to background thread
3. **Geometry pooling:** Reuse BufferGeometry for similar meshes
4. **LOD mods:** Generate lower-poly versions for complex mods

### Benchmarks
- Bundle decompress: target < 2 sec per mod (LZ4 is fast; LZMA slower)
- Texture decode: target < 500 ms per 2K texture
- Scene composition: target < 1 sec for 5 mods
- Frame rate: target 60 FPS with 6+ mods in view

---

## Error Handling Strategy

| Error | Scenario | Recovery |
|-------|----------|----------|
| **Corrupt bundle** | Malformed .unityFS file | Graceful error dialog; suggest re-download |
| **Unsupported version** | Unity version too old/new | Log warning; attempt best-effort parse |
| **Missing attachment point** | Mod references bone that doesn't exist | Skip mod or use fallback attachment |
| **OOM (OutOfMemory)** | Too many mods loaded | Automatically unload oldest mod; warn user |
| **WebGL context lost** | Browser tab backgrounded too long | Restore context + re-render scene |

---

## Security Considerations

- **WASM bytecode:** No sensitive code (all parsing is public)
- **User uploads:** Validate file signature (should start with `UnityFS\0`)
- **IndexedDB:** Origin-isolated; no cross-site access
- **External assets:** None (avatar + mods bundled locally)
