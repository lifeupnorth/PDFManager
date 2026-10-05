using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace PDFManager.Services
{
    /// <summary>Draws PDF pages as images, for the page thumbnails.</summary>
    public interface IPageRenderer
    {
        // Throws when the file cannot be opened, for example when it is password-protected
        Task<IRenderedDocument> OpenAsync(string path);
    }

    public interface IRenderedDocument : IDisposable
    {
        int PageCount { get; }

        // The page as a reader sees it (its /Rotate applied), width device-independent pixels wide.
        // Windows renders at the display's scale, so on a 150% screen the bitmap has 1.5x the pixels.
        // The image is frozen, so it can be created on any thread and shown on the UI thread
        Task<BitmapSource> RenderPageAsync(int pageNumber, int width);
    }
}
