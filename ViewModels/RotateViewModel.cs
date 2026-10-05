using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;
using iText.Kernel.Pdf;

namespace PDFManager.ViewModels
{
    public class RotateViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;

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
                }
            }
        }

        private bool _isPageSpecVisible;
        public bool IsPageSpecVisible => !RotateAllPages;

        private string _rotatePageSpec = string.Empty;
        public string RotatePageSpec
        {
            get => _rotatePageSpec;
            set => SetProperty(ref _rotatePageSpec, value);
        }

        private int _rotationAngle = 90;
        public int RotationAngle
        {
            get => _rotationAngle;
            set => SetProperty(ref _rotationAngle, value);
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
        public ICommand RunRotateCommand { get; }

        public RotateViewModel(IPdfService pdfService)
        {
            _pdfService = pdfService;
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput);
            RunRotateCommand = new RelayCommand(ExecuteRunRotate, CanExecuteRunRotate);
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
                FileName = "Rotated File.pdf",
                DefaultExt = ".pdf"
            };
            if (string.IsNullOrWhiteSpace(OutputPath))
                saveFileDialog.FileName = Path.GetFileNameWithoutExtension(SourcePath) + "_rotated.pdf";
            else
                saveFileDialog.FileName = Path.GetFileName(OutputPath);

            if (saveFileDialog.ShowDialog() == true)
                OutputPath = saveFileDialog.FileName;
        }

        private bool CanExecuteRunRotate(object? parameter)
        {
            return !IsBusy && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(OutputPath);
        }

        private async void ExecuteRunRotate(object? parameter)
        {
            // PdfWriter truncates the output as soon as it is created, so writing
            // over the source would destroy it before it is read
            if (string.Equals(Path.GetFullPath(SourcePath), Path.GetFullPath(OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("The output file cannot be the same as the source file. Please choose a different output path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string pageSpec = RotateAllPages ? null : RotatePageSpec;
            if (pageSpec != null && string.IsNullOrWhiteSpace(pageSpec))
            {
                MessageBox.Show("Please enter the pages to rotate (for example: 1, 3, 5-7), or choose \"All pages\".", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            try
            {
                IsBusy = true;
                // Read the inputs here: the work runs on a background thread
                string sourcePath = SourcePath, outputPath = OutputPath;
                int degrees = RotationAngle;

                await Task.Run(() => _pdfService.RotatePdf(sourcePath, outputPath, pageSpec, degrees));

                var openFolder = MessageBox.Show("PDF rotated successfully!\n\nOpen the output folder?", "Success", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (openFolder == MessageBoxResult.Yes)
                    ShowInExplorer(outputPath);
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                MessageBox.Show("This PDF is password-protected and cannot be rotated.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                MessageBox.Show("This file is not a valid PDF or is corrupted.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                // InvalidOperationException carries the service's own validation messages
                if (ex is not InvalidOperationException)
                    ErrorLog.Write(ex, "Rotate");
                MessageBox.Show($"Error rotating PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task UpdateFileAsync(string path)
        {
            SourcePath = path;
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

        private void ShowInExplorer(string filePath)
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
    }
}