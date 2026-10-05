using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;
using iText.Kernel.Pdf;

namespace PDFManager.ViewModels
{
    public class SplitViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;

        private string _sourcePath = string.Empty;
        public string SourcePath
        {
            get => _sourcePath;
            set => SetProperty(ref _sourcePath, value);
        }

        private string _splitPageNum = "1";
        public string SplitPageNum
        {
            get => _splitPageNum;
            set => SetProperty(ref _splitPageNum, value);
        }

        private string _outputFolder = string.Empty;
        public string OutputFolder
        {
            get => _outputFolder;
            set => SetProperty(ref _outputFolder, value);
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
        public ICommand RunSplitCommand { get; }

        public SplitViewModel(IPdfService pdfService)
        {
            _pdfService = pdfService;
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            RunSplitCommand = new RelayCommand(ExecuteSplit, CanExecuteSplit);
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

        private async void ExecuteSplit(object? parameter)
        {
            if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath))
            {
                MessageBox.Show("Please select a valid PDF file to split.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(SplitPageNum, out int pageNum) || pageNum < 1)
            {
                MessageBox.Show("Please enter a valid page number greater than 0.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string destPattern = _pdfService.GetSplitDestPattern(SourcePath);
            var existingOutputs = new[] { 1, 2 }
                .Select(i => string.Format(destPattern, i))
                .Where(File.Exists)
                .ToList();
            if (existingOutputs.Any())
            {
                var result = MessageBox.Show(
                    $"The following files already exist and will be overwritten:\n{string.Join("\n", existingOutputs)}\n\nContinue?",
                    "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            // Read the input here: the work runs on a background thread
            string sourcePath = SourcePath;
            try
            {
                IsBusy = true;
                await Task.Run(() => _pdfService.SplitPdf(sourcePath, pageNum, Path.GetDirectoryName(sourcePath)!));

                var openFolder = MessageBox.Show("PDF split successfully!\n\nOpen the output folder?", "Success", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (openFolder == MessageBoxResult.Yes)
                    ShowInExplorer(string.Format(destPattern, 1));
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                MessageBox.Show("This PDF is password-protected and cannot be split.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                MessageBox.Show("This file is not a valid PDF or is corrupted.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                // InvalidOperationException carries the service's own validation messages
                if (ex is not InvalidOperationException)
                    ErrorLog.Write(ex, "Split");
                MessageBox.Show($"Error splitting PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanExecuteSplit(object? parameter) => !IsBusy;

        public async Task UpdateFileAsync(string path)
        {
            SourcePath = path;
            OutputFolder = Path.GetDirectoryName(path)!;
            PageCountInfo = "Reading page count...";

            string info = await Task.Run(() =>
            {
                try
                {
                    int pages = _pdfService.GetPageCount(path);
                    return pages > 1
                        ? $"Enter a page from 1 to {pages - 1}. The document has {pages} pages."
                        : "This document has only 1 page and cannot be split.";
                }
                catch (iText.Kernel.Exceptions.BadPasswordException)
                {
                    return "This PDF is password-protected and cannot be split.";
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