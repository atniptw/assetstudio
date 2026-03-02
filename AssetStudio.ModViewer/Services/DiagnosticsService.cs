using System;
using System.Collections.Generic;

namespace AssetStudio.ModViewer.Services
{
    public class DiagnosticsService
    {
        private readonly List<Models.DiagnosticsEntry> entries = new();

        public IReadOnlyList<Models.DiagnosticsEntry> Entries => entries;

        public event Action? OnChanged;

        public void Add(string level, string message)
        {
            entries.Add(new Models.DiagnosticsEntry(DateTime.Now, level, message));
            OnChanged?.Invoke();
        }

        public void Clear()
        {
            entries.Clear();
            OnChanged?.Invoke();
        }
    }
}
