#nullable enable
using System.Collections.Generic;

namespace PDFManager.Services
{
    public enum DialogIcon { Information, Question, Warning, Error }

    // Everything a view model asks the user, or opens outside the app. View models use this
    // instead of MessageBox and the file dialogs, so their confirmation and error paths can be
    // tested with a scripted fake.
    public interface IDialogService
    {
        // A message with an OK button
        void Show(string message, string title, DialogIcon icon);

        // A Yes/No question; true means Yes
        bool Confirm(string message, string title, DialogIcon icon);

        // The chosen file, or null if the dialog was cancelled
        string? PickFile(string filter);

        // The chosen files, or none if the dialog was cancelled
        IReadOnlyList<string> PickFiles(string filter);

        // The chosen save path, or null if the dialog was cancelled
        string? PickSaveFile(string filter, string defaultExtension, string fileName);

        // The chosen folder, or null if the dialog was cancelled
        string? PickFolder(string title, string? initialFolder = null);

        // Opens Explorer with the file selected
        void ShowInExplorer(string filePath);
    }
}
