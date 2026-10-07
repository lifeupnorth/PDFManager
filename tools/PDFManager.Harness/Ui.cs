using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PDFManager.Harness
{
    // Finding and pressing the window's own controls, from the UI thread
    internal static class Ui
    {
        // The sidebar is the source of truth for navigation, so check its item
        public static void Navigate(Window window, string navItem) =>
            ((RadioButton)window.FindName(navItem)).IsChecked = true;

        // Lets queued work run, including the CanExecute re-check that a property change
        // schedules at Background priority; a button clicked before that may still be disabled
        public static async Task Idle()
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(300);
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }

        // Presses the one visible button bound to the command, through UI Automation's Invoke,
        // which goes through the real Command binding and does not wait for a dialog it opens.
        // Views are rebuilt on every page switch, so the button is looked up each time
        public static Task ClickAsync(Window window, ICommand command) =>
            ClickAsync(window, b => b.Command == command, "the command");

        // Presses the one visible button with this accessible name, e.g. a page thumbnail ("Page 4")
        public static Task ClickNamedAsync(Window window, string name) =>
            ClickAsync(window, b => AutomationProperties.GetName(b) == name, $"\"{name}\"");

        private static async Task ClickAsync(Window window, Func<Button, bool> match, string what)
        {
            await Idle();
            var buttons = Descendants(window).OfType<Button>().Where(b => match(b) && b.IsVisible).ToList();
            if (buttons.Count != 1)
                throw new InvalidOperationException($"expected one visible button for {what}, found {buttons.Count}");
            var peer = new ButtonAutomationPeer(buttons[0]);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        }

        // Saves what the window shows (its WPF content, without the title bar) as a PNG
        public static void SaveScreenshot(Window window, string path)
        {
            var content = (FrameworkElement)window.Content;
            var dpi = VisualTreeHelper.GetDpi(content);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path);
            encoder.Save(file);
        }

        // The nearest ScrollViewer above an element
        public static ScrollViewer ScrollViewerOf(DependencyObject element)
        {
            for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer scroll)
                    return scroll;
            throw new InvalidOperationException("no ScrollViewer above the element");
        }

        public static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs = 15000)
        {
            var waited = Stopwatch.StartNew();
            while (!condition() && waited.ElapsedMilliseconds < timeoutMs)
                await Task.Delay(50);
            return condition();
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }

    // Sample files, written and read with iText as the app does
    internal static class Pdfs
    {
        public static string Create(string path, int pages)
        {
            using (var document = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfWriter(path)))
                for (int i = 0; i < pages; i++)
                    document.AddNewPage();
            return path;
        }

        // Pages of the given widths, so their order can be read back from the file
        public static string CreateWithWidths(string path, params float[] widths)
        {
            using (var document = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfWriter(path)))
                foreach (float width in widths)
                    document.AddNewPage(new iText.Kernel.Geom.PageSize(width, 400));
            return path;
        }

        // Empty when the file is missing or not a PDF
        public static List<float> PageWidths(string path)
        {
            try
            {
                using var document = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(path));
                return Enumerable.Range(1, document.GetNumberOfPages()).Select(n => document.GetPage(n).GetPageSize().GetWidth()).ToList();
            }
            catch
            {
                return new List<float>();
            }
        }

        // -1 when the file is missing or not a PDF
        public static int PageCount(string path)
        {
            try
            {
                using var document = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(path));
                return document.GetNumberOfPages();
            }
            catch
            {
                return -1;
            }
        }
    }
}
