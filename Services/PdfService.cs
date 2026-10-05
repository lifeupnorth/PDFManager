using System;
using System.Collections.Generic;
using System.IO;
using iText.Forms;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Navigation;
using iText.Kernel.Utils;

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

        public void MergePdfs(string outputPath, List<string> sourceFiles)
        {
            using (var resultDoc = new PdfDocument(new PdfWriter(outputPath)))
            {
                var formCopier = new PdfPageFormCopier();
                int pageIndex = 1;

                foreach (var filePath in sourceFiles)
                {
                    using (var srcDoc = new PdfDocument(new PdfReader(filePath)))
                    {
                        int numberOfPages = srcDoc.GetNumberOfPages();
                        srcDoc.CopyPagesTo(1, numberOfPages, resultDoc, formCopier);

                        var rootOutline = resultDoc.GetOutlines(false);
                        var outline = rootOutline.AddOutline(Path.GetFileNameWithoutExtension(filePath));
                        outline.AddDestination(PdfExplicitDestination.CreateFit(resultDoc.GetPage(pageIndex)));

                        pageIndex += numberOfPages;
                    }
                }
            }
        }

        public void RotatePdf(string sourcePath, string outputPath, string pageSpec, int degrees, int totalPages)
        {
            ISet<int> pagesToRotate = ParsePageRanges(pageSpec, totalPages);

            using (var pdfDoc = new PdfDocument(new PdfReader(sourcePath), new PdfWriter(outputPath)))
            {
                for (int i = 1; i <= totalPages; i++)
                {
                    if (pagesToRotate.Contains(i))
                    {
                        PdfPage page = pdfDoc.GetPage(i);
                        page.SetRotation((page.GetRotation() + degrees) % 360);
                    }
                }
            }
        }

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
                throw new InvalidOperationException("Please enter at least one page to rotate.");

            return pages;
        }
    }
}