using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AssetStudio.ModViewer.Services
{
    public class AssetExtractor
    {
        private readonly DiagnosticsService diagnostics;

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
                    await LoadAssetsIntoAvatarData(assetFiles, avatarData);
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

        private async Task LoadAssetsIntoAvatarData(Dictionary<string, byte[]> assetFiles, Models.AvatarData avatarData)
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
                    var yamlMeshes = ExtractYamlMeshes(assetFiles);
                    avatarData.Meshes.AddRange(yamlMeshes);
                    if (yamlMeshes.Count > 0)
                    {
                        diagnostics.Add("info", $"Fallback YAML mesh parser extracted {yamlMeshes.Count} meshes");
                    }
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add("error", $"Failed to load assets from unitypackage: {ex.Message}");
                throw;
            }
        }

        private List<Models.AvatarData.MeshData> ExtractYamlMeshes(Dictionary<string, byte[]> assetFiles)
        {
            var meshes = new List<Models.AvatarData.MeshData>();

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
                        meshes.Add(mesh);
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add("warn", $"YAML mesh parse failed for {path}: {ex.Message}");
                }
            }

            return meshes;
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

            // Convert meshes to serializable format
            foreach (var mesh in meshes.Take(10)) // Limit to first 10 meshes for now
            {
                try
                {
                    var meshData = CreateMeshData(mesh);
                    if (meshData != null)
                        avatarData.Meshes.Add(meshData);
                }
                catch (Exception ex)
                {
                    diagnostics.Add("warn", $"Failed to convert mesh {mesh.Name}: {ex.Message}");
                }
            }

            // Convert textures to serializable format
            foreach (var texture in textures.Take(10)) // Limit to first 10 textures
            {
                try
                {
                    var textureData = CreateTextureData(texture);
                    if (textureData != null)
                        avatarData.Textures.Add(textureData);
                }
                catch (Exception ex)
                {
                    diagnostics.Add("warn", $"Failed to convert texture {texture.Name}: {ex.Message}");
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
                MaterialIndex = 0
            };

            return meshData;
        }

        private Models.AvatarData.TextureData CreateTextureData(Texture2D texture)
        {
            if (texture == null)
                return null;

            var textureData = new Models.AvatarData.TextureData
            {
                Name = texture.Name ?? "Texture",
                Width = texture.m_Width,
                Height = texture.m_Height,
                Format = texture.m_TextureFormat.ToString(),
                // For large textures, don't include base64 yet
                DataUrl = $"<{texture.m_Width}x{texture.m_Height}>"
            };

            return textureData;
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

        private sealed class UnityPackageEntry
        {
            public required string FullName { get; init; }
            public required byte[] Data { get; init; }
        }
    }
}
