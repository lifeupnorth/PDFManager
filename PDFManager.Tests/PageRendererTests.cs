using System;
using System.IO;
using System.Threading.Tasks;
using PDFManager.Services;

namespace PDFManager.Tests
{
    // Runs the real Windows.Data.Pdf engine against generated PDFs
    public class PageRendererTests : IDisposable
    {
        private readonly WindowsPageRenderer _renderer = new WindowsPageRenderer();
        private readonly TestPdfs _pdfs = new TestPdfs();

        public void Dispose() => _pdfs.Dispose();

        [Fact]
        public async Task Renders_each_page_at_the_requested_width_and_page_shape()
        {
            // Page 1 is 100 x 200 (portrait), page 2 is 400 x 200 (landscape)
            using var doc = await _renderer.OpenAsync(_pdfs.Create("doc.pdf", 100, 400));

            Assert.Equal(2, doc.PageCount);
            var portrait = await doc.RenderPageAsync(1, 120);
            var landscape = await doc.RenderPageAsync(2, 120);
            // Windows scales the width by the display's DPI, so check the shape rather than exact pixels
            Assert.InRange(portrait.PixelWidth, 120, 120 * 3);
            Assert.Equal(portrait.PixelWidth, landscape.PixelWidth);
            Assert.Equal(2.0, (double)portrait.PixelHeight / portrait.PixelWidth, 0.02);
            Assert.Equal(0.5, (double)landscape.PixelHeight / landscape.PixelWidth, 0.02);
            Assert.True(portrait.IsFrozen);
        }

        [Fact]
        public async Task Applies_the_page_rotation()
        {
            // Stored 400 x 200, shown 200 x 400 because of /Rotate 90
            using var doc = await _renderer.OpenAsync(_pdfs.Create("doc.pdf", new[] { 400f }, rotation: 90));

            var image = await doc.RenderPageAsync(1, 100);

            Assert.Equal(2.0, (double)image.PixelHeight / image.PixelWidth, 0.02);
        }

        [Fact]
        public async Task Does_not_keep_the_file_open()
        {
            string path = _pdfs.CreatePages("doc.pdf", 2);
            using var doc = await _renderer.OpenAsync(path);

            File.Delete(path); // would throw if the file were still open

            Assert.NotNull(await doc.RenderPageAsync(2, 50));
        }

        [Fact]
        public async Task Throws_for_a_password_protected_pdf()
        {
            await Assert.ThrowsAnyAsync<Exception>(() => _renderer.OpenAsync(_pdfs.CreateEncrypted("locked.pdf")));
        }

        [Fact]
        public async Task Throws_for_a_file_that_is_not_a_pdf()
        {
            await Assert.ThrowsAnyAsync<Exception>(() => _renderer.OpenAsync(_pdfs.CreateNotAPdf("notes.pdf")));
        }
    }
}
