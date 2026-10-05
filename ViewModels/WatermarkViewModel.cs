#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class WatermarkViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;
        private readonly IDialogService _dialogs;
        private readonly PageListSelection _pageSelection;

        // The preview is an A4 portrait page, 210 x 297 units
        public const double PreviewPageWidth = 210, PreviewPageHeight = 297;

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

        private string _watermarkText = "CONFIDENTIAL";
        public string WatermarkText
        {
            get => _watermarkText;
            set => SetProperty(ref _watermarkText, value);
        }

        private bool _isDiagonal = true;
        public bool IsDiagonal
        {
            get => _isDiagonal;
            set
            {
                if (SetProperty(ref _isDiagonal, value))
                {
                    OnPropertyChanged(nameof(PreviewAngle));
                    OnPropertyChanged(nameof(PreviewTextWidth));
                    OnPropertyChanged(nameof(PreviewTextLeft));
                }
            }
        }

        private int _opacityPercent = 30;
        public int OpacityPercent
        {
            get => _opacityPercent;
            set
            {
                if (SetProperty(ref _opacityPercent, value))
                    OnPropertyChanged(nameof(PreviewOpacity));
            }
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

        // The preview mirrors the service's layout: WPF rotates clockwise, so the corner-to-corner
        // angle is negative, and the text spans 75% of the line it runs along
        public double PreviewAngle => IsDiagonal ? -Math.Atan2(PreviewPageHeight, PreviewPageWidth) * 180 / Math.PI : 0;
        public double PreviewTextWidth => 0.75 * (IsDiagonal ? Math.Sqrt(PreviewPageWidth * PreviewPageWidth + PreviewPageHeight * PreviewPageHeight) : PreviewPageWidth);
        public double PreviewTextMaxHeight => Math.Min(PreviewPageWidth, PreviewPageHeight) / 4;
        public double PreviewOpacity => OpacityPercent / 100.0;
        // The text box sits on a Canvas, centred on the page, so a diagonal box wider than the
        // page is not clipped to a layout slot before it is rotated
        public double PreviewTextLeft => (PreviewPageWidth - PreviewTextWidth) / 2;
        public double PreviewTextTop => (PreviewPageHeight - PreviewTextMaxHeight) / 2;

        public PageThumbnailsViewModel Thumbnails { get; }

        public ICommand OpenFileCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand RunWatermarkCommand { get; }

        public WatermarkViewModel(IPdfService pdfService, IPageRenderer pageRenderer, IDialogService dialogs)
        {
            _pdfService = pdfService;
            _dialogs = dialogs;
            Thumbnails = new PageThumbnailsViewModel(pageRenderer);
            _pageSelection = new PageListSelection(Thumbnails, () => PageSpec, list => PageSpec = list, () => AllPages, () => AllPages = false);
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput);
            RunWatermarkCommand = new RelayCommand(ExecuteRunWatermark, CanExecuteRunWatermark);
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
                ? Path.GetFileNameWithoutExtension(SourcePath) + "_watermarked.pdf"
                : Path.GetFileName(OutputPath));
            if (path != null)
                OutputPath = path;
        }

        private bool CanExecuteRunWatermark(object? parameter)
        {
            return !IsBusy && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(OutputPath);
        }

        private async void ExecuteRunWatermark(object? parameter) => await WatermarkAsync();

        // The Add watermark button's work, including its messages and confirmations
        public async Task WatermarkAsync()
        {
            // PdfWriter truncates the output as soon as it is created, so writing
            // over the source would destroy it before it is read
            if (string.Equals(Path.GetFullPath(SourcePath), Path.GetFullPath(OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                _dialogs.Show("The output file cannot be the same as the source file. Please choose a different output path.", "Validation Error", DialogIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(WatermarkText))
            {
                _dialogs.Show("Please enter the watermark text.", "Validation Error", DialogIcon.Warning);
                return;
            }

            string? pageSpec = AllPages ? null : PageSpec;
            if (pageSpec != null && string.IsNullOrWhiteSpace(pageSpec))
            {
                _dialogs.Show("Please enter the pages to watermark (for example: 1, 3, 5-7), or choose \"All pages\".", "Validation Error", DialogIcon.Warning);
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
            string sourcePath = SourcePath, outputPath = OutputPath;
            var options = new WatermarkOptions(WatermarkText.Trim(), IsDiagonal, OpacityPercent, pageSpec);
            try
            {
                IsBusy = true;
                await Task.Run(() => _pdfService.AddWatermark(sourcePath, outputPath, options));

                if (_dialogs.Confirm("Watermark added successfully!\n\nOpen the output folder?", "Success", DialogIcon.Information))
                    _dialogs.ShowInExplorer(outputPath);
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                _dialogs.Show("This PDF is password-protected and cannot be watermarked.", "Error", DialogIcon.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                _dialogs.Show("This file is not a valid PDF or is corrupted.", "Error", DialogIcon.Error);
            }
            catch (Exception ex)
            {
                // InvalidOperationException carries the service's own validation messages
                if (ex is not InvalidOperationException)
                    ErrorLog.Write(ex, "Watermark");
                _dialogs.Show($"Error adding watermark: {ex.Message}", "Error", DialogIcon.Error);
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
            OutputPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_watermarked.pdf");
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
                    return "This PDF is password-protected and cannot be watermarked.";
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
