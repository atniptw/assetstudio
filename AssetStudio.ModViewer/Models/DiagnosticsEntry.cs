using System;

namespace AssetStudio.ModViewer.Models
{
    public record DiagnosticsEntry(DateTime Timestamp, string Level, string Message);
}
