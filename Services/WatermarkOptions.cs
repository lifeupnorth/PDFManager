#nullable enable
namespace PDFManager.Services
{
    // Text is drawn over each page, centred. Diagonal runs it corner to corner, otherwise it is
    // horizontal. A null PageSpec means every page; otherwise it is read by ParsePageRanges.
    public record WatermarkOptions(string Text, bool Diagonal, int OpacityPercent, string? PageSpec);
}
