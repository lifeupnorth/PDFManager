using System; // awaiting WinRT async operations
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
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
        public async Task<IRenderedDocument> OpenAsync(string path)
        {
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
