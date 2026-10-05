#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Path = System.IO.Path;

namespace PDFManager.Services
{
    public enum ImageFormat { Png, Jpeg }

    /// <summary>
    /// Saves PDF pages as PNG or JPEG files at a chosen resolution, one file per page.
    /// </summary>
    public class PdfImageExporter
    {
        // The longest side an image may have; a poster-sized page at 300 DPI would otherwise need gigabytes
        public const int MaxSidePixels = 10000;

        private const int WrongPasswordHResult = unchecked((int)0x8007052B);

        private readonly IPageRenderer _renderer;

        public PdfImageExporter(IPageRenderer renderer) => _renderer = renderer;

        // file_page01.png, file_page02.png... padded so the files sort in page order
        public static IReadOnlyList<string> PlanFiles(string sourcePath, string folder, IEnumerable<int> pages, int pageCount, ImageFormat format)
        {
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            string extension = format == ImageFormat.Png ? ".png" : ".jpg";
            int digits = pageCount.ToString().Length;
            return pages.Select(p => Path.Combine(folder, $"{name}_page{p.ToString().PadLeft(digits, '0')}{extension}")).ToList();
        }

        // Exports the given pages (all when null) into folder, creating it if needed.
        // progress receives the number of pages saved so far. Returns the files written
        public async Task<IReadOnlyList<string>> ExportAsync(string sourcePath, string folder, ISet<int>? pages, ImageFormat format, int dpi,
            IProgress<int>? progress = null, CancellationToken cancel = default)
        {
            IRenderedDocument document;
            try
            {
                document = await _renderer.OpenAsync(sourcePath);
            }
            catch (Exception ex) when (ex.HResult == WrongPasswordHResult)
            {
                throw new InvalidOperationException("This PDF is password-protected. Remove its password with Unlock PDF first.");
            }

            using (document)
            {
                var pageList = pages == null
                    ? Enumerable.Range(1, document.PageCount).ToList()
                    : pages.OrderBy(p => p).ToList();
                if (pageList.Any(p => p < 1 || p > document.PageCount))
                    throw new InvalidOperationException($"The document has {document.PageCount} pages.");

                var files = PlanFiles(sourcePath, folder, pageList, document.PageCount, format);
                Directory.CreateDirectory(folder);
                var written = new List<string>();
                for (int i = 0; i < pageList.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    BitmapSource image = await document.RenderPageAtDpiAsync(pageList[i], dpi, MaxSidePixels);
                    Save(image, files[i], format, dpi);
                    written.Add(files[i]);
                    progress?.Report(i + 1);
                }
                return written;
            }
        }

        // Encodes through a temporary file, so a failure never leaves a half-written image
        private static void Save(BitmapSource image, string path, ImageFormat format, int dpi)
        {
            // Pages render on white, so drop the alpha channel, and record the resolution in the file
            var rgb = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
            int stride = rgb.PixelWidth * 3;
            var pixels = new byte[stride * rgb.PixelHeight];
            rgb.CopyPixels(pixels, stride, 0);
            var output = BitmapSource.Create(rgb.PixelWidth, rgb.PixelHeight, dpi, dpi, PixelFormats.Bgr24, null, pixels, stride);

            BitmapEncoder encoder = format == ImageFormat.Png ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(output));

            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(tempPath))
                    encoder.Save(stream);
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
    }
}
