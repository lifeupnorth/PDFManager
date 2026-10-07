using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    /// <summary>
    /// Rearranges a PDF's pages. The thumbnails are the order: moving a page moves its thumbnail,
    /// and each keeps its original page number, so the strip reads as the new order (3, 1, 2...).
    /// </summary>
    public class ReorderViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;
        private readonly IDialogService _dialogs;

        // Set while ApplyOrder moves several pages, so the order is announced once at the end
        private bool _applyingOrder;

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

        private IList<PageThumbnail> Pages => Thumbnails.Pages;

        // The original page numbers in their new order
        public IReadOnlyList<int> PageOrder => Pages.Select(p => p.PageNumber).ToList();

        public bool IsOrderChanged => Pages.Where((page, index) => page.PageNumber != index + 1).Any();

        public string OrderSummary =>
            Pages.Count == 0 ? string.Empty
            : IsOrderChanged ? "New order: " + PageRanges.FormatSequence(PageOrder)
            : "Original order";

        public ICommand OpenFileCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand RunReorderCommand { get; }
        public ICommand MoveToStartCommand { get; }
        public ICommand MoveEarlierCommand { get; }
        public ICommand MoveLaterCommand { get; }
        public ICommand MoveToEndCommand { get; }
        public ICommand ReverseCommand { get; }
        public ICommand ResetOrderCommand { get; }

        public ReorderViewModel(IPdfService pdfService, IPageRenderer pageRenderer, IDialogService dialogs)
        {
            _pdfService = pdfService;
            _dialogs = dialogs;
            Thumbnails = new PageThumbnailsViewModel(pageRenderer);
            Thumbnails.PageClicked += TogglePage;
            Thumbnails.Pages.CollectionChanged += (_, _) => { if (!_applyingOrder) OrderChanged(); };

            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput);
            RunReorderCommand = new RelayCommand(ExecuteRunReorder, CanExecuteRunReorder);
            MoveToStartCommand = new RelayCommand(_ => MoveToStart(), _ => CanMoveSelection);
            MoveEarlierCommand = new RelayCommand(_ => MoveEarlier(), _ => CanMoveSelection);
            MoveLaterCommand = new RelayCommand(_ => MoveLater(), _ => CanMoveSelection);
            MoveToEndCommand = new RelayCommand(_ => MoveToEnd(), _ => CanMoveSelection);
            ReverseCommand = new RelayCommand(_ => Reverse(), _ => !IsBusy && Pages.Count > 1);
            ResetOrderCommand = new RelayCommand(_ => ResetOrder(), _ => !IsBusy && IsOrderChanged);
        }

        private bool CanMoveSelection => !IsBusy && Pages.Any(p => p.IsSelected);

        private void OrderChanged()
        {
            OnPropertyChanged(nameof(PageOrder));
            OnPropertyChanged(nameof(IsOrderChanged));
            OnPropertyChanged(nameof(OrderSummary));
        }

        private void TogglePage(int pageNumber)
        {
            if (Pages.FirstOrDefault(p => p.PageNumber == pageNumber) is PageThumbnail page)
                page.IsSelected = !page.IsSelected;
        }

        // --- Moving pages. Selected pages move together and keep their order among themselves ---

        public void MoveToStart() => MovePages(Pages.Where(p => p.IsSelected).ToList(), 0);

        public void MoveToEnd() => MovePages(Pages.Where(p => p.IsSelected).ToList(), Pages.Count);

        // Each selected page swaps with the unselected page before it, so a block moves as one
        public void MoveEarlier()
        {
            var order = Pages.ToList();
            for (int i = 1; i < order.Count; i++)
                if (order[i].IsSelected && !order[i - 1].IsSelected)
                    (order[i - 1], order[i]) = (order[i], order[i - 1]);
            ApplyOrder(order);
        }

        public void MoveLater()
        {
            var order = Pages.ToList();
            for (int i = order.Count - 2; i >= 0; i--)
                if (order[i].IsSelected && !order[i + 1].IsSelected)
                    (order[i], order[i + 1]) = (order[i + 1], order[i]);
            ApplyOrder(order);
        }

        // Puts `pages` at `insertIndex`, a position in the current order (0 to the page count),
        // in the order they have now. Dragging pages onto the strip lands here too
        public void MovePages(IReadOnlyCollection<PageThumbnail> pages, int insertIndex)
        {
            if (pages.Count == 0)
                return;
            var moving = Pages.Where(pages.Contains).ToList();
            // Pages that move out from before the insert position shift it back
            int index = insertIndex - Pages.Take(insertIndex).Count(pages.Contains);
            var rest = Pages.Where(p => !pages.Contains(p)).ToList();
            rest.InsertRange(Math.Clamp(index, 0, rest.Count), moving);
            ApplyOrder(rest);
        }

        public void Reverse() => ApplyOrder(Pages.Reverse().ToList());

        public void ResetOrder() => ApplyOrder(Pages.OrderBy(p => p.PageNumber).ToList());

        // Moves the thumbnails into `order` (the same pages), keeping each one's state and image
        private void ApplyOrder(IList<PageThumbnail> order)
        {
            _applyingOrder = true;
            try
            {
                for (int i = 0; i < order.Count; i++)
                {
                    int current = Pages.IndexOf(order[i]);
                    if (current != i)
                        Thumbnails.Pages.Move(current, i);
                }
            }
            finally
            {
                _applyingOrder = false;
            }
            OrderChanged();
        }

        // --- Files ---

        private async void ExecuteOpenFile(object? parameter)
        {
            string? path = _dialogs.PickFile("PDF Files (*.pdf)|*.pdf");
            if (path != null)
                await UpdateFileAsync(path);
        }

        private void ExecuteBrowseOutput(object? parameter)
        {
            string? path = _dialogs.PickSaveFile("PDF Files (*.pdf)|*.pdf", ".pdf", string.IsNullOrWhiteSpace(OutputPath)
                ? Path.GetFileNameWithoutExtension(SourcePath) + "_reordered.pdf"
                : Path.GetFileName(OutputPath));
            if (path != null)
                OutputPath = path;
        }

        private bool CanExecuteRunReorder(object? parameter)
        {
            return !IsBusy && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(OutputPath);
        }

        private async void ExecuteRunReorder(object? parameter) => await ReorderAsync();

        // The Save button's work, including its messages and confirmations
        public async Task ReorderAsync()
        {
            // PdfWriter truncates the output as soon as it is created, so writing
            // over the source would destroy it before it is read
            if (string.Equals(Path.GetFullPath(SourcePath), Path.GetFullPath(OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                _dialogs.Show("The output file cannot be the same as the source file. Please choose a different output path.", "Validation Error", DialogIcon.Warning);
                return;
            }

            // The order comes from the thumbnails, so without them there is nothing to save
            if (Pages.Count == 0)
            {
                _dialogs.Show("The pages of this PDF could not be loaded, so they cannot be reordered. It may be password-protected or damaged.", "Validation Error", DialogIcon.Warning);
                return;
            }

            if (!IsOrderChanged)
            {
                _dialogs.Show("The pages are still in their original order. Drag pages to a new place, or select pages and use the move buttons.", "Validation Error", DialogIcon.Warning);
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
            IReadOnlyList<int> order = PageOrder;
            try
            {
                IsBusy = true;
                await Task.Run(() => _pdfService.ReorderPages(sourcePath, outputPath, order));

                if (_dialogs.Confirm("Pages reordered successfully!\n\nOpen the output folder?", "Success", DialogIcon.Information))
                    _dialogs.ShowInExplorer(outputPath);
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                _dialogs.Show("This PDF is password-protected and its pages cannot be reordered.", "Error", DialogIcon.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                _dialogs.Show("This file is not a valid PDF or is corrupted.", "Error", DialogIcon.Error);
            }
            catch (Exception ex)
            {
                // InvalidOperationException carries the service's own validation messages
                if (ex is not InvalidOperationException)
                    ErrorLog.Write(ex, "Reorder");
                _dialogs.Show($"Error reordering pages: {ex.Message}", "Error", DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task UpdateFileAsync(string path)
        {
            SourcePath = path;
            _ = Thumbnails.LoadAsync(path); // the pages fill in while the page count is read
            OutputPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_reordered.pdf");
            PageCountInfo = "Reading page count...";

            string info = await Task.Run(() =>
            {
                try
                {
                    int pages = _pdfService.GetPageCount(path);
                    return $"Document has {pages} page{(pages == 1 ? "" : "s")}.";
                }
                catch (iText.Kernel.Exceptions.BadPasswordException)
                {
                    return "This PDF is password-protected and its pages cannot be reordered.";
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
