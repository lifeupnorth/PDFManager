#nullable enable
using System;
using System.Collections.Generic;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    /// <summary>
    /// Keeps a tool's typed page list ("1, 3, 5-7") and its thumbnail selection in step: clicking a
    /// page rewrites the list, and typing a valid list selects its pages. For tools with an
    /// "All pages" choice, no page is highlighted while it is on, and clicking a page turns it off.
    /// </summary>
    internal sealed class PageListSelection
    {
        private readonly PageThumbnailsViewModel _thumbnails;
        private readonly Func<string> _getList;
        private readonly Action<string> _setList;
        private readonly Func<bool> _isAllPages;
        private readonly Action _chooseSpecificPages;
        private bool _writingList;

        // Raised after the highlighted pages change
        public event Action? Changed;

        public PageListSelection(PageThumbnailsViewModel thumbnails, Func<string> getList, Action<string> setList,
            Func<bool>? isAllPages = null, Action? chooseSpecificPages = null)
        {
            _thumbnails = thumbnails;
            _getList = getList;
            _setList = setList;
            _isAllPages = isAllPages ?? (() => false);
            _chooseSpecificPages = chooseSpecificPages ?? (() => { });
            thumbnails.PageClicked += Toggle;
            thumbnails.PagesLoaded += Refresh;
        }

        // Call when the list or the "All pages" choice changes
        public void Refresh()
        {
            if (_writingList)
                return;

            if (_isAllPages())
            {
                _thumbnails.SetSelection(Array.Empty<int>());
            }
            else if (_thumbnails.Pages.Count > 0)
            {
                string list = _getList();
                // A list still being typed ("1-") does not parse; keep the last selection until it does
                var pages = string.IsNullOrWhiteSpace(list) ? new HashSet<int>() : PageRanges.TryParse(list, _thumbnails.Pages.Count);
                if (pages != null)
                    _thumbnails.SetSelection(pages);
            }
            Changed?.Invoke();
        }

        private void Toggle(int page)
        {
            // Coming from "All pages", start a new list with just this page
            var pages = _isAllPages() ? new HashSet<int>() : new HashSet<int>(_thumbnails.SelectedPages);
            if (!pages.Remove(page))
                pages.Add(page);

            _writingList = true;
            try
            {
                _chooseSpecificPages();
                _setList(PageRanges.Format(pages));
            }
            finally
            {
                _writingList = false;
            }

            _thumbnails.SetSelection(pages);
            Changed?.Invoke();
        }
    }
}
