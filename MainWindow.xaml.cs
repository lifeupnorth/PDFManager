using iText.Forms;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Navigation;
using iText.Kernel.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace PDFManager
{
    /// <summary>
    /// References:
    /// https://kb.itextpdf.com/home/it7kb/examples/splitting-a-pdf-file
    /// https://kb.itextpdf.com/home/it7kb/examples/merging-documents-and-create-a-table-of-contents
    /// </summary>
    public class FileNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is string path ? Path.GetFileName(path) : value;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Shows an element when a string is non-empty (or, with VisibleWhenEmpty, when it is empty).
    /// Used to switch a tool's workspace between its "pick a file" and "file selected" states.
    /// </summary>
    public class StringVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public bool VisibleWhenEmpty { get; set; }

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            bool isEmpty = string.IsNullOrWhiteSpace(value?.ToString());
            return isEmpty == VisibleWhenEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class InverseBoolToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            lblMergeOutput.Content = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Merged File.pdf");
            ((INotifyCollectionChanged)lstMergeFiles.Items).CollectionChanged += (s, e) => UpdateMergeButtonStates();
            lstMergeFiles.SelectionChanged += (s, e) => UpdateMergeButtonStates();
        }

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

        // Navigation: the left sidebar items and the home-screen tool cards carry the
        // index of the view they open in their Tag. The sidebar item is the source of
        // truth, so opening a tool from a card just checks the matching sidebar item.
        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (tabControl == null) return; // fired while InitializeComponent is still running
            if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out int index))
                tabControl.SelectedIndex = index;
        }

        private void OpenTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out int index))
                NavigateTo(index);
        }

        private void NavigateTo(int index)
        {
            var navItems = new[] { navHome, navSplit, navMerge, navRotate };
            if (index >= 0 && index < navItems.Length)
                navItems[index].IsChecked = true;
        }

        private void UpdateMergeButtonStates()
        {
            bool moreThanOne = lstMergeFiles.Items.Count > 1;
            bool hasSelection = lstMergeFiles.SelectedIndex >= 0;
            btnRunMerge.IsEnabled = moreThanOne;
            btnRemoveFileMerge.IsEnabled = hasSelection;
            btnMoveUp.IsEnabled = moreThanOne && hasSelection;
            btnMoveDown.IsEnabled = moreThanOne && hasSelection;
        }

        private async void btnRunSplit_Click(object sender, RoutedEventArgs e)
        {
            string sourcePath = lblSplitFileSource.Content?.ToString();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                MessageBox.Show("Please select a valid PDF file to split.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtSplitPageNum.Text, out int pageNum) || pageNum < 1)
            {
                MessageBox.Show("Please enter a valid page number greater than 0.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string destPattern = GetSplitDestPattern(sourcePath);
            var existingOutputs = new[] { 1, 2 }
                .Select(i => string.Format(destPattern, i))
                .Where(File.Exists)
                .ToList();
            if (existingOutputs.Any())
            {
                var result = MessageBox.Show(
                    $"The following files already exist and will be overwritten:\n{string.Join("\n", existingOutputs)}\n\nContinue?",
                    "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                btnRunSplit.IsEnabled = false;
                btnRunSplit.Content = "Splitting...";

                await Task.Run(() => SplitPdfFile(sourcePath, pageNum));

                var openFolder = MessageBox.Show("PDF split successfully!\n\nOpen the output folder?", "Success", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (openFolder == MessageBoxResult.Yes)
                    ShowInExplorer(string.Format(destPattern, 1));
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                MessageBox.Show("This PDF is password-protected and cannot be split.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                MessageBox.Show("This file is not a valid PDF or is corrupted.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error splitting PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnRunSplit.IsEnabled = true;
                btnRunSplit.Content = "Split PDF";
            }
        }

        private static string GetSplitDestPattern(string sourcePath)
        {
            string folderPath = Path.GetDirectoryName(sourcePath);
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            return Path.Combine(folderPath, baseName + "_part{0}.pdf");
        }

        private void SplitPdfFile(string sourcePath, int pageNum)
        {
            string splitDest = GetSplitDestPattern(sourcePath);

            using (var pdfDoc = new PdfDocument(new PdfReader(sourcePath)))
            {
                int totalPages = pdfDoc.GetNumberOfPages();
                if (totalPages < 2)
                    throw new InvalidOperationException("This document has only 1 page and cannot be split.");
                if (pageNum >= totalPages)
                    throw new InvalidOperationException($"Page number {pageNum} is out of range. The document has {totalPages} pages, so the split point must be between 1 and {totalPages - 1}.");

                // SplitByPageNumbers treats each number as the first page of the next document,
                // so "split after page N" means the next document starts at N + 1
                IList<PdfDocument> splitDocuments = new CustomPdfSplitter(pdfDoc, splitDest).SplitByPageNumbers(new int[] { pageNum + 1 });
                foreach (PdfDocument doc in splitDocuments)
                    doc.Close();
            }
        }

        private class CustomPdfSplitter : PdfSplitter
        {
            private readonly string dest;
            private int fileNumber = 1;

            public CustomPdfSplitter(PdfDocument pdfDocument, string dest) : base(pdfDocument)
            {
                this.dest = dest;
            }

            protected override PdfWriter GetNextPdfWriter(PageRange documentPageRange)
            {
                return new PdfWriter(string.Format(dest, fileNumber++));
            }
        }

        private async void btnRunMerge_Click(object sender, RoutedEventArgs e)
        {
            string outputPath = lblMergeOutput.Content?.ToString();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                MessageBox.Show("Please specify an output file path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(Path.GetDirectoryName(outputPath)))
            {
                MessageBox.Show("The output directory does not exist.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sourceFiles = lstMergeFiles.Items.Cast<string>().ToList();
            var missingFiles = sourceFiles.Where(f => !File.Exists(f)).ToList();
            if (missingFiles.Any())
            {
                MessageBox.Show($"The following files no longer exist:\n{string.Join("\n", missingFiles)}", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (sourceFiles.Any(f => string.Equals(f, outputPath, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("The output file cannot be one of the files being merged. Please choose a different output path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (File.Exists(outputPath))
            {
                var result = MessageBox.Show(
                    $"\"{outputPath}\" already exists and will be overwritten.\n\nContinue?",
                    "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                btnRunMerge.IsEnabled = false;
                btnRunMerge.Content = "Merging...";

                await Task.Run(() => MergePdfFiles(outputPath, sourceFiles));

                var openFolder = MessageBox.Show("PDFs merged successfully!\n\nOpen the output folder?", "Success", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (openFolder == MessageBoxResult.Yes)
                    ShowInExplorer(outputPath);
            }
            catch (Exception ex)
            {
                // The writer truncates the output file before merging starts,
                // so a failed merge leaves behind a broken partial PDF
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                MessageBox.Show($"Error merging PDFs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnRunMerge.Content = "Merge PDF";
                UpdateMergeButtonStates();
            }
        }

        private void MergePdfFiles(string outputPath, List<string> sourceFiles)
        {
            using (var resultDoc = new PdfDocument(new PdfWriter(outputPath)))
            {
                var formCopier = new PdfPageFormCopier();
                int pageIndex = 1;

                foreach (var filePath in sourceFiles)
                {
                    try
                    {
                        using (var srcDoc = new PdfDocument(new PdfReader(filePath)))
                        {
                            int numberOfPages = srcDoc.GetNumberOfPages();
                            srcDoc.CopyPagesTo(1, numberOfPages, resultDoc, formCopier);

                            var rootOutline = resultDoc.GetOutlines(false);
                            var outline = rootOutline.AddOutline(Path.GetFileNameWithoutExtension(filePath));
                            outline.AddDestination(PdfExplicitDestination.CreateFit(resultDoc.GetPage(pageIndex)));

                            pageIndex += numberOfPages;
                        }
                    }
                    catch (iText.Kernel.Exceptions.BadPasswordException)
                    {
                        throw new InvalidOperationException($"'{Path.GetFileName(filePath)}' is password-protected and cannot be merged.");
                    }
                    catch (iText.IO.Exceptions.IOException)
                    {
                        throw new InvalidOperationException($"'{Path.GetFileName(filePath)}' is not a valid PDF or is corrupted.");
                    }
                }
            }
        }

        private void btnAddFileMerge_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
                AddMergeFiles(openFileDialog.FileNames);
        }

        private void AddMergeFiles(IEnumerable<string> files)
        {
            var skipped = new List<string>();
            foreach (var file in files)
            {
                if (lstMergeFiles.Items.Cast<string>().Any(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase)))
                    continue;

                try
                {
                    using (new PdfDocument(new PdfReader(file))) { }
                    lstMergeFiles.Items.Add(file);
                }
                catch (iText.Kernel.Exceptions.BadPasswordException)
                {
                    skipped.Add($"{Path.GetFileName(file)} (password-protected)");
                }
                catch
                {
                    skipped.Add($"{Path.GetFileName(file)} (not a valid PDF)");
                }
            }

            if (skipped.Any())
                MessageBox.Show($"The following files were not added:\n{string.Join("\n", skipped)}", "Some Files Skipped", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void btnRemoveFileMerge_Click(object sender, RoutedEventArgs e)
        {
            while (lstMergeFiles.SelectedItems.Count > 0)
                lstMergeFiles.Items.Remove(lstMergeFiles.SelectedItems[0]);
        }

        private void btnBrowseMergeOutput_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = "Merged File.pdf",
                DefaultExt = ".pdf"
            };

            if (saveFileDialog.ShowDialog() == true)
                lblMergeOutput.Content = saveFileDialog.FileName;
        }

        private void btnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            int index = lstMergeFiles.SelectedIndex;
            if (index <= 0) return;
            var item = lstMergeFiles.Items[index];
            lstMergeFiles.Items.RemoveAt(index);
            lstMergeFiles.Items.Insert(index - 1, item);
            lstMergeFiles.SelectedIndex = index - 1;
        }

        private void btnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            int index = lstMergeFiles.SelectedIndex;
            if (index < 0 || index >= lstMergeFiles.Items.Count - 1) return;
            var item = lstMergeFiles.Items[index];
            lstMergeFiles.Items.RemoveAt(index);
            lstMergeFiles.Items.Insert(index + 1, item);
            lstMergeFiles.SelectedIndex = index + 1;
        }

        private void tab_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void splitTab_Drop(object sender, DragEventArgs e)
        {
            if (TryGetSingleDroppedPdf(e, "split", out string file))
                SetSplitSourceFile(file);
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

        private void mergeTab_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                AddMergeFiles(files);
        }

        private async void btnRunRotate_Click(object sender, RoutedEventArgs e)
        {
            string sourcePath = lblRotateFileSource.Content?.ToString();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                MessageBox.Show("Please select a valid PDF file to rotate.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string outputPath = lblRotateOutput.Content?.ToString();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                MessageBox.Show("Please specify an output file path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(Path.GetDirectoryName(outputPath)))
            {
                MessageBox.Show("The output directory does not exist.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("The output file cannot be the same as the source file. Please choose a different output path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string pageSpec = rbRotateAllPages.IsChecked == true ? null : txtRotatePages.Text;
            if (pageSpec != null && string.IsNullOrWhiteSpace(pageSpec))
            {
                MessageBox.Show("Please enter the pages to rotate (for example: 1, 3, 5-7), or choose \"All pages\".", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int degrees = rbRotate180.IsChecked == true ? 180
                        : rbRotate270.IsChecked == true ? 270
                        : 90;

            if (File.Exists(outputPath))
            {
                var result = MessageBox.Show(
                    $"\"{outputPath}\" already exists and will be overwritten.\n\nContinue?",
                    "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                btnRunRotate.IsEnabled = false;
                btnRunRotate.Content = "Rotating...";

                await Task.Run(() => RotatePdfFile(sourcePath, outputPath, pageSpec, degrees));

                var openFolder = MessageBox.Show("PDF rotated successfully!\n\nOpen the output folder?", "Success", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (openFolder == MessageBoxResult.Yes)
                    ShowInExplorer(outputPath);
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                MessageBox.Show("This PDF is password-protected and cannot be rotated.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                MessageBox.Show("This file is not a valid PDF or is corrupted.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                // The writer truncates the output file before rotation starts,
                // so a failed run leaves behind a broken partial PDF
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                MessageBox.Show($"Error rotating PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnRunRotate.IsEnabled = true;
                btnRunRotate.Content = "Rotate PDF";
            }
        }

        private static void RotatePdfFile(string sourcePath, string outputPath, string pageSpec, int degrees)
        {
            // Validate against the source before creating the writer, because
            // PdfWriter truncates the output file as soon as it is constructed
            int totalPages;
            using (var probe = new PdfDocument(new PdfReader(sourcePath)))
                totalPages = probe.GetNumberOfPages();

            ISet<int> pagesToRotate = pageSpec == null
                ? null
                : ParsePageRanges(pageSpec, totalPages);

            using (var pdfDoc = new PdfDocument(new PdfReader(sourcePath), new PdfWriter(outputPath)))
            {
                for (int i = 1; i <= totalPages; i++)
                {
                    if (pagesToRotate != null && !pagesToRotate.Contains(i))
                        continue;

                    PdfPage page = pdfDoc.GetPage(i);
                    page.SetRotation((page.GetRotation() + degrees) % 360);
                }
            }
        }

        /// <summary>
        /// Parses a page specification such as "1, 3, 5-7" into a set of 1-based page numbers.
        /// </summary>
        private static ISet<int> ParsePageRanges(string spec, int totalPages)
        {
            var pages = new SortedSet<int>();

            foreach (var rawToken in spec.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = rawToken.Trim();
                int start, end;

                int dash = token.IndexOf('-');
                if (dash < 0)
                {
                    if (!int.TryParse(token, out start))
                        throw new InvalidOperationException($"\"{token}\" is not a valid page number.");
                    end = start;
                }
                else
                {
                    if (!int.TryParse(token.Substring(0, dash), out start) || !int.TryParse(token.Substring(dash + 1), out end))
                        throw new InvalidOperationException($"\"{token}\" is not a valid page range. Use the form 5-7.");
                    if (end < start)
                        throw new InvalidOperationException($"\"{token}\" is not a valid page range: the end page is before the start page.");
                }

                if (start < 1 || end > totalPages)
                    throw new InvalidOperationException($"Page range \"{token}\" is out of range. The document has {totalPages} pages.");

                for (int p = start; p <= end; p++)
                    pages.Add(p);
            }

            if (pages.Count == 0)
                throw new InvalidOperationException("Please enter at least one page to rotate.");

            return pages;
        }

        private void openFileRotate_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf"
            };

            if (openFileDialog.ShowDialog() != true) return;

            SetRotateSourceFile(openFileDialog.FileName);
        }

        private void SetRotateSourceFile(string path)
        {
            lblRotateFileSource.Content = path;
            lblRotateOutput.Content = Path.Combine(
                Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + "_rotated.pdf");

            try
            {
                using (var pdfDoc = new PdfDocument(new PdfReader(path)))
                {
                    int pages = pdfDoc.GetNumberOfPages();
                    lblRotatePageCount.Content = $"Document has {pages} page{(pages == 1 ? "" : "s")}. Example: 1, 3, 5-7";
                }
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                lblRotatePageCount.Content = "This PDF is password-protected and cannot be rotated.";
            }
            catch
            {
                lblRotatePageCount.Content = "Unable to read the page count.";
            }
        }

        private void btnBrowseRotateOutput_Click(object sender, RoutedEventArgs e)
        {
            string current = lblRotateOutput.Content?.ToString();
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = string.IsNullOrWhiteSpace(current) ? "Rotated File.pdf" : Path.GetFileName(current),
                DefaultExt = ".pdf"
            };
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(Path.GetDirectoryName(current)))
                saveFileDialog.InitialDirectory = Path.GetDirectoryName(current);

            if (saveFileDialog.ShowDialog() == true)
                lblRotateOutput.Content = saveFileDialog.FileName;
        }

        private void rotateTab_Drop(object sender, DragEventArgs e)
        {
            if (TryGetSingleDroppedPdf(e, "rotate", out string file))
                SetRotateSourceFile(file);
        }

        private static void ShowInExplorer(string filePath)
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }

        private void openFileSplit_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf"
            };

            if (openFileDialog.ShowDialog() != true) return;

            SetSplitSourceFile(openFileDialog.FileName);
        }

        private void SetSplitSourceFile(string path)
        {
            lblSplitFileSource.Content = path;
            lblSplitOutputFolder.Content = Path.GetDirectoryName(path);

            try
            {
                using (var pdfDoc = new PdfDocument(new PdfReader(path)))
                {
                    int pages = pdfDoc.GetNumberOfPages();
                    lblSplitPageCount.Content = pages > 1
                        ? $"Enter a page from 1 to {pages - 1}. The document has {pages} pages."
                        : "This document has only 1 page and cannot be split.";
                }
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                lblSplitPageCount.Content = "This PDF is password-protected and cannot be split.";
            }
            catch
            {
                lblSplitPageCount.Content = "Unable to read the page count.";
            }
        }
    }
}
