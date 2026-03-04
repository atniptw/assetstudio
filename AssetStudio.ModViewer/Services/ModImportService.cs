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

        private static readonly HashSet<string> SupportedCompanionExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".ress",
            ".resource",
            ".assets",
            ".asset"
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

            var companionEntries = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Where(entry => !string.Equals(Path.GetExtension(entry.Name), ".hhh", StringComparison.OrdinalIgnoreCase))
                .Where(IsSupportedCompanionEntry)
                .ToList();

            var companionsByArchivePath = new Dictionary<string, ModImportItem.CompanionFilePayload>(StringComparer.OrdinalIgnoreCase);
            foreach (var companionEntry in companionEntries)
            {
                await using var companionStream = companionEntry.Open();
                await using var companionMemory = new MemoryStream();
                await companionStream.CopyToAsync(companionMemory);
                var companionBytes = companionMemory.ToArray();

                companionsByArchivePath[companionEntry.FullName] = new ModImportItem.CompanionFilePayload
                {
                    FileName = companionEntry.Name,
                    ArchivePath = companionEntry.FullName,
                    SizeBytes = companionBytes.LongLength,
                    DataBase64 = Convert.ToBase64String(companionBytes),
                    Sha256 = ComputeSha256(companionBytes)
                };
            }

            summary.TotalHhhFiles = hhhEntries.Count;

            foreach (var entry in hhhEntries)
            {
                summary.Items.Add(await ParseEntryAsync(entry, zipFile.Name, companionsByArchivePath));
            }

            return summary;
        }

        private static async Task<ModImportItem> ParseEntryAsync(
            ZipArchiveEntry entry,
            string sourceZipName,
            Dictionary<string, ModImportItem.CompanionFilePayload> companionsByArchivePath)
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
            var linkedCompanions = ResolveLinkedCompanions(fileNameWithoutExt, entry.FullName, bytes, companionsByArchivePath);

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
                CompanionFiles = linkedCompanions,
                IsValid = true,
                ErrorMessage = string.Empty
            };
        }

        private static List<ModImportItem.CompanionFilePayload> ResolveLinkedCompanions(
            string fileNameWithoutExt,
            string archivePath,
            byte[] hhhBytes,
            Dictionary<string, ModImportItem.CompanionFilePayload> companionsByArchivePath)
        {
            if (companionsByArchivePath.Count == 0)
                return new List<ModImportItem.CompanionFilePayload>();

            var result = new List<ModImportItem.CompanionFilePayload>();
            var archiveDirectory = Path.GetDirectoryName(archivePath)?.Replace('\\', '/') ?? string.Empty;
            var baseName = fileNameWithoutExt;
            var splitIndex = fileNameWithoutExt.LastIndexOf('_');
            if (splitIndex > 0)
            {
                baseName = fileNameWithoutExt[..splitIndex];
            }

            var cabTokens = ExtractCabTokens(hhhBytes);
            foreach (var companion in companionsByArchivePath.Values)
            {
                var companionDirectory = Path.GetDirectoryName(companion.ArchivePath)?.Replace('\\', '/') ?? string.Empty;
                var sameDirectory = string.Equals(companionDirectory, archiveDirectory, StringComparison.OrdinalIgnoreCase);
                var sameStem = companion.FileName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase);
                var referencedCab = cabTokens.Any(token =>
                    companion.FileName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    companion.ArchivePath.Contains(token, StringComparison.OrdinalIgnoreCase));

                if (sameDirectory || sameStem || referencedCab)
                {
                    result.Add(companion);
                }
            }

            return result
                .GroupBy(item => item.ArchivePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static List<string> ExtractCabTokens(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return new List<string>();

            var text = System.Text.Encoding.UTF8.GetString(bytes);
            var matches = System.Text.RegularExpressions.Regex.Matches(text, @"CAB-[a-fA-F0-9]{32}");
            return matches
                .Select(match => match.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSupportedCompanionEntry(ZipArchiveEntry entry)
        {
            var ext = Path.GetExtension(entry.Name);
            if (SupportedCompanionExtensions.Contains(ext))
                return true;

            return entry.Name.StartsWith("CAB-", StringComparison.OrdinalIgnoreCase) ||
                   entry.Name.Contains(".resS", StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeSha256(byte[] data)
        {
            var hash = SHA256.HashData(data);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
