using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;
using iText.Kernel.Pdf;

namespace PDFManager.ViewModels
{
    public class RotateViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;
        private readonly IDialogService _dialogs;
        private readonly PageListSelection _pageSelection;

        private string _sourcePath = string.Empty;
        public string SourcePath
        {
            get => _sourcePath;
            set { if (SetProperty(ref _sourcePath, value)) OnPropertyChanged(nameof(SourcePath)); }
        }

        private string _outputPath = string.Empty;
        public string OutputPath
        {
            get => _outputPath;
            set => SetProperty(ref _outputPath, value);
        }

        private bool _rotateAllPages = true;
        public bool RotateAllPages
        {
            get => _rotateAllPages;
            set
            {
                if (SetProperty(ref _rotateAllPages, value))
                {
                    OnPropertyChanged(nameof(IsPageSpecVisible));
                    _pageSelection.Refresh();
                }
            }
        }

        public bool IsPageSpecVisible => !RotateAllPages;

        private string _rotatePageSpec = string.Empty;
        public string RotatePageSpec
        {
            get => _rotatePageSpec;
            set { if (SetProperty(ref _rotatePageSpec, value)) _pageSelection.Refresh(); }
        }

        private int _rotationAngle = 90;
        public int RotationAngle
        {
            get => _rotationAngle;
            set { if (SetProperty(ref _rotationAngle, value)) ShowRotationPreview(); }
        }

        private string _pageCountInfo = string.Empty;
        public string PageCountInfo
        {
            get => _pageCountInfo;
            set => SetProperty(ref _pageCountInfo, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public PageThumbnailsViewModel Thumbnails { get; }

        public ICommand OpenFileCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand RunRotateCommand { get; }

        public RotateViewModel(IPdfService pdfService, IPageRenderer pageRenderer, IDialogService dialogs)
        {
            _pdfService = pdfService;
            _dialogs = dialogs;
            Thumbnails = new PageThumbnailsViewModel(pageRenderer);
            _pageSelection = new PageListSelection(Thumbnails, () => RotatePageSpec, list => RotatePageSpec = list, () => RotateAllPages, () => RotateAllPages = false);
            _pageSelection.Changed += ShowRotationPreview;
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput);
            RunRotateCommand = new RelayCommand(ExecuteRunRotate, CanExecuteRunRotate);
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
                ? Path.GetFileNameWithoutExtension(SourcePath) + "_rotated.pdf"
                : Path.GetFileName(OutputPath));
            if (path != null)
                OutputPath = path;
        }

        private bool CanExecuteRunRotate(object? parameter)
        {
            return !IsBusy && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(OutputPath);
        }

        private async void ExecuteRunRotate(object? parameter) => await RotateAsync();

        // The Rotate button's work, including its messages and confirmations
        public async Task RotateAsync()
        {
            // PdfWriter truncates the output as soon as it is created, so writing
            // over the source would destroy it before it is read
            if (string.Equals(Path.GetFullPath(SourcePath), Path.GetFullPath(OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                _dialogs.Show("The output file cannot be the same as the source file. Please choose a different output path.", "Validation Error", DialogIcon.Warning);
                return;
            }

            string pageSpec = RotateAllPages ? null : RotatePageSpec;
            if (pageSpec != null && string.IsNullOrWhiteSpace(pageSpec))
            {
                _dialogs.Show("Please enter the pages to rotate (for example: 1, 3, 5-7), or choose \"All pages\".", "Validation Error", DialogIcon.Warning);
                return;
            }

            if (File.Exists(OutputPath))
            {
                if (!_dialogs.Confirm(
                        $"\"{OutputPath}\" already exists and will be overwritten.\n\nContinue?",
                        "Confirm Overwrite", DialogIcon.Warning))
                    return;
            }

            try
            {
                IsBusy = true;
                // Read the inputs here: the work runs on a background thread
                string sourcePath = SourcePath, outputPath = OutputPath;
                int degrees = RotationAngle;

                await Task.Run(() => _pdfService.RotatePdf(sourcePath, outputPath, pageSpec, degrees));

                if (_dialogs.Confirm("PDF rotated successfully!\n\nOpen the output folder?", "Success", DialogIcon.Information))
                    _dialogs.ShowInExplorer(outputPath);
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                _dialogs.Show("This PDF is password-protected and cannot be rotated.", "Error", DialogIcon.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                _dialogs.Show("This file is not a valid PDF or is corrupted.", "Error", DialogIcon.Error);
            }
            catch (Exception ex)
            {
                // InvalidOperationException carries the service's own validation messages
                if (ex is not InvalidOperationException)
                    ErrorLog.Write(ex, "Rotate");
                _dialogs.Show($"Error rotating PDF: {ex.Message}", "Error", DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Turn the thumbnails of the pages that will rotate, so the result shows before running
        private void ShowRotationPreview()
        {
            foreach (var page in Thumbnails.Pages)
                page.PreviewRotation = RotateAllPages || page.IsSelected ? RotationAngle : 0;
        }

        public async Task UpdateFileAsync(string path)
        {
            SourcePath = path;
            _ = Thumbnails.LoadAsync(path); // the page previews fill in while the page count is read
            OutputPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_rotated.pdf");
            PageCountInfo = "Reading page count...";

            string info = await Task.Run(() =>
            {
                try
                {
                    int pages = _pdfService.GetPageCount(path);
                    return $"Document has {pages} page{(pages == 1 ? "" : "s")}. Example: 1, 3, 5-7";
                }
                catch (iText.Kernel.Exceptions.BadPasswordException)
                {
                    return "This PDF is password-protected and cannot be rotated.";
                }
                catch
                {
                    return "Unable to read the page count.";
                }
            });

            // Another file may have been picked while this one was being read
            if (SourcePath == path)
                PageCountInfo = info;
        }
    }
}