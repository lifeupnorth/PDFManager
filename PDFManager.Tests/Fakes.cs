using System;
using System.Collections.Generic;
using System.IO;
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
        public Action<string, string>? Rotate { get; init; }
        public Action<string, int, string>? Split { get; init; }
        public Action<string, List<string>>? Merge { get; init; }
        // The operations below get the source and output paths
        public Action<string, string>? Extract { get; init; }
        public Action<string, string>? SaveMetadata { get; init; }
        public Action<string, string>? Protect { get; init; }
        public Action<string, string>? Unlock { get; init; }
        public Action<string, string>? Watermark { get; init; }
        public Action<IList<string>, string>? CreateFromImages { get; init; }

        public int GetPageCount(string path) => PageCount(path);
        public string GetSplitDestPattern(string sourcePath) =>
            Path.Combine(Path.GetDirectoryName(sourcePath)!, Path.GetFileNameWithoutExtension(sourcePath) + "_part{0}.pdf");
        public void SplitPdf(string sourcePath, int splitPage, string folderPath) => (Split ?? throw new NotSupportedException())(sourcePath, splitPage, folderPath);
        public void MergePdfs(string outputPath, List<string> sourceFiles) => (Merge ?? throw new NotSupportedException())(outputPath, sourceFiles);
        public void RotatePdf(string sourcePath, string outputPath, string? pageSpec, int degrees) => (Rotate ?? throw new NotSupportedException())(sourcePath, outputPath);
        public void ExtractPages(string sourcePath, string outputPath, string pageSpec) => (Extract ?? throw new NotSupportedException())(sourcePath, outputPath);
        public PdfMetadata ReadMetadata(string path) => Metadata(path);
        public void WriteMetadata(string sourcePath, string outputPath, PdfMetadata metadata) => (SaveMetadata ?? throw new NotSupportedException())(sourcePath, outputPath);
        public PdfProtection GetProtection(string path) => Protection(path);
        public void ProtectPdf(string sourcePath, string outputPath, string password) => (Protect ?? throw new NotSupportedException())(sourcePath, outputPath);
        public void UnlockPdf(string sourcePath, string outputPath, string password) => (Unlock ?? throw new NotSupportedException())(sourcePath, outputPath);
        public void AddWatermark(string sourcePath, string outputPath, WatermarkOptions options) => (Watermark ?? throw new NotSupportedException())(sourcePath, outputPath);
        public void ImagesToPdf(IList<string> imagePaths, string outputPath, ImagePageOptions options) => (CreateFromImages ?? throw new NotSupportedException())(imagePaths, outputPath);
        public ISet<int> ParsePageRanges(string spec, int totalPages) => throw new NotSupportedException();
    }

    internal sealed record DialogCall(bool IsQuestion, string Message, string Title, DialogIcon Icon);

    internal sealed record SaveFileRequest(string Filter, string DefaultExtension, string FileName);

    internal sealed record FolderRequest(string Title, string? InitialFolder);

    // Records every message and question. Questions are answered by Answer, which by default
    // says No: the answer that changes nothing. Pickers return the Picked* values, where null
    // or empty means the dialog was cancelled.
    internal sealed class FakeDialogService : IDialogService
    {
        public Func<string, bool> Answer { get; set; } = _ => false;
        public string? PickedFile { get; set; }
        public IReadOnlyList<string> PickedFiles { get; set; } = Array.Empty<string>();
        public string? PickedSaveFile { get; set; }
        public string? PickedFolder { get; set; }

        public List<DialogCall> Calls { get; } = new List<DialogCall>();
        public List<SaveFileRequest> SaveFileRequests { get; } = new List<SaveFileRequest>();
        public List<FolderRequest> FolderRequests { get; } = new List<FolderRequest>();
        public List<string> ShownInExplorer { get; } = new List<string>();

        public void Show(string message, string title, DialogIcon icon) =>
            Calls.Add(new DialogCall(false, message, title, icon));

        public bool Confirm(string message, string title, DialogIcon icon)
        {
            Calls.Add(new DialogCall(true, message, title, icon));
            return Answer(message);
        }

        public string? PickFile(string filter) => PickedFile;
        public IReadOnlyList<string> PickFiles(string filter) => PickedFiles;

        public string? PickSaveFile(string filter, string defaultExtension, string fileName)
        {
            SaveFileRequests.Add(new SaveFileRequest(filter, defaultExtension, fileName));
            return PickedSaveFile;
        }

        public string? PickFolder(string title, string? initialFolder = null)
        {
            FolderRequests.Add(new FolderRequest(title, initialFolder));
            return PickedFolder;
        }
        public void ShowInExplorer(string filePath) => ShownInExplorer.Add(filePath);
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
            public Task<BitmapSource> RenderPageAtDpiAsync(int pageNumber, int dpi, int maxSide) => Task.FromResult(Pixel);
            public void Dispose() { }
        }
    }
}
