using AssetStudio.Extraction.Core.Abstractions;
using AssetStudio.Extraction.Core.Models;

namespace AssetStudio.Extraction.Core.Services
{
    public class AvatarExtractionService
    {
        public AvatarExtractionOptions DefaultOptions { get; } = new();

        public AvatarExtractionOptions MergeOptions(AvatarExtractionOptions? overrides)
        {
            if (overrides == null)
                return DefaultOptions;

            return new AvatarExtractionOptions
            {
                MaxTextureCount = overrides.MaxTextureCount,
                MaxTextureBytesPerTexture = overrides.MaxTextureBytesPerTexture,
                MaxTextureBytesTotal = overrides.MaxTextureBytesTotal,
                MaxMeshCount = overrides.MaxMeshCount
            };
        }

        public void LogStartup(IExtractionLogger? logger)
        {
            logger?.Info("AssetStudio.Extraction.Core initialized");
        }
    }
}
