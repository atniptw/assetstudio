using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AssetStudio.ModViewer.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace AssetStudio.ModViewer.Services
{
    public class ModImportService
    {
        private const long MaxZipBytes = 128 * 1024 * 1024;

        private static readonly HashSet<string> SupportedBodyPartTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "head",
            "neck",
            "body",
            "hip",
            "leftarm",
            "rightarm",
            "leftleg",
            "rightleg",
            "world"
        };

        public async Task<ModImportSummary> ImportZipAsync(IBrowserFile zipFile)
        {
            var summary = new ModImportSummary
            {
                SourceZipName = zipFile.Name
            };

            using var stream = zipFile.OpenReadStream(MaxZipBytes);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            memory.Position = 0;

            using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
            var hhhEntries = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Where(entry => string.Equals(Path.GetExtension(entry.Name), ".hhh", StringComparison.OrdinalIgnoreCase))
                .ToList();

            summary.TotalHhhFiles = hhhEntries.Count;

            foreach (var entry in hhhEntries)
            {
                summary.Items.Add(await ParseEntryAsync(entry, zipFile.Name));
            }

            return summary;
        }

        private static async Task<ModImportItem> ParseEntryAsync(ZipArchiveEntry entry, string sourceZipName)
        {
            var fileName = entry.Name;
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var splitIndex = fileNameWithoutExt.LastIndexOf('_');
            var importedAtUtc = DateTime.UtcNow;

            if (splitIndex <= 0 || splitIndex >= fileNameWithoutExt.Length - 1)
            {
                return new ModImportItem
                {
                    FileName = fileName,
                    ArchivePath = entry.FullName,
                    DisplayName = fileNameWithoutExt,
                    SourceZipName = sourceZipName,
                    BodyPartTag = string.Empty,
                    SizeBytes = entry.Length,
                    ImportedAtUtc = importedAtUtc,
                    IsValid = false,
                    ErrorMessage = "Missing required body-part tag suffix (expected *_tag.hhh)."
                };
            }

            var namePart = fileNameWithoutExt[..splitIndex];
            var tagPart = fileNameWithoutExt[(splitIndex + 1)..];

            if (!SupportedBodyPartTags.Contains(tagPart))
            {
                return new ModImportItem
                {
                    FileName = fileName,
                    ArchivePath = entry.FullName,
                    DisplayName = namePart,
                    SourceZipName = sourceZipName,
                    BodyPartTag = tagPart,
                    SizeBytes = entry.Length,
                    ImportedAtUtc = importedAtUtc,
                    IsValid = false,
                    ErrorMessage = "Unsupported body-part tag."
                };
            }

            await using var entryStream = entry.Open();
            await using var memory = new MemoryStream();
            await entryStream.CopyToAsync(memory);
            var bytes = memory.ToArray();
            var checksum = ComputeSha256(bytes);

            return new ModImportItem
            {
                Id = $"{checksum}:{entry.FullName}".ToLowerInvariant(),
                FileName = fileName,
                ArchivePath = entry.FullName,
                DisplayName = namePart,
                SourceZipName = sourceZipName,
                BodyPartTag = tagPart.ToLowerInvariant(),
                SizeBytes = bytes.LongLength,
                DataBase64 = Convert.ToBase64String(bytes),
                Sha256 = checksum,
                ImportedAtUtc = importedAtUtc,
                IsValid = true,
                ErrorMessage = string.Empty
            };
        }

        private static string ComputeSha256(byte[] data)
        {
            var hash = SHA256.HashData(data);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
