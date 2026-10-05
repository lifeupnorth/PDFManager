using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFManager.Services;

namespace PDFManager.Tests
{
    internal sealed class FakePdfService : IPdfService
    {
        public Func<string, int> PageCount { get; init; } = _ => 1;
        public Func<string, PdfMetadata> Metadata { get; init; } = _ => new PdfMetadata("", "", "", "");
        public Func<string, PdfProtection> Protection { get; init; } = _ => PdfProtection.None;

        public int GetPageCount(string path) => PageCount(path);
        public string GetSplitDestPattern(string sourcePath) => throw new NotSupportedException();
        public void SplitPdf(string sourcePath, int splitPage, string folderPath) => throw new NotSupportedException();
        public void MergePdfs(string outputPath, List<string> sourceFiles) => throw new NotSupportedException();
        public void RotatePdf(string sourcePath, string outputPath, string pageSpec, int degrees) => throw new NotSupportedException();
        public void ExtractPages(string sourcePath, string outputPath, string pageSpec) => throw new NotSupportedException();
        public PdfMetadata ReadMetadata(string path) => Metadata(path);
        public void WriteMetadata(string sourcePath, string outputPath, PdfMetadata metadata) => throw new NotSupportedException();
        public PdfProtection GetProtection(string path) => Protection(path);
        public void ProtectPdf(string sourcePath, string outputPath, string password) => throw new NotSupportedException();
        public void UnlockPdf(string sourcePath, string outputPath, string password) => throw new NotSupportedException();
        public void AddWatermark(string sourcePath, string outputPath, WatermarkOptions options) => throw new NotSupportedException();
        public ISet<int> ParsePageRanges(string spec, int totalPages) => throw new NotSupportedException();
    }

    // Pretends every file has PageCount pages, each drawn as a one-pixel image
    internal sealed class FakePageRenderer : IPageRenderer
    {
        private static readonly BitmapSource Pixel = CreatePixel();

        public Func<string, int> PageCount { get; init; } = _ => 3;

        public Task<IRenderedDocument> OpenAsync(string path) =>
            Task.FromResult<IRenderedDocument>(new Document(PageCount(path)));

        private static BitmapSource CreatePixel()
        {
            var pixel = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Gray8, null, new byte[] { 255 }, 1);
            pixel.Freeze();
            return pixel;
        }

        private sealed class Document : IRenderedDocument
        {
            public Document(int pageCount) => PageCount = pageCount;
            public int PageCount { get; }
            public Task<BitmapSource> RenderPageAsync(int pageNumber, int width) => Task.FromResult(Pixel);
            public void Dispose() { }
        }
    }
}
