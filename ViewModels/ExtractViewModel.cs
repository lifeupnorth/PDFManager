using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class ExtractViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;

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
            set => SetProperty(ref _pageSpec, value);
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

        public ICommand OpenFileCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand RunExtractCommand { get; }

        public ExtractViewModel(IPdfService pdfService)
        {
            _pdfService = pdfService;
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput);
            RunExtractCommand = new RelayCommand(ExecuteRunExtract, CanExecuteRunExtract);
        }

        private async void ExecuteOpenFile(object? parameter)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf"
            };

            if (openFileDialog.ShowDialog() == true)
                await UpdateFileAsync(openFileDialog.FileName);
        }

        private void ExecuteBrowseOutput(object? parameter)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                FileName = string.IsNullOrWhiteSpace(OutputPath)
                    ? Path.GetFileNameWithoutExtension(SourcePath) + "_extracted.pdf"
                    : Path.GetFileName(OutputPath)
            };

            if (saveFileDialog.ShowDialog() == true)
                OutputPath = saveFileDialog.FileName;
        }

        private bool CanExecuteRunExtract(object? parameter)
        {
            return !IsBusy && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(OutputPath);
        }

        private async void ExecuteRunExtract(object? parameter)
        {
            // PdfWriter truncates the output as soon as it is created, so writing
            // over the source would destroy it before it is read
            if (string.Equals(Path.GetFullPath(SourcePath), Path.GetFullPath(OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("The output file cannot be the same as the source file. Please choose a different output path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(PageSpec))
            {
                MessageBox.Show("Please enter the pages to extract (for example: 1, 3, 5-7).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (File.Exists(OutputPath))
            {
                var result = MessageBox.Show(
                    $"\"{OutputPath}\" already exists and will be overwritten.\n\nContinue?",
                    "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            // Read the inputs here: the work runs on a background thread
            string sourcePath = SourcePath, outputPath = OutputPath, pageSpec = PageSpec;
            try
            {
                IsBusy = true;
                await Task.Run(() => _pdfService.ExtractPages(sourcePath, outputPath, pageSpec));

                var openFolder = MessageBox.Show("Pages extracted successfully!\n\nOpen the output folder?", "Success", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (openFolder == MessageBoxResult.Yes)
                    ShowInExplorer(outputPath);
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                MessageBox.Show("This PDF is password-protected and pages cannot be extracted from it.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                MessageBox.Show("This file is not a valid PDF or is corrupted.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                // InvalidOperationException carries the service's own validation messages
                if (ex is not InvalidOperationException)
                    ErrorLog.Write(ex, "Extract");
                MessageBox.Show($"Error extracting pages: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task UpdateFileAsync(string path)
        {
            SourcePath = path;
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

        private void ShowInExplorer(string filePath)
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
    }
}
