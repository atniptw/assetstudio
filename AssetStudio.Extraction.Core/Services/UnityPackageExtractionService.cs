using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using AssetStudio.Extraction.Core.Abstractions;
using AssetStudio.Extraction.Core.Models;

namespace AssetStudio.Extraction.Core.Services
{
    public class UnityPackageExtractionService : IUnityPackageExtractionService
    {
        private readonly ExtractionConfigurationService extractionConfigurationService;
        private IExtractionLogger? logger;
        private readonly int maxTextureCount;
        private readonly int maxTextureBytesPerTexture;
        private readonly int maxTextureBytesTotal;
        private readonly int maxMeshCount;

        public UnityPackageExtractionService(ExtractionConfigurationService extractionConfigurationService)
        {
            this.extractionConfigurationService = extractionConfigurationService;
            ExtractionOptions options = extractionConfigurationService.MergeOptions(null);
            maxTextureCount = options.MaxTextureCount;
            maxTextureBytesPerTexture = options.MaxTextureBytesPerTexture;
            maxTextureBytesTotal = options.MaxTextureBytesTotal;
            maxMeshCount = options.MaxMeshCount;
        }

        /// <summary>
        /// Extracts scene data from a unitypackage (ZIP) stream
        /// </summary>
        public async Task<ExtractionSceneData> ExtractAsync(byte[] packageBytes, IExtractionLogger? logger = null)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var packageEntries = ReadUnityPackageEntries(packageBytes);

                this.logger = logger;

                var sceneData = new ExtractionSceneData
                {
                    Name = "ExtractedContent",
                    Meshes = new(),
                    Textures = new(),
                    Materials = new(),
                    Bones = new()
                };

                Log("info", $"Processing unitypackage with {packageEntries.Count} entries");

                // Extract asset files from unitypackage structure
                // unitypackages are organized as: <guid>/<type>/<actual_name>
                var assetFiles = new Dictionary<string, byte[]>();
                var pathnameByGuid = new Dictionary<string, string>();

                foreach (var entry in packageEntries)
                {
                    var fullName = entry.FullName;
                    if (string.IsNullOrWhiteSpace(fullName))
                        continue;

                    var parts = fullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                        continue;

                    var guid = parts[0];
                    var leafName = parts[^1];
                    if (!leafName.Equals("pathname", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var pathname = Encoding.UTF8.GetString(entry.Data).Trim('\0', '\r', '\n', ' ');
                    if (!string.IsNullOrWhiteSpace(pathname))
                    {
                        pathnameByGuid[guid] = pathname;
                    }
                }

                foreach (var entry in packageEntries)
                {
                    var fullName = entry.FullName;
                    if (string.IsNullOrWhiteSpace(fullName))
                        continue;

                    if (fullName.EndsWith("/pathname", StringComparison.OrdinalIgnoreCase) ||
                        fullName.EndsWith("/asset.meta", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parts = fullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                        continue;

                    var guid = parts[0];
                    var leafName = parts[^1];
                    if (leafName.Equals("asset", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = pathnameByGuid.TryGetValue(guid, out var pathname) && !string.IsNullOrWhiteSpace(pathname)
                            ? pathname
                            : fullName;
                        assetFiles[key] = entry.Data;
                    }
                }

                Log("info", $"Found {assetFiles.Count} asset files in package");

                // Load assets using AssetsManager
                if (assetFiles.Count > 0)
                {
                    await LoadAssetsIntoSceneData(assetFiles, pathnameByGuid, sceneData);
                }
                else
                {
                    Log("warn", "No asset files found in unitypackage");
                }

                PopulateAttachmentAnchors(sceneData);

                timer.Stop();
                Log("info", $"Extraction complete in {timer.ElapsedMilliseconds}ms");

                this.logger = null;
                return sceneData;
            }
            catch (Exception ex)
            {
                Log("error", $"Extraction failed: {ex.Message}");
                this.logger = null;
                throw;
            }
        }

        public async Task<ExtractionSceneData> ExtractStaticAssetAsync(
            byte[] assetBytes,
            string sourceName,
            string? bodyPartTag = null,
            Dictionary<string, byte[]>? companionFiles = null,
            IExtractionLogger? logger = null)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                this.logger = logger;

                var sceneData = new ExtractionSceneData
                {
                    Name = Path.GetFileNameWithoutExtension(sourceName) ?? "StaticAsset",
                    Meshes = new(),
                    Textures = new(),
                    Materials = new(),
                    Bones = new()
                };

                var key = string.IsNullOrWhiteSpace(sourceName) ? "asset.hhh" : sourceName;
                var assetFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [key] = assetBytes
                };

                if (companionFiles != null)
                {
                    foreach (var companion in companionFiles)
                    {
                        if (string.IsNullOrWhiteSpace(companion.Key) || companion.Value == null || companion.Value.Length == 0)
                            continue;

                        if (!assetFiles.ContainsKey(companion.Key))
                        {
                            assetFiles[companion.Key] = companion.Value;
                        }
                    }
                }

                Log("info", $"Extracting static asset: {key} ({assetBytes.Length:N0} bytes)");
                Log("info", $"Static extraction context includes {assetFiles.Count} files");
                await LoadStaticAssetIntoSceneData(assetFiles, sceneData, bodyPartTag);
                PopulateAttachmentAnchors(sceneData);

                timer.Stop();
                Log("info", $"Static extraction complete in {timer.ElapsedMilliseconds}ms");

                this.logger = null;
                return sceneData;
            }
            catch (Exception ex)
            {
                Log("error", $"Static extraction failed: {ex.Message}");
                this.logger = null;
                throw;
            }
        }

        private async Task LoadStaticAssetIntoSceneData(Dictionary<string, byte[]> assetFiles, ExtractionSceneData sceneData, string? bodyPartTag)
        {
            if (assetFiles == null || assetFiles.Count == 0)
                return;

            var tempRoot = Path.Combine(Path.GetTempPath(), $"assetstudio-static-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            try
            {
                var manager = new AssetsManager
                {
                    Silent = true,
                    SkipProcess = false,
                    Game = GameManager.GetGame(GameType.Normal)
                };

                var tempFiles = new List<string>(assetFiles.Count);
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in assetFiles)
                {
                    var fileName = Path.GetFileName(string.IsNullOrWhiteSpace(kvp.Key) ? "asset.hhh" : kvp.Key);
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        fileName = "asset.hhh";
                    }

                    fileName = GetUniqueFileName(fileName, usedNames);

                    var tempPath = Path.Combine(tempRoot, fileName);
                    await File.WriteAllBytesAsync(tempPath, kvp.Value);
                    tempFiles.Add(tempPath);
                }

                manager.LoadFiles(tempFiles.ToArray());
                Log("info", $"Loaded {manager.assetsFileList.Count} serialized files from static asset");

                ExtractMeshesAndTextures(manager, sceneData);
                NormalizeStaticMeshTransformsForTag(manager, sceneData, bodyPartTag);
                Log("info", $"Extracted {sceneData.Materials.Count} materials and {sceneData.Textures.Count} textures");
            }
            finally
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch
                {
                }
            }
        }

        private static string GetUniqueFileName(string fileName, HashSet<string> usedNames)
        {
            if (usedNames.Add(fileName))
                return fileName;

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var suffix = 1;
            while (true)
            {
                var candidate = $"{baseName}_{suffix}{extension}";
                if (usedNames.Add(candidate))
                    return candidate;

                suffix++;
            }
        }

        private List<UnityPackageEntry> ReadUnityPackageEntries(byte[] packageBytes)
        {
            if (packageBytes == null || packageBytes.Length < 4)
                throw new InvalidDataException("Unitypackage bytes are empty or invalid");

            if (IsZip(packageBytes))
            {
                Log("info", "Detected unitypackage format: zip");
                return ReadZipEntries(packageBytes);
            }

            if (IsGZip(packageBytes))
            {
                Log("info", "Detected unitypackage format: gzip/tar");
                return ReadTarGzipEntries(packageBytes);
            }

            throw new InvalidDataException("Unsupported unitypackage format (expected zip or gzip/tar)");
        }

        private static bool IsZip(byte[] bytes)
        {
            return bytes.Length >= 4 &&
                   bytes[0] == 0x50 &&
                   bytes[1] == 0x4B &&
                   (bytes[2] == 0x03 || bytes[2] == 0x05 || bytes[2] == 0x07) &&
                   (bytes[3] == 0x04 || bytes[3] == 0x06 || bytes[3] == 0x08);
        }

        private static bool IsGZip(byte[] bytes)
        {
            return bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;
        }

        private List<UnityPackageEntry> ReadZipEntries(byte[] packageBytes)
        {
            using var stream = new MemoryStream(packageBytes);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var entries = new List<UnityPackageEntry>(archive.Entries.Count);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.FullName) || entry.FullName.EndsWith('/'))
                    continue;

                entries.Add(new UnityPackageEntry
                {
                    FullName = entry.FullName,
                    Data = ReadZipEntry(entry)
                });
            }

            return entries;
        }

        private List<UnityPackageEntry> ReadTarGzipEntries(byte[] packageBytes)
        {
            using var compressedStream = new MemoryStream(packageBytes);
            using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var tarStream = new MemoryStream();
            gzipStream.CopyTo(tarStream);

            var entries = new List<UnityPackageEntry>();
            var tarBytes = tarStream.ToArray();
            var offset = 0;

            while (offset + 512 <= tarBytes.Length)
            {
                var header = new ReadOnlySpan<byte>(tarBytes, offset, 512);
                if (header.ToArray().All(b => b == 0))
                    break;

                var name = ReadTarString(header.Slice(0, 100));
                var prefix = ReadTarString(header.Slice(345, 155));
                var fullName = string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}";
                var size = ReadTarOctal(header.Slice(124, 12));

                offset += 512;
                if (size < 0 || offset + size > tarBytes.Length)
                    break;

                if (!string.IsNullOrWhiteSpace(fullName) && !fullName.EndsWith('/'))
                {
                    var data = new byte[size];
                    Buffer.BlockCopy(tarBytes, offset, data, 0, size);
                    entries.Add(new UnityPackageEntry
                    {
                        FullName = fullName,
                        Data = data
                    });
                }

                var paddedSize = ((size + 511) / 512) * 512;
                offset += paddedSize;
            }

            return entries;
        }

        private static string ReadTarString(ReadOnlySpan<byte> bytes)
        {
            var text = Encoding.UTF8.GetString(bytes);
            var nullIndex = text.IndexOf('\0');
            return (nullIndex >= 0 ? text[..nullIndex] : text).Trim();
        }

        private static int ReadTarOctal(ReadOnlySpan<byte> bytes)
        {
            var text = Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
            return string.IsNullOrEmpty(text) ? 0 : Convert.ToInt32(text, 8);
        }

        private byte[] ReadZipEntry(ZipArchiveEntry entry)
        {
            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            return ms.ToArray();
        }

        private async Task LoadAssetsIntoSceneData(Dictionary<string, byte[]> assetFiles, Dictionary<string, string> pathnameByGuid, ExtractionSceneData sceneData)
        {
            try
            {
                var manager = new AssetsManager
                {
                    Silent = true,
                    SkipProcess = false
                };

                var loadFileMethod = typeof(AssetsManager).GetMethod(
                    "LoadFile",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(FileReader) },
                    null);
                var readAssetsMethod = typeof(AssetsManager).GetMethod(
                    "ReadAssets",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var processAssetsMethod = typeof(AssetsManager).GetMethod(
                    "ProcessAssets",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (loadFileMethod == null || readAssetsMethod == null || processAssetsMethod == null)
                    throw new MissingMethodException("Unable to access AssetsManager internal loading methods");

                var openStreams = new List<MemoryStream>();

                // Load each asset file
                int loadedAssets = 0;
                try
                {
                    foreach (var kvp in assetFiles)
                    {
                        try
                        {
                            var assetStream = new MemoryStream(kvp.Value, writable: false);
                            openStreams.Add(assetStream);

                            var reader = new FileReader(kvp.Key, assetStream);
                            loadFileMethod.Invoke(manager, new object[] { reader });
                            loadedAssets++;
                        }
                        catch (Exception ex)
                        {
                            Log("warn", $"Failed to load asset {kvp.Key}: {ex.InnerException?.Message ?? ex.Message}");
                        }
                    }

                    readAssetsMethod.Invoke(manager, null);
                    processAssetsMethod.Invoke(manager, null);
                }
                finally
                {
                    foreach (var stream in openStreams)
                    {
                        stream.Dispose();
                    }
                }

                Log("info", $"Loaded {loadedAssets} assets from package");

                // Extract meshes and textures from manager
                ExtractMeshesAndTextures(manager, sceneData);

                if (sceneData.Meshes.Count == 0)
                {
                    const string message = "No meshes were extracted via canonical parser path.";
                    Log("error", message);
                    throw new InvalidDataException(message);
                }

                Log("info", $"Extracted {sceneData.Materials.Count} materials and {sceneData.Textures.Count} textures");
            }
            catch (Exception ex)
            {
                Log("error", $"Failed to load assets from unitypackage: {ex.Message}");
                throw;
            }
        }

        private static readonly string[] AnchorTags =
        {
            "head", "neck", "body", "hip", "leftarm", "rightarm", "leftleg", "rightleg", "world"
        };

        private static readonly Dictionary<string, string[]> ExplicitAnchorNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["head"] = new[] { "code_head_top" },
            ["neck"] = new[] { "code_head_bot_side" },
            ["body"] = new[] { "ANIM BODY TOP SCALE" },
            ["hip"] = new[] { "ANIM BODY BOT" },
            ["leftarm"] = new[] { "code_arm_l" },
            ["rightarm"] = new[] { "ANIM ARM R SCALE", "code_arm_r" },
            ["leftleg"] = new[] { "ANIM LEG L TOP" },
            ["rightleg"] = new[] { "ANIM LEG R TOP" },
            ["world"] = new[] { "[RIG]" }
        };

        private void PopulateAttachmentAnchors(ExtractionSceneData sceneData)
        {
            var anchors = new List<ExtractionSceneData.AttachmentAnchorData>(AnchorTags.Length);
            foreach (var tag in AnchorTags)
            {
                var anchor = ResolveAnchor(sceneData, tag);
                anchors.Add(anchor);
            }

            sceneData.AttachmentAnchors = anchors;
        }

        private ExtractionSceneData.AttachmentAnchorData ResolveAnchor(ExtractionSceneData sceneData, string tag)
        {
            var explicitNames = ExplicitAnchorNames.TryGetValue(tag, out var values)
                ? values
                : Array.Empty<string>();

            if (explicitNames.Length == 0)
            {
                return new ExtractionSceneData.AttachmentAnchorData
                {
                    Tag = tag,
                    SourceType = "missing-explicit-anchor",
                    Confidence = "explicit-only"
                };
            }

            var bone = sceneData.Bones
                .FirstOrDefault(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.Name) &&
                    explicitNames.Any(name => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

            if (bone != null)
            {
                var resolved = new ExtractionSceneData.AttachmentAnchorData
                {
                    Tag = tag,
                    Position = NormalizeVector3(bone.Position),
                    Rotation = NormalizeQuaternion(bone.Rotation),
                    Scale = NormalizeScale(bone.Scale),
                    SourceType = "bone",
                    SourceName = bone.Name,
                    SourcePath = null,
                    Confidence = "explicit"
                };

                Log(
                    "info",
                    $"Anchor '{tag}' resolved from explicit bone '{bone.Name}' pos=[{FormatVector(resolved.Position)}] rot=[{FormatVector(resolved.Rotation)}] scale=[{FormatVector(resolved.Scale)}]");

                return resolved;
            }

            var mesh = sceneData.Meshes
                .FirstOrDefault(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.Name) &&
                    explicitNames.Any(name => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

            if (mesh != null)
            {
                var resolved = new ExtractionSceneData.AttachmentAnchorData
                {
                    Tag = tag,
                    Position = NormalizeVector3(mesh.Position),
                    Rotation = NormalizeQuaternion(mesh.Rotation),
                    Scale = NormalizeScale(mesh.Scale),
                    SourceType = "mesh",
                    SourceName = mesh.Name,
                    SourcePath = null,
                    Confidence = "explicit"
                };

                Log(
                    "info",
                    $"Anchor '{tag}' resolved from explicit mesh '{mesh.Name}' pos=[{FormatVector(resolved.Position)}] rot=[{FormatVector(resolved.Rotation)}] scale=[{FormatVector(resolved.Scale)}]");

                return resolved;
            }

            Log("warn", $"Anchor '{tag}' missing explicit anchor source");

            return new ExtractionSceneData.AttachmentAnchorData
            {
                Tag = tag,
                SourceType = "missing-explicit-anchor",
                SourceName = null,
                SourcePath = null,
                Confidence = "explicit-only"
            };
        }

        private static float[] NormalizeVector3(float[]? value)
        {
            if (value == null || value.Length < 3)
                return new[] { 0f, 0f, 0f };

            return new[] { value[0], value[1], value[2] };
        }

        private static float[] NormalizeQuaternion(float[]? value)
        {
            if (value == null || value.Length < 4)
                return new[] { 0f, 0f, 0f, 1f };

            return new[] { value[0], value[1], value[2], value[3] };
        }

        private static float[] NormalizeScale(float[]? value)
        {
            if (value == null || value.Length < 3)
                return new[] { 1f, 1f, 1f };

            return new[] { value[0], value[1], value[2] };
        }

        private static float[] RotateVectorByQuaternion(float[] vector, float[] quaternion)
        {
            var x = vector[0];
            var y = vector[1];
            var z = vector[2];

            var qx = quaternion[0];
            var qy = quaternion[1];
            var qz = quaternion[2];
            var qw = quaternion[3];

            var ix = qw * x + qy * z - qz * y;
            var iy = qw * y + qz * x - qx * z;
            var iz = qw * z + qx * y - qy * x;
            var iw = -qx * x - qy * y - qz * z;

            return new[]
            {
                ix * qw + iw * -qx + iy * -qz - iz * -qy,
                iy * qw + iw * -qy + iz * -qx - ix * -qz,
                iz * qw + iw * -qz + ix * -qy - iy * -qx
            };
        }

        private static float[] MultiplyVectorComponents(float[] vector, float[] multiplier)
        {
            return new[]
            {
                vector[0] * multiplier[0],
                vector[1] * multiplier[1],
                vector[2] * multiplier[2]
            };
        }

        private static float[] DivideVectorComponents(float[] vector, float[] divisor)
        {
            return new[]
            {
                SafeDivide(vector[0], divisor[0]),
                SafeDivide(vector[1], divisor[1]),
                SafeDivide(vector[2], divisor[2])
            };
        }

        private static float[] MultiplyQuaternions(float[] a, float[] b)
        {
            return new[]
            {
                a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
                a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
                a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
                a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2]
            };
        }

        private static float[] InvertQuaternion(float[] quaternion)
        {
            var magnitudeSq = quaternion[0] * quaternion[0]
                + quaternion[1] * quaternion[1]
                + quaternion[2] * quaternion[2]
                + quaternion[3] * quaternion[3];

            if (magnitudeSq < 1e-8f)
                return new[] { 0f, 0f, 0f, 1f };

            var inverseMagnitude = 1f / magnitudeSq;
            return new[]
            {
                -quaternion[0] * inverseMagnitude,
                -quaternion[1] * inverseMagnitude,
                -quaternion[2] * inverseMagnitude,
                quaternion[3] * inverseMagnitude
            };
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Math.Abs(divisor) < 1e-6f ? value : value / divisor;
        }

        private static string FormatVector(float[] value)
        {
            return string.Join(", ", value.Select(component => component.ToString("0.###", CultureInfo.InvariantCulture)));
        }

        private void ExtractMeshesAndTextures(AssetsManager manager, ExtractionSceneData sceneData)
        {
            // Extract Mesh objects
            var meshes = manager.assetsFileList
                .SelectMany(f => f.Objects.OfType<Mesh>() ?? new List<Mesh>())
                .ToList();

            Log("info", $"Found {meshes.Count} meshes");

            // Extract Texture2D objects
            var textures = manager.assetsFileList
                .SelectMany(f => f.Objects.OfType<Texture2D>() ?? new List<Texture2D>())
                .ToList();

            Log("info", $"Found {textures.Count} textures");

            // Extract skeleton objects
            var avatars = manager.assetsFileList
                .SelectMany(f => f.Objects.OfType<Avatar>() ?? new List<Avatar>())
                .ToList();

            Log("info", $"Found {avatars.Count} avatars");

            var materialIndexByObjectKey = ExtractPrimaryMaterialsAndTextures(manager, sceneData);
            var materialIndexByMeshKey = BuildMeshMaterialMap(manager, materialIndexByObjectKey);
            var meshTransformByMeshKey = BuildMeshTransformMap(manager);

            // Convert meshes to serializable format
            foreach (var mesh in meshes.Take(maxMeshCount))
            {
                try
                {
                    var meshData = CreateMeshData(mesh);
                    if (meshData != null)
                    {
                        if (materialIndexByMeshKey.TryGetValue(GetObjectKey(mesh), out var materialIndex))
                        {
                            meshData.MaterialIndex = materialIndex;
                        }

                        if (meshTransformByMeshKey.TryGetValue(GetObjectKey(mesh), out var meshTransform))
                        {
                            meshData.Position = meshTransform.Position;
                            meshData.Rotation = meshTransform.Rotation;
                            meshData.Scale = meshTransform.Scale;
                        }

                        sceneData.Meshes.Add(meshData);
                    }
                }
                catch (Exception ex)
                {
                    Log("warn", $"Failed to convert mesh {mesh.Name}: {ex.Message}");
                }
            }

            // Convert skeleton hierarchy
            if (avatars.Count > 0)
            {
                try
                {
                    ExtractSkeletonBones(avatars[0], sceneData);
                }
                catch (Exception ex)
                {
                    Log("warn", $"Failed to extract avatar bones: {ex.Message}");
                }
            }
        }

        private ExtractionSceneData.MeshData CreateMeshData(Mesh mesh)
        {
            if (mesh == null)
                return null;

            var meshData = new ExtractionSceneData.MeshData
            {
                Name = mesh.Name ?? "Mesh",
                Vertices = ConvertVectorArrayToFloatArray(mesh.m_Vertices),
                Indices = mesh.m_Indices?.ToArray() ?? Array.Empty<uint>(),
                Normals = mesh.m_Normals != null ? ConvertVectorArrayToFloatArray(mesh.m_Normals) : Array.Empty<float>(),
                UV = mesh.m_UV0 != null ? ConvertVectorArrayToFloatArray(mesh.m_UV0) : Array.Empty<float>(),
                MaterialIndex = 0,
                Position = new[] { 0f, 0f, 0f },
                Rotation = new[] { 0f, 0f, 0f, 1f },
                Scale = new[] { 1f, 1f, 1f }
            };

            return meshData;
        }

        private Dictionary<string, int> ExtractPrimaryMaterialsAndTextures(AssetsManager manager, ExtractionSceneData sceneData)
        {
            var materialIndexByObjectKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var textureIndexByObjectKey = new Dictionary<string, int>(StringComparer.Ordinal);

            int totalTextureBytes = 0;
            int skippedByCap = 0;
            int decodeFailures = 0;

            var materials = manager.assetsFileList
                .SelectMany(f => f.Objects.OfType<Material>() ?? new List<Material>())
                .ToList();

            Log("info", $"Found {materials.Count} materials");

            foreach (var material in materials)
            {
                if (material == null)
                    continue;

                try
                {
                    var materialName = material.Name ?? "Material";
                    var textureKeys = material.m_SavedProperties?.m_TexEnvs?
                        .Select(entry => entry.Key)
                        .Where(key => !string.IsNullOrWhiteSpace(key))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray() ?? Array.Empty<string>();

                    var baseColor = GetMaterialColor(material, "_BaseColor", "_Color") ?? new[] { 1f, 1f, 1f, 1f };
                    var metallic = GetMaterialFloat(material, "_Metallic", "_Metalness");
                    var smoothness = GetMaterialFloat(material, "_Glossiness", "_Smoothness");
                    var roughness = Math.Clamp(1f - smoothness, 0f, 1f);

                    var albedoTextureIndex = ResolveMaterialTextureIndex(
                        material,
                        textureIndexByObjectKey,
                        sceneData,
                        ref totalTextureBytes,
                        ref skippedByCap,
                        ref decodeFailures,
                        out var resolvedAlbedoKey,
                        "_BaseMap",
                        "_BaseColorMap",
                        "_BaseColorTexture",
                        "_ColorMap",
                        "_MainColorMap",
                        "_MainTex",
                        "_DiffuseMap",
                        "_AlbedoMap",
                        "_BaseTex",
                        "_Tex");

                    var normalTextureIndex = ResolveMaterialTextureIndex(
                        material,
                        textureIndexByObjectKey,
                        sceneData,
                        ref totalTextureBytes,
                        ref skippedByCap,
                        ref decodeFailures,
                        out var resolvedNormalKey,
                        "_BumpMap",
                        "_NormalMap",
                        "_DetailNormalMap",
                        "_NormalTexture");

                    if (albedoTextureIndex < 0 && textureKeys.Length > 0)
                    {
                        var keyPreview = string.Join(", ", textureKeys.Take(8));
                        Log("info", $"Material '{materialName}' has no resolved albedo texture. Available keys: [{keyPreview}]");
                    }

                    if (textureKeys.Length > 0)
                    {
                        var keyPreview = string.Join(", ", textureKeys.Take(10));
                        Log("info", $"Material '{materialName}': albedoIndex={albedoTextureIndex}, albedoKey={resolvedAlbedoKey ?? "none"}, normalIndex={normalTextureIndex}, normalKey={resolvedNormalKey ?? "none"}, texKeys=[{keyPreview}]");
                    }

                    var materialData = new ExtractionSceneData.MaterialData
                    {
                        Name = materialName,
                        TextureIndex = albedoTextureIndex,
                        AlbedoTextureIndex = albedoTextureIndex,
                        NormalTextureIndex = normalTextureIndex,
                        BaseColor = baseColor,
                        Metallic = metallic,
                        Roughness = roughness
                    };

                    materialIndexByObjectKey[GetObjectKey(material)] = sceneData.Materials.Count;
                    sceneData.Materials.Add(materialData);
                }
                catch (Exception ex)
                {
                    Log("warn", $"Failed to parse material {material.Name}: {ex.Message}");
                }
            }

            if (skippedByCap > 0)
            {
                Log("warn", $"Skipped {skippedByCap} primary-path textures due to safety caps");
            }

            if (decodeFailures > 0)
            {
                Log("warn", $"Failed to decode {decodeFailures} primary-path textures");
            }

            return materialIndexByObjectKey;
        }

        private Dictionary<string, int> BuildMeshMaterialMap(AssetsManager manager, Dictionary<string, int> materialIndexByObjectKey)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            var gameObjects = manager.assetsFileList
                .SelectMany(f => f.Objects.OfType<GameObject>() ?? new List<GameObject>())
                .ToList();

            foreach (var gameObject in gameObjects)
            {
                if (!TryGetRenderer(gameObject, out var renderer))
                    continue;

                var materialIndex = ResolveRendererMaterialIndex(renderer, materialIndexByObjectKey);
                if (materialIndex < 0)
                    continue;

                var mesh = ResolveRendererMesh(gameObject, renderer);
                if (mesh == null)
                    continue;

                var meshKey = GetObjectKey(mesh);
                if (!map.ContainsKey(meshKey))
                {
                    map[meshKey] = materialIndex;
                }
            }

            return map;
        }

        private Dictionary<string, MeshTransformData> BuildMeshTransformMap(AssetsManager manager)
        {
            var map = new Dictionary<string, MeshTransformData>(StringComparer.Ordinal);
            var transformWorldByKey = new Dictionary<string, MeshTransformData>(StringComparer.Ordinal);
            var gameObjects = manager.assetsFileList
                .SelectMany(f => f.Objects.OfType<GameObject>() ?? new List<GameObject>())
                .ToList();

            foreach (var gameObject in gameObjects)
            {
                if (!TryGetRenderer(gameObject, out var renderer))
                    continue;

                var mesh = ResolveRendererMesh(gameObject, renderer);
                if (mesh == null)
                    continue;

                if (gameObject?.m_Transform == null)
                    continue;

                var meshKey = GetObjectKey(mesh);
                if (map.ContainsKey(meshKey))
                    continue;

                var transform = gameObject.m_Transform;
                var world = ComputeTransformWorld(transform, transformWorldByKey, new HashSet<string>(StringComparer.Ordinal));
                if (world == null)
                    continue;

                map[meshKey] = world;
            }

            return map;
        }

        private MeshTransformData? ComputeTransformWorld(
            Transform transform,
            Dictionary<string, MeshTransformData> cache,
            HashSet<string> activeStack)
        {
            if (transform == null)
                return null;

            var transformKey = GetObjectKey(transform);
            if (string.IsNullOrWhiteSpace(transformKey))
                return CreateLocalTransformData(transform);

            if (cache.TryGetValue(transformKey, out var cached))
                return cached;

            if (!activeStack.Add(transformKey))
                return CreateLocalTransformData(transform);

            try
            {
                var local = CreateLocalTransformData(transform);
                if (transform.m_Father != null && transform.m_Father.TryGet(out var parentTransform) && parentTransform != null)
                {
                    var parentWorld = ComputeTransformWorld(parentTransform, cache, activeStack);
                    if (parentWorld != null)
                    {
                        var scaledLocal = MultiplyVectorComponents(local.Position, parentWorld.Scale);
                        var rotatedLocal = RotateVectorByQuaternion(scaledLocal, parentWorld.Rotation);
                        local = new MeshTransformData
                        {
                            Position = new[]
                            {
                                parentWorld.Position[0] + rotatedLocal[0],
                                parentWorld.Position[1] + rotatedLocal[1],
                                parentWorld.Position[2] + rotatedLocal[2]
                            },
                            Rotation = MultiplyQuaternions(parentWorld.Rotation, local.Rotation),
                            Scale = MultiplyVectorComponents(local.Scale, parentWorld.Scale)
                        };
                    }
                }

                cache[transformKey] = local;
                return local;
            }
            finally
            {
                activeStack.Remove(transformKey);
            }
        }

        private static MeshTransformData CreateLocalTransformData(Transform transform)
        {
            return new MeshTransformData
            {
                Position = new[] { transform.m_LocalPosition.X, transform.m_LocalPosition.Y, transform.m_LocalPosition.Z },
                Rotation = new[] { transform.m_LocalRotation.X, transform.m_LocalRotation.Y, transform.m_LocalRotation.Z, transform.m_LocalRotation.W },
                Scale = new[] { transform.m_LocalScale.X, transform.m_LocalScale.Y, transform.m_LocalScale.Z }
            };
        }

        private void NormalizeStaticMeshTransformsForTag(AssetsManager manager, ExtractionSceneData sceneData, string? bodyPartTag)
        {
            if (sceneData.Meshes.Count == 0)
                return;

            var normalizedTag = NormalizeBodyPartTag(bodyPartTag);
            if (string.IsNullOrWhiteSpace(normalizedTag) || normalizedTag.Equals("world", StringComparison.OrdinalIgnoreCase))
                return;

            if (!TryFindAnchorTransformByTag(manager, normalizedTag, out var anchorTransform))
            {
                Log("warn", $"Static mesh normalization skipped: missing explicit '{normalizedTag}' anchor transform in asset hierarchy");
                return;
            }

            var inverseAnchorRotation = InvertQuaternion(anchorTransform.Rotation);
            for (var i = 0; i < sceneData.Meshes.Count; i++)
            {
                var mesh = sceneData.Meshes[i];
                var worldPosition = NormalizeVector3(mesh.Position);
                var worldRotation = NormalizeQuaternion(mesh.Rotation);
                var worldScale = NormalizeScale(mesh.Scale);

                var positionDelta = new[]
                {
                    worldPosition[0] - anchorTransform.Position[0],
                    worldPosition[1] - anchorTransform.Position[1],
                    worldPosition[2] - anchorTransform.Position[2]
                };

                var localPosition = RotateVectorByQuaternion(DivideVectorComponents(positionDelta, anchorTransform.Scale), inverseAnchorRotation);
                var localRotation = MultiplyQuaternions(inverseAnchorRotation, worldRotation);
                var localScale = DivideVectorComponents(worldScale, anchorTransform.Scale);

                mesh.Position = localPosition;
                mesh.Rotation = localRotation;
                mesh.Scale = localScale;
            }

            Log(
                "info",
                $"Normalized {sceneData.Meshes.Count} static meshes to explicit '{normalizedTag}' anchor local space at [{FormatVector(anchorTransform.Position)}]");
        }

        private bool TryFindAnchorTransformByTag(AssetsManager manager, string tag, out MeshTransformData anchorTransform)
        {
            anchorTransform = null!;

            if (!ExplicitAnchorNames.TryGetValue(tag, out var explicitNames) || explicitNames.Length == 0)
                return false;

            var gameObjects = manager.assetsFileList
                .SelectMany(f => f.Objects.OfType<GameObject>() ?? new List<GameObject>())
                .Where(go => go?.m_Transform != null)
                .ToList();

            if (gameObjects.Count == 0)
                return false;

            var transformCache = new Dictionary<string, MeshTransformData>(StringComparer.Ordinal);
            foreach (var gameObject in gameObjects)
            {
                var name = gameObject.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!explicitNames.Any(candidate => name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var world = ComputeTransformWorld(gameObject.m_Transform, transformCache, new HashSet<string>(StringComparer.Ordinal));
                if (world == null)
                    continue;

                anchorTransform = world;
                return true;
            }

            return false;
        }

        private static string? NormalizeBodyPartTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var normalized = tag.Trim().ToLowerInvariant();
            return AnchorTags.Contains(normalized) ? normalized : null;
        }

        private static bool TryGetRenderer(GameObject gameObject, out Renderer renderer)
        {
            if (gameObject?.m_SkinnedMeshRenderer != null)
            {
                renderer = gameObject.m_SkinnedMeshRenderer;
                return true;
            }

            if (gameObject?.m_MeshRenderer != null)
            {
                renderer = gameObject.m_MeshRenderer;
                return true;
            }

            renderer = null;
            return false;
        }

        private static Mesh ResolveRendererMesh(GameObject gameObject, Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                return skinnedMeshRenderer.m_Mesh.TryGet(out var skinnedMesh) ? skinnedMesh : null;
            }

            if (gameObject?.m_MeshFilter != null && gameObject.m_MeshFilter.m_Mesh.TryGet(out var mesh))
            {
                return mesh;
            }

            return null;
        }

        private static int ResolveRendererMaterialIndex(Renderer renderer, Dictionary<string, int> materialIndexByObjectKey)
        {
            if (renderer?.m_Materials == null || renderer.m_Materials.Count == 0)
                return -1;

            foreach (var materialPtr in renderer.m_Materials)
            {
                if (materialPtr.TryGet(out Material material) &&
                    materialIndexByObjectKey.TryGetValue(GetObjectKey(material), out var materialIndex))
                {
                    return materialIndex;
                }
            }

            return -1;
        }

        private int ResolveMaterialTextureIndex(
            Material material,
            Dictionary<string, int> textureIndexByObjectKey,
            ExtractionSceneData sceneData,
            ref int totalTextureBytes,
            ref int skippedByCap,
            ref int decodeFailures,
            out string? matchedTextureKey,
            params string[] textureKeys)
        {
            matchedTextureKey = null;
            var materialName = material?.Name ?? "Material";
            foreach (var candidate in EnumerateMaterialTextures(material, textureKeys))
            {
                var texture = candidate.Texture;
                var textureKey = GetObjectKey(texture);
                if (textureIndexByObjectKey.TryGetValue(textureKey, out var existingIndex))
                {
                    matchedTextureKey = candidate.Key;
                    return existingIndex;
                }

                if (!TryCreateTextureData(texture, out var textureData, out var encodedSize))
                {
                    decodeFailures++;
                    var streamPath = texture.m_StreamData?.path;
                    var streamSize = texture.image_data?.Size ?? 0;
                    var streamOffset = texture.m_StreamData?.offset ?? 0;
                    var streamLabel = string.IsNullOrWhiteSpace(streamPath) ? "embedded" : streamPath;
                    var versionLabel = texture.version != null && texture.version.Length >= 2
                        ? $"{texture.version[0]}.{texture.version[1]}"
                        : "unknown";
                    var signature = GetTextureDataSignature(texture);
                    Log("warn", $"Texture decode failed for material '{materialName}', key '{candidate.Key}', texture '{texture.Name}', format '{texture.m_TextureFormat}' ({texture.m_Width}x{texture.m_Height}), unity={versionLabel}, platform={texture.platform}, dataSize={streamSize}, streamOffset={streamOffset}, stream='{streamLabel}', signature='{signature}'");
                    continue;
                }

                if (sceneData.Textures.Count >= maxTextureCount ||
                    encodedSize > maxTextureBytesPerTexture ||
                    totalTextureBytes + encodedSize > maxTextureBytesTotal)
                {
                    skippedByCap++;
                    Log("warn", $"Skipped texture for material '{materialName}', key '{candidate.Key}' due to texture safety caps");
                    continue;
                }

                var textureIndex = sceneData.Textures.Count;
                sceneData.Textures.Add(textureData);
                textureIndexByObjectKey[textureKey] = textureIndex;
                totalTextureBytes += encodedSize;
                matchedTextureKey = candidate.Key;
                return textureIndex;
            }

            return -1;
        }

        private static List<(string Key, Texture2D Texture)> EnumerateMaterialTextures(
            Material material,
            params string[] keys)
        {
            var results = new List<(string Key, Texture2D Texture)>();
            var seenTextureKeys = new HashSet<string>(StringComparer.Ordinal);

            if (material?.m_SavedProperties?.m_TexEnvs == null || keys == null || keys.Length == 0)
                return results;

            foreach (var key in keys)
            {
                var texEnv = material.m_SavedProperties.m_TexEnvs
                    .FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(texEnv.Key))
                    continue;

                if (texEnv.Value?.m_Texture == null || !texEnv.Value.m_Texture.TryGet<Texture2D>(out var resolvedTexture))
                    continue;

                var objectKey = GetObjectKey(resolvedTexture);
                if (!seenTextureKeys.Add(objectKey))
                    continue;

                results.Add((texEnv.Key, resolvedTexture));
            }

            return results;
        }

        private bool TryCreateTextureData(Texture2D texture, out ExtractionSceneData.TextureData textureData, out int encodedSize)
        {
            textureData = null;
            encodedSize = 0;

            if (!StableTextureEncoder.TryEncodePngDataUrl(texture, out var encoded) || encoded == null)
                return false;

            textureData = new ExtractionSceneData.TextureData
            {
                Name = encoded.Name,
                Width = encoded.Width,
                Height = encoded.Height,
                Format = encoded.Format,
                DataUrl = encoded.DataUrl
            };
            encodedSize = encoded.EncodedSize;
            return true;
        }

        private static float[] GetMaterialColor(Material material, params string[] keys)
        {
            return StableMaterialPropertyReader.GetColor(material, keys);
        }

        private static float GetMaterialFloat(Material material, params string[] keys)
        {
            if (StableMaterialPropertyReader.TryGetFloat(material, out var value, keys))
                return value;

            throw new InvalidDataException($"Missing required material float property. Keys: {string.Join(", ", keys ?? Array.Empty<string>())}");
        }

        private static string GetObjectKey(AssetStudio.Object obj)
        {
            if (obj == null)
                return string.Empty;

            return $"{obj.assetsFile?.fileName}:{obj.m_PathID}";
        }

        private void ExtractSkeletonBones(Avatar avatar, ExtractionSceneData sceneData)
        {
            if (avatar?.m_Avatar?.m_AvatarSkeleton == null)
                return;

            var skeleton = avatar.m_Avatar.m_AvatarSkeleton;
            var boneCount = skeleton.m_Node.Count;

            Log("info", $"Skeleton has {boneCount} bones");

            // Extract bones from the skeleton
            for (int i = 0; i < boneCount && i < skeleton.m_Node.Count; i++)
            {
                var node = skeleton.m_Node[i];
                if (node == null)
                    continue;

                // Try to get the bone name from m_ID or use index-based name
                var boneName = $"Bone_{i}";
                if (skeleton.m_ID != null && i < skeleton.m_ID.Length)
                {
                    boneName = $"Bone_{skeleton.m_ID[i]}";
                }

                var boneData = new ExtractionSceneData.BoneData
                {
                    Name = boneName,
                    ParentIndex = node.m_ParentId,
                    Position = new[] { 0f, 0f, 0f }, // Default position
                    Rotation = new[] { 0f, 0f, 0f, 1f }, // Default quaternion (identity)
                    Scale = new[] { 1f, 1f, 1f }
                };

                sceneData.Bones.Add(boneData);
            }
        }

        private float[] ConvertVectorArrayToFloatArray(object data)
        {
            // Handle float[] array directly
            if (data is float[] floatArray)
            {
                return floatArray;
            }

            // Handle PackedFloatVector
            if (data != null && data.GetType().Name == "PackedFloatVector")
            {
                try
                {
                    // Use reflection to call UnpackFloats method if available
                    var method = data.GetType().GetMethod("UnpackFloats");
                    if (method != null)
                    {
                        // Try with common parameters
                        var result = method.Invoke(data, new object[] { 3, 12 });
                        if (result is float[] unpacked)
                            return unpacked;
                    }
                }
                catch
                {
                    // Fall through
                }
            }

            return Array.Empty<float>();
        }


        private void Log(string level, string message)
        {
            if (logger == null)
                return;

            switch (level)
            {
                case "info":
                    logger.Info(message);
                    break;
                case "warn":
                    logger.Warn(message);
                    break;
                case "error":
                    logger.Error(message);
                    break;
                default:
                    logger.Info(message);
                    break;
            }
        }

        private static string GetTextureDataSignature(Texture2D texture)
        {
            try
            {
                var data = texture?.image_data?.GetData();
                if (data == null || data.Length == 0)
                    return "empty";

                var head = data.Take(16).ToArray();
                var hex = BitConverter.ToString(head);
                var ascii = new string(head.Select(value => value >= 32 && value <= 126 ? (char)value : '.').ToArray());
                return $"hex={hex};ascii={ascii}";
            }
            catch
            {
                return "unavailable";
            }
        }

        private sealed class MeshTransformData
        {
            public required float[] Position { get; init; }
            public required float[] Rotation { get; init; }
            public required float[] Scale { get; init; }
        }

        private sealed class UnityPackageEntry
        {
            public required string FullName { get; init; }
            public required byte[] Data { get; init; }
        }
    }
}
