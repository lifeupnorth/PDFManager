using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using Path = System.IO.Path;

namespace PDFManager.Tests
{
    // A temporary folder of generated PDFs, deleted when the test finishes.
    // Each page gets its own width, so tests can tell which source page ended up where.
    public sealed class TestPdfs : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "PDFManager.Tests", Guid.NewGuid().ToString("N"));

        public TestPdfs() => Directory.CreateDirectory(Folder);

        public string PathFor(string fileName) => Path.Combine(Folder, fileName);

        public string Create(string fileName, params float[] pageWidths) => Create(fileName, pageWidths, rotation: 0);

        public string Create(string fileName, float[] pageWidths, int rotation)
        {
            string path = PathFor(fileName);
            using (var writer = new PdfWriter(path))
            using (var doc = new PdfDocument(writer))
                AddPages(doc, pageWidths, rotation);
            return path;
        }

        public string CreatePages(string fileName, int pageCount, float firstWidth = 100) =>
            Create(fileName, Enumerable.Range(0, pageCount).Select(i => firstWidth + i).ToArray());

        public string CreateEncrypted(string fileName, int pageCount = 2)
        {
            string path = PathFor(fileName);
            var properties = new WriterProperties().SetStandardEncryption(
                System.Text.Encoding.ASCII.GetBytes("user"), System.Text.Encoding.ASCII.GetBytes("owner"),
                EncryptionConstants.ALLOW_PRINTING, EncryptionConstants.ENCRYPTION_AES_128);
            using (var writer = new PdfWriter(path, properties))
            using (var doc = new PdfDocument(writer))
                AddPages(doc, Enumerable.Range(0, pageCount).Select(i => 100f + i).ToArray(), 0);
            return path;
        }

        public string CreateNotAPdf(string fileName)
        {
            string path = PathFor(fileName);
            File.WriteAllText(path, "This is plain text, not a PDF.");
            return path;
        }

        public static List<float> PageWidths(string path)
        {
            using (var doc = new PdfDocument(new PdfReader(path)))
                return Enumerable.Range(1, doc.GetNumberOfPages()).Select(i => doc.GetPage(i).GetMediaBox().GetWidth()).ToList();
        }

        public static List<int> PageRotations(string path)
        {
            using (var doc = new PdfDocument(new PdfReader(path)))
                return Enumerable.Range(1, doc.GetNumberOfPages()).Select(i => doc.GetPage(i).GetRotation()).ToList();
        }

        private static void AddPages(PdfDocument doc, float[] pageWidths, int rotation)
        {
            foreach (float width in pageWidths)
                doc.AddNewPage(new PageSize(width, 200)).SetRotation(rotation);
        }

        public void Dispose()
        {
            try { Directory.Delete(Folder, recursive: true); } catch { }
        }
    }
}
