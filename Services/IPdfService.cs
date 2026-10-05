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
        PdfProtection GetProtection(string path);
        void ProtectPdf(string sourcePath, string outputPath, string password);
        void UnlockPdf(string sourcePath, string outputPath, string password);
        void AddWatermark(string sourcePath, string outputPath, WatermarkOptions options);
        void ImagesToPdf(IList<string> imagePaths, string outputPath, ImagePageOptions options);
        ISet<int> ParsePageRanges(string spec, int totalPages);
    }
}