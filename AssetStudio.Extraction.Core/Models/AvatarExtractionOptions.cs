namespace AssetStudio.Extraction.Core.Models
{
    public sealed class AvatarExtractionOptions
    {
        public int MaxTextureCount { get; init; } = 24;
        public int MaxTextureBytesPerTexture { get; init; } = 8 * 1024 * 1024;
        public int MaxTextureBytesTotal { get; init; } = 48 * 1024 * 1024;
        public int MaxMeshCount { get; init; } = 10;
    }
}
