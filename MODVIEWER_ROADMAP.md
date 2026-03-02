# AssetStudio.ModViewer – Development Roadmap

**Project:** Blazor WASM app for R.E.P.O avatar modding preview  
**Team:** Solo + AI agent  
**Repository:** Same as AssetStudio (new `/AssetStudio.ModViewer` folder)  
**Timeline:** Open-ended, phased delivery

---

## Phase 1: Foundation (Weeks 1–2)

**Goal:** Get AssetStudio compiling to WASM and basic Blazor app structure.

### Milestones

| Task | Owner | Status |
|------|-------|--------|
| 1.1 Remove FBX/P/Invoke dependencies from AssetStudio.sln | AI | — |
| 1.2 Create IFileSystem abstraction + implement for File API | You | — |
| 1.3 Create Blazor WASM project (AssetStudio.ModViewer.csproj) | You | — |
| 1.4 Test AssetStudio WASM compilation | Both | — |
| 1.5 Embed base R.E.P.O avatar unitypackage in Blazor assets | You | — |

### Deliverables
- AssetStudio compiles to WASM without errors
- Basic Blazor project scaffold with three.js CDN linked
- Base avatar loads on app startup (verified in browser console)

**Effort:** ~40 hours combined

---

## Phase 2: Asset Pipeline (Weeks 3–4)

**Goal:** Stream .unityFS bundles through AssetStudio → extract Texture2D, Mesh, Avatar classes.

### Milestones

| Task | Owner | Status |
|------|-------|--------|
| 2.1 Build ModLoader service in C# (wraps AssetsManager) | You | — |
| 2.2 Extract Texture2D → raw RGBA via Texture2DConverter | Both | — |
| 2.3 Extract Mesh → BufferGeometry format (vertices, normals, indices) | Both | — |
| 2.4 Extract Avatar + SkinnedMeshRenderer → skeletal hierarchy | You | — |
| 2.5 Serialize assets to JSON for JS bridge | You | — |
| 2.6 Implement IndexedDB caching layer | You | — |

### Deliverables
- Upload a test .unityFS mod → ModLoader extracts all geometries/textures without crashes
- JavaScript receives serialized asset JSON
- Parsed mods persist in IndexedDB

**Effort:** ~60 hours (most of the project)

**Risk:** Mesh decompression failures if VertexData stride parsing is off → **mitigation: enable verbose logging in EndianBinaryReader.cs**

---

## Phase 3: Rendering (Weeks 5–6)

**Goal:** Integrate three.js canvas, compose base avatar + mods, handle interaction.

### Milestones

| Task | Owner | Status |
|------|-------|--------|
| 3.1 Build AssetConverter.js (three.js bridge) | You | — |
| 3.2 CreateTextureFromPixels, CreateMeshGeometry functions | You | — |
| 3.3 Implement skeletal hierarchy binding (preserves attachment points) | Both | — |
| 3.4 Composite base avatar + mod in single scene | You | — |
| 3.5 Add orbit controls (camera pan/zoom/rotate) | You | — |
| 3.6 Build Blazor UI for file upload + canvas preview | You | — |

### Deliverables
- three.js canvas renders base avatar correctly
- Upload mod → composite avatar shows mod applied
- Camera controls responsive
- UI is clean and responsive

**Effort:** ~50 hours

---

## Phase 4: Polish & Testing (Weeks 7–8)

**Goal:** Optimize performance, test across browsers, handle edge cases.

### Milestones

| Task | Owner | Status |
|------|-------|--------|
| 4.1 Profile WASM decompression (benchmark bundle parse times) | Both | — |
| 4.2 Optimize texture memory (web workers for decoding?) | AI | — |
| 4.3 Cross-browser testing (Chrome, Firefox, Safari) | You | — |
| 4.4 Error handling for malformed mods | Both | — |
| 4.5 Add mod management UI (view saved mods, delete, reorder) | You | — |
| 4.6 Write user guide (how to create/upload mods) | You | — |

### Deliverables
- WASM decompression < 2 seconds per mod on modern hardware
- Zero crashes with 10+ complex mods loaded
- Works in Chrome, Firefox, Safari (ES2020+ support)
- User-facing README with screenshots

**Effort:** ~40 hours

---

## Phase 5: Deployment & Future Work (Ongoing)

### Launch Checklist
- [ ] Blazor app builds & runs locally (`dotnet run`)
- [ ] GitHub Actions CI configured (build + test on push)
- [ ] Docker container for self-hosting (optional)
- [ ] GitHub Pages or Netlify deployment
- [ ] Sample mods provided for users

### Future Enhancements (Post-Launch)
- [ ] Animation playback (idle poses from AnimationClip)
- [ ] Shader preview (basic material editing)
- [ ] Mod sharing URL (serialize scene state to query params)
- [ ] Export scene as three.js JSON for external viewers
- [ ] WebGL2 performance optimizations
- [ ] Mobile touch controls

---

## Critical Path & Dependencies

```
Phase 1 (WASM Setup)
  └─→ Phase 2 (Asset Pipeline)
        └─→ Phase 3 (Rendering, parallelizable)
        └─→ Phase 4 (Testing & Optimization)
              └─→ Phase 5 (Launch)
```

**Blockers to Watch:**
1. **Phase 1:** AppDomain/File I/O abstraction complexity
2. **Phase 2:** Mesh VertexData decompression correctness (test against known good meshes)
3. **Phase 3:** Three.js skeletal binding (ensure bone IDs match Avatar hierarchy)
4. **Phase 4:** WebAssembly memory limits (stress test with 50+ large mods)

---

## Success Metrics

| Metric | Target | Verification |
|--------|--------|--------------|
| WASM bundle size | < 25 MB | `du -sh bin/Release/net9.0/publish` |
| Parse time / mod | < 2 sec | Benchmark `AssetsManager.LoadAssetsAsync()` |
| Frame rate | 60 FPS | Chrome DevTools Performance tab |
| Browser support | Chrome, Firefox, Safari | Manual testing |
| Mod capacity | 10+ simultaneous | Load 10 mods, check memory usage |

---

## Notes

- **Parallel work:** Phases 3 & 4 UI work can proceed while Phase 2 parsing is being debugged
- **Testing strategy:** Create test suite of 5 R.E.P.O avatar mods (small, medium, complex) early in Phase 2
- **Documentation:** Keep dev guide updated as new patterns emerge
