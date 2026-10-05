#nullable enable
namespace PDFManager.Services
{
    // The document properties the metadata tool edits. Empty strings mean "not set".
    public record PdfMetadata(string Title, string Author, string Subject, string Keywords);
}
