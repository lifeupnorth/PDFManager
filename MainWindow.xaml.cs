using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PDFManager.ViewModels;
using PDFManager.Services;

namespace PDFManager
{
    public partial class MainWindow : Window
    {
        // Shared with the view models; the drop handlers use it too
        private readonly IDialogService _dialogs = new WindowsDialogService();

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new MainViewModel(new PdfService(), new WindowsPageRenderer(), _dialogs, ThemeManager.Apply);
        }

        private MainViewModel ViewModel => (MainViewModel)DataContext;

        // The window chrome follows the theme: a dark title bar for the dark palette
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ThemeManager.SetTitleBar(this, ThemeManager.IsDark);
        }

        // Home tool cards carry the same view index as the sidebar items. The sidebar is the
        // source of truth, so a card just checks the matching sidebar item.
        private void OpenTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out int index))
            {
                var navItems = new[] { navHome, navSplit, navMerge, navRotate, navExtract, navMetadata, navProtect, navUnlock, navWatermark, navBatch, navPdfToImage, navImagesToPdf, navReorder };
                if (index >= 0 && index < navItems.Length)
                    navItems[index].IsChecked = true;
            }
        }

        // Sidebar items carry the index of the view they open in their Tag
        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return; // fired while InitializeComponent is still running
            if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out int index))
            {
                // Keep the chosen tool's highlight visible, even when it was opened from a Home card
                vm.Sidebar.ExpandSectionOf(index);
                vm.CurrentView = index switch
                {
                    1 => vm.Split,
                    2 => vm.Merge,
                    3 => vm.Rotate,
                    4 => vm.Extract,
                    5 => vm.Metadata,
                    6 => vm.Protect,
                    7 => vm.Unlock,
                    8 => vm.Watermark,
                    9 => vm.Batch,
                    10 => vm.PdfToImage,
                    11 => vm.ImagesToPdf,
                    12 => vm.Reorder,
                    _ => vm.HomeView
                };
            }
        }

        private void tab_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        // The dropped files and folders, or none when the drop is not files. The handlers below
        // only pass them on; what a drop may contain is checked in the view models
        private static string[] DroppedPaths(DragEventArgs e) =>
            e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();

        private async void splitTab_Drop(object sender, DragEventArgs e)
        {
            if (DroppedFiles.SinglePdf(DroppedPaths(e), "split", _dialogs) is string file)
                await ViewModel.Split.UpdateFileAsync(file);
        }

        private async void mergeTab_Drop(object sender, DragEventArgs e)
        {
            string[] files = DroppedPaths(e);
            if (files.Length > 0)
                await ViewModel.Merge.AddFilesAsync(files);
        }

        private async void rotateTab_Drop(object sender, DragEventArgs e)
        {
            if (DroppedFiles.SinglePdf(DroppedPaths(e), "rotate", _dialogs) is string file)
                await ViewModel.Rotate.UpdateFileAsync(file);
        }

        private async void extractTab_Drop(object sender, DragEventArgs e)
        {
            if (DroppedFiles.SinglePdf(DroppedPaths(e), "extract pages from", _dialogs) is string file)
                await ViewModel.Extract.UpdateFileAsync(file);
        }

        private async void metadataTab_Drop(object sender, DragEventArgs e)
        {
            if (DroppedFiles.SinglePdf(DroppedPaths(e), "edit", _dialogs) is string file)
                await ViewModel.Metadata.UpdateFileAsync(file);
        }

        private async void protectTab_Drop(object sender, DragEventArgs e)
        {
            if (DroppedFiles.SinglePdf(DroppedPaths(e), "protect", _dialogs) is string file)
                await ViewModel.Protect.UpdateFileAsync(file);
        }

        private async void unlockTab_Drop(object sender, DragEventArgs e)
        {
            if (DroppedFiles.SinglePdf(DroppedPaths(e), "unlock", _dialogs) is string file)
                await ViewModel.Unlock.UpdateFileAsync(file);
        }

        private async void watermarkTab_Drop(object sender, DragEventArgs e)
        {
            if (DroppedFiles.SinglePdf(DroppedPaths(e), "watermark", _dialogs) is string file)
                await ViewModel.Watermark.UpdateFileAsync(file);
        }

        private async void pdfToImageTab_Drop(object sender, DragEventArgs e)
        {
            if (!ViewModel.PdfToImage.IsBusy && DroppedFiles.SinglePdf(DroppedPaths(e), "convert", _dialogs) is string file)
                await ViewModel.PdfToImage.UpdateFileAsync(file);
        }

        private async void reorderTab_Drop(object sender, DragEventArgs e)
        {
            if (!ViewModel.Reorder.IsBusy && DroppedFiles.SinglePdf(DroppedPaths(e), "reorder", _dialogs) is string file)
                await ViewModel.Reorder.UpdateFileAsync(file);
        }

        // --- Reorder: dragging pages within the strip. A press that doesn't move stays a click,
        // which selects the page. Dropped files go on to reorderTab_Drop ---

        private const string PageDragFormat = "PDFManager.ReorderPages";
        private Point _pageDragStart;
        private PageThumbnail? _pageDragCandidate;

        private void reorderPage_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pageDragStart = e.GetPosition(this);
            _pageDragCandidate = (sender as FrameworkElement)?.DataContext as PageThumbnail;
        }

        private void reorderPage_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pageDragCandidate is not PageThumbnail page || ViewModel.Reorder.IsBusy)
                return;
            Vector moved = e.GetPosition(this) - _pageDragStart;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            _pageDragCandidate = null;

            // A selected page brings the rest of the selection along; an unselected one moves alone
            var allPages = ViewModel.Reorder.Thumbnails.Pages;
            var pages = page.IsSelected ? allPages.Where(p => p.IsSelected).ToList() : new List<PageThumbnail> { page };
            // The button captured the mouse when pressed. Releasing it lets the strip see the drag,
            // and keeps the button from treating the drop as a click
            Mouse.Capture(null);
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(PageDragFormat, pages), DragDropEffects.Move);
            ClearDropMarkers();
        }

        private void reorderStrip_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(PageDragFormat))
                return; // files go on to the tab's own handler
            var target = DropTarget((ItemsControl)sender, e.GetPosition((IInputElement)sender));
            e.Effects = target == null ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
            foreach (var page in ViewModel.Reorder.Thumbnails.Pages)
                page.DropMarker = page == target?.Page ? target.Value.Side : DropSide.None;
        }

        private void reorderStrip_DragLeave(object sender, DragEventArgs e)
        {
            // Also raised when moving between pages; only clear once the pointer has left the strip
            var strip = (FrameworkElement)sender;
            if (!new Rect(strip.RenderSize).Contains(e.GetPosition(strip)))
                ClearDropMarkers();
        }

        private void reorderStrip_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(PageDragFormat) is not List<PageThumbnail> pages)
                return;
            e.Handled = true;
            ClearDropMarkers();
            if (DropTarget((ItemsControl)sender, e.GetPosition((IInputElement)sender)) is (PageThumbnail page, DropSide side))
            {
                var reorder = ViewModel.Reorder;
                int index = reorder.Thumbnails.Pages.IndexOf(page) + (side == DropSide.After ? 1 : 0);
                reorder.MovePages(pages, index);
            }
        }

        // Scrolls while a page is dragged near the top or bottom edge, so it can reach any position
        private void reorderScroll_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (sender is not ScrollViewer scroll || !e.Data.GetDataPresent(PageDragFormat))
                return;
            const double edge = 40, step = 12;
            double y = e.GetPosition(scroll).Y;
            if (y < edge)
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset - step);
            else if (y > scroll.ActualHeight - edge)
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + step);
        }

        // Where a drop at `point` puts the pages: before the first page in that row whose middle is
        // to the right of the point, or after the row's last page. Gaps between pages count too
        private static (PageThumbnail Page, DropSide Side)? DropTarget(ItemsControl strip, Point point)
        {
            PageThumbnail? lastInRow = null;
            foreach (object item in strip.Items)
            {
                if (item is not PageThumbnail page || strip.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container)
                    continue;
                // The container includes the page's margin, so the gaps belong to a page
                Rect bounds = container.TransformToAncestor(strip).TransformBounds(new Rect(container.RenderSize));
                if (point.Y < bounds.Top || point.Y >= bounds.Bottom)
                    continue;
                if (point.X < bounds.Left + bounds.Width / 2)
                    return (page, DropSide.Before);
                lastInRow = page;
            }
            return lastInRow == null ? null : (lastInRow, DropSide.After);
        }

        private void ClearDropMarkers()
        {
            foreach (var page in ViewModel.Reorder.Thumbnails.Pages)
                page.DropMarker = DropSide.None;
        }

        // Image to PDF takes images and folders of images
        private void imagesToPdfTab_Drop(object sender, DragEventArgs e) => ViewModel.ImagesToPdf.AddDropped(DroppedPaths(e));

        // Batch takes any mix of PDFs and folders
        private void batchTab_Drop(object sender, DragEventArgs e) => ViewModel.Batch.AddDropped(DroppedPaths(e));
    }
}
