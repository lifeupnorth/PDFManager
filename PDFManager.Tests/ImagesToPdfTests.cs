using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using PDFManager.Services;
using Path = System.IO.Path;
using Matrix = iText.Kernel.Geom.Matrix;

namespace PDFManager.Tests
{
    public class ImagesToPdfTests : IDisposable
    {
        private readonly PdfService _service = new PdfService();
        private readonly TestPdfs _pdfs = new TestPdfs();

        public void Dispose() => _pdfs.Dispose();

        private static readonly ImagePageOptions SameAsImage = new(ImagePageSize.SameAsImage, false);

        // An image whose left half is red and right half blue, so tests can tell which way up it is shown
        private string CreateImage(string name, int width, int height, double dpi = 96, ushort orientation = 0)
        {
            var pixels = new byte[width * height * 3];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 3;
                    if (x < width / 2) pixels[i + 2] = 255; // red (Bgr24)
                    else pixels[i] = 255;                   // blue
                }
            var source = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgr24, null, pixels, width * 3);

            string path = _pdfs.PathFor(name);
            BitmapEncoder encoder = Path.GetExtension(name).ToLowerInvariant() switch
            {
                ".jpg" => new JpegBitmapEncoder { QualityLevel = 95 },
                ".gif" => new GifBitmapEncoder(),
                ".bmp" => new BmpBitmapEncoder(),
                ".tif" => new TiffBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };
            BitmapMetadata? metadata = null;
            if (orientation != 0)
            {
                metadata = new BitmapMetadata("jpg");
                metadata.SetQuery("System.Photo.Orientation", orientation);
            }
            encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
            using (var stream = File.Create(path))
                encoder.Save(stream);
            return path;
        }

        private static List<Rectangle> PageSizes(string path)
        {
            using var doc = new PdfDocument(new PdfReader(path));
            return Enumerable.Range(1, doc.GetNumberOfPages()).Select(i => doc.GetPage(i).GetPageSize()).ToList();
        }

        // Where the page's image lands: the box its unit square is mapped to
        private static Rectangle ImageBox(string path, int page)
        {
            using var doc = new PdfDocument(new PdfReader(path));
            var listener = new ImageCollector();
            new PdfCanvasProcessor(listener).ProcessPageContent(doc.GetPage(page));
            var m = Assert.Single(listener.Matrices);
            float[] xs = { m.Get(6), m.Get(6) + m.Get(0), m.Get(6) + m.Get(3), m.Get(6) + m.Get(0) + m.Get(3) };
            float[] ys = { m.Get(7), m.Get(7) + m.Get(1), m.Get(7) + m.Get(4), m.Get(7) + m.Get(1) + m.Get(4) };
            return new Rectangle(xs.Min(), ys.Min(), xs.Max() - xs.Min(), ys.Max() - ys.Min());
        }

        private sealed class ImageCollector : IEventListener
        {
            public List<Matrix> Matrices { get; } = new();
            public void EventOccurred(IEventData data, EventType type)
            {
                if (data is ImageRenderInfo info) Matrices.Add(info.GetImageCtm());
            }
            public ICollection<EventType> GetSupportedEvents() => new[] { EventType.RENDER_IMAGE };
        }

        // The colour at a point of the page as Windows draws it, as fractions of its width and height
        private static async Task<string> ColourAt(string pdf, double fx, double fy)
        {
            using var doc = await new WindowsPageRenderer().OpenAsync(pdf);
            var image = new FormatConvertedBitmap(await doc.RenderPageAtDpiAsync(1, 72, 10000), PixelFormats.Bgr24, null, 0);
            var pixel = new byte[3];
            image.CopyPixels(new System.Windows.Int32Rect((int)(image.PixelWidth * fx), (int)(image.PixelHeight * fy), 1, 1), pixel, 3, 0);
            return pixel[2] > 150 && pixel[0] < 100 ? "red" : pixel[0] > 150 && pixel[2] < 100 ? "blue" : $"other({pixel[2]},{pixel[1]},{pixel[0]})";
        }

        // --- Page size ---

        [Fact]
        public void SameAsImage_makes_each_page_the_size_of_its_image_at_the_image_resolution()
        {
            string output = _pdfs.PathFor("out.pdf");

            _service.ImagesToPdf(new[] { CreateImage("screen.png", 200, 100), CreateImage("scan.jpg", 600, 300, dpi: 300) }, output, SameAsImage);

            var sizes = PageSizes(output);
            Assert.Equal(150, sizes[0].GetWidth(), 0.5);   // 200 px at 96 DPI
            Assert.Equal(75, sizes[0].GetHeight(), 0.5);
            Assert.Equal(144, sizes[1].GetWidth(), 0.5);   // 600 px at 300 DPI
            Assert.Equal(72, sizes[1].GetHeight(), 0.5);
            var box = ImageBox(output, 1);
            Assert.Equal((0f, 0f, 150f, 75f), (box.GetX(), box.GetY(), (float)Math.Round(box.GetWidth()), (float)Math.Round(box.GetHeight())));
        }

        [Theory]
        [InlineData(ImagePageSize.A4, 595, 842)]
        [InlineData(ImagePageSize.Letter, 612, 792)]
        public void Paper_sizes_turn_to_match_the_image_and_centre_it(ImagePageSize paper, float shortSide, float longSide)
        {
            string output = _pdfs.PathFor("out.pdf");

            _service.ImagesToPdf(new[] { CreateImage("tall.png", 100, 400), CreateImage("wide.png", 400, 100) }, output, new ImagePageOptions(paper, false));

            var sizes = PageSizes(output);
            Assert.Equal((shortSide, longSide), ((float)Math.Round(sizes[0].GetWidth()), (float)Math.Round(sizes[0].GetHeight())));
            Assert.Equal((longSide, shortSide), ((float)Math.Round(sizes[1].GetWidth()), (float)Math.Round(sizes[1].GetHeight())));

            // The tall image fills the page height and is centred across it
            var box = ImageBox(output, 1);
            Assert.Equal(longSide, box.GetHeight(), 0.5);
            Assert.Equal(longSide / 4, box.GetWidth(), 0.5);
            Assert.Equal((shortSide - box.GetWidth()) / 2, box.GetX(), 0.5);
        }

        [Fact]
        public void A_margin_keeps_the_image_half_an_inch_from_the_edges()
        {
            string output = _pdfs.PathFor("out.pdf");

            _service.ImagesToPdf(new[] { CreateImage("tall.png", 100, 400) }, output, new ImagePageOptions(ImagePageSize.A4, true));

            var box = ImageBox(output, 1);
            Assert.Equal(36, box.GetY(), 0.5);
            Assert.Equal(842 - 72, box.GetHeight(), 0.5);
        }

        [Fact]
        public void Small_images_are_scaled_up_to_fit_the_paper()
        {
            string output = _pdfs.PathFor("out.pdf");

            _service.ImagesToPdf(new[] { CreateImage("icon.png", 10, 10) }, output, new ImagePageOptions(ImagePageSize.A4, false));

            Assert.Equal(595, ImageBox(output, 1).GetWidth(), 0.5);
        }

        // --- Orientation ---

        [Theory]
        [InlineData((ushort)1, 0.25, 0.5, "red", 0.75, 0.5, "blue")]  // as stored: red left, blue right
        [InlineData((ushort)6, 0.5, 0.25, "red", 0.5, 0.75, "blue")]  // turned 90 clockwise: red on top
        [InlineData((ushort)8, 0.5, 0.25, "blue", 0.5, 0.75, "red")]  // turned 90 counter-clockwise: blue on top
        [InlineData((ushort)3, 0.25, 0.5, "blue", 0.75, 0.5, "red")]  // turned 180: blue left
        public async Task Photos_are_placed_upright_using_their_EXIF_orientation(ushort orientation, double x1, double y1, string colour1, double x2, double y2, string colour2)
        {
            string photo = CreateImage("photo.jpg", 400, 200, orientation: orientation);
            string output = _pdfs.PathFor("out.pdf");

            _service.ImagesToPdf(new[] { photo }, output, SameAsImage);

            var size = PageSizes(output)[0];
            bool sideways = orientation is 6 or 8;
            Assert.Equal(sideways ? 150 : 300, size.GetWidth(), 0.5);
            Assert.Equal(sideways ? 300 : 150, size.GetHeight(), 0.5);
            Assert.Equal(colour1, await ColourAt(output, x1, y1));
            Assert.Equal(colour2, await ColourAt(output, x2, y2));
        }

        [Fact]
        public void ReadRotation_and_thumbnails_follow_the_EXIF_orientation()
        {
            string photo = CreateImage("photo.jpg", 400, 200, orientation: 6);

            Assert.Equal(90, ImageFiles.ReadRotation(photo));
            Assert.Equal(0, ImageFiles.ReadRotation(CreateImage("plain.png", 40, 20)));
            var thumbnail = ImageFiles.LoadThumbnail(photo, 64);
            Assert.True(thumbnail.PixelHeight > thumbnail.PixelWidth);
            Assert.True(thumbnail.IsFrozen);
        }

        // --- Files ---

        [Fact]
        public void Accepts_every_listed_format_in_the_given_order()
        {
            var images = new[] { "a.gif", "b.bmp", "c.tif", "d.jpg", "e.png" }.Select((name, i) => CreateImage(name, 100 + 10 * i, 50)).ToList();
            string output = _pdfs.PathFor("out.pdf");

            _service.ImagesToPdf(images, output, SameAsImage);

            Assert.Equal(new[] { 75f, 82.5f, 90f, 97.5f, 105f }, PageSizes(output).Select(s => (float)Math.Round(s.GetWidth() * 2) / 2));
            Assert.All(images, i => Assert.True(ImageFiles.IsImage(i)));
            Assert.False(ImageFiles.IsImage("notes.txt"));
        }

        [Fact]
        public void A_damaged_image_is_named_and_nothing_is_written()
        {
            string fake = _pdfs.PathFor("holiday.png");
            File.WriteAllText(fake, "not an image");
            string output = _pdfs.CreateNotAPdf("out.pdf");
            byte[] before = File.ReadAllBytes(output);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _service.ImagesToPdf(new[] { CreateImage("fine.png", 20, 20), fake }, output, SameAsImage));

            Assert.Equal("'holiday.png' is not a supported image, or the file is damaged.", ex.Message);
            Assert.Equal(before, File.ReadAllBytes(output));
            Assert.Empty(Directory.GetFiles(_pdfs.Folder, "*.tmp"));
        }
    }
}
