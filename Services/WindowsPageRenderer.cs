using System; // awaiting WinRT async operations
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace PDFManager.Services
{
    /// <summary>
    /// Renders pages with Windows.Data.Pdf, the PDF engine built into Windows 10 and 11.
    /// </summary>
    public class WindowsPageRenderer : IPageRenderer
    {
        // Once pages have been rendered and every rendered document is released, the process can
        // crash on exit inside the graphics driver (seen with Qualcomm Adreno on ARM64: an access
        // violation in qcdx11arm64xum.dll, or exception 0x87a, after the window has closed).
        // Keeping one rendered document alive until the process ends avoids it, so the first
        // document opened also renders a blank page that is never released.
        private static readonly Lazy<Task> KeepAlive = new Lazy<Task>(() => Task.Run(KeepEngineAliveAsync));
        private static PdfDocument? _keptDocument;
        private static InMemoryRandomAccessStream? _keptSource;

        // True once the blank page has rendered; false if it could not be, which only loses the workaround
        internal static async Task<bool> IsEngineKeptAliveAsync()
        {
            await KeepAlive.Value;
            return _keptDocument != null && _keptSource != null;
        }

        private static async Task KeepEngineAliveAsync()
        {
            try
            {
                var source = new InMemoryRandomAccessStream();
                await source.WriteAsync(BlankPagePdf().AsBuffer());
                source.Seek(0);
                PdfDocument document = await PdfDocument.LoadFromStreamAsync(source);
                using (PdfPage page = document.GetPage(0))
                using (var png = new InMemoryRandomAccessStream())
                    await page.RenderToStreamAsync(png, new PdfPageRenderOptions { DestinationWidth = 1 });
                _keptDocument = document;
                _keptSource = source;
            }
            catch
            {
                // Rendering still works without it; only the crash on exit can come back
            }
        }

        // A one-inch blank page, built here so the workaround does not depend on iText or a file
        private static byte[] BlankPagePdf()
        {
            string[] objects =
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 72 72] >>"
            };
            var pdf = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            for (int i = 0; i < objects.Length; i++)
            {
                offsets.Add(pdf.Length);
                pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }
            int xref = pdf.Length;
            pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (int offset in offsets)
                pdf.Append($"{offset:D10} 00000 n \n");
            pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return Encoding.ASCII.GetBytes(pdf.ToString());
        }

        public async Task<IRenderedDocument> OpenAsync(string path)
        {
            await KeepAlive.Value;

            // Work from a copy in memory so the file is not held open while pages render;
            // the user may overwrite it with one of the tools in the meantime
            byte[] bytes = await File.ReadAllBytesAsync(path);
            var stream = new InMemoryRandomAccessStream();
            try
            {
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);
                return new Document(await PdfDocument.LoadFromStreamAsync(stream), stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        private sealed class Document : IRenderedDocument
        {
            private readonly PdfDocument _document;
            private readonly InMemoryRandomAccessStream _source;

            public Document(PdfDocument document, InMemoryRandomAccessStream source)
            {
                _document = document;
                _source = source;
            }

            public int PageCount => (int)_document.PageCount;

            public async Task<BitmapSource> RenderPageAsync(int pageNumber, int width)
            {
                using PdfPage page = _document.GetPage((uint)(pageNumber - 1));
                return await RenderAsync(page, width);
            }

            public async Task<BitmapSource> RenderPageAtDpiAsync(int pageNumber, int dpi, int maxSide)
            {
                using PdfPage page = _document.GetPage((uint)(pageNumber - 1));
                // page.Size is in 1/96 inch, with the page's rotation applied
                double width = page.Size.Width / 96 * dpi, height = page.Size.Height / 96 * dpi;
                double shrink = Math.Min(1, maxSide / Math.Max(width, height));
                int pixelWidth = Math.Max(1, (int)Math.Round(width * shrink));
                int pixelHeight = Math.Max(1, (int)Math.Round(height * shrink));

                // Windows multiplies the requested size by the display scale, which is only known
                // from a rendered bitmap: the first render of the document measures it, against the
                // whole number of pixels actually requested, and later renders divide it out.
                // Both sides are given, because a height derived from a rounded width drifts
                uint requested = (uint)Math.Round(pixelWidth / _displayScale);
                BitmapSource image = await RenderAsync(page, requested, pixelHeight / _displayScale);
                if (Math.Abs(image.PixelWidth - pixelWidth) > 1)
                {
                    _displayScale = (double)image.PixelWidth / requested;
                    image = await RenderAsync(page, pixelWidth / _displayScale, pixelHeight / _displayScale);
                }
                return image;
            }

            // Measured per document, which renders one page at a time
            private double _displayScale = 1;

            // A height of 0 lets Windows keep the page's proportions
            private static async Task<BitmapSource> RenderAsync(PdfPage page, double width, double height = 0)
            {
                using var png = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(png, new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Round(width),
                    DestinationHeight = (uint)Math.Round(height)
                });

                using Stream pngStream = png.AsStreamForRead();
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad; // decode now, so the stream can be closed
                image.StreamSource = pngStream;
                image.EndInit();
                image.Freeze();
                return image;
            }

            public void Dispose() => _source.Dispose();
        }
    }
}
