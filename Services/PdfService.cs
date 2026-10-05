using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using iText.Forms;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Navigation;
using iText.Kernel.Utils;
using iText.Kernel.XMP;

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
            string folderPath = Path.GetDirectoryName(sourcePath);
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
                PdfOutline rootOutline = null;
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

        public void RotatePdf(string sourcePath, string outputPath, string pageSpec, int degrees)
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
                    ISet<int> pagesToRotate = pageSpec == null
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

        private static string ValueOrNull(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        public ISet<int> ParsePageRanges(string spec, int totalPages)
        {
            var pages = new SortedSet<int>();

            foreach (var rawToken in spec.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = rawToken.Trim();
                int start, end;

                int dash = token.IndexOf('-');
                if (dash < 0)
                {
                    if (!int.TryParse(token, out start))
                        throw new InvalidOperationException($"\"{token}\" is not a valid page number.");
                    end = start;
                }
                else
                {
                    if (!int.TryParse(token.Substring(0, dash), out start) || !int.TryParse(token.Substring(dash + 1), out end))
                        throw new InvalidOperationException($"\"{token}\" is not a valid page range. Use the form 5-7.");
                    if (end < start)
                        throw new InvalidOperationException($"\"{token}\" is not a valid page range: the end page is before the start page.");
                }

                if (start < 1 || end > totalPages)
                    throw new InvalidOperationException($"Page range \"{token}\" is out of range. The document has {totalPages} pages.");

                for (int p = start; p <= end; p++)
                    pages.Add(p);
            }

            if (pages.Count == 0)
                throw new InvalidOperationException("Please enter at least one page.");

            return pages;
        }
    }
}