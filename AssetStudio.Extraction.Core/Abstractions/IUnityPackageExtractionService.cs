using System.Threading.Tasks;
using System.Collections.Generic;
using AssetStudio.Extraction.Core.Models;

namespace AssetStudio.Extraction.Core.Abstractions
{
    public interface IUnityPackageExtractionService
    {
        Task<ExtractionSceneData> ExtractAsync(byte[] packageBytes, IExtractionLogger? logger = null);
        Task<ExtractionSceneData> ExtractStaticAssetAsync(
            byte[] assetBytes,
            string sourceName,
            string? bodyPartTag = null,
            Dictionary<string, byte[]>? companionFiles = null,
            IExtractionLogger? logger = null);
    }
}