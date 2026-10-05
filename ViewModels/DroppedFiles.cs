#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    // The checks behind dropping files on a tool's page. MainWindow's drop handlers only take
    // the dropped paths out of the drag data.
    public static class DroppedFiles
    {
        // The one PDF dropped on a single-file tool, or null. A drop of several files or of a
        // file that is not a PDF is explained; `action` completes "Please drop a single PDF file to ..."
        public static string? SinglePdf(IReadOnlyList<string> paths, string action, IDialogService dialogs)
        {
            if (paths.Count == 0)
                return null;

            if (paths.Count > 1)
            {
                dialogs.Show($"Please drop a single PDF file to {action}.", "Validation Error", DialogIcon.Warning);
                return null;
            }

            if (!string.Equals(Path.GetExtension(paths[0]), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                dialogs.Show("Only PDF files are supported.", "Validation Error", DialogIcon.Warning);
                return null;
            }

            return paths[0];
        }
    }
}
