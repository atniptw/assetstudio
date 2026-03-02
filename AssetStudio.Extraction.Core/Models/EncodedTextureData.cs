namespace AssetStudio.Extraction.Core.Models
{
    public sealed class EncodedTextureData
    {
        public required string Name { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required string Format { get; init; }
        public required string DataUrl { get; init; }
        public required int EncodedSize { get; init; }
    }
}
