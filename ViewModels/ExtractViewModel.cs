#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class ExtractViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;
        private readonly IDialogService _dialogs;
        private readonly PageListSelection _pageSelection;

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

        private string _pageSpec = string.Empty;
        public string PageSpec
        {
            get => _pageSpec;
            set { if (SetProperty(ref _pageSpec, value)) _pageSelection.Refresh(); }
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
        public ICommand RunExtractCommand { get; }

        public ExtractViewModel(IPdfService pdfService, IPageRenderer pageRenderer, IDialogService dialogs)
        {
            _pdfService = pdfService;
            _dialogs = dialogs;
            Thumbnails = new PageThumbnailsViewModel(pageRenderer);
            _pageSelection = new PageListSelection(Thumbnails, () => PageSpec, list => PageSpec = list);
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput);
            RunExtractCommand = new RelayCommand(ExecuteRunExtract, CanExecuteRunExtract);
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
                ? Path.GetFileNameWithoutExtension(SourcePath) + "_extracted.pdf"
                : Path.GetFileName(OutputPath));
            if (path != null)
                OutputPath = path;
        }

        private bool CanExecuteRunExtract(object? parameter)
        {
            return !IsBusy && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(OutputPath);
        }

        private async void ExecuteRunExtract(object? parameter) => await ExtractAsync();

        // The Extract button's work, including its messages and confirmations
        public async Task ExtractAsync()
        {
            // PdfWriter truncates the output as soon as it is created, so writing
            // over the source would destroy it before it is read
            if (string.Equals(Path.GetFullPath(SourcePath), Path.GetFullPath(OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                _dialogs.Show("The output file cannot be the same as the source file. Please choose a different output path.", "Validation Error", DialogIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(PageSpec))
            {
                _dialogs.Show("Please enter the pages to extract (for example: 1, 3, 5-7).", "Validation Error", DialogIcon.Warning);
                return;
            }

            if (File.Exists(OutputPath))
            {
                if (!_dialogs.Confirm(
                        $"\"{OutputPath}\" already exists and will be overwritten.\n\nContinue?",
                        "Confirm Overwrite", DialogIcon.Warning))
                    return;
            }

            // Read the inputs here: the work runs on a background thread
            string sourcePath = SourcePath, outputPath = OutputPath, pageSpec = PageSpec;
            try
            {
                IsBusy = true;
                await Task.Run(() => _pdfService.ExtractPages(sourcePath, outputPath, pageSpec));

                if (_dialogs.Confirm("Pages extracted successfully!\n\nOpen the output folder?", "Success", DialogIcon.Information))
                    _dialogs.ShowInExplorer(outputPath);
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                _dialogs.Show("This PDF is password-protected and pages cannot be extracted from it.", "Error", DialogIcon.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                _dialogs.Show("This file is not a valid PDF or is corrupted.", "Error", DialogIcon.Error);
            }
            catch (Exception ex)
            {
                // InvalidOperationException carries the service's own validation messages
                if (ex is not InvalidOperationException)
                    ErrorLog.Write(ex, "Extract");
                _dialogs.Show($"Error extracting pages: {ex.Message}", "Error", DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task UpdateFileAsync(string path)
        {
            SourcePath = path;
            _ = Thumbnails.LoadAsync(path); // the page previews fill in while the page count is read
            OutputPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_extracted.pdf");
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
                    return "This PDF is password-protected and pages cannot be extracted from it.";
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
