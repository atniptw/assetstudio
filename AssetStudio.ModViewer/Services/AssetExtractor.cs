using System.Linq;
using System.Threading.Tasks;
using AssetStudio.Extraction.Core.Abstractions;
using AssetStudio.Extraction.Core.Models;

namespace AssetStudio.ModViewer.Services
{
    public class AssetExtractor
    {
        private readonly DiagnosticsService diagnostics;
        private readonly IUnityPackageExtractionService extractionService;

        public AssetExtractor(DiagnosticsService diagnostics, IUnityPackageExtractionService extractionService)
        {
            this.diagnostics = diagnostics;
            this.extractionService = extractionService;
        }

        public async Task<Models.AvatarData> ExtractAvatarAsync(byte[] packageBytes)
        {
            var scene = await extractionService.ExtractAsync(packageBytes, new DiagnosticsLogger(diagnostics));
            return MapSceneToAvatar(scene);
        }

        public async Task<Models.AvatarData> ExtractStaticAssetAsync(byte[] assetBytes, string sourceName)
        {
            var scene = await extractionService.ExtractStaticAssetAsync(assetBytes, sourceName, new DiagnosticsLogger(diagnostics));
            return MapSceneToAvatar(scene);
        }

        private static Models.AvatarData MapSceneToAvatar(ExtractionSceneData scene)
        {
            return new Models.AvatarData
            {
                Name = scene.Name,
                RootBoneIndex = scene.RootBoneIndex,
                Meshes = scene.Meshes.Select(mesh => new Models.AvatarData.MeshData
                {
                    Name = mesh.Name,
                    Vertices = mesh.Vertices,
                    Indices = mesh.Indices,
                    Normals = mesh.Normals,
                    UV = mesh.UV,
                    MaterialIndex = mesh.MaterialIndex,
                    Position = mesh.Position,
                    Rotation = mesh.Rotation,
                    Scale = mesh.Scale
                }).ToList(),
                Textures = scene.Textures.Select(texture => new Models.AvatarData.TextureData
                {
                    Name = texture.Name,
                    Width = texture.Width,
                    Height = texture.Height,
                    Format = texture.Format,
                    DataUrl = texture.DataUrl
                }).ToList(),
                Materials = scene.Materials.Select(material => new Models.AvatarData.MaterialData
                {
                    Name = material.Name,
                    TextureIndex = material.TextureIndex,
                    AlbedoTextureIndex = material.AlbedoTextureIndex,
                    NormalTextureIndex = material.NormalTextureIndex,
                    BaseColor = material.BaseColor,
                    Metallic = material.Metallic,
                    Roughness = material.Roughness
                }).ToList(),
                Bones = scene.Bones.Select(bone => new Models.AvatarData.BoneData
                {
                    Name = bone.Name,
                    ParentIndex = bone.ParentIndex,
                    Position = bone.Position,
                    Rotation = bone.Rotation,
                    Scale = bone.Scale
                }).ToList(),
                AttachmentAnchors = scene.AttachmentAnchors.Select(anchor => new Models.AvatarData.AttachmentAnchorData
                {
                    Tag = anchor.Tag,
                    Position = anchor.Position,
                    Rotation = anchor.Rotation,
                    Scale = anchor.Scale
                }).ToList()
            };
        }

        private sealed class DiagnosticsLogger : IExtractionLogger
        {
            private readonly DiagnosticsService diagnostics;

            public DiagnosticsLogger(DiagnosticsService diagnostics)
            {
                this.diagnostics = diagnostics;
            }

            public void Info(string message) => diagnostics.Add("info", message);

            public void Warn(string message) => diagnostics.Add("warn", message);

            public void Error(string message) => diagnostics.Add("error", message);
        }
    }
}
