using System.Threading.Tasks;
using AssetStudio.Extraction.Core.Models;

namespace AssetStudio.Extraction.Core.Abstractions
{
    public interface IUnityPackageExtractionService
    {
        Task<ExtractionSceneData> ExtractAsync(byte[] packageBytes, IExtractionLogger? logger = null);
    }
}