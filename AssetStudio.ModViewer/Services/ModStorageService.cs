using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AssetStudio.ModViewer.Models;
using Microsoft.JSInterop;

namespace AssetStudio.ModViewer.Services
{
    public class ModStorageService
    {
        private readonly IJSRuntime jsRuntime;

        public ModStorageService(IJSRuntime jsRuntime)
        {
            this.jsRuntime = jsRuntime;
        }

        public async Task<List<PersistedModFile>> GetValidFilesAsync()
        {
            var files = await jsRuntime.InvokeAsync<List<PersistedModFile>>("modViewer.getStoredHhhFiles");
            files ??= new List<PersistedModFile>();
            return files
                .Where(file => !string.IsNullOrWhiteSpace(file.BodyPartTag))
                .Select(file =>
                {
                    file.Enabled = false;
                    return file;
                })
                .OrderByDescending(file => file.ImportedAtUtc)
                .ToList();
        }

        public Task StoreValidFilesAsync(IEnumerable<ModImportItem> validItems)
        {
            var records = validItems.Select(item => new PersistedModFile
            {
                Id = item.Id,
                FileName = item.FileName,
                ArchivePath = item.ArchivePath,
                DisplayName = item.DisplayName,
                SourceZipName = item.SourceZipName,
                BodyPartTag = item.BodyPartTag,
                SizeBytes = item.SizeBytes,
                DataBase64 = item.DataBase64,
                Sha256 = item.Sha256,
                ImportedAtUtc = item.ImportedAtUtc,
                Enabled = false
            }).ToList();

            return jsRuntime.InvokeVoidAsync("modViewer.storeHhhFiles", records).AsTask();
        }

        public Task ClearAsync()
        {
            return jsRuntime.InvokeVoidAsync("modViewer.clearIndexedDb").AsTask();
        }
    }
}
