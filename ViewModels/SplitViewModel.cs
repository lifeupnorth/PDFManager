using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class SplitViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;
        private readonly IDialogService _dialogs;

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
            set { if (SetProperty(ref _splitPageNum, value)) ShowSplitPoint(); }
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

        public PageThumbnailsViewModel Thumbnails { get; }

        public ICommand OpenFileCommand { get; }
        public ICommand RunSplitCommand { get; }

        public SplitViewModel(IPdfService pdfService, IPageRenderer pageRenderer, IDialogService dialogs)
        {
            _pdfService = pdfService;
            _dialogs = dialogs;
            Thumbnails = new PageThumbnailsViewModel(pageRenderer);
            Thumbnails.PageClicked += SplitAfterPage;
            Thumbnails.PagesLoaded += ShowSplitPoint;
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            RunSplitCommand = new RelayCommand(ExecuteSplit, CanExecuteSplit);
        }

        private async void ExecuteOpenFile(object? parameter)
        {
            string? path = _dialogs.PickFile("PDF Files (*.pdf)|*.pdf");
            if (path != null)
                await UpdateFileAsync(path);
        }

        private async void ExecuteSplit(object? parameter) => await SplitAsync();

        // The Split button's work, including its messages and confirmations
        public async Task SplitAsync()
        {
            if (string.IsNullOrWhiteSpace(SourcePath) || !File.Exists(SourcePath))
            {
                _dialogs.Show("Please select a valid PDF file to split.", "Validation Error", DialogIcon.Warning);
                return;
            }

            if (!int.TryParse(SplitPageNum, out int pageNum) || pageNum < 1)
            {
                _dialogs.Show("Please enter a valid page number greater than 0.", "Validation Error", DialogIcon.Warning);
                return;
            }

            string destPattern = _pdfService.GetSplitDestPattern(SourcePath);
            var existingOutputs = new[] { 1, 2 }
                .Select(i => string.Format(destPattern, i))
                .Where(File.Exists)
                .ToList();
            if (existingOutputs.Any() && !_dialogs.Confirm(
                    $"The following files already exist and will be overwritten:\n{string.Join("\n", existingOutputs)}\n\nContinue?",
                    "Confirm Overwrite", DialogIcon.Warning))
                return;

            // Read the input here: the work runs on a background thread
            string sourcePath = SourcePath;
            try
            {
                IsBusy = true;
                await Task.Run(() => _pdfService.SplitPdf(sourcePath, pageNum, Path.GetDirectoryName(sourcePath)!));

                if (_dialogs.Confirm("PDF split successfully!\n\nOpen the output folder?", "Success", DialogIcon.Information))
                    _dialogs.ShowInExplorer(string.Format(destPattern, 1));
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                _dialogs.Show("This PDF is password-protected and cannot be split.", "Error", DialogIcon.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                _dialogs.Show("This file is not a valid PDF or is corrupted.", "Error", DialogIcon.Error);
            }
            catch (Exception ex)
            {
                // InvalidOperationException carries the service's own validation messages
                if (ex is not InvalidOperationException)
                    ErrorLog.Write(ex, "Split");
                _dialogs.Show($"Error splitting PDF: {ex.Message}", "Error", DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanExecuteSplit(object? parameter) => !IsBusy;

        // A click on a page splits after it; there is nothing to split after the last page
        private void SplitAfterPage(int page)
        {
            if (page < Thumbnails.Pages.Count)
                SplitPageNum = page.ToString();
        }

        private void ShowSplitPoint()
        {
            int.TryParse(SplitPageNum, out int splitAfter);
            foreach (var page in Thumbnails.Pages)
                page.IsSplitAfter = page.PageNumber == splitAfter && splitAfter < Thumbnails.Pages.Count;
        }

        public async Task UpdateFileAsync(string path)
        {
            SourcePath = path;
            _ = Thumbnails.LoadAsync(path); // the page previews fill in while the page count is read
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
    }
}