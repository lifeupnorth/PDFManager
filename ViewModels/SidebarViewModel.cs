#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    /// <summary>
    /// Which sidebar sections are expanded. Collapsed sections are remembered between sessions,
    /// and opening a tool expands its section so its highlight is always visible.
    /// </summary>
    public class SidebarViewModel : ObservableObject
    {
        private readonly HashSet<string> _collapsed;

        public SidebarViewModel()
        {
            _collapsed = new HashSet<string>(AppSettings.Load().CollapsedSections, StringComparer.OrdinalIgnoreCase);
        }

        public bool OrganizeExpanded { get => IsExpanded("Organize"); set => SetExpanded("Organize", value, nameof(OrganizeExpanded)); }
        public bool EditExpanded { get => IsExpanded("Edit"); set => SetExpanded("Edit", value, nameof(EditExpanded)); }
        public bool SecurityExpanded { get => IsExpanded("Security"); set => SetExpanded("Security", value, nameof(SecurityExpanded)); }
        public bool ConvertExpanded { get => IsExpanded("Convert"); set => SetExpanded("Convert", value, nameof(ConvertExpanded)); }
        public bool BatchExpanded { get => IsExpanded("Batch"); set => SetExpanded("Batch", value, nameof(BatchExpanded)); }

        // The tool indexes match the sidebar items' Tag values
        public void ExpandSectionOf(int toolIndex)
        {
            switch (toolIndex)
            {
                case 1 or 2 or 4: OrganizeExpanded = true; break;      // Split, Merge, Extract
                case 3 or 5 or 8: EditExpanded = true; break;          // Rotate, Metadata, Watermark
                case 6 or 7: SecurityExpanded = true; break;           // Protect, Unlock
                case 10 or 11: ConvertExpanded = true; break;          // PDF to image, Image to PDF
                case 9: BatchExpanded = true; break;                   // Batch
            }
        }

        private bool IsExpanded(string section) => !_collapsed.Contains(section);

        private void SetExpanded(string section, bool expanded, string propertyName)
        {
            bool changed = expanded ? _collapsed.Remove(section) : _collapsed.Add(section);
            if (!changed)
                return;
            OnPropertyChanged(propertyName);
            // Re-read so only this setting changes, whatever else the file holds
            var settings = AppSettings.Load();
            settings.CollapsedSections = _collapsed.OrderBy(s => s).ToList();
            AppSettings.Save(settings);
        }
    }
}
