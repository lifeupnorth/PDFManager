using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PDFManager.Services;
using Path = System.IO.Path;

namespace PDFManager.Tests
{
    // Runs the real Windows.Data.Pdf engine and WPF encoders against generated PDFs
    public class ImageExportTests : IDisposable
    {
        private readonly PdfImageExporter _exporter = new PdfImageExporter(new WindowsPageRenderer());
        private readonly TestPdfs _pdfs = new TestPdfs();

        public void Dispose() => _pdfs.Dispose();

        private string Out => _pdfs.PathFor("images");

        private static BitmapFrame Load(string path)
        {
            using var stream = File.OpenRead(path);
            return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }

        [Theory]
        [InlineData(72, 595, 842)]
        [InlineData(150, 1240, 1754)]
        [InlineData(300, 2479, 3508)]
        public async Task Exports_each_page_at_the_exact_resolution_whatever_the_display_scale(int dpi, int width, int height)
        {
            // An A4 page: 595 x 842 points, so 8.26 x 11.69 inches
            string source = _pdfs.PathFor("a4.pdf");
            using (var doc = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfWriter(source)))
                doc.AddNewPage(iText.Kernel.Geom.PageSize.A4);

            var files = await _exporter.ExportAsync(source, Out, null, ImageFormat.Png, dpi);

            var image = Load(Assert.Single(files));
            Assert.InRange(image.PixelWidth, width - 1, width + 1);
            Assert.InRange(image.PixelHeight, height - 1, height + 1);
            Assert.Equal(dpi, image.DpiX, 0.5);
        }

        [Fact]
        public async Task Names_files_after_the_source_padded_to_sort_in_page_order()
        {
            string source = _pdfs.CreatePages("Report.pdf", 12);

            var files = await _exporter.ExportAsync(source, Out, new HashSet<int> { 10, 2 }, ImageFormat.Png, 72);

            Assert.Equal(new[] { "Report_page02.png", "Report_page10.png" }, files.Select(Path.GetFileName));
            Assert.Equal(new[] { "Report_page02.png", "Report_page10.png" }, Directory.GetFiles(Out).Select(Path.GetFileName).OrderBy(n => n));
        }

        [Fact]
        public async Task Writes_real_JPEG_and_PNG_files()
        {
            string source = _pdfs.CreatePages("doc.pdf", 1);

            string jpeg = (await _exporter.ExportAsync(source, Out, null, ImageFormat.Jpeg, 72)).Single();
            string png = (await _exporter.ExportAsync(source, Out, null, ImageFormat.Png, 72)).Single();

            Assert.EndsWith(".jpg", jpeg);
            Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, File.ReadAllBytes(jpeg).Take(3));
            Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, File.ReadAllBytes(png).Take(4));
        }

        [Fact]
        public async Task Exports_a_page_rotated_for_viewing_the_way_it_is_seen()
        {
            // Stored 400 x 200 points, shown 200 x 400 because of /Rotate 90
            string source = _pdfs.Create("doc.pdf", new[] { 400f }, rotation: 90);

            var image = Load((await _exporter.ExportAsync(source, Out, null, ImageFormat.Png, 72)).Single());

            Assert.InRange(image.PixelWidth, 199, 201);
            Assert.InRange(image.PixelHeight, 399, 401);
        }

        [Fact]
        public async Task Limits_the_longest_side_of_huge_pages()
        {
            // 100 inches tall: 30000 pixels at 300 DPI without the limit
            string source = _pdfs.PathFor("poster.pdf");
            using (var doc = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfWriter(source)))
                doc.AddNewPage(new iText.Kernel.Geom.PageSize(3600, 7200));

            var image = Load((await _exporter.ExportAsync(source, Out, null, ImageFormat.Jpeg, 300)).Single());

            Assert.InRange(image.PixelHeight, PdfImageExporter.MaxSidePixels - 1, PdfImageExporter.MaxSidePixels);
            Assert.InRange(image.PixelWidth, 4999, 5001);
        }

        [Fact]
        public async Task Overwrites_existing_images_and_leaves_no_temp_files()
        {
            string source = _pdfs.CreatePages("doc.pdf", 1);
            Directory.CreateDirectory(Out);
            File.WriteAllText(Path.Combine(Out, "doc_page1.png"), "old");

            await _exporter.ExportAsync(source, Out, null, ImageFormat.Png, 72);

            Assert.NotEqual("old", File.ReadAllText(Path.Combine(Out, "doc_page1.png")));
            Assert.Empty(Directory.GetFiles(Out, "*.tmp"));
        }

        [Fact]
        public async Task Explains_a_password_protected_pdf()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _exporter.ExportAsync(_pdfs.CreateEncrypted("locked.pdf"), Out, null, ImageFormat.Png, 72));

            Assert.Equal("This PDF is password-protected. Remove its password with Unlock PDF first.", ex.Message);
        }

        [Fact]
        public async Task Stops_when_cancelled_keeping_the_pages_already_saved()
        {
            string source = _pdfs.CreatePages("doc.pdf", 5);
            using var cancel = new CancellationTokenSource();
            var progress = new SyncProgress(saved => { if (saved == 2) cancel.Cancel(); });

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _exporter.ExportAsync(source, Out, null, ImageFormat.Png, 72, progress, cancel.Token));

            Assert.Equal(new[] { "doc_page1.png", "doc_page2.png" }, Directory.GetFiles(Out).Select(Path.GetFileName).OrderBy(n => n));
        }

        // Progress<T> reports asynchronously; this one reports inline so the test is deterministic
        private sealed class SyncProgress : IProgress<int>
        {
            private readonly Action<int> _report;
            public SyncProgress(Action<int> report) => _report = report;
            public void Report(int value) => _report(value);
        }
    }
}
