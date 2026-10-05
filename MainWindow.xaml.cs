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
            this.DataContext = new MainViewModel(new PdfService());
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
                var navItems = new[] { navHome, navSplit, navMerge, navRotate, navExtract };
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
                vm.CurrentView = index switch
                {
                    1 => vm.Split,
                    2 => vm.Merge,
                    3 => vm.Rotate,
                    4 => vm.Extract,
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
