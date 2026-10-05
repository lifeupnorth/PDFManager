using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    /// <summary>One page in a thumbnail strip.</summary>
    public class PageThumbnail : ObservableObject
    {
        public PageThumbnail(int pageNumber) => PageNumber = pageNumber;

        public int PageNumber { get; }

        // Null until the page has been rendered; the view shows the page number meanwhile
        private ImageSource? _image;
        public ImageSource? Image
        {
            get => _image;
            set => SetProperty(ref _image, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                    OnPropertyChanged(nameof(AccessibleName));
            }
        }

        // Extra rotation drawn on the thumbnail, to preview what Rotate will do
        private double _previewRotation;
        public double PreviewRotation
        {
            get => _previewRotation;
            set => SetProperty(ref _previewRotation, value);
        }

        // Split marks the page the document will be split after
        private bool _isSplitAfter;
        public bool IsSplitAfter
        {
            get => _isSplitAfter;
            set
            {
                if (SetProperty(ref _isSplitAfter, value))
                    OnPropertyChanged(nameof(AccessibleName));
            }
        }

        public string AccessibleName =>
            $"Page {PageNumber}" + (IsSelected ? ", selected" : "") + (IsSplitAfter ? ", split after this page" : "");
    }

    /// <summary>
    /// Page thumbnails for the selected file. Each tool decides what a click on a page means,
    /// through PageClicked, and marks pages through the PageThumbnail properties.
    /// </summary>
    public class PageThumbnailsViewModel : ObservableObject
    {
        // Thumbnail width in device-independent pixels; Windows renders at the display's scale
        public const int ThumbnailWidth = 104;

        private readonly IPageRenderer _renderer;
        private CancellationTokenSource? _loading;

        public ObservableCollection<PageThumbnail> Pages { get; } = new ObservableCollection<PageThumbnail>();

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public ICommand PageClickCommand { get; }

        // Raised with the page number when a thumbnail is clicked
        public event Action<int>? PageClicked;

        // Raised once a file's pages are listed, before their images arrive, so the tool can mark them
        public event Action? PagesLoaded;

        // The load in progress, for callers that need to wait for every image
        public Task Loading { get; private set; } = Task.CompletedTask;

        public PageThumbnailsViewModel(IPageRenderer renderer)
        {
            _renderer = renderer;
            PageClickCommand = new RelayCommand(p => { if (p is PageThumbnail page) PageClicked?.Invoke(page.PageNumber); });
        }

        public IEnumerable<int> SelectedPages => Pages.Where(p => p.IsSelected).Select(p => p.PageNumber);

        public void SetSelection(ICollection<int> pages)
        {
            foreach (var page in Pages)
                page.IsSelected = pages.Contains(page.PageNumber);
        }

        // Starts showing a file's pages; a load already running for another file stops
        public Task LoadAsync(string path)
        {
            _loading?.Cancel();
            var loading = new CancellationTokenSource();
            _loading = loading;
            Loading = LoadPagesAsync(path, loading.Token);
            return Loading;
        }

        private async Task LoadPagesAsync(string path, CancellationToken cancelled)
        {
            Pages.Clear();
            StatusText = "Loading page previews...";

            IRenderedDocument document;
            try
            {
                document = await Task.Run(() => _renderer.OpenAsync(path));
            }
            catch
            {
                if (!cancelled.IsCancellationRequested)
                    StatusText = "Page previews are not available for this file.";
                return;
            }

            using (document)
            {
                if (cancelled.IsCancellationRequested)
                    return;

                for (int i = 1; i <= document.PageCount; i++)
                    Pages.Add(new PageThumbnail(i));
                StatusText = string.Empty;
                PagesLoaded?.Invoke();

                // One page at a time, in order, so the first pages appear first
                foreach (var page in Pages.ToList())
                {
                    if (cancelled.IsCancellationRequested)
                        return;
                    try
                    {
                        page.Image = await Task.Run(() => document.RenderPageAsync(page.PageNumber, ThumbnailWidth));
                    }
                    catch
                    {
                        // A page that fails to render keeps its numbered placeholder
                    }
                }
            }
        }
    }
}
