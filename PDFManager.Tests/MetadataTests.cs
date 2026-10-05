using System.IO;
using PDFManager.Services;

namespace PDFManager.Tests
{
    public class MetadataTests : System.IDisposable
    {
        private readonly PdfService _service = new PdfService();
        private readonly TestPdfs _pdfs = new TestPdfs();

        public void Dispose() => _pdfs.Dispose();

        [Fact]
        public void ReadMetadata_returns_the_document_properties()
        {
            string source = _pdfs.CreateWithMetadata("doc.pdf", "Annual report", "Finance team", "Results for 2026", "report, finance");

            Assert.Equal(new PdfMetadata("Annual report", "Finance team", "Results for 2026", "report, finance"), _service.ReadMetadata(source));
        }

        [Fact]
        public void ReadMetadata_returns_empty_strings_for_missing_properties()
        {
            Assert.Equal(new PdfMetadata("", "", "", ""), _service.ReadMetadata(_pdfs.CreatePages("plain.pdf", 1)));
        }

        [Fact]
        public void ReadMetadata_throws_BadPasswordException_for_a_protected_pdf()
        {
            Assert.Throws<iText.Kernel.Exceptions.BadPasswordException>(() => _service.ReadMetadata(_pdfs.CreateEncrypted("locked.pdf")));
        }

        [Fact]
        public void WriteMetadata_sets_every_property_and_keeps_the_pages()
        {
            string source = _pdfs.Create("doc.pdf", 101, 102, 103);
            string output = _pdfs.PathFor("edited.pdf");
            var metadata = new PdfMetadata("Résumé – 日本語", "Ana Núñez", "Ünïcödé subject", "one; two");

            _service.WriteMetadata(source, output, metadata);

            Assert.Equal(metadata, _service.ReadMetadata(output));
            Assert.Equal(new[] { 101f, 102f, 103f }, TestPdfs.PageWidths(output));
            Assert.Equal(new PdfMetadata("", "", "", ""), _service.ReadMetadata(source));
        }

        [Fact]
        public void WriteMetadata_trims_values_and_removes_blank_ones()
        {
            string source = _pdfs.CreateWithMetadata("doc.pdf", "Old title", "Old author", "Old subject", "old");
            string output = _pdfs.PathFor("edited.pdf");

            _service.WriteMetadata(source, output, new PdfMetadata("  New title  ", "", "   ", "new"));

            Assert.Equal(new PdfMetadata("New title", "", "", "new"), _service.ReadMetadata(output));
        }

        [Fact]
        public void WriteMetadata_updates_the_XMP_copy_of_the_properties()
        {
            string source = _pdfs.CreateWithMetadata("doc.pdf", "Old title", "Old author", "Old subject", "oldword", withXmp: true);
            string output = _pdfs.PathFor("edited.pdf");

            _service.WriteMetadata(source, output, new PdfMetadata("New title", "New author", "New subject", "newword"));

            string xmp = TestPdfs.XmpText(output);
            foreach (string value in new[] { "New title", "New author", "New subject", "newword" })
                Assert.Contains(value, xmp);
            foreach (string value in new[] { "Old title", "Old author", "Old subject", "oldword" })
                Assert.DoesNotContain(value, xmp);
        }

        [Fact]
        public void WriteMetadata_clears_properties_from_both_the_info_and_the_XMP()
        {
            // iText fills a removed info entry back in from XMP, so both copies must go
            string source = _pdfs.CreateWithMetadata("doc.pdf", "Old title", "Old author", "Old subject", "oldword", withXmp: true);
            string output = _pdfs.PathFor("edited.pdf");

            _service.WriteMetadata(source, output, new PdfMetadata("Kept title", "", "", ""));

            Assert.Equal(new PdfMetadata("Kept title", "", "", ""), _service.ReadMetadata(output));
            string xmp = TestPdfs.XmpText(output);
            Assert.Contains("Kept title", xmp);
            foreach (string value in new[] { "Old author", "Old subject", "oldword" })
                Assert.DoesNotContain(value, xmp);
        }

        [Fact]
        public void WriteMetadata_can_save_over_the_source()
        {
            string source = _pdfs.CreateWithMetadata("doc.pdf", "Old title", "Old author", "", "");

            _service.WriteMetadata(source, source, new PdfMetadata("New title", "Old author", "", ""));

            Assert.Equal(new PdfMetadata("New title", "Old author", "", ""), _service.ReadMetadata(source));
            Assert.Equal(2, _service.GetPageCount(source));
            Assert.Single(Directory.GetFiles(_pdfs.Folder));
        }

        [Fact]
        public void WriteMetadata_with_a_protected_source_leaves_no_temp_file_and_the_output_untouched()
        {
            string source = _pdfs.CreateEncrypted("locked.pdf");
            string output = _pdfs.CreateNotAPdf("edited.pdf");
            byte[] before = File.ReadAllBytes(output);

            Assert.Throws<iText.Kernel.Exceptions.BadPasswordException>(() => _service.WriteMetadata(source, output, new PdfMetadata("T", "", "", "")));

            Assert.Equal(before, File.ReadAllBytes(output));
            Assert.Empty(Directory.GetFiles(_pdfs.Folder, "*.tmp"));
        }

        [Fact]
        public void WriteMetadata_failing_while_writing_pages_leaves_the_source_untouched_when_saving_over_it()
        {
            string source = _pdfs.CreateWithBrokenPageTree("broken.pdf");
            byte[] before = File.ReadAllBytes(source);

            Assert.ThrowsAny<iText.Kernel.Exceptions.PdfException>(() => _service.WriteMetadata(source, source, new PdfMetadata("T", "", "", "")));

            Assert.Equal(before, File.ReadAllBytes(source));
            Assert.Empty(Directory.GetFiles(_pdfs.Folder, "*.tmp"));
        }
    }
}
