using System;

namespace AssetStudio.ModViewer.Models
{
    public class ModImportItem
    {
        public string Id { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ArchivePath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string SourceZipName { get; set; } = string.Empty;
        public string BodyPartTag { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string DataBase64 { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public DateTime ImportedAtUtc { get; set; }
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
