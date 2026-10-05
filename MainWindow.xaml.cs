using System;
using System.IO;
using System.Windows;
using PDFManager.ViewModels;
using PDFManager.Services;

namespace PDFManager
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new MainViewModel(new PdfService(), new WindowsPageRenderer());
        }

        private MainViewModel ViewModel => (MainViewModel)DataContext;

        // Ask DWM for a dark title bar so the window chrome matches the dark theme.
        // Silently ignored on Windows versions that do not support the attribute.
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
                int enabled = 1;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
            }
            catch { }
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

        private async void splitTab_Drop(object sender, DragEventArgs e)
        {
            if (TryGetSingleDroppedPdf(e, "split", out string file))
                await ViewModel.Split.UpdateFileAsync(file);
        }

        private async void mergeTab_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                await ViewModel.Merge.AddFilesAsync(files);
        }

        private async void rotateTab_Drop(object sender, DragEventArgs e)
        {
            if (TryGetSingleDroppedPdf(e, "rotate", out string file))
                await ViewModel.Rotate.UpdateFileAsync(file);
        }

        private async void extractTab_Drop(object sender, DragEventArgs e)
        {
            if (TryGetSingleDroppedPdf(e, "extract pages from", out string file))
                await ViewModel.Extract.UpdateFileAsync(file);
        }

        private async void metadataTab_Drop(object sender, DragEventArgs e)
        {
            if (TryGetSingleDroppedPdf(e, "edit", out string file))
                await ViewModel.Metadata.UpdateFileAsync(file);
        }

        private async void protectTab_Drop(object sender, DragEventArgs e)
        {
            if (TryGetSingleDroppedPdf(e, "protect", out string file))
                await ViewModel.Protect.UpdateFileAsync(file);
        }

        private async void unlockTab_Drop(object sender, DragEventArgs e)
        {
            if (TryGetSingleDroppedPdf(e, "unlock", out string file))
                await ViewModel.Unlock.UpdateFileAsync(file);
        }

        private async void watermarkTab_Drop(object sender, DragEventArgs e)
        {
            if (TryGetSingleDroppedPdf(e, "watermark", out string file))
                await ViewModel.Watermark.UpdateFileAsync(file);
        }

        private async void pdfToImageTab_Drop(object sender, DragEventArgs e)
        {
            if (!ViewModel.PdfToImage.IsBusy && TryGetSingleDroppedPdf(e, "convert", out string file))
                await ViewModel.PdfToImage.UpdateFileAsync(file);
        }

        // Image to PDF takes images and folders of images
        private void imagesToPdfTab_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0 && !ViewModel.ImagesToPdf.IsBusy
                && ViewModel.ImagesToPdf.AddPaths(paths) == 0)
                MessageBox.Show("No new images were found in what you dropped. Supported formats are JPEG, PNG, BMP, GIF and TIFF.", "No Images Added", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Batch takes any mix of PDFs and folders
        private void batchTab_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0 && !ViewModel.Batch.IsBusy
                && ViewModel.Batch.AddPaths(paths) == 0)
                MessageBox.Show("No new PDF files were found in what you dropped.", "No Files Added", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static bool TryGetSingleDroppedPdf(DragEventArgs e, string action, out string file)
        {
            file = null;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
                return false;

            if (files.Length > 1)
            {
                MessageBox.Show($"Please drop a single PDF file to {action}.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!string.Equals(Path.GetExtension(files[0]), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Only PDF files are supported.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            file = files[0];
            return true;
        }
    }
}
