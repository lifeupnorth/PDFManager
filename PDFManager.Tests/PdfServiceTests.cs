using System;
using System.IO;
using System.Linq;
using iText.Kernel.Pdf;
using PDFManager.Services;

namespace PDFManager.Tests
{
    public class PdfServiceTests : IDisposable
    {
        private readonly PdfService _service = new PdfService();
        private readonly TestPdfs _pdfs = new TestPdfs();

        public void Dispose() => _pdfs.Dispose();

        // --- Page count ---

        [Fact]
        public void GetPageCount_returns_the_number_of_pages()
        {
            Assert.Equal(3, _service.GetPageCount(_pdfs.CreatePages("three.pdf", 3)));
        }

        [Fact]
        public void GetPageCount_throws_BadPasswordException_for_a_protected_pdf()
        {
            Assert.Throws<iText.Kernel.Exceptions.BadPasswordException>(() => _service.GetPageCount(_pdfs.CreateEncrypted("locked.pdf")));
        }

        [Fact]
        public void GetPageCount_throws_iText_IOException_for_a_file_that_is_not_a_pdf()
        {
            Assert.Throws<iText.IO.Exceptions.IOException>(() => _service.GetPageCount(_pdfs.CreateNotAPdf("notes.pdf")));
        }

        // --- Split ---

        [Fact]
        public void GetSplitDestPattern_puts_numbered_parts_next_to_the_source()
        {
            string source = Path.Combine(@"C:\Docs", "Report.pdf");
            Assert.Equal(Path.Combine(@"C:\Docs", "Report_part{0}.pdf"), _service.GetSplitDestPattern(source));
        }

        [Fact]
        public void SplitPdf_puts_pages_up_to_the_split_page_in_part1_and_the_rest_in_part2()
        {
            string source = _pdfs.Create("doc.pdf", 101, 102, 103, 104, 105);

            _service.SplitPdf(source, 2, _pdfs.Folder);

            Assert.Equal(new[] { 101f, 102f }, TestPdfs.PageWidths(_pdfs.PathFor("doc_part1.pdf")));
            Assert.Equal(new[] { 103f, 104f, 105f }, TestPdfs.PageWidths(_pdfs.PathFor("doc_part2.pdf")));
        }

        [Fact]
        public void SplitPdf_after_the_second_to_last_page_leaves_one_page_in_part2()
        {
            string source = _pdfs.Create("doc.pdf", 101, 102, 103);

            _service.SplitPdf(source, 2, _pdfs.Folder);

            Assert.Equal(new[] { 103f }, TestPdfs.PageWidths(_pdfs.PathFor("doc_part2.pdf")));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(7)]
        public void SplitPdf_rejects_a_split_page_at_or_past_the_end_without_writing_anything(int splitPage)
        {
            string source = _pdfs.CreatePages("doc.pdf", 3);

            var ex = Assert.Throws<InvalidOperationException>(() => _service.SplitPdf(source, splitPage, _pdfs.Folder));

            Assert.Equal($"Page number {splitPage} is out of range. The document has 3 pages, so the split point must be between 1 and 2.", ex.Message);
            Assert.Equal(new[] { source }, Directory.GetFiles(_pdfs.Folder));
        }

        [Fact]
        public void SplitPdf_rejects_a_single_page_document()
        {
            string source = _pdfs.CreatePages("one.pdf", 1);

            var ex = Assert.Throws<InvalidOperationException>(() => _service.SplitPdf(source, 1, _pdfs.Folder));

            Assert.Equal("This document has only 1 page and cannot be split.", ex.Message);
        }

        [Fact]
        public void SplitPdf_throws_BadPasswordException_for_a_protected_pdf()
        {
            string source = _pdfs.CreateEncrypted("locked.pdf", 4);

            Assert.Throws<iText.Kernel.Exceptions.BadPasswordException>(() => _service.SplitPdf(source, 2, _pdfs.Folder));
        }

        // --- Merge ---

        [Fact]
        public void MergePdfs_appends_files_in_list_order()
        {
            string first = _pdfs.Create("First.pdf", 101, 102);
            string second = _pdfs.Create("Second.pdf", 201);
            string third = _pdfs.Create("Third.pdf", 301, 302);
            string output = _pdfs.PathFor("merged.pdf");

            _service.MergePdfs(output, new() { second, first, third });

            Assert.Equal(new[] { 201f, 101f, 102f, 301f, 302f }, TestPdfs.PageWidths(output));
        }

        [Fact]
        public void MergePdfs_adds_a_bookmark_per_file_pointing_at_its_first_page()
        {
            string first = _pdfs.Create("First.pdf", 101, 102);
            string second = _pdfs.Create("Second.pdf", 201);
            string output = _pdfs.PathFor("merged.pdf");

            _service.MergePdfs(output, new() { first, second });

            using var doc = new PdfDocument(new PdfReader(output));
            var bookmarks = doc.GetOutlines(false).GetAllChildren();
            Assert.Equal(new[] { "First", "Second" }, bookmarks.Select(b => b.GetTitle()));

            var pageOfSecond = doc.GetPage(3).GetPdfObject();
            var destination = (PdfArray)bookmarks[1].GetDestination().GetPdfObject();
            Assert.Same(pageOfSecond, destination.Get(0));
        }

        [Fact]
        public void MergePdfs_overwrites_an_existing_output()
        {
            string output = _pdfs.CreatePages("merged.pdf", 9);

            _service.MergePdfs(output, new() { _pdfs.CreatePages("a.pdf", 1), _pdfs.CreatePages("b.pdf", 1) });

            Assert.Equal(2, _service.GetPageCount(output));
        }

        [Fact]
        public void MergePdfs_names_a_password_protected_input()
        {
            var files = new System.Collections.Generic.List<string> { _pdfs.CreatePages("a.pdf", 1), _pdfs.CreateEncrypted("Locked.pdf") };

            var ex = Assert.Throws<InvalidOperationException>(() => _service.MergePdfs(_pdfs.PathFor("merged.pdf"), files));

            Assert.Equal("'Locked.pdf' is password-protected and cannot be merged.", ex.Message);
        }

        [Fact]
        public void MergePdfs_names_an_input_that_is_not_a_pdf()
        {
            var files = new System.Collections.Generic.List<string> { _pdfs.CreatePages("a.pdf", 1), _pdfs.CreateNotAPdf("Notes.pdf") };

            var ex = Assert.Throws<InvalidOperationException>(() => _service.MergePdfs(_pdfs.PathFor("merged.pdf"), files));

            Assert.Equal("'Notes.pdf' is not a valid PDF or is corrupted.", ex.Message);
        }

        // --- Rotate ---

        [Fact]
        public void RotatePdf_with_no_page_spec_rotates_every_page()
        {
            string source = _pdfs.CreatePages("doc.pdf", 3);
            string output = _pdfs.PathFor("rotated.pdf");

            _service.RotatePdf(source, output, null!, 90);

            Assert.Equal(new[] { 90, 90, 90 }, TestPdfs.PageRotations(output));
            Assert.Equal(new[] { 0, 0, 0 }, TestPdfs.PageRotations(source));
        }

        [Fact]
        public void RotatePdf_rotates_only_the_listed_pages()
        {
            string source = _pdfs.CreatePages("doc.pdf", 5);
            string output = _pdfs.PathFor("rotated.pdf");

            _service.RotatePdf(source, output, "1, 3-4", 180);

            Assert.Equal(new[] { 180, 0, 180, 180, 0 }, TestPdfs.PageRotations(output));
        }

        [Theory]
        [InlineData(0, 90, 90)]
        [InlineData(270, 180, 90)]
        [InlineData(90, 270, 0)]
        public void RotatePdf_adds_to_the_existing_rotation(int existing, int degrees, int expected)
        {
            string source = _pdfs.Create("doc.pdf", new[] { 100f, 101f }, rotation: existing);
            string output = _pdfs.PathFor("rotated.pdf");

            _service.RotatePdf(source, output, null!, degrees);

            Assert.Equal(new[] { expected, expected }, TestPdfs.PageRotations(output));
        }

        [Fact]
        public void RotatePdf_keeps_the_pages_in_order()
        {
            string source = _pdfs.Create("doc.pdf", 101, 102, 103);
            string output = _pdfs.PathFor("rotated.pdf");

            _service.RotatePdf(source, output, "2", 90);

            Assert.Equal(new[] { 101f, 102f, 103f }, TestPdfs.PageWidths(output));
        }

        [Fact]
        public void RotatePdf_with_an_invalid_page_spec_leaves_an_existing_output_untouched()
        {
            string source = _pdfs.CreatePages("doc.pdf", 3);
            string output = _pdfs.CreateNotAPdf("rotated.pdf");
            byte[] before = File.ReadAllBytes(output);

            Assert.Throws<InvalidOperationException>(() => _service.RotatePdf(source, output, "9", 90));

            Assert.Equal(before, File.ReadAllBytes(output));
            Assert.Empty(Directory.GetFiles(_pdfs.Folder, "*.tmp"));
        }

        [Fact]
        public void RotatePdf_with_a_protected_source_leaves_no_temp_file_and_the_output_untouched()
        {
            string source = _pdfs.CreateEncrypted("locked.pdf");
            string output = _pdfs.CreateNotAPdf("rotated.pdf");
            byte[] before = File.ReadAllBytes(output);

            Assert.Throws<iText.Kernel.Exceptions.BadPasswordException>(() => _service.RotatePdf(source, output, null!, 90));

            Assert.Equal(before, File.ReadAllBytes(output));
            Assert.Empty(Directory.GetFiles(_pdfs.Folder, "*.tmp"));
        }
    }
}
