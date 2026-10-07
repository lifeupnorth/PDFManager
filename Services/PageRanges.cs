using System;
using System.Collections.Generic;
using System.Linq;

namespace PDFManager.Services
{
    /// <summary>
    /// Page lists as typed by the user, such as "1, 3, 5-7". Parse turns one into page numbers and
    /// Format turns page numbers back into the shortest such list.
    /// </summary>
    public static class PageRanges
    {
        public static ISet<int> Parse(string spec, int totalPages)
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

        // Parse for live input: null instead of an exception while the list is incomplete or wrong
        public static ISet<int>? TryParse(string spec, int totalPages)
        {
            try { return Parse(spec, totalPages); }
            catch (InvalidOperationException) { return null; }
        }

        // 1, 2, 3, 5, 7, 8 becomes "1-3, 5, 7-8"
        public static string Format(IEnumerable<int> pages) => FormatSequence(pages.Distinct().OrderBy(p => p));

        // Pages in the order given, with ascending runs shortened: 3, 1, 2, 4, 5, 6 becomes "3, 1-2, 4-6".
        // For describing a new page order, where Format would sort it away
        public static string FormatSequence(IEnumerable<int> pages)
        {
            var runs = new List<string>();
            int start = -1, previous = -1;
            foreach (int page in pages)
            {
                if (start < 0)
                    start = page;
                else if (page != previous + 1)
                {
                    runs.Add(start == previous ? $"{start}" : $"{start}-{previous}");
                    start = page;
                }
                previous = page;
            }
            if (start >= 0)
                runs.Add(start == previous ? $"{start}" : $"{start}-{previous}");
            return string.Join(", ", runs);
        }
    }
}
