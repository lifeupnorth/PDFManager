using System;
using System.Windows;
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
                var navItems = new[] { navHome, navSplit, navMerge, navRotate, navExtract, navMetadata, navProtect, navUnlock, navWatermark, navBatch, navPdfToImage, navImagesToPdf };
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

        // Image to PDF takes images and folders of images
        private void imagesToPdfTab_Drop(object sender, DragEventArgs e) => ViewModel.ImagesToPdf.AddDropped(DroppedPaths(e));

        // Batch takes any mix of PDFs and folders
        private void batchTab_Drop(object sender, DragEventArgs e) => ViewModel.Batch.AddDropped(DroppedPaths(e));
    }
}
