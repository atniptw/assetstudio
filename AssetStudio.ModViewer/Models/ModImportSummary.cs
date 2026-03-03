using System.Collections.Generic;
using System.Linq;

namespace AssetStudio.ModViewer.Models
{
    public class ModImportSummary
    {
        public string SourceZipName { get; set; } = string.Empty;
        public int TotalHhhFiles { get; set; }
        public List<ModImportItem> Items { get; set; } = new();

        public int AcceptedCount => Items.Count(item => item.IsValid);
        public int RejectedCount => Items.Count(item => !item.IsValid);
    }
}
