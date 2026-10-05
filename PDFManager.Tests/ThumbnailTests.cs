using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    public class ThumbnailTests
    {
        private const string Folder = @"C:\Docs";
        private static readonly string Doc = Path.Combine(Folder, "Doc.pdf");

        private static string Marks(PageThumbnailsViewModel thumbnails) =>
            string.Concat(thumbnails.Pages.Select(p => p.IsSelected ? "x" : "."));

        // --- Page list formatting ---

        [Theory]
        [InlineData(new int[0], "")]
        [InlineData(new[] { 4 }, "4")]
        [InlineData(new[] { 1, 2, 3, 5, 7, 8 }, "1-3, 5, 7-8")]
        [InlineData(new[] { 8, 7, 1, 3, 2, 3 }, "1-3, 7-8")]
        public void Format_writes_the_shortest_page_list(int[] pages, string expected)
        {
            Assert.Equal(expected, PageRanges.Format(pages));
        }

        [Fact]
        public void TryParse_returns_null_for_a_list_still_being_typed()
        {
            Assert.Null(PageRanges.TryParse("1-", 10));
            Assert.Equal(new[] { 1, 2 }, PageRanges.TryParse("1-2", 10));
        }

        // --- The thumbnail strip ---

        [Fact]
        public async Task Loads_a_placeholder_per_page_then_their_images()
        {
            var thumbnails = new PageThumbnailsViewModel(new FakePageRenderer { PageCount = _ => 4 });

            await thumbnails.LoadAsync(Doc);

            Assert.Equal(new[] { 1, 2, 3, 4 }, thumbnails.Pages.Select(p => p.PageNumber));
            Assert.All(thumbnails.Pages, p => Assert.NotNull(p.Image));
            Assert.Equal("", thumbnails.StatusText);
        }

        [Fact]
        public async Task Explains_when_a_file_cannot_be_previewed()
        {
            var thumbnails = new PageThumbnailsViewModel(new FakePageRenderer { PageCount = _ => throw new IOException("locked") });

            await thumbnails.LoadAsync(Doc);

            Assert.Empty(thumbnails.Pages);
            Assert.Equal("Page previews are not available for this file.", thumbnails.StatusText);
        }

        [Fact]
        public async Task A_slow_file_replaced_by_another_never_shows_its_pages()
        {
            using var firstMayOpen = new ManualResetEventSlim();
            string first = Path.Combine(Folder, "Slow.pdf");
            var thumbnails = new PageThumbnailsViewModel(new FakePageRenderer
            {
                PageCount = path =>
                {
                    if (path == first)
                        firstMayOpen.Wait();
                    return path == first ? 9 : 2;
                }
            });

            Task slow = thumbnails.LoadAsync(first);
            await thumbnails.LoadAsync(Doc);
            firstMayOpen.Set();
            await slow;

            Assert.Equal(2, thumbnails.Pages.Count);
        }

        [Fact]
        public void Gives_each_page_a_screen_reader_name()
        {
            var page = new PageThumbnail(3);
            Assert.Equal("Page 3", page.AccessibleName);

            page.IsSelected = true;
            page.IsSplitAfter = true;

            Assert.Equal("Page 3, selected, split after this page", page.AccessibleName);
        }

        // --- Extract: clicking and typing stay in step ---

        private static async Task<ExtractViewModel> LoadedExtract(int pages)
        {
            var vm = new ExtractViewModel(new FakePdfService { PageCount = _ => pages }, new FakePageRenderer { PageCount = _ => pages });
            await vm.UpdateFileAsync(Doc);
            await vm.Thumbnails.Loading;
            return vm;
        }

        [Fact]
        public async Task Extract_clicking_pages_writes_the_page_list()
        {
            var vm = await LoadedExtract(6);

            foreach (int page in new[] { 2, 3, 6, 1 })
                vm.Thumbnails.PageClickCommand.Execute(vm.Thumbnails.Pages[page - 1]);

            Assert.Equal("1-3, 6", vm.PageSpec);
            Assert.Equal("xxx..x", Marks(vm.Thumbnails));

            vm.Thumbnails.PageClickCommand.Execute(vm.Thumbnails.Pages[1]); // deselect page 2
            Assert.Equal("1, 3, 6", vm.PageSpec);
        }

        [Fact]
        public async Task Extract_typing_a_page_list_selects_its_pages_and_ignores_a_half_typed_one()
        {
            var vm = await LoadedExtract(6);

            vm.PageSpec = "2, 4-5";
            Assert.Equal(".x.xx.", Marks(vm.Thumbnails));

            vm.PageSpec = "2, 4-"; // still typing
            Assert.Equal(".x.xx.", Marks(vm.Thumbnails));

            vm.PageSpec = "";
            Assert.Equal("......", Marks(vm.Thumbnails));
        }

        [Fact]
        public async Task Extract_applies_a_list_typed_before_the_pages_load()
        {
            var vm = new ExtractViewModel(new FakePdfService { PageCount = _ => 4 }, new FakePageRenderer { PageCount = _ => 4 });
            vm.PageSpec = "3-4";

            await vm.UpdateFileAsync(Doc);
            await vm.Thumbnails.Loading;

            Assert.Equal("..xx", Marks(vm.Thumbnails));
        }

        // --- Rotate: the previews turn ---

        private static async Task<RotateViewModel> LoadedRotate(int pages)
        {
            var vm = new RotateViewModel(new FakePdfService { PageCount = _ => pages }, new FakePageRenderer { PageCount = _ => pages });
            await vm.UpdateFileAsync(Doc);
            await vm.Thumbnails.Loading;
            return vm;
        }

        private static string Rotations(RotateViewModel vm) => string.Join(",", vm.Thumbnails.Pages.Select(p => p.PreviewRotation));

        [Fact]
        public async Task Rotate_with_all_pages_turns_every_preview_and_highlights_none()
        {
            var vm = await LoadedRotate(3);

            Assert.Equal("90,90,90", Rotations(vm));
            Assert.Equal("...", Marks(vm.Thumbnails));

            vm.RotationAngle = 180;
            Assert.Equal("180,180,180", Rotations(vm));
        }

        [Fact]
        public async Task Rotate_clicking_a_page_switches_to_that_page_only()
        {
            var vm = await LoadedRotate(3);

            vm.Thumbnails.PageClickCommand.Execute(vm.Thumbnails.Pages[1]);

            Assert.False(vm.RotateAllPages);
            Assert.Equal("2", vm.RotatePageSpec);
            Assert.Equal(".x.", Marks(vm.Thumbnails));
            Assert.Equal("0,90,0", Rotations(vm));
        }

        [Fact]
        public async Task Rotate_typed_pages_turn_their_previews()
        {
            var vm = await LoadedRotate(4);
            vm.RotateAllPages = false;

            vm.RotatePageSpec = "1, 4";
            vm.RotationAngle = 270;

            Assert.Equal("270,0,0,270", Rotations(vm));

            vm.RotateAllPages = true;
            Assert.Equal("270,270,270,270", Rotations(vm));
            Assert.Equal("....", Marks(vm.Thumbnails));
        }

        // --- Watermark ---

        [Fact]
        public async Task Watermark_clicking_a_page_switches_off_all_pages()
        {
            var vm = new WatermarkViewModel(new FakePdfService { PageCount = _ => 3 }, new FakePageRenderer { PageCount = _ => 3 });
            await vm.UpdateFileAsync(Doc);
            await vm.Thumbnails.Loading;

            vm.Thumbnails.PageClickCommand.Execute(vm.Thumbnails.Pages[2]);

            Assert.False(vm.AllPages);
            Assert.Equal("3", vm.PageSpec);
            Assert.Equal("..x", Marks(vm.Thumbnails));
        }

        // --- Split: click to choose the split point ---

        private static async Task<SplitViewModel> LoadedSplit(int pages)
        {
            var vm = new SplitViewModel(new FakePdfService { PageCount = _ => pages }, new FakePageRenderer { PageCount = _ => pages }, new FakeDialogService());
            await vm.UpdateFileAsync(Doc);
            await vm.Thumbnails.Loading;
            return vm;
        }

        private static string SplitMarks(SplitViewModel vm) => string.Concat(vm.Thumbnails.Pages.Select(p => p.IsSplitAfter ? "|" : "."));

        [Fact]
        public async Task Split_marks_the_default_split_point_once_the_pages_load()
        {
            var vm = await LoadedSplit(4);

            Assert.Equal("|...", SplitMarks(vm)); // SplitPageNum starts at 1
        }

        [Fact]
        public async Task Split_clicking_a_page_splits_after_it()
        {
            var vm = await LoadedSplit(5);

            vm.Thumbnails.PageClickCommand.Execute(vm.Thumbnails.Pages[2]);

            Assert.Equal("3", vm.SplitPageNum);
            Assert.Equal("..|..", SplitMarks(vm));
        }

        [Fact]
        public async Task Split_ignores_a_click_on_the_last_page_and_marks_typed_numbers()
        {
            var vm = await LoadedSplit(5);
            vm.SplitPageNum = "2";

            vm.Thumbnails.PageClickCommand.Execute(vm.Thumbnails.Pages[4]);
            Assert.Equal("2", vm.SplitPageNum);
            Assert.Equal(".|...", SplitMarks(vm));

            vm.SplitPageNum = "9"; // out of range: no marker
            Assert.Equal(".....", SplitMarks(vm));
        }
    }
}
