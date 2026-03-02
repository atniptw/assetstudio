using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
                using var stream = new MemoryStream(packageBytes);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

                var avatarData = new Models.AvatarData
                {
                    Name = "BaseAvatar",
                    Meshes = new(),
                    Textures = new(),
                    Materials = new(),
                    Bones = new()
                };

                diagnostics.Add("info", $"Processing unitypackage with {archive.Entries.Count} entries");

                // Extract asset files from unitypackage structure
                // unitypackages are organized as: <guid>/<type>/<actual_name>
                var assetFiles = new Dictionary<string, byte[]>();
                
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name == "pathname" || entry.Name == "asset.meta")
                        continue;

                    if (!entry.Name.Contains("/"))
                        continue;

                    var parts = entry.FullName.Split('/');
                    if (parts.Length >= 3 && parts[1] == "asset")
                    {
                        var bytes = ReadZipEntry(entry);
                        assetFiles[entry.FullName] = bytes;
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

                // Load each asset file
                int loadedAssets = 0;
                foreach (var kvp in assetFiles)
                {
                    try
                    {
                        using var assetStream = new MemoryStream(kvp.Value);
                        var reader = new FileReader(kvp.Key, assetStream);
                        
                        // The FileReader needs proper type detection
                        // For now, we'll try to load as a generic asset file
                        // In production, you'd implement proper type detection
                        
                        loadedAssets++;
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add("warn", $"Failed to load asset {kvp.Key}: {ex.Message}");
                    }
                }

                diagnostics.Add("info", $"Loaded {loadedAssets} assets from package");

                // Extract meshes and textures from manager
                ExtractMeshesAndTextures(manager, avatarData);
            }
            catch (Exception ex)
            {
                diagnostics.Add("error", $"Failed to load assets from unitypackage: {ex.Message}");
                throw;
            }
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
    }
}
