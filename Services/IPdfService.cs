using System;
using System.Collections.Generic;
using System.IO;

namespace PDFManager.Services
{
    public interface IPdfService
    {
        int GetPageCount(string path);
        string GetSplitDestPattern(string sourcePath);
        void SplitPdf(string sourcePath, int splitPage, string folderPath);
        void MergePdfs(string outputPath, List<string> sourceFiles);
        void RotatePdf(string sourcePath, string outputPath, string pageSpec, int degrees);
        void ExtractPages(string sourcePath, string outputPath, string pageSpec);
        PdfMetadata ReadMetadata(string path);
        void WriteMetadata(string sourcePath, string outputPath, PdfMetadata metadata);
        ISet<int> ParsePageRanges(string spec, int totalPages);
    }
}