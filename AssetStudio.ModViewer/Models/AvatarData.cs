using System;
using System.Collections.Generic;

namespace AssetStudio.ModViewer.Models
{
    /// <summary>
    /// Serializable avatar data for three.js rendering
    /// </summary>
    public class AvatarData
    {
        public class MeshData
        {
            public string Name { get; set; }
            public float[] Vertices { get; set; }
            public uint[] Indices { get; set; }
            public float[] Normals { get; set; }
            public float[] UV { get; set; }
            public int MaterialIndex { get; set; }
            public float[] Position { get; set; } = { 0f, 0f, 0f };
            public float[] Rotation { get; set; } = { 0f, 0f, 0f, 1f };
            public float[] Scale { get; set; } = { 1f, 1f, 1f };
        }

        public class TextureData
        {
            public string Name { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public string Format { get; set; }
            public string DataUrl { get; set; } // base64 data URL
        }

        public class MaterialData
        {
            public string Name { get; set; }
            public int TextureIndex { get; set; } = -1;
            public int AlbedoTextureIndex { get; set; } = -1;
            public int NormalTextureIndex { get; set; } = -1;
            public float[] BaseColor { get; set; } = { 1, 1, 1, 1 };
            public float Metallic { get; set; }
            public float Roughness { get; set; }
        }

        public class BoneData
        {
            public string Name { get; set; }
            public int ParentIndex { get; set; }
            public float[] Position { get; set; }
            public float[] Rotation { get; set; } // quaternion (x, y, z, w)
            public float[] Scale { get; set; }
        }

        public class AttachmentAnchorData
        {
            public string Tag { get; set; }
            public float[] Position { get; set; } = { 0f, 0f, 0f };
            public float[] Rotation { get; set; } = { 0f, 0f, 0f, 1f };
            public float[] Scale { get; set; } = { 1f, 1f, 1f };
            public string? SourceType { get; set; }
            public string? SourceName { get; set; }
            public string? SourcePath { get; set; }
            public string? Confidence { get; set; }
        }

        public string Name { get; set; }
        public List<MeshData> Meshes { get; set; } = new();
        public List<TextureData> Textures { get; set; } = new();
        public List<MaterialData> Materials { get; set; } = new();
        public List<BoneData> Bones { get; set; } = new();
        public List<AttachmentAnchorData> AttachmentAnchors { get; set; } = new();
        public int RootBoneIndex { get; set; } = 0;
    }
}
