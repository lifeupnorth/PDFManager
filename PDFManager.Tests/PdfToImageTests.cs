using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PDFManager.Services;
using PDFManager.ViewModels;
using Path = System.IO.Path;

namespace PDFManager.Tests
{
    public class PdfToImageTests : IDisposable
    {
        private readonly TestPdfs _pdfs = new TestPdfs();

        public void Dispose() => _pdfs.Dispose();

        private static PdfToImageViewModel NewViewModel(IPageRenderer? renderer = null) =>
            new PdfToImageViewModel(new PdfService(), renderer ?? new WindowsPageRenderer());

        [Fact]
        public async Task Picking_a_file_suggests_an_images_folder_next_to_it()
        {
            var vm = NewViewModel();
            string source = _pdfs.CreatePages("Brochure.pdf", 3);

            await vm.UpdateFileAsync(source);

            Assert.Equal(Path.Combine(_pdfs.Folder, "Brochure_images"), vm.OutputFolder);
            Assert.Equal("Document has 3 pages. Example: 1, 3, 5-7", vm.PageCountInfo);
        }

        [Fact]
        public async Task Converts_every_page_by_default()
        {
            var vm = NewViewModel();
            await vm.UpdateFileAsync(_pdfs.CreatePages("doc.pdf", 3));

            var outcome = await vm.ConvertAsync(null);

            Assert.Equal(3, outcome.Saved);
            Assert.False(outcome.Cancelled);
            Assert.Null(outcome.Error);
            Assert.Equal(new[] { "doc_page1.png", "doc_page2.png", "doc_page3.png" }, Directory.GetFiles(vm.OutputFolder).Select(Path.GetFileName).OrderBy(n => n));
            Assert.Equal(outcome.FirstFile, Path.Combine(vm.OutputFolder, "doc_page1.png"));
            // Not checking vm.Progress: without a UI thread, Progress<T> posts its last update to
            // the thread pool, so it can land after the conversion has returned
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task Converts_the_pages_picked_on_the_thumbnails_as_JPEG()
        {
            var vm = NewViewModel();
            await vm.UpdateFileAsync(_pdfs.CreatePages("doc.pdf", 4));
            await vm.Thumbnails.Loading;
            vm.Format = ImageFormat.Jpeg;

            vm.Thumbnails.PageClickCommand.Execute(vm.Thumbnails.Pages[1]);
            vm.Thumbnails.PageClickCommand.Execute(vm.Thumbnails.Pages[3]);
            Assert.False(vm.AllPages);
            Assert.Equal("2, 4", vm.PageSpec);
            var outcome = await vm.ConvertAsync(PageRanges.Parse(vm.PageSpec, 4));

            Assert.Equal(2, outcome.Saved);
            Assert.Equal(new[] { "doc_page2.jpg", "doc_page4.jpg" }, Directory.GetFiles(vm.OutputFolder).Select(Path.GetFileName).OrderBy(n => n));
        }

        [Fact]
        public async Task Reports_a_password_protected_file()
        {
            var vm = NewViewModel();
            await vm.UpdateFileAsync(_pdfs.CreateEncrypted("locked.pdf"));
            Assert.Equal("This PDF is password-protected. Remove its password with Unlock PDF first.", vm.PageCountInfo);

            var outcome = await vm.ConvertAsync(null);

            Assert.Equal("This PDF is password-protected. Remove its password with Unlock PDF first.", outcome.Error);
            Assert.False(Directory.Exists(vm.OutputFolder));
        }

        [Fact]
        public async Task Cancelling_reports_how_many_pages_were_saved()
        {
            // A renderer that waits on page 2 until the test has pressed Cancel
            using var secondPageStarted = new ManualResetEventSlim();
            using var mayContinue = new ManualResetEventSlim();
            var vm = NewViewModel(new GatedRenderer(new WindowsPageRenderer(), 2, secondPageStarted, mayContinue));
            await vm.UpdateFileAsync(_pdfs.CreatePages("doc.pdf", 5));

            Task<PdfToImageViewModel.ConvertOutcome> run = vm.ConvertAsync(null);
            secondPageStarted.Wait();
            vm.CancelCommand.Execute(null);
            mayContinue.Set();
            var outcome = await run;

            Assert.True(outcome.Cancelled);
            Assert.Equal(2, outcome.Saved); // page 2 was already rendering, so it is finished and kept
            Assert.Equal(2, Directory.GetFiles(vm.OutputFolder).Length);
        }

        private sealed class GatedRenderer : IPageRenderer
        {
            private readonly IPageRenderer _inner;
            private readonly int _gatePage;
            private readonly ManualResetEventSlim _started, _mayContinue;

            public GatedRenderer(IPageRenderer inner, int gatePage, ManualResetEventSlim started, ManualResetEventSlim mayContinue)
            {
                _inner = inner; _gatePage = gatePage; _started = started; _mayContinue = mayContinue;
            }

            public async Task<IRenderedDocument> OpenAsync(string path) => new Document(await _inner.OpenAsync(path), this);

            private sealed class Document : IRenderedDocument
            {
                private readonly IRenderedDocument _inner;
                private readonly GatedRenderer _gate;
                public Document(IRenderedDocument inner, GatedRenderer gate) { _inner = inner; _gate = gate; }
                public int PageCount => _inner.PageCount;
                public Task<System.Windows.Media.Imaging.BitmapSource> RenderPageAsync(int pageNumber, int width) => _inner.RenderPageAsync(pageNumber, width);

                public async Task<System.Windows.Media.Imaging.BitmapSource> RenderPageAtDpiAsync(int pageNumber, int dpi, int maxSide)
                {
                    if (pageNumber == _gate._gatePage)
                    {
                        _gate._started.Set();
                        _gate._mayContinue.Wait();
                    }
                    return await _inner.RenderPageAtDpiAsync(pageNumber, dpi, maxSide);
                }

                public void Dispose() => _inner.Dispose();
            }
        }
    }
}
