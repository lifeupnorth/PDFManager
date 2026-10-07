using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Navigation;
using PDFManager.Services;
using PDFManager.ViewModels;
using Path = System.IO.Path;

namespace PDFManager.Tests
{
    // Reorder pages: the service against real PDFs, then the view model's moves and checks.
    // Its shared run, open and browse paths are in ToolDialogFlowTests
    public class ReorderTests : IDisposable
    {
        private readonly PdfService _service = new PdfService();
        private readonly TestPdfs _pdfs = new TestPdfs();
        private readonly FakeDialogService _dialogs = new FakeDialogService();

        public void Dispose() => _pdfs.Dispose();

        // --- The service ---

        [Fact]
        public void ReorderPages_puts_the_pages_in_the_given_order()
        {
            string source = _pdfs.CreatePages("four.pdf", 4); // widths 100-103 tell the pages apart
            string output = _pdfs.PathFor("reordered.pdf");

            _service.ReorderPages(source, output, new[] { 3, 1, 4, 2 });

            Assert.Equal(new float[] { 102, 100, 103, 101 }, TestPdfs.PageWidths(output));
            Assert.Equal(new float[] { 100, 101, 102, 103 }, TestPdfs.PageWidths(source));
        }

        [Fact]
        public void ReorderPages_keeps_each_page_rotation()
        {
            string source = _pdfs.Create("rotated.pdf", new float[] { 100, 101 }, rotation: 90);
            string output = _pdfs.PathFor("reordered.pdf");

            _service.ReorderPages(source, output, new[] { 2, 1 });

            Assert.Equal(new float[] { 101, 100 }, TestPdfs.PageWidths(output));
            Assert.Equal(new[] { 90, 90 }, TestPdfs.PageRotations(output));
        }

        // Pages move within the document, so bookmarks still lead to the same page wherever it
        // ends up, and the document keeps its properties
        [Fact]
        public void ReorderPages_keeps_bookmarks_on_their_pages_and_the_document_properties()
        {
            string source = _pdfs.PathFor("chapters.pdf");
            using (var doc = new PdfDocument(new PdfWriter(source)))
            {
                var outlines = doc.GetOutlines(false);
                for (int i = 0; i < 3; i++)
                {
                    var page = doc.AddNewPage(new PageSize(100 + i, 200));
                    outlines.AddOutline($"Chapter {i + 1}").AddDestination(PdfExplicitDestination.CreateFit(page));
                }
                doc.GetDocumentInfo().SetTitle("Annual report");
            }
            string output = _pdfs.PathFor("reordered.pdf");

            _service.ReorderPages(source, output, new[] { 3, 1, 2 });

            using var result = new PdfDocument(new PdfReader(output));
            var bookmarks = result.GetOutlines(false).GetAllChildren();
            Assert.Equal(new[] { "Chapter 1", "Chapter 2", "Chapter 3" }, bookmarks.Select(b => b.GetTitle()));
            var pageOfEach = bookmarks.Select(b => result.GetPageNumber((PdfDictionary)((PdfArray)b.GetDestination().GetPdfObject()).Get(0)));
            Assert.Equal(new[] { 2, 3, 1 }, pageOfEach);
            Assert.Equal("Annual report", result.GetDocumentInfo().GetTitle());
        }

        public static TheoryData<int[]> OrdersThatDontMatch() => new TheoryData<int[]>
        {
            new[] { 1, 2 },          // a page left out
            new[] { 1, 2, 3, 3 },    // one page twice
            new[] { 3, 2, 1, 1 },
            new[] { 1, 2, 4 },       // no such page
            new[] { 0, 1, 2 },
        };

        [Theory]
        [MemberData(nameof(OrdersThatDontMatch))]
        public void ReorderPages_refuses_an_order_that_does_not_list_every_page_once_and_leaves_the_output_alone(int[] order)
        {
            string source = _pdfs.CreatePages("three.pdf", 3);
            string output = _pdfs.CreateNotAPdf("reordered.pdf");
            string before = File.ReadAllText(output);

            var error = Assert.Throws<InvalidOperationException>(() => _service.ReorderPages(source, output, order));

            Assert.Equal("The new order doesn't match the pages in this PDF. Please open the file again.", error.Message);
            Assert.Equal(before, File.ReadAllText(output));
            Assert.Empty(Directory.EnumerateFiles(_pdfs.Folder, "*.tmp"));
        }

        [Fact]
        public void ReorderPages_of_a_password_protected_PDF_throws_BadPasswordException()
        {
            string source = _pdfs.CreateEncrypted("locked.pdf");

            Assert.Throws<iText.Kernel.Exceptions.BadPasswordException>(() => _service.ReorderPages(source, _pdfs.PathFor("out.pdf"), new[] { 2, 1 }));
        }

        [Theory]
        [InlineData(new int[0], "")]
        [InlineData(new[] { 1, 2, 3 }, "1-3")]
        [InlineData(new[] { 3, 1, 2, 4, 5, 6 }, "3, 1-2, 4-6")]
        [InlineData(new[] { 3, 2, 1 }, "3, 2, 1")]
        [InlineData(new[] { 2, 4, 1, 3, 5 }, "2, 4, 1, 3, 5")]
        public void FormatSequence_keeps_the_order_and_shortens_ascending_runs(int[] pages, string expected)
        {
            Assert.Equal(expected, PageRanges.FormatSequence(pages));
        }

        // --- The view model's moves ---

        private async Task<ReorderViewModel> Loaded(int pageCount, FakePdfService? pdf = null)
        {
            var vm = new ReorderViewModel(pdf ?? new FakePdfService(), new FakePageRenderer { PageCount = _ => pageCount }, _dialogs)
            {
                SourcePath = _pdfs.CreatePages("source.pdf", 1),
                OutputPath = _pdfs.PathFor("result.pdf")
            };
            await vm.Thumbnails.LoadAsync(vm.SourcePath);
            return vm;
        }

        // Selects pages the way a click on each thumbnail does
        private static void Click(ReorderViewModel vm, params int[] pageNumbers)
        {
            foreach (int number in pageNumbers)
                vm.Thumbnails.PageClickCommand.Execute(vm.Thumbnails.Pages.Single(p => p.PageNumber == number));
        }

        private static PageThumbnail Page(ReorderViewModel vm, int pageNumber) => vm.Thumbnails.Pages.Single(p => p.PageNumber == pageNumber);

        [Fact]
        public async Task A_loaded_file_starts_in_its_original_order_with_nothing_selected()
        {
            var vm = await Loaded(4);

            Assert.Equal(new[] { 1, 2, 3, 4 }, vm.PageOrder);
            Assert.False(vm.IsOrderChanged);
            Assert.Equal("Original order", vm.OrderSummary);
            Assert.Empty(vm.Thumbnails.SelectedPages);
        }

        [Fact]
        public async Task A_click_selects_a_page_and_a_second_click_deselects_it()
        {
            var vm = await Loaded(3);

            Click(vm, 2);
            Assert.Equal(new[] { 2 }, vm.Thumbnails.SelectedPages);
            Click(vm, 2);
            Assert.Empty(vm.Thumbnails.SelectedPages);
        }

        [Fact]
        public async Task Move_to_start_and_end_take_the_selected_pages_in_their_order()
        {
            var vm = await Loaded(6);
            Click(vm, 5, 3);

            vm.MoveToStart();
            Assert.Equal(new[] { 3, 5, 1, 2, 4, 6 }, vm.PageOrder);

            vm.MoveToEnd();
            Assert.Equal(new[] { 1, 2, 4, 6, 3, 5 }, vm.PageOrder);
            Assert.Equal(new[] { 3, 5 }, vm.Thumbnails.SelectedPages); // still selected, for another move
        }

        [Fact]
        public async Task Move_earlier_and_later_shift_each_selected_page_one_place()
        {
            var vm = await Loaded(5);
            Click(vm, 2, 4);

            vm.MoveEarlier();
            Assert.Equal(new[] { 2, 1, 4, 3, 5 }, vm.PageOrder);
            vm.MoveEarlier(); // 2 is already first; 4 keeps going
            Assert.Equal(new[] { 2, 4, 1, 3, 5 }, vm.PageOrder);
            vm.MoveEarlier(); // both at the front: nothing moves
            Assert.Equal(new[] { 2, 4, 1, 3, 5 }, vm.PageOrder);

            vm.MoveLater();
            Assert.Equal(new[] { 1, 2, 4, 3, 5 }, vm.PageOrder);
        }

        [Fact]
        public async Task Selected_neighbours_move_as_a_block()
        {
            var vm = await Loaded(5);
            Click(vm, 3, 4);

            vm.MoveEarlier();
            Assert.Equal(new[] { 1, 3, 4, 2, 5 }, vm.PageOrder);
            vm.MoveLater();
            vm.MoveLater();
            Assert.Equal(new[] { 1, 2, 5, 3, 4 }, vm.PageOrder);
        }

        // Dragging: the insert position counts places in the order shown when the drag started
        [Theory]
        [InlineData(new[] { 1 }, 3, new[] { 2, 3, 1, 4, 5 })]       // page 1 dropped after page 3
        [InlineData(new[] { 5 }, 0, new[] { 5, 1, 2, 3, 4 })]       // to the front
        [InlineData(new[] { 2 }, 5, new[] { 1, 3, 4, 5, 2 })]       // to the end
        [InlineData(new[] { 4, 2 }, 0, new[] { 2, 4, 1, 3, 5 })]    // several keep their order
        [InlineData(new[] { 2, 4 }, 3, new[] { 1, 3, 2, 4, 5 })]    // dropped between them
        [InlineData(new[] { 3 }, 2, new[] { 1, 2, 3, 4, 5 })]       // onto its own place
        [InlineData(new[] { 3 }, 3, new[] { 1, 2, 3, 4, 5 })]
        public async Task Dragged_pages_land_at_the_drop_position(int[] dragged, int insertIndex, int[] expected)
        {
            var vm = await Loaded(5);

            vm.MovePages(dragged.Select(n => Page(vm, n)).ToList(), insertIndex);

            Assert.Equal(expected, vm.PageOrder);
        }

        [Fact]
        public async Task Moves_keep_the_same_thumbnails_with_their_images()
        {
            var vm = await Loaded(3);
            await vm.Thumbnails.Loading;
            var third = Page(vm, 3);

            vm.Reverse();

            Assert.Same(third, vm.Thumbnails.Pages[0]);
            Assert.NotNull(third.Image);
        }

        [Fact]
        public async Task Reverse_and_reset_order_and_the_summary_says_what_changed()
        {
            var vm = await Loaded(6);

            vm.Reverse();
            Assert.Equal(new[] { 6, 5, 4, 3, 2, 1 }, vm.PageOrder);
            Assert.Equal("New order: 6, 5, 4, 3, 2, 1", vm.OrderSummary);

            vm.ResetOrder();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, vm.PageOrder);
            Assert.Equal("Original order", vm.OrderSummary);

            Click(vm, 4);
            vm.MoveToStart();
            Assert.True(vm.IsOrderChanged);
            Assert.Equal("New order: 4, 1-3, 5-6", vm.OrderSummary);
        }

        [Fact]
        public async Task The_buttons_are_enabled_only_when_they_can_do_something()
        {
            var vm = await Loaded(3);

            Assert.False(vm.MoveToStartCommand.CanExecute(null));
            Assert.False(vm.MoveEarlierCommand.CanExecute(null));
            Assert.False(vm.ResetOrderCommand.CanExecute(null));
            Assert.True(vm.ReverseCommand.CanExecute(null));

            Click(vm, 2);
            vm.MoveToEnd();
            Assert.True(vm.MoveToStartCommand.CanExecute(null));
            Assert.True(vm.MoveLaterCommand.CanExecute(null));
            Assert.True(vm.ResetOrderCommand.CanExecute(null));

            vm.IsBusy = true;
            Assert.False(vm.MoveToStartCommand.CanExecute(null));
            Assert.False(vm.ReverseCommand.CanExecute(null));
            Assert.False(vm.ResetOrderCommand.CanExecute(null));
        }

        [Fact]
        public async Task Reverse_needs_two_pages()
        {
            var vm = await Loaded(1);

            Assert.False(vm.ReverseCommand.CanExecute(null));
        }

        [Fact]
        public async Task Opening_another_file_starts_over_in_its_order()
        {
            var vm = await Loaded(4);
            Click(vm, 4);
            vm.MoveToStart();

            await vm.UpdateFileAsync(_pdfs.CreatePages("other.pdf", 1));
            await vm.Thumbnails.Loading;

            Assert.Equal(new[] { 1, 2, 3, 4 }, vm.PageOrder);
            Assert.Empty(vm.Thumbnails.SelectedPages);
            Assert.EndsWith("other_reordered.pdf", vm.OutputPath);
        }

        // --- Saving ---

        [Fact]
        public async Task Saving_passes_the_new_order_to_the_service()
        {
            var pdf = new FakePdfService { Reorder = (_, _) => { } };
            var vm = await Loaded(3, pdf);
            Click(vm, 3);
            vm.MoveToStart();

            await vm.ReorderAsync();

            Assert.Equal(new[] { 3, 1, 2 }, pdf.ReorderedPages);
            Assert.StartsWith("Pages reordered successfully!", Assert.Single(_dialogs.Calls).Message);
        }

        [Fact]
        public async Task Saving_the_original_order_asks_for_a_change_first()
        {
            var pdf = new FakePdfService { Reorder = (_, _) => throw new InvalidOperationException("should not run") };
            var vm = await Loaded(3, pdf);

            await vm.ReorderAsync();

            Assert.Equal(new DialogCall(false, "The pages are still in their original order. Drag pages to a new place, or select pages and use the move buttons.", "Validation Error", DialogIcon.Warning),
                Assert.Single(_dialogs.Calls));
            Assert.Null(pdf.ReorderedPages);
        }

        [Fact]
        public async Task Saving_without_pages_explains_that_they_could_not_be_loaded()
        {
            var pdf = new FakePdfService { Reorder = (_, _) => throw new InvalidOperationException("should not run") };
            var vm = new ReorderViewModel(pdf, new FakePageRenderer(), _dialogs)
            {
                SourcePath = _pdfs.CreatePages("source.pdf", 1),
                OutputPath = _pdfs.PathFor("result.pdf")
            };

            await vm.ReorderAsync();

            Assert.Equal(new DialogCall(false, "The pages of this PDF could not be loaded, so they cannot be reordered. It may be password-protected or damaged.", "Validation Error", DialogIcon.Warning),
                Assert.Single(_dialogs.Calls));
            Assert.Null(pdf.ReorderedPages);
        }

        [Fact]
        public async Task Opening_a_file_reports_its_page_count()
        {
            var vm = new ReorderViewModel(new FakePdfService { PageCount = _ => 7 }, new FakePageRenderer(), _dialogs);

            await vm.UpdateFileAsync(_pdfs.CreatePages("seven.pdf", 1));

            Assert.Equal("Document has 7 pages.", vm.PageCountInfo);
        }
    }
}
