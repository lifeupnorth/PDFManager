#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;

namespace PDFManager.Services
{
    public sealed class WindowsDialogService : IDialogService
    {
        public void Show(string message, string title, DialogIcon icon) =>
            MessageBox.Show(message, title, MessageBoxButton.OK, ToImage(icon));

        public bool Confirm(string message, string title, DialogIcon icon) =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, ToImage(icon)) == MessageBoxResult.Yes;

        public string? PickFile(string filter)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public IReadOnlyList<string> PickFiles(string filter)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, Multiselect = true };
            return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
        }

        public string? PickSaveFile(string filter, string defaultExtension, string fileName)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = filter, DefaultExt = defaultExtension, FileName = fileName };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string? PickFolder(string title, string? initialFolder = null)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
            if (initialFolder != null)
                dialog.InitialDirectory = initialFolder;
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        public void ShowInExplorer(string filePath) =>
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");

        private static MessageBoxImage ToImage(DialogIcon icon) => icon switch
        {
            DialogIcon.Question => MessageBoxImage.Question,
            DialogIcon.Warning => MessageBoxImage.Warning,
            DialogIcon.Error => MessageBoxImage.Error,
            _ => MessageBoxImage.Information
        };
    }
}
