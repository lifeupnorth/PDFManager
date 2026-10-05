using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

        // A 12-page PDF whose page count still reads correctly but whose pages 1-10 cannot be
        // loaded, so a tool fails only once it copies pages: after its output writer exists.
        // iText refuses to write such a file, so the bytes are edited. iText groups the first ten
        // pages under one /Kids array; its last entry is pointed at an object number that does not
        // exist, keeping the length so the xref stays valid (the page objects' numbers are two
        // digits long, so 99 is unused).
        public string CreateWithBrokenPageTree(string fileName)
        {
            string path = CreatePages(fileName, 12);
            var latin1 = System.Text.Encoding.Latin1;
            string text = latin1.GetString(File.ReadAllBytes(path));

            var kids = Regex.Match(text, @"/Kids\s*\[[^\]]*\]");
            var lastKid = Regex.Matches(kids.Value, @"(\d+) 0 R").Last();
            string missing = new string('9', lastKid.Groups[1].Length);
            int size = int.Parse(Regex.Match(text, @"/Size (\d+)").Groups[1].Value);
            if (int.Parse(missing) < size)
                throw new InvalidOperationException("No unused object number of the same length is available.");

            int at = kids.Index + lastKid.Groups[1].Index;
            text = text.Substring(0, at) + missing + text.Substring(at + missing.Length);
            File.WriteAllBytes(path, latin1.GetBytes(text));
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
