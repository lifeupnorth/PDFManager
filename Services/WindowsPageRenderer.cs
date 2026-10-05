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
                using var png = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(png, new PdfPageRenderOptions { DestinationWidth = (uint)width });

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
