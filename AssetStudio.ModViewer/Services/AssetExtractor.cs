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
using AssetStudio.Extraction.Core.Services;

namespace AssetStudio.ModViewer.Services
{
    public class AssetExtractor
    {
        private readonly DiagnosticsService diagnostics;
        private const int MaxTextureCount = 24;
        private const int MaxTextureBytesPerTexture = 8 * 1024 * 1024;
        private const int MaxTextureBytesTotal = 48 * 1024 * 1024;

        public AssetExtractor(DiagnosticsService diagnostics)
        {
            this.diagnostics = diagnostics;
        }

        /// <summary>
        /// Extracts avatar data from a unitypackage (ZIP) stream
        /// </summary>
        public async Task<Models.AvatarData> ExtractAvatarAsync(byte[] packageBytes)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var packageEntries = ReadUnityPackageEntries(packageBytes);

                var avatarData = new Models.AvatarData
                {
                    Name = "BaseAvatar",
                    Meshes = new(),
                    Textures = new(),
                    Materials = new(),
                    Bones = new()
                };

                diagnostics.Add("info", $"Processing unitypackage with {packageEntries.Count} entries");

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

                diagnostics.Add("info", $"Found {assetFiles.Count} asset files in package");

                // Load assets using AssetsManager
                if (assetFiles.Count > 0)
                {
                    await LoadAssetsIntoAvatarData(assetFiles, pathnameByGuid, avatarData);
                }
                else
                {
                    diagnostics.Add("warn", "No asset files found in unitypackage");
                }

                timer.Stop();
                diagnostics.Add("info", $"Avatar extraction complete in {timer.ElapsedMilliseconds}ms");

                return avatarData;
            }
            catch (Exception ex)
            {
                diagnostics.Add("error", $"Avatar extraction failed: {ex.Message}");
                throw;
            }
        }

        private List<UnityPackageEntry> ReadUnityPackageEntries(byte[] packageBytes)
        {
            if (packageBytes == null || packageBytes.Length < 4)
                throw new InvalidDataException("Unitypackage bytes are empty or invalid");

            if (IsZip(packageBytes))
            {
                diagnostics.Add("info", "Detected unitypackage format: zip");
                return ReadZipEntries(packageBytes);
            }

            if (IsGZip(packageBytes))
            {
                diagnostics.Add("info", "Detected unitypackage format: gzip/tar");
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

        private async Task LoadAssetsIntoAvatarData(Dictionary<string, byte[]> assetFiles, Dictionary<string, string> pathnameByGuid, Models.AvatarData avatarData)
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

                if (loadFileMethod == null || readAssetsMethod == null)
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
                            diagnostics.Add("warn", $"Failed to load asset {kvp.Key}: {ex.InnerException?.Message ?? ex.Message}");
                        }
                    }

                    readAssetsMethod.Invoke(manager, null);
                }
                finally
                {
                    foreach (var stream in openStreams)
                    {
                        stream.Dispose();
                    }
                }

                diagnostics.Add("info", $"Loaded {loadedAssets} assets from package");

                // Extract meshes and textures from manager
                ExtractMeshesAndTextures(manager, avatarData);

                if (avatarData.Meshes.Count == 0)
                {
                    var yamlMeshesByPath = ExtractYamlMeshes(assetFiles);
                    var parsedTexturesByPath = BuildParsedTextureLookup(manager);
                    var materialIndexByPath = ExtractYamlMaterialsAndTextures(assetFiles, pathnameByGuid, avatarData, parsedTexturesByPath);
                    var placedMeshes = ExtractPlacedYamlMeshes(assetFiles, pathnameByGuid, yamlMeshesByPath, materialIndexByPath);

                    if (placedMeshes.Count > 0)
                    {
                        avatarData.Meshes.AddRange(placedMeshes);
                        diagnostics.Add("info", $"Fallback YAML mesh parser extracted {placedMeshes.Count} meshes with prefab transforms");
                    }
                    else
                    {
                        avatarData.Meshes.AddRange(yamlMeshesByPath.Values);
                        if (yamlMeshesByPath.Count > 0)
                        {
                            diagnostics.Add("info", $"Fallback YAML mesh parser extracted {yamlMeshesByPath.Count} meshes");
                        }
                    }
                }

                diagnostics.Add("info", $"Extracted {avatarData.Materials.Count} materials and {avatarData.Textures.Count} textures");
            }
            catch (Exception ex)
            {
                diagnostics.Add("error", $"Failed to load assets from unitypackage: {ex.Message}");
                throw;
            }
        }

        private Dictionary<string, int> ExtractYamlMaterialsAndTextures(
            Dictionary<string, byte[]> assetFiles,
            Dictionary<string, string> pathnameByGuid,
            Models.AvatarData avatarData,
            Dictionary<string, Texture2D> parsedTexturesByPath)
        {
            var materialIndexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var textureIndexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int totalTextureBytes = 0;
            int skippedByCap = 0;
            int decodeFailures = 0;
            int missingGuidRefs = 0;
            int missingPathMappings = 0;
            int missingAssetEntries = 0;
            int resolvedViaParsedTextures = 0;

            foreach (var asset in assetFiles)
            {
                var path = asset.Key;
                if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (asset.Value.Length < 8 || !(asset.Value[0] == (byte)'%' && asset.Value[1] == (byte)'Y'))
                    continue;

                try
                {
                    var yaml = Encoding.UTF8.GetString(asset.Value);
                    var matName = MatchValue(yaml, @"^\s*m_Name:\s*(.+)$") ?? Path.GetFileNameWithoutExtension(path);
                    var texEnvGuidByName = ParseYamlTexEnvGuidMap(yaml);

                    var baseColor = ParseColor(yaml, "_Color") ?? new[] { 1f, 1f, 1f, 1f };
                    var metallic = ParseFloatProperty(yaml, "_Metallic", 0f);
                    var glossiness = ParseFloatProperty(yaml, "_Glossiness", 0.5f);
                    var roughness = Math.Clamp(1f - glossiness, 0f, 1f);

                    var albedoTextureIndex = ResolveYamlTextureIndex(
                        yaml,
                        pathnameByGuid,
                        assetFiles,
                        textureIndexByPath,
                        avatarData,
                        parsedTexturesByPath,
                        ref totalTextureBytes,
                        ref skippedByCap,
                        ref decodeFailures,
                        ref missingGuidRefs,
                        ref missingPathMappings,
                        ref missingAssetEntries,
                        ref resolvedViaParsedTextures,
                        "_BaseMap",
                        "_MainTex",
                        "_DiffuseMap");

                    var normalTextureIndex = ResolveYamlTextureIndex(
                        yaml,
                        pathnameByGuid,
                        assetFiles,
                        textureIndexByPath,
                        avatarData,
                        parsedTexturesByPath,
                        ref totalTextureBytes,
                        ref skippedByCap,
                        ref decodeFailures,
                        ref missingGuidRefs,
                        ref missingPathMappings,
                        ref missingAssetEntries,
                        ref resolvedViaParsedTextures,
                        "_BumpMap",
                        "_NormalMap");

                    if (albedoTextureIndex < 0)
                    {
                        var albedoGuid = SelectYamlTexEnvGuid(texEnvGuidByName, preferNormal: false);
                        if (!string.IsNullOrWhiteSpace(albedoGuid))
                        {
                            albedoTextureIndex = ResolveYamlTextureIndexByGuid(
                                albedoGuid,
                                pathnameByGuid,
                                assetFiles,
                                textureIndexByPath,
                                avatarData,
                                parsedTexturesByPath,
                                ref totalTextureBytes,
                                ref skippedByCap,
                                ref decodeFailures,
                                ref missingPathMappings,
                                ref missingAssetEntries,
                                ref resolvedViaParsedTextures);
                        }
                    }

                    if (normalTextureIndex < 0)
                    {
                        var normalGuid = SelectYamlTexEnvGuid(texEnvGuidByName, preferNormal: true);
                        if (!string.IsNullOrWhiteSpace(normalGuid))
                        {
                            normalTextureIndex = ResolveYamlTextureIndexByGuid(
                                normalGuid,
                                pathnameByGuid,
                                assetFiles,
                                textureIndexByPath,
                                avatarData,
                                parsedTexturesByPath,
                                ref totalTextureBytes,
                                ref skippedByCap,
                                ref decodeFailures,
                                ref missingPathMappings,
                                ref missingAssetEntries,
                                ref resolvedViaParsedTextures);
                        }
                    }

                    var materialData = new Models.AvatarData.MaterialData
                    {
                        Name = matName,
                        TextureIndex = albedoTextureIndex,
                        AlbedoTextureIndex = albedoTextureIndex,
                        NormalTextureIndex = normalTextureIndex,
                        BaseColor = baseColor,
                        Metallic = metallic,
                        Roughness = roughness
                    };

                    materialIndexByPath[path] = avatarData.Materials.Count;
                    avatarData.Materials.Add(materialData);
                }
                catch (Exception ex)
                {
                    diagnostics.Add("warn", $"Material parse failed for {path}: {ex.Message}");
                }
            }

            if (skippedByCap > 0)
            {
                diagnostics.Add("warn", $"Skipped {skippedByCap} textures due to safety caps");
            }
            if (decodeFailures > 0)
            {
                diagnostics.Add("warn", $"Failed to decode {decodeFailures} textures (unsupported format)");
            }
            if (resolvedViaParsedTextures > 0)
            {
                diagnostics.Add("info", $"Resolved {resolvedViaParsedTextures} YAML texture refs via parsed Texture2D decode");
            }
            if (missingGuidRefs > 0 || missingPathMappings > 0 || missingAssetEntries > 0)
            {
                diagnostics.Add(
                    "info",
                    $"YAML texture refs unresolved: missingGuid={missingGuidRefs}, missingPath={missingPathMappings}, missingAsset={missingAssetEntries}");
            }

            return materialIndexByPath;
        }

        private int ResolveYamlTextureIndex(
            string yaml,
            Dictionary<string, string> pathnameByGuid,
            Dictionary<string, byte[]> assetFiles,
            Dictionary<string, int> textureIndexByPath,
            Models.AvatarData avatarData,
            Dictionary<string, Texture2D> parsedTexturesByPath,
            ref int totalTextureBytes,
            ref int skippedByCap,
            ref int decodeFailures,
            ref int missingGuidRefs,
            ref int missingPathMappings,
            ref int missingAssetEntries,
            ref int resolvedViaParsedTextures,
            params string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return -1;

            foreach (var key in keys)
            {
                var guid = ParseTextureGuid(yaml, key);
                if (string.IsNullOrWhiteSpace(guid))
                {
                    missingGuidRefs++;
                    continue;
                }
                var resolvedIndex = ResolveYamlTextureIndexByGuid(
                    guid,
                    pathnameByGuid,
                    assetFiles,
                    textureIndexByPath,
                    avatarData,
                    parsedTexturesByPath,
                    ref totalTextureBytes,
                    ref skippedByCap,
                    ref decodeFailures,
                    ref missingPathMappings,
                    ref missingAssetEntries,
                    ref resolvedViaParsedTextures);
                if (resolvedIndex >= 0)
                    return resolvedIndex;
            }

            return -1;
        }

        private int ResolveYamlTextureIndexByGuid(
            string guid,
            Dictionary<string, string> pathnameByGuid,
            Dictionary<string, byte[]> assetFiles,
            Dictionary<string, int> textureIndexByPath,
            Models.AvatarData avatarData,
            Dictionary<string, Texture2D> parsedTexturesByPath,
            ref int totalTextureBytes,
            ref int skippedByCap,
            ref int decodeFailures,
            ref int missingPathMappings,
            ref int missingAssetEntries,
            ref int resolvedViaParsedTextures)
        {
            if (string.IsNullOrWhiteSpace(guid))
                return -1;

            if (!pathnameByGuid.TryGetValue(guid, out var texturePath))
            {
                missingPathMappings++;
                return -1;
            }

            if (textureIndexByPath.TryGetValue(texturePath, out var existingIndex))
                return existingIndex;

            var normalizedTexturePath = NormalizeAssetPathKey(texturePath);
            if (parsedTexturesByPath != null && parsedTexturesByPath.TryGetValue(normalizedTexturePath, out var parsedTexture))
            {
                if (TryCreateTextureData(parsedTexture, out var parsedTextureData, out var parsedEncodedSize))
                {
                    if (avatarData.Textures.Count >= MaxTextureCount ||
                        parsedEncodedSize > MaxTextureBytesPerTexture ||
                        totalTextureBytes + parsedEncodedSize > MaxTextureBytesTotal)
                    {
                        skippedByCap++;
                        return -1;
                    }

                    var parsedIndex = avatarData.Textures.Count;
                    avatarData.Textures.Add(parsedTextureData);
                    textureIndexByPath[texturePath] = parsedIndex;
                    totalTextureBytes += parsedEncodedSize;
                    resolvedViaParsedTextures++;
                    return parsedIndex;
                }
            }

            if (!assetFiles.TryGetValue(texturePath, out var textureBytes))
            {
                missingAssetEntries++;
                return -1;
            }

            if (avatarData.Textures.Count >= MaxTextureCount ||
                textureBytes.Length > MaxTextureBytesPerTexture ||
                totalTextureBytes + textureBytes.Length > MaxTextureBytesTotal)
            {
                skippedByCap++;
                return -1;
            }

            var mime = DetectImageMimeType(textureBytes);
            if (mime == null)
            {
                decodeFailures++;
                return -1;
            }

            var (width, height) = TryReadImageSize(textureBytes, mime);
            var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(textureBytes)}";
            var index = avatarData.Textures.Count;
            avatarData.Textures.Add(new Models.AvatarData.TextureData
            {
                Name = Path.GetFileNameWithoutExtension(texturePath),
                Width = width,
                Height = height,
                Format = mime,
                DataUrl = dataUrl
            });
            textureIndexByPath[texturePath] = index;
            totalTextureBytes += textureBytes.Length;
            return index;
        }

        private Dictionary<string, Texture2D> BuildParsedTextureLookup(AssetsManager manager)
        {
            var map = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            if (manager?.assetsFileList == null)
                return map;

            var textures = manager.assetsFileList
                .SelectMany(f => f.Objects.OfType<Texture2D>() ?? new List<Texture2D>())
                .ToList();

            foreach (var texture in textures)
            {
                if (texture?.assetsFile?.fileName == null)
                    continue;

                var normalizedPath = NormalizeAssetPathKey(texture.assetsFile.fileName);
                if (!map.ContainsKey(normalizedPath))
                {
                    map[normalizedPath] = texture;
                }
            }

            return map;
        }

        private static string NormalizeAssetPathKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Replace('\\', '/').Trim().ToLowerInvariant();
        }

        private static Dictionary<string, string> ParseYamlTexEnvGuidMap(string yaml)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(yaml))
                return map;

            var matches = Regex.Matches(
                yaml,
                @"^\s*-\s*([^:]+):\s*\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-fA-F]{32})",
                RegexOptions.Multiline);

            foreach (Match match in matches)
            {
                var key = match.Groups[1].Value.Trim();
                var guid = match.Groups[2].Value.Trim();
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(guid) && !map.ContainsKey(key))
                {
                    map[key] = guid;
                }
            }

            return map;
        }

        private static string? SelectYamlTexEnvGuid(Dictionary<string, string> texEnvGuidByName, bool preferNormal)
        {
            if (texEnvGuidByName == null || texEnvGuidByName.Count == 0)
                return null;

            if (preferNormal)
            {
                var normalEntry = texEnvGuidByName.FirstOrDefault(pair =>
                    pair.Key.Contains("normal", StringComparison.OrdinalIgnoreCase) ||
                    pair.Key.Contains("bump", StringComparison.OrdinalIgnoreCase) ||
                    pair.Key.Contains("nrm", StringComparison.OrdinalIgnoreCase));
                return string.IsNullOrWhiteSpace(normalEntry.Key) ? null : normalEntry.Value;
            }

            var albedoEntry = texEnvGuidByName.FirstOrDefault(pair =>
                pair.Key.Contains("base", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Contains("main", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Contains("diff", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Contains("albedo", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(albedoEntry.Key))
                return albedoEntry.Value;

            var firstNonNormal = texEnvGuidByName.FirstOrDefault(pair =>
                !pair.Key.Contains("normal", StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.Contains("bump", StringComparison.OrdinalIgnoreCase) &&
                !pair.Key.Contains("nrm", StringComparison.OrdinalIgnoreCase));

            return string.IsNullOrWhiteSpace(firstNonNormal.Key) ? texEnvGuidByName.First().Value : firstNonNormal.Value;
        }

        private Dictionary<string, Models.AvatarData.MeshData> ExtractYamlMeshes(Dictionary<string, byte[]> assetFiles)
        {
            var meshes = new Dictionary<string, Models.AvatarData.MeshData>(StringComparer.OrdinalIgnoreCase);

            foreach (var asset in assetFiles)
            {
                var path = asset.Key;
                if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (asset.Value.Length < 8 ||
                    !(asset.Value[0] == (byte)'%' && asset.Value[1] == (byte)'Y'))
                    continue;

                var text = Encoding.UTF8.GetString(asset.Value);
                if (!text.Contains("\nMesh:", StringComparison.Ordinal))
                    continue;

                try
                {
                    var mesh = ParseYamlMesh(path, text);
                    if (mesh != null && mesh.Vertices.Length >= 9 && mesh.Indices.Length >= 3)
                    {
                        meshes[path] = mesh;
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add("warn", $"YAML mesh parse failed for {path}: {ex.Message}");
                }
            }

            return meshes;
        }

        private List<Models.AvatarData.MeshData> ExtractPlacedYamlMeshes(
            Dictionary<string, byte[]> assetFiles,
            Dictionary<string, string> pathnameByGuid,
            Dictionary<string, Models.AvatarData.MeshData> yamlMeshesByPath,
            Dictionary<string, int> materialIndexByPath)
        {
            var prefabEntry = assetFiles
                .FirstOrDefault(kvp => kvp.Key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));
            if (prefabEntry.Value == null || prefabEntry.Value.Length == 0)
                return new List<Models.AvatarData.MeshData>();

            var prefabYaml = Encoding.UTF8.GetString(prefabEntry.Value);
            var sections = Regex.Matches(prefabYaml, @"^--- !u!(\d+) &(\d+)\r?\n([\s\S]*?)(?=^--- !u!|\z)", RegexOptions.Multiline);

            var gameObjectNameById = new Dictionary<long, string>();
            var transformById = new Dictionary<long, TransformState>();
            var transformIdByGameObject = new Dictionary<long, long>();
            var meshGuidByGameObject = new Dictionary<long, string>();
            var materialGuidByGameObject = new Dictionary<long, string>();

            foreach (Match section in sections)
            {
                if (!int.TryParse(section.Groups[1].Value, out var classId))
                    continue;
                if (!long.TryParse(section.Groups[2].Value, out var objectId))
                    continue;

                var body = section.Groups[3].Value;

                if (classId == 1)
                {
                    var goName = MatchValue(body, @"^\s*m_Name:\s*(.+)$");
                    if (!string.IsNullOrWhiteSpace(goName))
                    {
                        gameObjectNameById[objectId] = goName;
                    }
                }
                else if (classId == 4)
                {
                    var gameObjectId = ParseFileId(body, "m_GameObject");
                    if (gameObjectId == 0)
                        continue;

                    var parentTransformId = ParseFileId(body, "m_Father");
                    var localPosition = ParseVector3(body, "m_LocalPosition");
                    var localRotation = ParseQuaternion(body, "m_LocalRotation");
                    var localScale = ParseVector3(body, "m_LocalScale", new[] { 1f, 1f, 1f });

                    transformById[objectId] = new TransformState
                    {
                        TransformId = objectId,
                        GameObjectId = gameObjectId,
                        ParentTransformId = parentTransformId,
                        LocalPosition = localPosition,
                        LocalRotation = localRotation,
                        LocalScale = localScale
                    };

                    transformIdByGameObject[gameObjectId] = objectId;
                }
                else if (classId == 33)
                {
                    var gameObjectId = ParseFileId(body, "m_GameObject");
                    var meshGuid = ParseGuid(body, "m_Mesh");
                    if (gameObjectId != 0 && !string.IsNullOrWhiteSpace(meshGuid))
                    {
                        meshGuidByGameObject[gameObjectId] = meshGuid;
                    }
                }
                else if (classId == 23)
                {
                    var gameObjectId = ParseFileId(body, "m_GameObject");
                    if (gameObjectId == 0)
                        continue;

                    var materialGuid = ParseFirstMaterialGuid(body);
                    if (!string.IsNullOrWhiteSpace(materialGuid))
                    {
                        materialGuidByGameObject[gameObjectId] = materialGuid;
                    }
                }
            }

            var worldCache = new Dictionary<long, TransformWorld>();
            var result = new List<Models.AvatarData.MeshData>();

            foreach (var meshBinding in meshGuidByGameObject)
            {
                var gameObjectId = meshBinding.Key;
                var meshGuid = meshBinding.Value;
                if (!pathnameByGuid.TryGetValue(meshGuid, out var meshPath))
                    continue;
                if (!yamlMeshesByPath.TryGetValue(meshPath, out var meshTemplate))
                    continue;
                if (!transformIdByGameObject.TryGetValue(gameObjectId, out var transformId))
                    continue;

                var world = ComputeWorldTransform(transformId, transformById, worldCache);
                if (world == null)
                    continue;

                result.Add(new Models.AvatarData.MeshData
                {
                    Name = gameObjectNameById.TryGetValue(gameObjectId, out var goName) ? goName : meshTemplate.Name,
                    Vertices = meshTemplate.Vertices,
                    Indices = meshTemplate.Indices,
                    Normals = meshTemplate.Normals,
                    UV = meshTemplate.UV,
                    MaterialIndex = ResolveMaterialIndex(gameObjectId, materialGuidByGameObject, pathnameByGuid, materialIndexByPath),
                    Position = world.Position,
                    Rotation = world.Rotation,
                    Scale = world.Scale
                });
            }

            return result;
        }

        private static int ResolveMaterialIndex(
            long gameObjectId,
            Dictionary<long, string> materialGuidByGameObject,
            Dictionary<string, string> pathnameByGuid,
            Dictionary<string, int> materialIndexByPath)
        {
            if (!materialGuidByGameObject.TryGetValue(gameObjectId, out var materialGuid))
                return 0;
            if (!pathnameByGuid.TryGetValue(materialGuid, out var materialPath))
                return 0;
            return materialIndexByPath.TryGetValue(materialPath, out var materialIndex) ? materialIndex : 0;
        }

        private static TransformWorld? ComputeWorldTransform(
            long transformId,
            Dictionary<long, TransformState> transformById,
            Dictionary<long, TransformWorld> cache)
        {
            if (cache.TryGetValue(transformId, out var cached))
                return cached;
            if (!transformById.TryGetValue(transformId, out var state))
                return null;

            TransformWorld world;
            if (state.ParentTransformId != 0 && transformById.ContainsKey(state.ParentTransformId))
            {
                var parentWorld = ComputeWorldTransform(state.ParentTransformId, transformById, cache);
                if (parentWorld == null)
                    return null;

                var rotatedLocal = RotateVectorByQuaternion(state.LocalPosition, parentWorld.Rotation);
                world = new TransformWorld
                {
                    Position = new[]
                    {
                        parentWorld.Position[0] + rotatedLocal[0],
                        parentWorld.Position[1] + rotatedLocal[1],
                        parentWorld.Position[2] + rotatedLocal[2]
                    },
                    Rotation = MultiplyQuaternions(parentWorld.Rotation, state.LocalRotation),
                    Scale = new[]
                    {
                        parentWorld.Scale[0] * state.LocalScale[0],
                        parentWorld.Scale[1] * state.LocalScale[1],
                        parentWorld.Scale[2] * state.LocalScale[2]
                    }
                };
            }
            else
            {
                world = new TransformWorld
                {
                    Position = state.LocalPosition,
                    Rotation = state.LocalRotation,
                    Scale = state.LocalScale
                };
            }

            cache[transformId] = world;
            return world;
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

        private static long ParseFileId(string body, string fieldName)
        {
            var match = Regex.Match(body, $@"^\s*{Regex.Escape(fieldName)}:\s*\{{fileID:\s*(-?\d+)", RegexOptions.Multiline);
            return match.Success && long.TryParse(match.Groups[1].Value, out var fileId) ? fileId : 0;
        }

        private static string? ParseGuid(string body, string fieldName)
        {
            var match = Regex.Match(body, $@"^\s*{Regex.Escape(fieldName)}:\s*\{{fileID:\s*-?\d+,\s*guid:\s*([0-9a-fA-F]{{32}})", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string? ParseFirstMaterialGuid(string body)
        {
            var match = Regex.Match(
                body,
                @"^\s*-\s*\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-fA-F]{32})",
                RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string? ParseTextureGuid(string yaml, string key)
        {
            var match = Regex.Match(
                yaml,
                $@"^\s*-\s*{Regex.Escape(key)}:\s*\{{fileID:\s*-?\d+,\s*guid:\s*([0-9a-fA-F]{{32}})",
                RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static float[]? ParseColor(string yaml, string key)
        {
            var match = Regex.Match(
                yaml,
                $@"^\s*-\s*{Regex.Escape(key)}:\s*\{{r:\s*([^,]+),\s*g:\s*([^,]+),\s*b:\s*([^,]+),\s*a:\s*([^\}}]+)\}}",
                RegexOptions.Multiline);
            if (!match.Success)
                return null;

            return new[]
            {
                ParseFloat(match.Groups[1].Value, 1f),
                ParseFloat(match.Groups[2].Value, 1f),
                ParseFloat(match.Groups[3].Value, 1f),
                ParseFloat(match.Groups[4].Value, 1f)
            };
        }

        private static float ParseFloatProperty(string yaml, string key, float fallback)
        {
            var match = Regex.Match(
                yaml,
                $@"^\s*-\s*{Regex.Escape(key)}:\s*([^\r\n]+)$",
                RegexOptions.Multiline);
            return match.Success ? ParseFloat(match.Groups[1].Value, fallback) : fallback;
        }

        private static string? DetectImageMimeType(byte[] data)
        {
            if (data.Length >= 8 &&
                data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
                return "image/png";

            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return "image/jpeg";

            return null;
        }

        private static (int width, int height) TryReadImageSize(byte[] data, string mime)
        {
            if (mime == "image/png" && data.Length >= 24)
            {
                var width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
                var height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
                return (Math.Max(0, width), Math.Max(0, height));
            }

            if (mime == "image/jpeg")
            {
                int i = 2;
                while (i + 9 < data.Length)
                {
                    if (data[i] != 0xFF)
                    {
                        i++;
                        continue;
                    }

                    var marker = data[i + 1];
                    if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
                    {
                        var height = (data[i + 5] << 8) + data[i + 6];
                        var width = (data[i + 7] << 8) + data[i + 8];
                        return (Math.Max(0, width), Math.Max(0, height));
                    }

                    if (i + 3 >= data.Length)
                        break;
                    var segmentLen = (data[i + 2] << 8) + data[i + 3];
                    if (segmentLen <= 0)
                        break;
                    i += 2 + segmentLen;
                }
            }

            return (0, 0);
        }

        private static float[] ParseVector3(string body, string fieldName, float[]? fallback = null)
        {
            fallback ??= new[] { 0f, 0f, 0f };
            var match = Regex.Match(body, $@"^\s*{Regex.Escape(fieldName)}:\s*\{{x:\s*([^,]+),\s*y:\s*([^,]+),\s*z:\s*([^\}}]+)\}}", RegexOptions.Multiline);
            if (!match.Success)
                return fallback;

            return new[]
            {
                ParseFloat(match.Groups[1].Value, fallback[0]),
                ParseFloat(match.Groups[2].Value, fallback[1]),
                ParseFloat(match.Groups[3].Value, fallback[2])
            };
        }

        private static float[] ParseQuaternion(string body, string fieldName)
        {
            var match = Regex.Match(body, $@"^\s*{Regex.Escape(fieldName)}:\s*\{{x:\s*([^,]+),\s*y:\s*([^,]+),\s*z:\s*([^,]+),\s*w:\s*([^\}}]+)\}}", RegexOptions.Multiline);
            if (!match.Success)
                return new[] { 0f, 0f, 0f, 1f };

            return new[]
            {
                ParseFloat(match.Groups[1].Value, 0f),
                ParseFloat(match.Groups[2].Value, 0f),
                ParseFloat(match.Groups[3].Value, 0f),
                ParseFloat(match.Groups[4].Value, 1f)
            };
        }

        private static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }

        private Models.AvatarData.MeshData? ParseYamlMesh(string path, string yaml)
        {
            var name = MatchValue(yaml, @"^\s*m_Name:\s*(.+)$") ?? Path.GetFileNameWithoutExtension(path);
            var vertexCountText = MatchValue(yaml, @"^\s*m_VertexCount:\s*(\d+)$");
            var dataHex = MatchValue(yaml, @"^\s*_typelessdata:\s*([0-9a-fA-F]+)$");
            var indexHex = MatchValue(yaml, @"^\s*m_IndexBuffer:\s*([0-9a-fA-F]+)$");
            var indexFormatText = MatchValue(yaml, @"^\s*m_IndexFormat:\s*(\d+)$");

            if (!int.TryParse(vertexCountText, out var vertexCount) || vertexCount <= 0)
                return null;
            if (string.IsNullOrWhiteSpace(dataHex) || string.IsNullOrWhiteSpace(indexHex))
                return null;

            var vertexBytes = HexToBytes(dataHex);
            var indexBytes = HexToBytes(indexHex);
            if (vertexBytes.Length == 0 || indexBytes.Length == 0)
                return null;

            var stride = vertexBytes.Length / vertexCount;
            if (stride < 12)
                return null;

            var vertices = new float[vertexCount * 3];
            for (var i = 0; i < vertexCount; i++)
            {
                var offset = i * stride;
                if (offset + 12 > vertexBytes.Length)
                    break;

                vertices[i * 3] = BitConverter.ToSingle(vertexBytes, offset);
                vertices[i * 3 + 1] = BitConverter.ToSingle(vertexBytes, offset + 4);
                vertices[i * 3 + 2] = BitConverter.ToSingle(vertexBytes, offset + 8);
            }

            var indexFormat = int.TryParse(indexFormatText, out var parsedFormat) ? parsedFormat : 0;
            uint[] indices;
            if (indexFormat == 0)
            {
                var count = indexBytes.Length / 2;
                indices = new uint[count];
                for (var i = 0; i < count; i++)
                {
                    indices[i] = BitConverter.ToUInt16(indexBytes, i * 2);
                }
            }
            else
            {
                var count = indexBytes.Length / 4;
                indices = new uint[count];
                for (var i = 0; i < count; i++)
                {
                    indices[i] = BitConverter.ToUInt32(indexBytes, i * 4);
                }
            }

            return new Models.AvatarData.MeshData
            {
                Name = name,
                Vertices = vertices,
                Indices = indices,
                Normals = Array.Empty<float>(),
                UV = Array.Empty<float>(),
                MaterialIndex = 0
            };
        }

        private static string? MatchValue(string text, string pattern)
        {
            var match = Regex.Match(text, pattern, RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return Array.Empty<byte>();

            var cleaned = hex.Trim();
            if (cleaned.Length % 2 != 0)
                return Array.Empty<byte>();

            var bytes = new byte[cleaned.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        private void ExtractMeshesAndTextures(AssetsManager manager, Models.AvatarData avatarData)
        {
            // Extract Mesh objects
            var meshes = manager.assetsFileList
                .SelectMany(f => f.Objects.OfType<Mesh>() ?? new List<Mesh>())
                .ToList();

            diagnostics.Add("info", $"Found {meshes.Count} meshes");

            // Extract Texture2D objects
            var textures = manager.assetsFileList
                .SelectMany(f => f.Objects.OfType<Texture2D>() ?? new List<Texture2D>())
                .ToList();

            diagnostics.Add("info", $"Found {textures.Count} textures");

            // Extract Avatar objects (skeleton hierarchy)
            var avatars = manager.assetsFileList
                .SelectMany(f => f.Objects.OfType<Avatar>() ?? new List<Avatar>())
                .ToList();

            diagnostics.Add("info", $"Found {avatars.Count} avatars");

            var materialIndexByObjectKey = ExtractPrimaryMaterialsAndTextures(manager, avatarData);
            var materialIndexByMeshKey = BuildMeshMaterialMap(manager, materialIndexByObjectKey);

            // Convert meshes to serializable format
            foreach (var mesh in meshes.Take(10)) // Limit to first 10 meshes for now
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
                        avatarData.Meshes.Add(meshData);
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add("warn", $"Failed to convert mesh {mesh.Name}: {ex.Message}");
                }
            }

            // Convert skeleton hierarchy
            if (avatars.Count > 0)
            {
                try
                {
                    ExtractAvatarBones(avatars[0], avatarData);
                }
                catch (Exception ex)
                {
                    diagnostics.Add("warn", $"Failed to extract avatar bones: {ex.Message}");
                }
            }
        }

        private Models.AvatarData.MeshData CreateMeshData(Mesh mesh)
        {
            if (mesh == null)
                return null;

            var meshData = new Models.AvatarData.MeshData
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

        private Dictionary<string, int> ExtractPrimaryMaterialsAndTextures(AssetsManager manager, Models.AvatarData avatarData)
        {
            var materialIndexByObjectKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var textureIndexByObjectKey = new Dictionary<string, int>(StringComparer.Ordinal);

            int totalTextureBytes = 0;
            int skippedByCap = 0;
            int decodeFailures = 0;

            var materials = manager.assetsFileList
                .SelectMany(f => f.Objects.OfType<Material>() ?? new List<Material>())
                .ToList();

            diagnostics.Add("info", $"Found {materials.Count} materials");

            foreach (var material in materials)
            {
                if (material == null)
                    continue;

                try
                {
                    var baseColor = GetMaterialColor(material, "_BaseColor", "_Color") ?? new[] { 1f, 1f, 1f, 1f };
                    var metallic = GetMaterialFloat(material, 0f, "_Metallic", "_Metalness");
                    var smoothness = GetMaterialFloat(material, 0.5f, "_Glossiness", "_Smoothness");
                    var roughness = Math.Clamp(1f - smoothness, 0f, 1f);

                    var albedoTextureIndex = ResolveMaterialTextureIndex(
                        material,
                        textureIndexByObjectKey,
                        avatarData,
                        ref totalTextureBytes,
                        ref skippedByCap,
                        ref decodeFailures,
                        "_BaseMap",
                        "_MainTex",
                        "_DiffuseMap");

                    var normalTextureIndex = ResolveMaterialTextureIndex(
                        material,
                        textureIndexByObjectKey,
                        avatarData,
                        ref totalTextureBytes,
                        ref skippedByCap,
                        ref decodeFailures,
                        "_BumpMap",
                        "_NormalMap");

                    var materialData = new Models.AvatarData.MaterialData
                    {
                        Name = material.Name ?? "Material",
                        TextureIndex = albedoTextureIndex,
                        AlbedoTextureIndex = albedoTextureIndex,
                        NormalTextureIndex = normalTextureIndex,
                        BaseColor = baseColor,
                        Metallic = metallic,
                        Roughness = roughness
                    };

                    materialIndexByObjectKey[GetObjectKey(material)] = avatarData.Materials.Count;
                    avatarData.Materials.Add(materialData);
                }
                catch (Exception ex)
                {
                    diagnostics.Add("warn", $"Failed to parse material {material.Name}: {ex.Message}");
                }
            }

            if (skippedByCap > 0)
            {
                diagnostics.Add("warn", $"Skipped {skippedByCap} primary-path textures due to safety caps");
            }

            if (decodeFailures > 0)
            {
                diagnostics.Add("warn", $"Failed to decode {decodeFailures} primary-path textures");
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
            Models.AvatarData avatarData,
            ref int totalTextureBytes,
            ref int skippedByCap,
            ref int decodeFailures,
            params string[] textureKeys)
        {
            if (!TryGetMaterialTexture(material, out var texture, textureKeys))
                return -1;

            var textureKey = GetObjectKey(texture);
            if (textureIndexByObjectKey.TryGetValue(textureKey, out var existingIndex))
                return existingIndex;

            if (!TryCreateTextureData(texture, out var textureData, out var encodedSize))
            {
                decodeFailures++;
                return -1;
            }

            if (avatarData.Textures.Count >= MaxTextureCount ||
                encodedSize > MaxTextureBytesPerTexture ||
                totalTextureBytes + encodedSize > MaxTextureBytesTotal)
            {
                skippedByCap++;
                return -1;
            }

            var textureIndex = avatarData.Textures.Count;
            avatarData.Textures.Add(textureData);
            textureIndexByObjectKey[textureKey] = textureIndex;
            totalTextureBytes += encodedSize;

            return textureIndex;
        }

        private static bool TryGetMaterialTexture(Material material, out Texture2D texture, params string[] keys)
        {
            texture = null;
            if (material?.m_SavedProperties?.m_TexEnvs == null || keys == null || keys.Length == 0)
                return false;

            foreach (var key in keys)
            {
                var texEnv = material.m_SavedProperties.m_TexEnvs
                    .FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(texEnv.Key))
                    continue;

                if (texEnv.Value?.m_Texture != null && texEnv.Value.m_Texture.TryGet<Texture2D>(out var resolvedTexture))
                {
                    texture = resolvedTexture;
                    return true;
                }
            }

            return false;
        }

        private bool TryCreateTextureData(Texture2D texture, out Models.AvatarData.TextureData textureData, out int encodedSize)
        {
            textureData = null;
            encodedSize = 0;

            if (!StableTextureEncoder.TryEncodePngDataUrl(texture, out var encoded) || encoded == null)
                return false;

            textureData = new Models.AvatarData.TextureData
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

        private static float GetMaterialFloat(Material material, float fallback, params string[] keys)
        {
            return StableMaterialPropertyReader.GetFloat(material, fallback, keys);
        }

        private static string GetObjectKey(AssetStudio.Object obj)
        {
            if (obj == null)
                return string.Empty;

            return $"{obj.assetsFile?.fileName}:{obj.m_PathID}";
        }

        private void ExtractAvatarBones(Avatar avatar, Models.AvatarData avatarData)
        {
            if (avatar?.m_Avatar?.m_AvatarSkeleton == null)
                return;

            var skeleton = avatar.m_Avatar.m_AvatarSkeleton;
            var boneCount = skeleton.m_Node.Count;

            diagnostics.Add("info", $"Avatar has {boneCount} bones");

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

                var boneData = new Models.AvatarData.BoneData
                {
                    Name = boneName,
                    ParentIndex = node.m_ParentId,
                    Position = new[] { 0f, 0f, 0f }, // Default position
                    Rotation = new[] { 0f, 0f, 0f, 1f }, // Default quaternion (identity)
                    Scale = new[] { 1f, 1f, 1f }
                };

                avatarData.Bones.Add(boneData);
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

        private sealed class TransformState
        {
            public long TransformId { get; init; }
            public long GameObjectId { get; init; }
            public long ParentTransformId { get; init; }
            public required float[] LocalPosition { get; init; }
            public required float[] LocalRotation { get; init; }
            public required float[] LocalScale { get; init; }
        }

        private sealed class TransformWorld
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
