using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    // Covers the view-model paths that never show a MessageBox: the page-count hints
    // and merge-list de-duplication
    public class ViewModelTests
    {
        private const string Folder = @"C:\Docs";

        [Theory]
        [InlineData(5, "Enter a page from 1 to 4. The document has 5 pages.")]
        [InlineData(2, "Enter a page from 1 to 1. The document has 2 pages.")]
        [InlineData(1, "This document has only 1 page and cannot be split.")]
        public async Task Split_hint_describes_the_valid_split_pages(int pages, string hint)
        {
            var vm = new SplitViewModel(new FakePdfService { PageCount = _ => pages });

            await vm.UpdateFileAsync(Path.Combine(Folder, "Report.pdf"));

            Assert.Equal(hint, vm.PageCountInfo);
            Assert.Equal(Folder, vm.OutputFolder);
        }

        [Fact]
        public async Task Split_hint_flags_a_password_protected_file()
        {
            var vm = new SplitViewModel(new FakePdfService { PageCount = _ => throw new iText.Kernel.Exceptions.BadPasswordException("locked") });

            await vm.UpdateFileAsync(Path.Combine(Folder, "Locked.pdf"));

            Assert.Equal("This PDF is password-protected and cannot be split.", vm.PageCountInfo);
        }

        [Fact]
        public async Task Split_hint_reports_an_unreadable_file()
        {
            var vm = new SplitViewModel(new FakePdfService { PageCount = _ => throw new IOException("gone") });

            await vm.UpdateFileAsync(Path.Combine(Folder, "Broken.pdf"));

            Assert.Equal("Unable to read the page count.", vm.PageCountInfo);
        }

        [Fact]
        public async Task Split_hint_ignores_a_slow_result_for_a_file_that_was_replaced()
        {
            using var firstMayFinish = new ManualResetEventSlim();
            string first = Path.Combine(Folder, "Slow.pdf"), second = Path.Combine(Folder, "Fast.pdf");
            var vm = new SplitViewModel(new FakePdfService
            {
                PageCount = path =>
                {
                    if (path == first)
                        firstMayFinish.Wait();
                    return path == first ? 9 : 3;
                }
            });

            Task slow = vm.UpdateFileAsync(first);
            await vm.UpdateFileAsync(second);
            firstMayFinish.Set();
            await slow;

            Assert.Equal(second, vm.SourcePath);
            Assert.Equal("Enter a page from 1 to 2. The document has 3 pages.", vm.PageCountInfo);
        }

        [Theory]
        [InlineData(1, "Document has 1 page. Example: 1, 3, 5-7")]
        [InlineData(4, "Document has 4 pages. Example: 1, 3, 5-7")]
        public async Task Rotate_hint_gives_the_page_count_and_an_example(int pages, string hint)
        {
            var vm = new RotateViewModel(new FakePdfService { PageCount = _ => pages });

            await vm.UpdateFileAsync(Path.Combine(Folder, "Scan.pdf"));

            Assert.Equal(hint, vm.PageCountInfo);
        }

        [Fact]
        public async Task Rotate_suggests_an_output_next_to_the_source()
        {
            var vm = new RotateViewModel(new FakePdfService { PageCount = _ => 1 });

            await vm.UpdateFileAsync(Path.Combine(Folder, "Scan.pdf"));

            Assert.Equal(Path.Combine(Folder, "Scan_rotated.pdf"), vm.OutputPath);
        }

        [Fact]
        public async Task Rotate_hint_flags_a_password_protected_file()
        {
            var vm = new RotateViewModel(new FakePdfService { PageCount = _ => throw new iText.Kernel.Exceptions.BadPasswordException("locked") });

            await vm.UpdateFileAsync(Path.Combine(Folder, "Locked.pdf"));

            Assert.Equal("This PDF is password-protected and cannot be rotated.", vm.PageCountInfo);
        }

        [Fact]
        public async Task Merge_adds_files_in_order_and_skips_ones_already_listed_ignoring_case()
        {
            var vm = new MergeViewModel(new FakePdfService { PageCount = _ => 1 });
            string a = Path.Combine(Folder, "a.pdf"), b = Path.Combine(Folder, "b.pdf"), c = Path.Combine(Folder, "c.pdf");

            await vm.AddFilesAsync(new[] { b, a });
            await vm.AddFilesAsync(new[] { a.ToUpperInvariant(), c, c });

            Assert.Equal(new[] { b, a, c }, vm.MergeFiles);
            Assert.False(vm.IsAddingFiles);
        }

        private sealed class FakePdfService : IPdfService
        {
            public Func<string, int> PageCount { get; init; } = _ => 1;

            public int GetPageCount(string path) => PageCount(path);
            public string GetSplitDestPattern(string sourcePath) => throw new NotSupportedException();
            public void SplitPdf(string sourcePath, int splitPage, string folderPath) => throw new NotSupportedException();
            public void MergePdfs(string outputPath, List<string> sourceFiles) => throw new NotSupportedException();
            public void RotatePdf(string sourcePath, string outputPath, string pageSpec, int degrees) => throw new NotSupportedException();
            public ISet<int> ParsePageRanges(string spec, int totalPages) => throw new NotSupportedException();
        }
    }
}
