using AssetStudio.Extraction.Core.Abstractions;
using AssetStudio.Extraction.Core.Models;

namespace AssetStudio.Extraction.Core.Services
{
    public class ExtractionConfigurationService
    {
        public ExtractionOptions DefaultOptions { get; } = new();

        public ExtractionOptions MergeOptions(ExtractionOptions? overrides)
        {
            if (overrides == null)
                return DefaultOptions;

            return new ExtractionOptions
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
