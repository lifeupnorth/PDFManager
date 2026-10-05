#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using iText.Forms;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Extgstate;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Navigation;
using iText.Kernel.Utils;
using iText.Kernel.XMP;
using Path = System.IO.Path;

namespace PDFManager.Services
{
    public class PdfService : IPdfService
    {
        // Helper class moved from MainWindow
        private class CustomPdfSplitter : PdfSplitter
        {
            private readonly string dest;
            private int fileNumber = 1;

            public CustomPdfSplitter(PdfDocument pdfDocument, string dest) : base(pdfDocument)
            {
                this.dest = dest;
            }

            protected override PdfWriter GetNextPdfWriter(PageRange documentPageRange)
            {
                return new PdfWriter(string.Format(dest, fileNumber++));
            }
        }

        public string GetSplitDestPattern(string sourcePath)
        {
            string folderPath = Path.GetDirectoryName(sourcePath) ?? throw new ArgumentException("The path has no folder.", nameof(sourcePath));
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            return Path.Combine(folderPath, baseName + "_part{0}.pdf");
        }

        public void SplitPdf(string sourcePath, int splitPage, string folderPath)
        {
            string splitDest = GetSplitDestPattern(sourcePath);

            using (var pdfDoc = new PdfDocument(new PdfReader(sourcePath)))
            {
                int totalPages = pdfDoc.GetNumberOfPages();
                if (totalPages < 2)
                    throw new InvalidOperationException("This document has only 1 page and cannot be split.");
                if (splitPage >= totalPages)
                    throw new InvalidOperationException($"Page number {splitPage} is out of range. The document has {totalPages} pages, so the split point must be between 1 and {totalPages - 1}.");

                IList<PdfDocument> splitDocuments = new CustomPdfSplitter(pdfDoc, splitDest).SplitByPageNumbers(new int[] { splitPage + 1 });
                foreach (PdfDocument doc in splitDocuments)
                    doc.Close();
            }
        }

        public int GetPageCount(string path)
        {
            using (var pdfDoc = new PdfDocument(new PdfReader(path)))
                return pdfDoc.GetNumberOfPages();
        }

        public void MergePdfs(string outputPath, List<string> sourceFiles)
        {
            // Smart mode lets iText reuse identical objects (fonts, images, resource
            // dictionaries) shared by the source documents instead of writing a copy per file
            using (var resultDoc = new PdfDocument(new PdfWriter(outputPath, new WriterProperties().UseSmartMode())))
            {
                var formCopier = new PdfPageFormCopier();
                PdfOutline? rootOutline = null;
                int pageIndex = 1;

                foreach (var filePath in sourceFiles)
                {
                    try
                    {
                        using (var srcDoc = new PdfDocument(new PdfReader(filePath)))
                        {
                            int numberOfPages = srcDoc.GetNumberOfPages();
                            srcDoc.CopyPagesTo(1, numberOfPages, resultDoc, formCopier);

                            rootOutline ??= resultDoc.GetOutlines(false);
                            var outline = rootOutline.AddOutline(Path.GetFileNameWithoutExtension(filePath));
                            outline.AddDestination(PdfExplicitDestination.CreateFit(resultDoc.GetPage(pageIndex)));

                            pageIndex += numberOfPages;
                        }
                    }
                    catch (iText.Kernel.Exceptions.BadPasswordException)
                    {
                        throw new InvalidOperationException($"'{Path.GetFileName(filePath)}' is password-protected and cannot be merged.");
                    }
                    catch (iText.IO.Exceptions.IOException)
                    {
                        throw new InvalidOperationException($"'{Path.GetFileName(filePath)}' is not a valid PDF or is corrupted.");
                    }
                }
            }
        }

        public void RotatePdf(string sourcePath, string outputPath, string? pageSpec, int degrees)
        {
            // Rotating writes to a temporary file that replaces the output only on success,
            // so the source is read once and a failed run leaves the output untouched
            string tempPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                // The reader and writer get their own usings: if PdfDocument's constructor throws
                // (e.g. a password-protected source), nothing else would close the temp file
                using (var reader = new PdfReader(sourcePath))
                using (var writer = new PdfWriter(tempPath))
                using (var pdfDoc = new PdfDocument(reader, writer))
                {
                    int totalPages = pdfDoc.GetNumberOfPages();

                    // A null spec means "All pages"
                    ISet<int>? pagesToRotate = pageSpec == null
                        ? null
                        : ParsePageRanges(pageSpec, totalPages);

                    for (int i = 1; i <= totalPages; i++)
                    {
                        if (pagesToRotate == null || pagesToRotate.Contains(i))
                        {
                            PdfPage page = pdfDoc.GetPage(i);
                            page.SetRotation((page.GetRotation() + degrees) % 360);
                        }
                    }
                }

                File.Move(tempPath, outputPath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        public void ExtractPages(string sourcePath, string outputPath, string pageSpec)
        {
            // Like rotating, extraction writes to a temporary file that replaces the output
            // only on success, so a bad page list never truncates an existing output
            string tempPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var reader = new PdfReader(sourcePath))
                using (var sourceDoc = new PdfDocument(reader))
                {
                    // Parse before creating the writer: there is nothing to clean up if the list is invalid
                    var pages = new List<int>(ParsePageRanges(pageSpec, sourceDoc.GetNumberOfPages()));

                    using (var writer = new PdfWriter(tempPath, new WriterProperties().UseSmartMode()))
                    using (var resultDoc = new PdfDocument(writer))
                        sourceDoc.CopyPagesTo(pages, resultDoc, new PdfPageFormCopier());
                }

                File.Move(tempPath, outputPath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        public PdfMetadata ReadMetadata(string path)
        {
            using (var reader = new PdfReader(path))
            using (var doc = new PdfDocument(reader))
            {
                PdfDocumentInfo info = doc.GetDocumentInfo();
                return new PdfMetadata(info.GetTitle() ?? "", info.GetAuthor() ?? "", info.GetSubject() ?? "", info.GetKeywords() ?? "");
            }
        }

        public void WriteMetadata(string sourcePath, string outputPath, PdfMetadata metadata)
        {
            // The temporary file is moved into place only after the source has been closed,
            // so the output may be the source itself
            string tempPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var reader = new PdfReader(sourcePath))
                using (var writer = new PdfWriter(tempPath))
                using (var doc = new PdfDocument(reader, writer))
                {
                    // A blank value removes the entry rather than storing an empty string
                    PdfDocumentInfo info = doc.GetDocumentInfo();
                    info.SetMoreInfo("Title", ValueOrNull(metadata.Title));
                    info.SetMoreInfo("Author", ValueOrNull(metadata.Author));
                    info.SetMoreInfo("Subject", ValueOrNull(metadata.Subject));
                    info.SetMoreInfo("Keywords", ValueOrNull(metadata.Keywords));

                    // Many PDFs repeat these properties in an XMP stream, which some viewers prefer.
                    // iText copies the document info into it on close but never removes anything,
                    // and a value left there fills a removed info entry back in. So drop the old
                    // values here and let that copy write back the ones still set
                    XMPMeta xmp = doc.GetXmpMetadata();
                    if (xmp != null)
                    {
                        xmp.DeleteProperty(XMPConst.NS_DC, PdfConst.Title);
                        xmp.DeleteProperty(XMPConst.NS_DC, PdfConst.Creator);
                        xmp.DeleteProperty(XMPConst.NS_DC, PdfConst.Description);
                        xmp.DeleteProperty(XMPConst.NS_DC, PdfConst.Subject);
                        xmp.DeleteProperty(XMPConst.NS_PDF, PdfConst.Keywords);
                        doc.SetXmpMetadata(xmp);
                    }
                }

                File.Move(tempPath, outputPath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        public PdfProtection GetProtection(string path)
        {
            try
            {
                using (var reader = new PdfReader(path))
                using (var doc = new PdfDocument(reader))
                    return reader.IsEncrypted() ? PdfProtection.PermissionsOnly : PdfProtection.None;
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                return PdfProtection.PasswordToOpen;
            }
        }

        public void ProtectPdf(string sourcePath, string outputPath, string password)
        {
            if (GetProtection(sourcePath) != PdfProtection.None)
                throw new InvalidOperationException("This PDF is already protected. Remove its password with Unlock PDF first.");

            // Whoever has the password gets full access, so it serves as both the open and the
            // owner password. AES-256 takes UTF-8 passwords, so any characters work
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            var properties = new WriterProperties().SetStandardEncryption(passwordBytes, passwordBytes, AllPermissions, EncryptionConstants.ENCRYPTION_AES_256);
            WriteThroughTempFile(sourcePath, outputPath, properties, new ReaderProperties());
        }

        public void UnlockPdf(string sourcePath, string outputPath, string password)
        {
            PdfProtection protection = GetProtection(sourcePath);
            if (protection == PdfProtection.None)
                throw new InvalidOperationException("This PDF is not password-protected.");

            // Check the password before creating any output. Removing the protection needs the
            // owner password; the open password alone is not enough when the two differ
            var readerProperties = new ReaderProperties().SetPassword(Encoding.UTF8.GetBytes(password));
            try
            {
                using (var reader = new PdfReader(sourcePath, readerProperties))
                using (var doc = new PdfDocument(reader))
                {
                    if (!reader.IsOpenedWithFullPermission())
                        throw new InvalidOperationException(protection == PdfProtection.PasswordToOpen
                            ? "This password opens the PDF, but its owner has restricted changes. Enter the owner password to remove the protection."
                            : "The password is incorrect.");
                }
            }
            catch (iText.Kernel.Exceptions.BadPasswordException)
            {
                throw new InvalidOperationException("The password is incorrect.");
            }

            // Rewriting a document drops its encryption unless StampingProperties.PreserveEncryption is set
            WriteThroughTempFile(sourcePath, outputPath, new WriterProperties(), readerProperties);
        }

        public void AddWatermark(string sourcePath, string outputPath, WatermarkOptions options)
        {
            string tempPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var reader = new PdfReader(sourcePath))
                using (var writer = new PdfWriter(tempPath))
                using (var doc = new PdfDocument(reader, writer))
                {
                    int totalPages = doc.GetNumberOfPages();
                    ISet<int>? pages = options.PageSpec == null ? null : ParsePageRanges(options.PageSpec, totalPages);
                    PdfFont font = CreateWatermarkFont(options.Text);
                    var transparency = new PdfExtGState().SetFillOpacity(options.OpacityPercent / 100f);

                    for (int i = 1; i <= totalPages; i++)
                    {
                        if (pages == null || pages.Contains(i))
                            DrawWatermark(doc, doc.GetPage(i), font, transparency, options);
                    }
                }

                File.Move(tempPath, outputPath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        private static void DrawWatermark(PdfDocument doc, PdfPage page, PdfFont font, PdfExtGState transparency, WatermarkOptions options)
        {
            // Work in what the reader sees: a page with /Rotate 90 or 270 shows its box sideways.
            // A direction at angle a in page space appears at a - rotation on screen
            Rectangle box = page.GetCropBox();
            int rotation = page.GetRotation();
            bool sideways = rotation % 180 != 0;
            double visibleWidth = sideways ? box.GetHeight() : box.GetWidth();
            double visibleHeight = sideways ? box.GetWidth() : box.GetHeight();

            double visibleAngle = options.Diagonal ? Math.Atan2(visibleHeight, visibleWidth) * 180 / Math.PI : 0;
            double radians = (visibleAngle + rotation) * Math.PI / 180;

            // Fit the text to 75% of the line it runs along, but never taller than a quarter of the page
            double lineLength = options.Diagonal ? Math.Sqrt(visibleWidth * visibleWidth + visibleHeight * visibleHeight) : visibleWidth;
            double widthAtOnePoint = font.GetWidth(options.Text, 1);
            float fontSize = (float)Math.Min(lineLength * 0.75 / widthAtOnePoint, Math.Min(visibleWidth, visibleHeight) / 4);

            // Centre the text on the page: half its width back along the line, and roughly
            // half the capital height down across it
            double cos = Math.Cos(radians), sin = Math.Sin(radians);
            double alongX = -font.GetWidth(options.Text, fontSize) / 2, acrossY = -fontSize * 0.35;
            double x = box.GetX() + box.GetWidth() / 2 + alongX * cos - acrossY * sin;
            double y = box.GetY() + box.GetHeight() / 2 + alongX * sin + acrossY * cos;

            // Existing content can leave the graphics state changed (a scale, a colour), so wrap it
            // in q/Q; the watermark then starts from a clean state. The Q is written directly because
            // PdfCanvas refuses to restore a state that a different canvas saved
            new PdfCanvas(page.NewContentStreamBefore(), page.GetResources(), doc).SaveState().Release();
            PdfStream after = page.NewContentStreamAfter();
            after.GetOutputStream().WriteString("Q\n");
            var canvas = new PdfCanvas(after, page.GetResources(), doc);
            canvas.SaveState()
                .BeginMarkedContent(PdfName.Artifact) // decoration, so screen readers skip it
                .SetExtGState(transparency)
                .SetFillColor(new DeviceGray(0.45f))
                .BeginText()
                .SetFontAndSize(font, fontSize)
                .SetTextMatrix((float)cos, (float)sin, (float)-sin, (float)cos, (float)x, (float)y)
                .ShowText(options.Text)
                .EndText()
                .EndMarkedContent()
                .RestoreState()
                .Release();
        }

        // Helvetica needs no embedding but only covers Western European text, so other scripts
        // use the first installed bold font that has every character, embedding just those glyphs
        private static readonly (string File, int TtcIndex)[] WatermarkFonts =
        {
            ("arialbd.ttf", -1), ("segoeuib.ttf", -1), ("tahomabd.ttf", -1), ("msyhbd.ttc", 0),
            ("YuGothB.ttc", 0), ("malgunbd.ttf", -1), ("Nirmala.ttc", 0), ("seguisym.ttf", -1)
        };

        private static PdfFont CreateWatermarkFont(string text)
        {
            PdfFont helvetica = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            if (ContainsAll(helvetica, text))
                return helvetica;

            string fontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var (file, ttcIndex) in WatermarkFonts)
            {
                string path = Path.Combine(fontsFolder, file);
                if (!File.Exists(path))
                    continue;
                PdfFont font = ttcIndex < 0
                    ? PdfFontFactory.CreateFont(path, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED)
                    : PdfFontFactory.CreateTtcFont(path, ttcIndex, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED, false);
                if (ContainsAll(font, text))
                    return font;
            }

            throw new InvalidOperationException("The watermark contains characters that none of the installed fonts can show.");
        }

        private static bool ContainsAll(PdfFont font, string text)
        {
            for (int i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
            {
                if (!font.ContainsGlyph(char.ConvertToUtf32(text, i)))
                    return false;
            }
            return true;
        }

        public void ImagesToPdf(IList<string> imagePaths, string outputPath, ImagePageOptions options)
        {
            string tempPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var writer = new PdfWriter(tempPath, new WriterProperties().UseSmartMode()))
                using (var doc = new PdfDocument(writer))
                {
                    foreach (string imagePath in imagePaths)
                        AddImagePage(doc, imagePath, options);
                }

                File.Move(tempPath, outputPath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        private const float MarginPoints = 36; // half an inch

        private static void AddImagePage(PdfDocument doc, string imagePath, ImagePageOptions options)
        {
            iText.IO.Image.ImageData image;
            try
            {
                image = iText.IO.Image.ImageDataFactory.Create(imagePath);
            }
            catch (Exception ex) when (ex is iText.Commons.Exceptions.ITextException or IOException)
            {
                throw new InvalidOperationException($"'{Path.GetFileName(imagePath)}' is not a supported image, or the file is damaged.");
            }

            // Size as shown: the stored pixels turned upright, at the image's own resolution (96 DPI if it has none)
            int rotation = ImageFiles.ReadRotation(imagePath);
            bool sideways = rotation % 180 != 0;
            float dpiX = image.GetDpiX() > 0 ? image.GetDpiX() : 96, dpiY = image.GetDpiY() > 0 ? image.GetDpiY() : 96;
            float widthPoints = image.GetWidth() * 72f / dpiX, heightPoints = image.GetHeight() * 72f / dpiY;
            float shownWidth = sideways ? heightPoints : widthPoints, shownHeight = sideways ? widthPoints : heightPoints;

            PageSize pageSize;
            float x, y, drawWidth, drawHeight;
            if (options.PageSize == ImagePageSize.SameAsImage)
            {
                pageSize = new PageSize(shownWidth, shownHeight);
                (x, y, drawWidth, drawHeight) = (0, 0, shownWidth, shownHeight);
            }
            else
            {
                // A4 or Letter, turned to match the image, with the image scaled to fit and centred
                PageSize paper = options.PageSize == ImagePageSize.A4 ? PageSize.A4 : PageSize.LETTER;
                pageSize = shownWidth > shownHeight ? paper.Rotate() : paper;
                float margin = options.Margin ? MarginPoints : 0;
                float scale = Math.Min((pageSize.GetWidth() - 2 * margin) / shownWidth, (pageSize.GetHeight() - 2 * margin) / shownHeight);
                drawWidth = shownWidth * scale;
                drawHeight = shownHeight * scale;
                x = (pageSize.GetWidth() - drawWidth) / 2;
                y = (pageSize.GetHeight() - drawHeight) / 2;
            }

            // Map the image's unit square onto the box (x, y, drawWidth, drawHeight), turned clockwise
            // by rotation: at 90 the stored top edge becomes the right edge, and so on
            var (a, b, c, d, e, f) = rotation switch
            {
                90 => (0f, -drawHeight, drawWidth, 0f, x, y + drawHeight),
                180 => (-drawWidth, 0f, 0f, -drawHeight, x + drawWidth, y + drawHeight),
                270 => (0f, drawHeight, -drawWidth, 0f, x + drawWidth, y),
                _ => (drawWidth, 0f, 0f, drawHeight, x, y)
            };
            var canvas = new PdfCanvas(doc.AddNewPage(pageSize));
            canvas.AddImageWithTransformationMatrix(image, a, b, c, d, e, f);
            canvas.Release();
        }

        private const int AllPermissions = EncryptionConstants.ALLOW_PRINTING | EncryptionConstants.ALLOW_DEGRADED_PRINTING
            | EncryptionConstants.ALLOW_MODIFY_CONTENTS | EncryptionConstants.ALLOW_ASSEMBLY | EncryptionConstants.ALLOW_COPY
            | EncryptionConstants.ALLOW_SCREENREADERS | EncryptionConstants.ALLOW_MODIFY_ANNOTATIONS | EncryptionConstants.ALLOW_FILL_IN;

        // Rewrites the source into a temporary file that replaces the output only on success,
        // after the source is closed, so a failure never touches an existing output
        private static void WriteThroughTempFile(string sourcePath, string outputPath, WriterProperties writerProperties, ReaderProperties readerProperties)
        {
            string tempPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var reader = new PdfReader(sourcePath, readerProperties))
                using (var writer = new PdfWriter(tempPath, writerProperties))
                using (var doc = new PdfDocument(reader, writer))
                {
                }

                File.Move(tempPath, outputPath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        private static string? ValueOrNull(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        public ISet<int> ParsePageRanges(string spec, int totalPages) => PageRanges.Parse(spec, totalPages);
    }
}