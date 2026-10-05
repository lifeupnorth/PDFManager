using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    // The messages, confirmations and data-loss guards behind each tool's run button, checked
    // through FakeDialogService. Some paths write to ErrorLog, so this class shares the
    // ErrorLog.Folder redirect (and its collection) with ErrorLogTests.
    [Collection("Error log")]
    public class DialogFlowTests : IDisposable
    {
        private readonly string _originalLogFolder = ErrorLog.Folder;
        private readonly TestPdfs _files = new TestPdfs();
        private readonly FakeDialogService _dialogs = new FakeDialogService();

        public DialogFlowTests() => ErrorLog.Folder = Path.Combine(_files.Folder, "logs");

        public void Dispose()
        {
            ErrorLog.Folder = _originalLogFolder;
            _files.Dispose();
        }

        private bool AnythingLogged => Directory.Exists(ErrorLog.Folder) && Directory.EnumerateFiles(ErrorLog.Folder).Any();

        private string CreateFile(string name, string content = "original")
        {
            string path = _files.PathFor(name);
            File.WriteAllText(path, content);
            return path;
        }

        private DialogCall SingleCall() => Assert.Single(_dialogs.Calls);

        private static async Task Eventually(Func<bool> condition)
        {
            for (int i = 0; i < 200 && !condition(); i++)
                await Task.Delay(25);
            Assert.True(condition());
        }

        // --- Split ---

        private SplitViewModel NewSplit(Action<string, int, string>? split = null) =>
            new SplitViewModel(new FakePdfService { Split = split ?? ((_, _, _) => { }) }, new FakePageRenderer(), _dialogs);

        [Fact]
        public async Task Split_without_a_file_asks_for_one_and_does_not_split()
        {
            bool split = false;
            var vm = NewSplit((_, _, _) => split = true);

            await vm.SplitAsync();

            Assert.Equal(new DialogCall(false, "Please select a valid PDF file to split.", "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(split);
        }

        [Fact]
        public async Task Split_of_a_file_that_was_deleted_asks_for_a_valid_one()
        {
            bool split = false;
            var vm = NewSplit((_, _, _) => split = true);
            vm.SourcePath = _files.PathFor("Gone.pdf");

            await vm.SplitAsync();

            Assert.Equal("Please select a valid PDF file to split.", SingleCall().Message);
            Assert.False(split);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-2")]
        [InlineData("two")]
        [InlineData("")]
        public async Task Split_rejects_a_page_number_that_is_not_positive(string page)
        {
            bool split = false;
            var vm = NewSplit((_, _, _) => split = true);
            vm.SourcePath = CreateFile("Report.pdf");
            vm.SplitPageNum = page;

            await vm.SplitAsync();

            Assert.Equal(new DialogCall(false, "Please enter a valid page number greater than 0.", "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(split);
        }

        [Fact]
        public async Task Split_asks_before_overwriting_and_No_leaves_the_existing_part_alone()
        {
            bool split = false;
            var vm = NewSplit((_, _, _) => split = true);
            vm.SourcePath = CreateFile("Report.pdf");
            string part2 = CreateFile("Report_part2.pdf", "keep me");

            await vm.SplitAsync();

            var question = SingleCall();
            Assert.True(question.IsQuestion);
            Assert.Equal("Confirm Overwrite", question.Title);
            Assert.Equal(DialogIcon.Warning, question.Icon);
            Assert.Equal($"The following files already exist and will be overwritten:\n{part2}\n\nContinue?", question.Message);
            Assert.False(split);
            Assert.Equal("keep me", File.ReadAllText(part2));
        }

        [Fact]
        public async Task Split_lists_both_parts_when_both_exist_and_goes_ahead_on_Yes()
        {
            (string Source, int Page, string Folder)? split = null;
            var vm = NewSplit((source, page, folder) => split = (source, page, folder));
            string source = vm.SourcePath = CreateFile("Report.pdf");
            vm.SplitPageNum = "3";
            string part1 = CreateFile("Report_part1.pdf"), part2 = CreateFile("Report_part2.pdf");
            _dialogs.Answer = message => message.Contains("already exist");

            await vm.SplitAsync();

            Assert.Contains($"\n{part1}\n{part2}\n", _dialogs.Calls[0].Message);
            Assert.Equal((source, 3, _files.Folder), split!.Value);
        }

        [Fact]
        public async Task Split_success_offers_the_folder_and_Yes_selects_part_one()
        {
            bool busyWhileSplitting = false;
            SplitViewModel? vm = null;
            vm = NewSplit((_, _, _) => busyWhileSplitting = vm!.IsBusy);
            vm.SourcePath = CreateFile("Report.pdf");
            _dialogs.Answer = message => message.StartsWith("PDF split successfully!");

            await vm.SplitAsync();

            Assert.Equal(new DialogCall(true, "PDF split successfully!\n\nOpen the output folder?", "Success", DialogIcon.Information), SingleCall());
            Assert.Equal(new[] { _files.PathFor("Report_part1.pdf") }, _dialogs.ShownInExplorer);
            Assert.True(busyWhileSplitting);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task Split_success_with_No_opens_nothing()
        {
            var vm = NewSplit();
            vm.SourcePath = CreateFile("Report.pdf");

            await vm.SplitAsync();

            Assert.True(SingleCall().IsQuestion);
            Assert.Empty(_dialogs.ShownInExplorer);
        }

        public static IEnumerable<object[]> SplitFailures() => new[]
        {
            new object[] { new iText.Kernel.Exceptions.BadPasswordException("locked"), "This PDF is password-protected and cannot be split.", false },
            new object[] { new iText.IO.Exceptions.IOException("bad header"), "This file is not a valid PDF or is corrupted.", false },
            new object[] { new InvalidOperationException("The page is past the end."), "Error splitting PDF: The page is past the end.", false },
            new object[] { new NullReferenceException("boom"), "Error splitting PDF: boom", true },
        };

        [Theory]
        [MemberData(nameof(SplitFailures))]
        public async Task Split_failures_get_a_message_and_only_unexpected_ones_are_logged(Exception error, string message, bool logged)
        {
            var vm = NewSplit((_, _, _) => throw error);
            vm.SourcePath = CreateFile("Report.pdf");

            await vm.SplitAsync();

            Assert.Equal(new DialogCall(false, message, "Error", DialogIcon.Error), SingleCall());
            Assert.Equal(logged, AnythingLogged);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public void Split_open_loads_the_picked_file_and_a_cancelled_pick_changes_nothing()
        {
            var vm = NewSplit();
            string first = CreateFile("First.pdf");
            _dialogs.PickedFile = first;

            vm.OpenFileCommand.Execute(null);
            _dialogs.PickedFile = null;
            vm.OpenFileCommand.Execute(null);

            Assert.Equal(first, vm.SourcePath);
        }

        // --- Merge ---

        private MergeViewModel NewMerge(Action<string, List<string>>? merge = null, Func<string, int>? pageCount = null) =>
            new MergeViewModel(new FakePdfService { Merge = merge ?? ((_, _) => { }), PageCount = pageCount ?? (_ => 1) }, _dialogs);

        [Fact]
        public async Task Merge_lists_the_files_that_no_longer_exist_and_does_not_merge()
        {
            bool merged = false;
            var vm = NewMerge((_, _) => merged = true);
            string gone = _files.PathFor("Gone.pdf");
            vm.MergeFiles.Add(CreateFile("a.pdf"));
            vm.MergeFiles.Add(gone);
            vm.OutputPath = _files.PathFor("Merged.pdf");

            await vm.MergeAsync();

            Assert.Equal(new DialogCall(false, $"The following files no longer exist:\n{gone}", "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(merged);
        }

        [Theory]
        [InlineData("A.PDF")]
        [InlineData(@"sub\..\a.pdf")]
        public async Task Merge_refuses_an_output_that_is_one_of_the_inputs(string outputName)
        {
            bool merged = false;
            var vm = NewMerge((_, _) => merged = true);
            string a = CreateFile("a.pdf", "keep me");
            vm.MergeFiles.Add(a);
            vm.MergeFiles.Add(CreateFile("b.pdf"));
            vm.OutputPath = Path.Combine(_files.Folder, outputName);

            await vm.MergeAsync();

            Assert.Equal(new DialogCall(false, "The output file cannot be one of the files being merged. Please choose a different output path.", "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(merged);
            Assert.Equal("keep me", File.ReadAllText(a));
        }

        [Fact]
        public async Task Merge_asks_before_overwriting_and_No_leaves_the_output_alone()
        {
            bool merged = false;
            var vm = NewMerge((_, _) => merged = true);
            vm.MergeFiles.Add(CreateFile("a.pdf"));
            vm.MergeFiles.Add(CreateFile("b.pdf"));
            string output = vm.OutputPath = CreateFile("Merged.pdf", "keep me");

            await vm.MergeAsync();

            Assert.Equal(new DialogCall(true, $"\"{output}\" already exists and will be overwritten.\n\nContinue?", "Confirm Overwrite", DialogIcon.Warning), SingleCall());
            Assert.False(merged);
            Assert.Equal("keep me", File.ReadAllText(output));
        }

        [Fact]
        public async Task Merge_goes_ahead_on_Yes_with_the_files_in_list_order()
        {
            (string Output, List<string> Files)? merged = null;
            var vm = NewMerge((output, files) => merged = (output, files));
            string b = CreateFile("b.pdf"), a = CreateFile("a.pdf");
            vm.MergeFiles.Add(b);
            vm.MergeFiles.Add(a);
            string output = vm.OutputPath = CreateFile("Merged.pdf");
            _dialogs.Answer = message => message.Contains("already exists");

            await vm.MergeAsync();

            Assert.Equal(output, merged?.Output);
            Assert.Equal(new[] { b, a }, merged?.Files);
        }

        [Fact]
        public async Task Merge_success_offers_the_folder_and_Yes_selects_the_output()
        {
            var vm = NewMerge();
            vm.MergeFiles.Add(CreateFile("a.pdf"));
            vm.MergeFiles.Add(CreateFile("b.pdf"));
            string output = vm.OutputPath = _files.PathFor("Merged.pdf");
            _dialogs.Answer = message => message.StartsWith("PDFs merged successfully!");

            await vm.MergeAsync();

            Assert.Equal(new DialogCall(true, "PDFs merged successfully!\n\nOpen the output folder?", "Success", DialogIcon.Information), SingleCall());
            Assert.Equal(new[] { output }, _dialogs.ShownInExplorer);
            Assert.False(vm.IsBusy);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Merge_failure_deletes_the_partial_output_and_logs_only_unexpected_errors(bool unexpected)
        {
            Exception error = unexpected ? new NullReferenceException("boom") : new InvalidOperationException("boom");
            var vm = NewMerge((output, _) => { File.WriteAllText(output, "partial"); throw error; });
            vm.MergeFiles.Add(CreateFile("a.pdf"));
            vm.MergeFiles.Add(CreateFile("b.pdf"));
            string output = vm.OutputPath = _files.PathFor("Merged.pdf");

            await vm.MergeAsync();

            Assert.Equal(new DialogCall(false, "Error merging PDFs: boom", "Error", DialogIcon.Error), SingleCall());
            Assert.False(File.Exists(output));
            Assert.Equal(unexpected, AnythingLogged);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task Merge_add_reports_the_files_it_skipped_and_why()
        {
            string good = _files.PathFor("good.pdf"), locked = _files.PathFor("locked.pdf"), broken = _files.PathFor("broken.pdf");
            var vm = NewMerge(pageCount: path =>
                path == locked ? throw new iText.Kernel.Exceptions.BadPasswordException("locked")
                : path == broken ? throw new iText.IO.Exceptions.IOException("bad")
                : 2);

            await vm.AddFilesAsync(new[] { locked, good, broken });

            Assert.Equal(new[] { good }, vm.MergeFiles);
            Assert.Equal(new DialogCall(false, "The following files were not added:\nlocked.pdf (password-protected)\nbroken.pdf (not a valid PDF)", "Some Files Skipped", DialogIcon.Warning), SingleCall());
        }

        [Fact]
        public async Task Merge_add_of_valid_files_shows_no_message()
        {
            var vm = NewMerge();

            await vm.AddFilesAsync(new[] { _files.PathFor("a.pdf"), _files.PathFor("b.pdf") });

            Assert.Equal(2, vm.MergeFiles.Count);
            Assert.Empty(_dialogs.Calls);
        }

        [Fact]
        public async Task Merge_add_button_adds_the_picked_files()
        {
            var vm = NewMerge();
            string a = _files.PathFor("a.pdf"), b = _files.PathFor("b.pdf");
            _dialogs.PickedFiles = new[] { a, b };

            vm.AddFilesCommand.Execute(null);

            await Eventually(() => vm.MergeFiles.Count == 2 && !vm.IsAddingFiles);
            Assert.Equal(new[] { a, b }, vm.MergeFiles);
        }

        [Fact]
        public void Merge_browse_suggests_a_name_and_a_cancelled_pick_keeps_the_output()
        {
            var vm = NewMerge();
            string chosen = _files.PathFor("Chosen.pdf");
            _dialogs.PickedSaveFile = chosen;

            vm.BrowseOutputCommand.Execute(null);
            _dialogs.PickedSaveFile = null;
            vm.BrowseOutputCommand.Execute(null);

            Assert.Equal(chosen, vm.OutputPath);
            Assert.All(_dialogs.SaveFileRequests, request =>
                Assert.Equal(new SaveFileRequest("PDF Files (*.pdf)|*.pdf", ".pdf", "Merged File.pdf"), request));
        }
    }
}
