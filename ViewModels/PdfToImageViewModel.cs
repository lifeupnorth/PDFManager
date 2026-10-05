using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class PdfToImageViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;
        private readonly IDialogService _dialogs;
        private readonly PdfImageExporter _exporter;
        private readonly PageListSelection _pageSelection;
        private CancellationTokenSource? _cancel;
        private int _pageCount;

        private string _sourcePath = string.Empty;
        public string SourcePath
        {
            get => _sourcePath;
            set => SetProperty(ref _sourcePath, value);
        }

        private string _outputFolder = string.Empty;
        public string OutputFolder
        {
            get => _outputFolder;
            set => SetProperty(ref _outputFolder, value);
        }

        private ImageFormat _format = ImageFormat.Png;
        public ImageFormat Format
        {
            get => _format;
            set => SetProperty(ref _format, value);
        }

        private int _dpi = 150;
        public int Dpi
        {
            get => _dpi;
            set => SetProperty(ref _dpi, value);
        }

        private bool _allPages = true;
        public bool AllPages
        {
            get => _allPages;
            set
            {
                if (SetProperty(ref _allPages, value))
                {
                    OnPropertyChanged(nameof(IsPageSpecEnabled));
                    _pageSelection.Refresh();
                }
            }
        }

        public bool IsPageSpecEnabled => !AllPages;

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

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private string _progressText = string.Empty;
        public string ProgressText
        {
            get => _progressText;
            set => SetProperty(ref _progressText, value);
        }

        public PageThumbnailsViewModel Thumbnails { get; }

        public ICommand OpenFileCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand RunConvertCommand { get; }
        public ICommand CancelCommand { get; }

        public PdfToImageViewModel(IPdfService pdfService, IPageRenderer pageRenderer, IDialogService dialogs)
        {
            _pdfService = pdfService;
            _dialogs = dialogs;
            _exporter = new PdfImageExporter(pageRenderer);
            Thumbnails = new PageThumbnailsViewModel(pageRenderer);
            _pageSelection = new PageListSelection(Thumbnails, () => PageSpec, list => PageSpec = list, () => AllPages, () => AllPages = false);
            OpenFileCommand = new RelayCommand(ExecuteOpenFile, _ => !IsBusy);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput, _ => !IsBusy);
            RunConvertCommand = new RelayCommand(ExecuteRunConvert, _ => !IsBusy && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(OutputFolder));
            CancelCommand = new RelayCommand(_ => _cancel?.Cancel(), _ => IsBusy);
        }

        private async void ExecuteOpenFile(object? parameter)
        {
            string? path = _dialogs.PickFile("PDF Files (*.pdf)|*.pdf");
            if (path != null)
                await UpdateFileAsync(path);
        }

        private void ExecuteBrowseOutput(object? parameter)
        {
            string? existing = Directory.Exists(OutputFolder) ? OutputFolder : Path.GetDirectoryName(OutputFolder);
            string? folder = _dialogs.PickFolder("Choose where to save the images",
                !string.IsNullOrEmpty(existing) && Directory.Exists(existing) ? existing : null);
            if (folder != null)
                OutputFolder = folder;
        }

        private async void ExecuteRunConvert(object? parameter) => await RunConvertAsync();

        // The Convert button's work, including its messages and confirmations
        public async Task RunConvertAsync()
        {
            ISet<int>? pages = null;
            if (!AllPages)
            {
                if (string.IsNullOrWhiteSpace(PageSpec))
                {
                    _dialogs.Show("Please enter the pages to convert (for example: 1, 3, 5-7), or choose \"All pages\".", "Validation Error", DialogIcon.Warning);
                    return;
                }
                try
                {
                    pages = PageRanges.Parse(PageSpec, _pageCount > 0 ? _pageCount : int.MaxValue);
                }
                catch (InvalidOperationException ex)
                {
                    _dialogs.Show(ex.Message, "Validation Error", DialogIcon.Warning);
                    return;
                }
            }

            if (_pageCount > 0)
            {
                var planned = PdfImageExporter.PlanFiles(SourcePath, OutputFolder, pages ?? Enumerable.Range(1, _pageCount), _pageCount, Format);
                int existing = planned.Count(File.Exists);
                if (existing > 0)
                {
                    if (!_dialogs.Confirm(
                            $"{existing} image{(existing == 1 ? "" : "s")} in \"{OutputFolder}\" will be overwritten.\n\nContinue?",
                            "Confirm Overwrite", DialogIcon.Warning))
                        return;
                }
            }

            int total = pages?.Count ?? _pageCount;
            var outcome = await ConvertAsync(pages);
            if (outcome.Error != null)
            {
                _dialogs.Show(outcome.Error, "Error", DialogIcon.Error);
            }
            else if (outcome.Cancelled)
            {
                _dialogs.Show($"Conversion cancelled after {outcome.Saved} of {total} pages. The images already saved were kept.", "Cancelled", DialogIcon.Information);
            }
            else
            {
                if (_dialogs.Confirm($"Saved {outcome.Saved} image{(outcome.Saved == 1 ? "" : "s")} to:\n{OutputFolder}\n\nOpen the folder?", "Success", DialogIcon.Information) && outcome.FirstFile != null)
                    _dialogs.ShowInExplorer(outcome.FirstFile);
            }
        }

        public record ConvertOutcome(int Saved, bool Cancelled, string? Error, string? FirstFile);

        // Converts the pages (all when null) with the current settings. Shows no dialogs;
        // validation and confirmation happen before, and the caller reports the outcome
        public async Task<ConvertOutcome> ConvertAsync(ISet<int>? pages)
        {
            // Read the inputs here: the work runs on a background thread
            string sourcePath = SourcePath, folder = OutputFolder;
            ImageFormat format = Format;
            int dpi = Dpi, total = pages?.Count ?? _pageCount, saved = 0;

            using var cancel = new CancellationTokenSource();
            _cancel = cancel;
            IsBusy = true;
            Progress = 0;
            ProgressText = "Converting...";
            // Count saved pages as they happen; the bar and text update on the UI thread
            var showProgress = new Progress<int>(count =>
            {
                Progress = total > 0 ? 100.0 * count / total : 0;
                ProgressText = total > 0 ? $"Saved {count} of {total} pages" : $"Saved {count} pages";
            });
            var progress = new InlineProgress(count =>
            {
                Interlocked.Exchange(ref saved, count);
                ((IProgress<int>)showProgress).Report(count);
            });
            try
            {
                // Rendering and encoding are slow; keep them off the UI thread
                var files = await Task.Run(() => _exporter.ExportAsync(sourcePath, folder, pages, format, dpi, progress, cancel.Token));
                return new ConvertOutcome(files.Count, false, null, files.FirstOrDefault());
            }
            catch (OperationCanceledException)
            {
                return new ConvertOutcome(saved, true, null, null);
            }
            catch (InvalidOperationException ex)
            {
                return new ConvertOutcome(saved, false, ex.Message, null);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "PdfToImage");
                return new ConvertOutcome(saved, false, $"Error converting PDF: {ex.Message}", null);
            }
            finally
            {
                IsBusy = false;
                ProgressText = string.Empty;
                _cancel = null;
            }
        }

        private sealed class InlineProgress : IProgress<int>
        {
            private readonly Action<int> _report;
            public InlineProgress(Action<int> report) => _report = report;
            public void Report(int value) => _report(value);
        }

        public async Task UpdateFileAsync(string path)
        {
            SourcePath = path;
            OutputFolder = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_images");
            _pageCount = 0;
            _ = Thumbnails.LoadAsync(path); // the page previews fill in while the page count is read
            PageCountInfo = "Reading page count...";

            (int pages, string info) = await Task.Run(() =>
            {
                try
                {
                    int count = _pdfService.GetPageCount(path);
                    return (count, $"Document has {count} page{(count == 1 ? "" : "s")}. Example: 1, 3, 5-7");
                }
                catch (iText.Kernel.Exceptions.BadPasswordException)
                {
                    return (0, "This PDF is password-protected. Remove its password with Unlock PDF first.");
                }
                catch
                {
                    return (0, "Unable to read the page count.");
                }
            });

            // Another file may have been picked while this one was being read
            if (SourcePath == path)
            {
                _pageCount = pages;
                PageCountInfo = info;
            }
        }
    }
}
