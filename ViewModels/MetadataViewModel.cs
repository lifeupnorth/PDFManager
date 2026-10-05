using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class MetadataViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;
        private readonly IDialogService _dialogs;

        private string _sourcePath = string.Empty;
        public string SourcePath
        {
            get => _sourcePath;
            set => SetProperty(ref _sourcePath, value);
        }

        private string _outputPath = string.Empty;
        public string OutputPath
        {
            get => _outputPath;
            set => SetProperty(ref _outputPath, value);
        }

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private string _author = string.Empty;
        public string Author
        {
            get => _author;
            set => SetProperty(ref _author, value);
        }

        private string _subject = string.Empty;
        public string Subject
        {
            get => _subject;
            set => SetProperty(ref _subject, value);
        }

        private string _keywords = string.Empty;
        public string Keywords
        {
            get => _keywords;
            set => SetProperty(ref _keywords, value);
        }

        // True once the current file's properties have been read. Until then the fields are
        // disabled and saving is not offered, so a save can never blank properties not yet loaded
        private bool _isLoaded;
        public bool IsLoaded
        {
            get => _isLoaded;
            set => SetProperty(ref _isLoaded, value);
        }

        private string _statusInfo = string.Empty;
        public string StatusInfo
        {
            get => _statusInfo;
            set => SetProperty(ref _statusInfo, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ICommand OpenFileCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand RunSaveCommand { get; }

        public MetadataViewModel(IPdfService pdfService, IDialogService dialogs)
        {
            _pdfService = pdfService;
            _dialogs = dialogs;
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput);
            RunSaveCommand = new RelayCommand(ExecuteRunSave, CanExecuteRunSave);
        }

        private async void ExecuteOpenFile(object? parameter)
        {
            string? path = _dialogs.PickFile("PDF Files (*.pdf)|*.pdf");
            if (path != null)
                await UpdateFileAsync(path);
        }

        private void ExecuteBrowseOutput(object? parameter)
        {
            string? path = _dialogs.PickSaveFile("PDF Files (*.pdf)|*.pdf", ".pdf", string.IsNullOrWhiteSpace(OutputPath)
                ? Path.GetFileNameWithoutExtension(SourcePath) + "_edited.pdf"
                : Path.GetFileName(OutputPath));
            if (path != null)
                OutputPath = path;
        }

        private bool CanExecuteRunSave(object? parameter)
        {
            return !IsBusy && IsLoaded && !string.IsNullOrWhiteSpace(OutputPath);
        }

        private async void ExecuteRunSave(object? parameter) => await SavePropertiesAsync();

        // The Save button's work, including its messages and confirmations
        public async Task SavePropertiesAsync()
        {
            // Saving over the source is safe here: the service writes a temporary file and moves
            // it into place only after the source is closed. Still confirm, since it changes the original
            bool replacesSource = string.Equals(Path.GetFullPath(SourcePath), Path.GetFullPath(OutputPath), StringComparison.OrdinalIgnoreCase);
            if (replacesSource || File.Exists(OutputPath))
            {
                string question = replacesSource
                    ? $"This will update the original file:\n{SourcePath}\n\nContinue?"
                    : $"\"{OutputPath}\" already exists and will be overwritten.\n\nContinue?";
                if (!_dialogs.Confirm(question, "Confirm Overwrite", DialogIcon.Warning))
                    return;
            }

            // Read the inputs here: the work runs on a background thread
            string sourcePath = SourcePath, outputPath = OutputPath;
            var metadata = new PdfMetadata(Title, Author, Subject, Keywords);
            try
            {
                IsBusy = true;
                await Task.Run(() => _pdfService.WriteMetadata(sourcePath, outputPath, metadata));

                if (_dialogs.Confirm("Properties saved successfully!\n\nOpen the output folder?", "Success", DialogIcon.Information))
                    _dialogs.ShowInExplorer(outputPath);
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                _dialogs.Show("This PDF is password-protected and its properties cannot be changed.", "Error", DialogIcon.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                _dialogs.Show("This file is not a valid PDF or is corrupted.", "Error", DialogIcon.Error);
            }
            catch (Exception ex)
            {
                // InvalidOperationException carries the service's own validation messages
                if (ex is not InvalidOperationException)
                    ErrorLog.Write(ex, "Metadata");
                _dialogs.Show($"Error saving properties: {ex.Message}", "Error", DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task UpdateFileAsync(string path)
        {
            SourcePath = path;
            OutputPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_edited.pdf");
            IsLoaded = false;
            ApplyMetadata(new PdfMetadata("", "", "", ""));
            StatusInfo = "Reading document properties...";

            (PdfMetadata? metadata, string status) = await Task.Run<(PdfMetadata?, string)>(() =>
            {
                try
                {
                    return (_pdfService.ReadMetadata(path), "Leave a field empty to remove that property.");
                }
                catch (iText.Kernel.Exceptions.BadPasswordException)
                {
                    return (null, "This PDF is password-protected and its properties cannot be changed.");
                }
                catch
                {
                    return (null, "Unable to read this file's properties.");
                }
            });

            // Another file may have been picked while this one was being read
            if (SourcePath != path)
                return;
            if (metadata != null)
                ApplyMetadata(metadata);
            IsLoaded = metadata != null;
            StatusInfo = status;
        }

        private void ApplyMetadata(PdfMetadata metadata)
        {
            Title = metadata.Title;
            Author = metadata.Author;
            Subject = metadata.Subject;
            Keywords = metadata.Keywords;
        }
    }
}
