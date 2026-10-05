using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using PDFManager.Services;
using Path = System.IO.Path;

namespace PDFManager.Tests
{
    public class WatermarkTests : IDisposable
    {
        private readonly PdfService _service = new PdfService();
        private readonly TestPdfs _pdfs = new TestPdfs();

        public void Dispose() => _pdfs.Dispose();

        private static WatermarkOptions Options(string text = "CONFIDENTIAL", bool diagonal = false, int opacity = 30, string? pages = null) =>
            new WatermarkOptions(text, diagonal, opacity, pages!);

        // A text run as drawn on the page: its baseline in page space, size, opacity and font
        private record DrawnText(string Text, double Angle, double MidX, double MidY, double Length, float FontSize, float Opacity, string FontName);

        private static List<DrawnText> TextOn(string path, int pageNumber)
        {
            using var doc = new PdfDocument(new PdfReader(path));
            var listener = new TextCollector();
            new PdfCanvasProcessor(listener).ProcessPageContent(doc.GetPage(pageNumber));
            return listener.Found;
        }

        private sealed class TextCollector : IEventListener
        {
            public List<DrawnText> Found { get; } = new();

            public void EventOccurred(IEventData data, EventType type)
            {
                if (data is not TextRenderInfo info)
                    return;
                var baseline = info.GetBaseline();
                Vector start = baseline.GetStartPoint(), end = baseline.GetEndPoint();
                double dx = end.Get(0) - start.Get(0), dy = end.Get(1) - start.Get(1);
                Found.Add(new DrawnText(info.GetText(), Math.Atan2(dy, dx) * 180 / Math.PI,
                    (start.Get(0) + end.Get(0)) / 2, (start.Get(1) + end.Get(1)) / 2, Math.Sqrt(dx * dx + dy * dy),
                    info.GetFontSize(), info.GetGraphicsState().GetFillOpacity(),
                    info.GetFont().GetFontProgram().GetFontNames().GetFontName()));
            }

            public ICollection<EventType> GetSupportedEvents() => new[] { EventType.RENDER_TEXT };
        }

        private static double AngleDifference(double a, double b) => Math.Abs(((a - b) % 360 + 540) % 360 - 180);

        [Fact]
        public void AddWatermark_draws_the_text_centred_and_horizontal_on_every_page()
        {
            string source = _pdfs.Create("doc.pdf", 400, 400, 400);
            string output = _pdfs.PathFor("watermarked.pdf");

            _service.AddWatermark(source, output, Options());

            for (int page = 1; page <= 3; page++)
            {
                var mark = Assert.Single(TextOn(output, page));
                Assert.Equal("CONFIDENTIAL", mark.Text);
                Assert.True(AngleDifference(mark.Angle, 0) < 0.01, $"angle {mark.Angle}");
                Assert.Equal(200, mark.MidX, 0.5);
                // The baseline sits about a third of the font size below the centre
                Assert.InRange(100 - mark.MidY, 0.2 * mark.FontSize, 0.5 * mark.FontSize);
            }
            Assert.Empty(TextOn(source, 1));
        }

        [Fact]
        public void AddWatermark_diagonal_runs_corner_to_corner()
        {
            string source = _pdfs.Create("doc.pdf", 300);
            string output = _pdfs.PathFor("watermarked.pdf");

            _service.AddWatermark(source, output, Options(diagonal: true));

            var mark = Assert.Single(TextOn(output, 1));
            Assert.Equal(Math.Atan2(200, 300) * 180 / Math.PI, mark.Angle, 0.01);
        }

        [Theory]
        [InlineData(90, false)]
        [InlineData(270, false)]
        [InlineData(180, false)]
        [InlineData(90, true)]
        public void AddWatermark_reads_upright_on_a_page_rotated_for_viewing(int rotation, bool diagonal)
        {
            // The page box is 300 wide and 200 tall; with /Rotate 90 or 270 the reader sees it 200 x 300
            string source = _pdfs.Create("doc.pdf", new[] { 300f }, rotation);
            string output = _pdfs.PathFor("watermarked.pdf");

            _service.AddWatermark(source, output, Options(diagonal: diagonal));

            var mark = Assert.Single(TextOn(output, 1));
            bool sideways = rotation % 180 != 0;
            double visibleAngle = diagonal ? Math.Atan2(sideways ? 300 : 200, sideways ? 200 : 300) * 180 / Math.PI : 0;
            // A direction at angle a in page space appears at a - rotation on screen
            Assert.True(AngleDifference(mark.Angle - rotation, visibleAngle) < 0.01, $"page-space angle {mark.Angle}");
            Assert.Equal(150, mark.MidX, 0.35 * mark.FontSize + 1);
            Assert.Equal(100, mark.MidY, 0.35 * mark.FontSize + 1);
        }

        [Fact]
        public void AddWatermark_fits_the_text_to_the_page()
        {
            string source = _pdfs.Create("doc.pdf", 300, 1000);
            string output = _pdfs.PathFor("watermarked.pdf");

            _service.AddWatermark(source, output, Options("A much longer watermark sentence than usual"));
            _service.AddWatermark(source, _pdfs.PathFor("short.pdf"), Options("X"));

            // Long text: limited by the width
            var longMark = Assert.Single(TextOn(output, 1));
            Assert.Equal(300 * 0.75, longMark.Length, 1.0);
            // Short text: limited to a quarter of the page's smaller side
            var shortMark = Assert.Single(TextOn(_pdfs.PathFor("short.pdf"), 2));
            Assert.Equal(200 / 4f, shortMark.FontSize, 0.01);
        }

        [Fact]
        public void AddWatermark_marks_only_the_listed_pages()
        {
            string source = _pdfs.CreatePages("doc.pdf", 4);
            string output = _pdfs.PathFor("watermarked.pdf");

            _service.AddWatermark(source, output, Options(pages: "1, 3-4"));

            Assert.Equal(new[] { 1, 0, 1, 1 }, Enumerable.Range(1, 4).Select(p => TextOn(output, p).Count));
        }

        [Theory]
        [InlineData(15)]
        [InlineData(50)]
        public void AddWatermark_applies_the_opacity(int percent)
        {
            string output = _pdfs.PathFor("watermarked.pdf");

            _service.AddWatermark(_pdfs.CreatePages("doc.pdf", 1), output, Options(opacity: percent));

            Assert.Equal(percent / 100f, Assert.Single(TextOn(output, 1)).Opacity, 0.001);
        }

        [Fact]
        public void AddWatermark_keeps_the_page_content_and_is_not_distorted_by_its_graphics_state()
        {
            // Page content that leaves a 10% scale and a saved state behind, as some generators do
            string source = _pdfs.PathFor("doc.pdf");
            using (var doc = new PdfDocument(new PdfWriter(source)))
            {
                var page = doc.AddNewPage(new PageSize(400, 200));
                new PdfCanvas(page).BeginText().SetFontAndSize(PdfFontFactory.CreateFont(StandardFonts.HELVETICA), 12)
                    .MoveText(20, 20).ShowText("Original text").EndText()
                    .SaveState().ConcatMatrix(0.1, 0, 0, 0.1, 0, 0);
            }
            string output = _pdfs.PathFor("watermarked.pdf");

            _service.AddWatermark(source, output, Options());

            var drawn = TextOn(output, 1);
            Assert.Contains(drawn, t => t.Text == "Original text");
            var mark = Assert.Single(drawn, t => t.Text == "CONFIDENTIAL");
            Assert.Equal(200, mark.MidX, 0.5);
            Assert.Equal(400 * 0.75, mark.Length, 1.0);
        }

        [Fact]
        public void AddWatermark_uses_Helvetica_for_Western_text()
        {
            string output = _pdfs.PathFor("watermarked.pdf");

            _service.AddWatermark(_pdfs.CreatePages("doc.pdf", 1), output, Options("Brouillon – Entwurf ©"));

            var mark = Assert.Single(TextOn(output, 1));
            Assert.Equal("Brouillon – Entwurf ©", mark.Text);
            Assert.Equal("Helvetica-Bold", mark.FontName);
        }

        [Theory]
        [InlineData("Черновик")]
        [InlineData("Πρόχειρο")]
        [InlineData("机密文件")]
        public void AddWatermark_embeds_an_installed_font_for_other_scripts(string text)
        {
            string output = _pdfs.PathFor("watermarked.pdf");

            _service.AddWatermark(_pdfs.CreatePages("doc.pdf", 1), output, Options(text));

            var mark = Assert.Single(TextOn(output, 1));
            Assert.Equal(text, mark.Text);
            Assert.NotEqual("Helvetica-Bold", mark.FontName);
        }

        [Fact]
        public void AddWatermark_with_characters_no_font_has_writes_nothing()
        {
            string source = _pdfs.CreatePages("doc.pdf", 1);
            string output = _pdfs.CreateNotAPdf("watermarked.pdf");
            byte[] before = File.ReadAllBytes(output);

            var ex = Assert.Throws<InvalidOperationException>(() => _service.AddWatermark(source, output, Options("Draft \U0010FFFD")));

            Assert.Equal("The watermark contains characters that none of the installed fonts can show.", ex.Message);
            Assert.Equal(before, File.ReadAllBytes(output));
            Assert.Empty(Directory.GetFiles(_pdfs.Folder, "*.tmp"));
        }

        [Fact]
        public void AddWatermark_with_an_invalid_page_spec_leaves_an_existing_output_untouched()
        {
            string source = _pdfs.CreatePages("doc.pdf", 3);
            string output = _pdfs.CreateNotAPdf("watermarked.pdf");
            byte[] before = File.ReadAllBytes(output);

            Assert.Throws<InvalidOperationException>(() => _service.AddWatermark(source, output, Options(pages: "5")));

            Assert.Equal(before, File.ReadAllBytes(output));
            Assert.Empty(Directory.GetFiles(_pdfs.Folder, "*.tmp"));
        }

        [Fact]
        public void AddWatermark_failing_while_writing_pages_leaves_the_output_untouched()
        {
            string source = _pdfs.CreateWithBrokenPageTree("broken.pdf");
            string output = _pdfs.CreateNotAPdf("watermarked.pdf");
            byte[] before = File.ReadAllBytes(output);

            Assert.ThrowsAny<iText.Kernel.Exceptions.PdfException>(() => _service.AddWatermark(source, output, Options()));

            Assert.Equal(before, File.ReadAllBytes(output));
            Assert.Empty(Directory.GetFiles(_pdfs.Folder, "*.tmp"));
        }

        [Fact]
        public void AddWatermark_throws_BadPasswordException_for_a_protected_pdf()
        {
            Assert.Throws<iText.Kernel.Exceptions.BadPasswordException>(() =>
                _service.AddWatermark(_pdfs.CreateEncrypted("locked.pdf"), _pdfs.PathFor("watermarked.pdf"), Options()));
        }
    }
}
