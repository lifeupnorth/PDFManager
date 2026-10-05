using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    // What the drop handlers in MainWindow do with the dropped paths, and what they tell the user
    public class DropTests : IDisposable
    {
        private readonly TestPdfs _files = new TestPdfs();
        private readonly FakeDialogService _dialogs = new FakeDialogService();

        public void Dispose() => _files.Dispose();

        private string CreateFile(string name)
        {
            string path = _files.PathFor(name);
            File.WriteAllText(path, "");
            return path;
        }

        private string Png(string name)
        {
            string path = _files.PathFor(name);
            var source = BitmapSource.Create(4, 2, 96, 96, PixelFormats.Gray8, null, new byte[8], 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(path))
                encoder.Save(stream);
            return path;
        }

        // --- Tools that take one PDF ---

        [Theory]
        [InlineData("Report.pdf")]
        [InlineData("REPORT.PDF")]
        public void A_single_PDF_is_taken_without_a_word(string name)
        {
            string file = _files.PathFor(name);

            Assert.Equal(file, DroppedFiles.SinglePdf(new[] { file }, "split", _dialogs));
            Assert.Empty(_dialogs.Calls);
        }

        [Fact]
        public void Several_files_are_refused_with_what_the_tool_would_do()
        {
            var dropped = new[] { _files.PathFor("a.pdf"), _files.PathFor("b.pdf") };

            Assert.Null(DroppedFiles.SinglePdf(dropped, "extract pages from", _dialogs));
            Assert.Equal(new DialogCall(false, "Please drop a single PDF file to extract pages from.", "Validation Error", DialogIcon.Warning), Assert.Single(_dialogs.Calls));
        }

        [Theory]
        [InlineData("notes.txt")]
        [InlineData("archive.pdf.zip")]
        [InlineData("folder")]
        public void Anything_but_a_PDF_is_refused(string name)
        {
            Assert.Null(DroppedFiles.SinglePdf(new[] { _files.PathFor(name) }, "rotate", _dialogs));
            Assert.Equal(new DialogCall(false, "Only PDF files are supported.", "Validation Error", DialogIcon.Warning), Assert.Single(_dialogs.Calls));
        }

        [Fact]
        public void A_drop_with_no_files_is_ignored()
        {
            Assert.Null(DroppedFiles.SinglePdf(Array.Empty<string>(), "split", _dialogs));
            Assert.Empty(_dialogs.Calls);
        }

        // --- Batch ---

        [Fact]
        public void Batch_adds_dropped_PDFs_without_a_word()
        {
            var batch = new BatchViewModel(new FakePdfService(), _dialogs);
            string a = CreateFile("a.pdf");

            batch.AddDropped(new[] { a });

            Assert.Equal(new[] { a }, batch.Files.Select(f => f.Path));
            Assert.Empty(_dialogs.Calls);
        }

        [Fact]
        public void Batch_says_so_when_a_drop_adds_nothing_new()
        {
            var batch = new BatchViewModel(new FakePdfService(), _dialogs);
            string a = CreateFile("a.pdf");
            batch.AddDropped(new[] { a });

            batch.AddDropped(new[] { a, CreateFile("notes.txt") });

            Assert.Single(batch.Files);
            Assert.Equal(new DialogCall(false, "No new PDF files were found in what you dropped.", "No Files Added", DialogIcon.Information), Assert.Single(_dialogs.Calls));
        }

        [Fact]
        public void Batch_ignores_drops_while_it_runs()
        {
            var batch = new BatchViewModel(new FakePdfService(), _dialogs) { IsBusy = true };

            batch.AddDropped(new[] { CreateFile("a.pdf") });
            batch.AddDropped(new[] { CreateFile("notes.txt") });

            Assert.Empty(batch.Files);
            Assert.Empty(_dialogs.Calls);
        }

        // --- Image to PDF ---

        [Fact]
        public void ImagesToPdf_adds_dropped_images_without_a_word()
        {
            var vm = new ImagesToPdfViewModel(new FakePdfService(), _dialogs);
            string a = Png("a.png");

            vm.AddDropped(new[] { a });

            Assert.Equal(new[] { a }, vm.Images.Select(i => i.Path));
            Assert.Empty(_dialogs.Calls);
        }

        [Fact]
        public void ImagesToPdf_says_so_when_a_drop_has_no_new_image()
        {
            var vm = new ImagesToPdfViewModel(new FakePdfService(), _dialogs);

            vm.AddDropped(new[] { CreateFile("report.pdf") });

            Assert.Empty(vm.Images);
            Assert.Equal(new DialogCall(false, "No new images were found in what you dropped. Supported formats are JPEG, PNG, BMP, GIF and TIFF.", "No Images Added", DialogIcon.Information), Assert.Single(_dialogs.Calls));
        }

        [Fact]
        public void ImagesToPdf_ignores_drops_while_it_creates_a_PDF()
        {
            var vm = new ImagesToPdfViewModel(new FakePdfService(), _dialogs) { IsBusy = true };

            vm.AddDropped(new[] { Png("a.png") });
            vm.AddDropped(new[] { CreateFile("report.pdf") });

            Assert.Empty(vm.Images);
            Assert.Empty(_dialogs.Calls);
        }
    }
}
