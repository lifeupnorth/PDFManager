#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    /// <summary>An image in the list, with a small upright preview that loads in the background.</summary>
    public class ImageItem : ObservableObject
    {
        public ImageItem(string path) => Path = path;

        public string Path { get; }
        public string FileName => System.IO.Path.GetFileName(Path);

        private ImageSource? _thumbnail;
        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set => SetProperty(ref _thumbnail, value);
        }

        private string _details = string.Empty;
        public string Details
        {
            get => _details;
            set => SetProperty(ref _details, value);
        }

        // The list's row tooltip shows the item itself
        public override string ToString() => Path;
    }

    public class ImagesToPdfViewModel : ObservableObject
    {
        private const int ThumbnailWidth = 96;

        private readonly IPdfService _pdfService;
        private readonly IDialogService _dialogs;

        public ObservableCollection<ImageItem> Images { get; } = new ObservableCollection<ImageItem>();

        private ImagePageSize _pageSize = ImagePageSize.A4;
        public ImagePageSize PageSize
        {
            get => _pageSize;
            set
            {
                if (SetProperty(ref _pageSize, value))
                    OnPropertyChanged(nameof(CanHaveMargin));
            }
        }

        // Margins only apply to paper sizes; a page the size of its image has none
        public bool CanHaveMargin => PageSize != ImagePageSize.SameAsImage;

        private bool _margin = true;
        public bool Margin
        {
            get => _margin;
            set => SetProperty(ref _margin, value);
        }

        private string _outputPath = string.Empty;
        public string OutputPath
        {
            get => _outputPath;
            set => SetProperty(ref _outputPath, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ICommand AddImagesCommand { get; }
        public ICommand RemoveImagesCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand RunCreateCommand { get; }

        public ImagesToPdfViewModel(IPdfService pdfService, IDialogService dialogs)
        {
            _pdfService = pdfService;
            _dialogs = dialogs;
            AddImagesCommand = new RelayCommand(ExecuteAddImages, _ => !IsBusy);
            RemoveImagesCommand = new RelayCommand(ExecuteRemoveImages, p => !IsBusy && p is IList { Count: > 0 });
            MoveUpCommand = new RelayCommand(p => Move(p, -1), p => !IsBusy && CanMove(p, -1));
            MoveDownCommand = new RelayCommand(p => Move(p, +1), p => !IsBusy && CanMove(p, +1));
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput, _ => !IsBusy);
            RunCreateCommand = new RelayCommand(ExecuteRunCreate, _ => !IsBusy && Images.Count > 0 && !string.IsNullOrWhiteSpace(OutputPath));

            // Collection changes are not property changes, so ask for a CanExecute requery here too
            Images.CollectionChanged += (s, e) => CommandManager.InvalidateRequerySuggested();
        }

        private void ExecuteAddImages(object? parameter)
        {
            AddPaths(_dialogs.PickFiles(ImageFiles.DialogFilter));
        }

        // Files and folders dropped on the page: added like AddPaths, saying so when no new image
        // was found. Ignored while a PDF is being created
        public void AddDropped(IReadOnlyList<string> paths)
        {
            if (paths.Count > 0 && !IsBusy && AddPaths(paths) == 0)
                _dialogs.Show("No new images were found in what you dropped. Supported formats are JPEG, PNG, BMP, GIF and TIFF.", "No Images Added", DialogIcon.Information);
        }

        // Adds image files, and the images directly inside any folders, in name order. Images
        // already listed are skipped. The first images added also choose the output name.
        // Returns how many were added
        public int AddPaths(IEnumerable<string> paths)
        {
            var listed = new HashSet<string>(Images.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
            var added = new List<ImageItem>();
            foreach (string path in paths)
            {
                IEnumerable<string> candidates = Directory.Exists(path)
                    ? Directory.GetFiles(path).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    : new[] { path };
                foreach (string file in candidates.Where(ImageFiles.IsImage))
                {
                    string full = System.IO.Path.GetFullPath(file);
                    if (listed.Add(full))
                    {
                        var item = new ImageItem(full);
                        Images.Add(item);
                        added.Add(item);
                    }
                }
            }

            if (added.Count > 0 && string.IsNullOrWhiteSpace(OutputPath))
            {
                string folder = System.IO.Path.GetDirectoryName(added[0].Path)!;
                string name = Images.Count == 1 ? System.IO.Path.GetFileNameWithoutExtension(added[0].Path) : "Images";
                OutputPath = System.IO.Path.Combine(folder, name + ".pdf");
            }

            if (added.Count > 0)
                Previews = LoadPreviewsAsync(added);
            return added.Count;
        }

        // The previews for added images, so the list fills in without freezing the window
        public Task Previews { get; private set; } = Task.CompletedTask;

        private static async Task LoadPreviewsAsync(List<ImageItem> items)
        {
            foreach (var item in items)
            {
                try
                {
                    item.Thumbnail = await Task.Run(() => ImageFiles.LoadThumbnail(item.Path, ThumbnailWidth));
                }
                catch
                {
                    item.Details = "Cannot be read as an image";
                }
            }
        }

        private void ExecuteRemoveImages(object? parameter)
        {
            // Snapshot the selection: removing items changes it
            if (parameter is IList selected)
                foreach (var item in selected.Cast<ImageItem>().ToList())
                    Images.Remove(item);
        }

        // Moving works on exactly one selected image, as in Merge
        private static ImageItem? SingleSelected(object? parameter) =>
            parameter is IList { Count: 1 } selected ? selected[0] as ImageItem : null;

        private bool CanMove(object? parameter, int step)
        {
            var item = SingleSelected(parameter);
            int index = item == null ? -1 : Images.IndexOf(item);
            return index >= 0 && index + step >= 0 && index + step < Images.Count;
        }

        private void Move(object? parameter, int step)
        {
            if (CanMove(parameter, step) && SingleSelected(parameter) is ImageItem item)
            {
                int index = Images.IndexOf(item);
                Images.Move(index, index + step);
            }
        }

        private void ExecuteBrowseOutput(object? parameter)
        {
            string? path = _dialogs.PickSaveFile("PDF Files (*.pdf)|*.pdf", ".pdf", string.IsNullOrWhiteSpace(OutputPath) ? "Images.pdf" : System.IO.Path.GetFileName(OutputPath));
            if (path != null)
                OutputPath = path;
        }

        private async void ExecuteRunCreate(object? parameter) => await CreatePdfAsync();

        // The Create PDF button's work, including its messages and confirmations
        public async Task CreatePdfAsync()
        {
            var missing = Images.Where(i => !File.Exists(i.Path)).Select(i => i.FileName).ToList();
            if (missing.Any())
            {
                _dialogs.Show($"The following images no longer exist:\n{string.Join("\n", missing)}", "Validation Error", DialogIcon.Warning);
                return;
            }

            if (!Directory.Exists(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(OutputPath))))
            {
                _dialogs.Show("The output folder does not exist. Please choose another output file.", "Validation Error", DialogIcon.Warning);
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
            var paths = Images.Select(i => i.Path).ToList();
            string outputPath = OutputPath;
            var options = new ImagePageOptions(PageSize, CanHaveMargin && Margin);
            try
            {
                IsBusy = true;
                await Task.Run(() => _pdfService.ImagesToPdf(paths, outputPath, options));

                if (_dialogs.Confirm($"PDF created with {paths.Count} page{(paths.Count == 1 ? "" : "s")}!\n\nOpen the output folder?", "Success", DialogIcon.Information))
                    _dialogs.ShowInExplorer(outputPath);
            }
            catch (InvalidOperationException ex)
            {
                // The service names the image it could not read
                _dialogs.Show(ex.Message, "Error", DialogIcon.Error);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "ImagesToPdf");
                _dialogs.Show($"Error creating PDF: {ex.Message}", "Error", DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
