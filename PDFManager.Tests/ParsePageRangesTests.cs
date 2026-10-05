using System;
using PDFManager.Services;

namespace PDFManager.Tests
{
    public class ParsePageRangesTests
    {
        private readonly PdfService _service = new PdfService();

        [Theory]
        [InlineData("1", new[] { 1 })]
        [InlineData("1, 3, 5-7", new[] { 1, 3, 5, 6, 7 })]
        [InlineData("2;4 6", new[] { 2, 4, 6 })]
        [InlineData(" 3 ,  1 ", new[] { 1, 3 })]
        [InlineData("2, 1-3, 2", new[] { 1, 2, 3 })]
        [InlineData("4-4", new[] { 4 })]
        [InlineData("1-10", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
        public void Parses_pages_and_ranges_into_a_sorted_set(string spec, int[] expected)
        {
            Assert.Equal(expected, _service.ParsePageRanges(spec, totalPages: 10));
        }

        [Theory]
        [InlineData("abc", "\"abc\" is not a valid page number.")]
        [InlineData("1.5", "\"1.5\" is not a valid page number.")]
        [InlineData("3-", "\"3-\" is not a valid page range. Use the form 5-7.")]
        [InlineData("-3", "\"-3\" is not a valid page range. Use the form 5-7.")]
        [InlineData("a-b", "\"a-b\" is not a valid page range. Use the form 5-7.")]
        [InlineData("1-2-3", "\"1-2-3\" is not a valid page range. Use the form 5-7.")]
        [InlineData("5-3", "\"5-3\" is not a valid page range: the end page is before the start page.")]
        [InlineData("0", "Page range \"0\" is out of range. The document has 10 pages.")]
        [InlineData("11", "Page range \"11\" is out of range. The document has 10 pages.")]
        [InlineData("8-12", "Page range \"8-12\" is out of range. The document has 10 pages.")]
        [InlineData("", "Please enter at least one page.")]
        [InlineData(" , ; ", "Please enter at least one page.")]
        public void Rejects_invalid_specs_with_a_readable_message(string spec, string message)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => _service.ParsePageRanges(spec, totalPages: 10));
            Assert.Equal(message, ex.Message);
        }
    }
}
