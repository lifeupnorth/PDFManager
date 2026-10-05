using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        // --- Batch ---

        private BatchViewModel NewBatch(Action<string, string>? rotate = null) =>
            new BatchViewModel(new FakePdfService { Rotate = rotate ?? ((_, _) => { }) }, _dialogs);

        // Two PDFs listed for the default action, Rotate, saved next to the originals
        private BatchViewModel BatchOfTwo(Action<string, string>? rotate = null)
        {
            var batch = NewBatch(rotate);
            batch.AddPaths(new[] { CreateFile("a.pdf"), CreateFile("b.pdf") });
            return batch;
        }

        [Theory]
        [InlineData("watermark", "Please enter the watermark text.")]
        [InlineData("extract", "Please enter the pages to take from every file, for example: 1 or 1-3.")]
        [InlineData("protect", "Please enter a password.")]
        [InlineData("mismatch", "The passwords do not match. Please type the same password in both boxes.")]
        [InlineData("unlock", "Please enter the password of the PDFs.")]
        [InlineData("folder", "The output folder no longer exists. Please choose another one.")]
        public async Task Batch_checks_its_settings_before_processing_anything(string problem, string message)
        {
            var batch = BatchOfTwo();
            switch (problem)
            {
                case "watermark": batch.Action = BatchAction.Watermark; batch.WatermarkText = " "; break;
                case "extract": batch.Action = BatchAction.Extract; batch.PageSpec = "first"; break;
                case "protect": batch.Action = BatchAction.Protect; break;
                case "mismatch": batch.Action = BatchAction.Protect; batch.Password = "secret"; batch.ConfirmPassword = "Secret"; break;
                case "unlock": batch.Action = BatchAction.Unlock; break;
                case "folder": batch.OutputFolder = _files.PathFor("gone"); break;
            }

            await batch.RunBatchAsync();

            Assert.Equal(new DialogCall(false, message, "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.All(batch.Files, file => Assert.Equal(BatchFileStatus.Waiting, file.Status));
        }

        [Fact]
        public async Task Batch_asks_before_overwriting_and_No_processes_nothing()
        {
            int rotated = 0;
            var batch = BatchOfTwo((_, _) => rotated++);
            string existing = CreateFile("a_rotated.pdf", "keep me");

            await batch.RunBatchAsync();

            Assert.Equal(new DialogCall(true, "1 output file already exists and will be overwritten:\na_rotated.pdf\n\nContinue?", "Confirm Overwrite", DialogIcon.Warning), SingleCall());
            Assert.Equal(0, rotated);
            Assert.Equal("keep me", File.ReadAllText(existing));
        }

        [Fact]
        public async Task Batch_overwrite_question_names_eight_files_and_counts_the_rest()
        {
            var batch = NewBatch();
            for (int i = 1; i <= 10; i++)
            {
                batch.AddPaths(new[] { CreateFile($"f{i:D2}.pdf") });
                CreateFile($"f{i:D2}_rotated.pdf");
            }

            await batch.RunBatchAsync();

            string listed = string.Join("\n", Enumerable.Range(1, 8).Select(i => $"f{i:D2}_rotated.pdf"));
            Assert.Equal($"10 output files already exist and will be overwritten:\n{listed}\n...and 2 more\n\nContinue?", SingleCall().Message);
        }

        [Fact]
        public async Task Batch_finish_offers_the_folder_and_Yes_selects_the_first_output()
        {
            var processed = new List<(string, string)>();
            var batch = BatchOfTwo((source, output) => processed.Add((source, output)));
            _dialogs.Answer = message => message.StartsWith("2 of 2");

            await batch.RunBatchAsync();

            Assert.Equal(new DialogCall(true, "2 of 2 files processed successfully.\n\nOpen the output folder?", "Batch Finished", DialogIcon.Information), SingleCall());
            Assert.Equal(new[] { _files.PathFor("a_rotated.pdf") }, _dialogs.ShownInExplorer);
            Assert.Equal(new[] { (_files.PathFor("a.pdf"), _files.PathFor("a_rotated.pdf")), (_files.PathFor("b.pdf"), _files.PathFor("b_rotated.pdf")) }, processed);
        }

        [Fact]
        public async Task Batch_finish_with_a_failure_warns_and_still_offers_the_folder()
        {
            var batch = BatchOfTwo((source, _) => { if (source.EndsWith("b.pdf")) throw new InvalidOperationException("b is broken"); });

            await batch.RunBatchAsync();

            Assert.Equal(new DialogCall(true, "1 of 2 files processed successfully.\n1 failed. The list shows why.\n\nOpen the output folder?", "Batch Finished", DialogIcon.Warning), SingleCall());
            Assert.Empty(_dialogs.ShownInExplorer);
        }

        [Fact]
        public async Task Batch_where_every_file_failed_only_reports_it()
        {
            var batch = BatchOfTwo((_, _) => throw new InvalidOperationException("broken"));

            await batch.RunBatchAsync();

            Assert.Equal(new DialogCall(false, "0 of 2 files processed successfully.\n2 failed. The list shows why.", "Batch Finished", DialogIcon.Warning), SingleCall());
        }

        [Fact]
        public void Batch_add_buttons_add_the_picked_files_and_folders()
        {
            var batch = NewBatch();
            string a = CreateFile("a.pdf");
            string folder = _files.PathFor("more");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "c.pdf"), "");
            _dialogs.PickedFiles = new[] { a };
            _dialogs.PickedFolder = folder;

            batch.AddFilesCommand.Execute(null);
            batch.AddFolderCommand.Execute(null);
            _dialogs.PickedFiles = Array.Empty<string>();
            _dialogs.PickedFolder = null;
            batch.AddFilesCommand.Execute(null);
            batch.AddFolderCommand.Execute(null);

            Assert.Equal(new[] { a, Path.Combine(folder, "c.pdf") }, batch.Files.Select(f => f.Path));
            Assert.Equal("Choose a folder of PDFs", _dialogs.FolderRequests[0].Title);
            Assert.Empty(_dialogs.Calls);
        }

        [Fact]
        public void Batch_add_folder_says_so_when_the_folder_has_no_PDFs()
        {
            var batch = NewBatch();
            string folder = _files.PathFor("empty");
            Directory.CreateDirectory(folder);
            _dialogs.PickedFolder = folder;

            batch.AddFolderCommand.Execute(null);

            Assert.Equal(new DialogCall(false, "That folder has no PDF files.", "No Files Added", DialogIcon.Information), SingleCall());
            Assert.Empty(batch.Files);
        }

        [Fact]
        public void Batch_browse_starts_in_the_chosen_folder_and_a_cancelled_pick_keeps_it()
        {
            var batch = NewBatch();
            string chosen = _files.PathFor("out");
            _dialogs.PickedFolder = chosen;

            batch.BrowseOutputCommand.Execute(null);
            _dialogs.PickedFolder = null;
            batch.BrowseOutputCommand.Execute(null);

            Assert.Equal(chosen, batch.OutputFolder);
            Assert.Equal(new[]
            {
                new FolderRequest("Choose where to save the processed files", null),
                new FolderRequest("Choose where to save the processed files", chosen)
            }, _dialogs.FolderRequests);
        }

        // --- PDF to image ---

        // The real renderer and service: the run plans its files from the real page count
        private async Task<PdfToImageViewModel> PdfToImageOf(int pages)
        {
            var vm = new PdfToImageViewModel(new PdfService(), new WindowsPageRenderer(), _dialogs);
            await vm.UpdateFileAsync(_files.CreatePages("doc.pdf", pages));
            return vm;
        }

        [Fact]
        public async Task PdfToImage_of_specific_pages_asks_for_them()
        {
            var vm = await PdfToImageOf(3);
            vm.AllPages = false;
            vm.PageSpec = " ";

            await vm.RunConvertAsync();

            Assert.Equal(new DialogCall(false, "Please enter the pages to convert (for example: 1, 3, 5-7), or choose \"All pages\".", "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(Directory.Exists(vm.OutputFolder));
        }

        [Fact]
        public async Task PdfToImage_explains_a_page_list_it_cannot_use()
        {
            var vm = await PdfToImageOf(3);
            vm.AllPages = false;
            vm.PageSpec = "2-9";

            await vm.RunConvertAsync();

            Assert.Equal(new DialogCall(false, "Page range \"2-9\" is out of range. The document has 3 pages.", "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(Directory.Exists(vm.OutputFolder));
        }

        [Fact]
        public async Task PdfToImage_asks_before_overwriting_images_and_No_writes_nothing()
        {
            var vm = await PdfToImageOf(3);
            Directory.CreateDirectory(vm.OutputFolder);
            string first = Path.Combine(vm.OutputFolder, "doc_page1.png");
            File.WriteAllText(first, "keep me");

            await vm.RunConvertAsync();

            Assert.Equal(new DialogCall(true, $"1 image in \"{vm.OutputFolder}\" will be overwritten.\n\nContinue?", "Confirm Overwrite", DialogIcon.Warning), SingleCall());
            Assert.Equal("keep me", File.ReadAllText(first));
            Assert.Equal(new[] { first }, Directory.GetFiles(vm.OutputFolder));
        }

        [Fact]
        public async Task PdfToImage_success_offers_the_folder_and_Yes_selects_the_first_image()
        {
            var vm = await PdfToImageOf(3);
            _dialogs.Answer = message => message.StartsWith("Saved 3 images");

            await vm.RunConvertAsync();

            Assert.Equal(new DialogCall(true, $"Saved 3 images to:\n{vm.OutputFolder}\n\nOpen the folder?", "Success", DialogIcon.Information), SingleCall());
            Assert.Equal(new[] { Path.Combine(vm.OutputFolder, "doc_page1.png") }, _dialogs.ShownInExplorer);
        }

        [Fact]
        public async Task PdfToImage_reports_a_file_it_cannot_convert()
        {
            var vm = new PdfToImageViewModel(new PdfService(), new WindowsPageRenderer(), _dialogs);
            await vm.UpdateFileAsync(_files.CreateEncrypted("locked.pdf"));

            await vm.RunConvertAsync();

            var call = SingleCall();
            Assert.False(call.IsQuestion);
            Assert.Equal(("Error", DialogIcon.Error), (call.Title, call.Icon));
            Assert.Contains("password", call.Message);
            Assert.Empty(_dialogs.ShownInExplorer);
        }

        [Fact]
        public async Task PdfToImage_browse_starts_where_the_images_would_go()
        {
            var vm = await PdfToImageOf(1);
            string suggested = vm.OutputFolder; // doc_images, which does not exist yet
            string chosen = _files.PathFor("pictures");
            _dialogs.PickedFolder = chosen;

            vm.BrowseOutputCommand.Execute(null);
            _dialogs.PickedFolder = null;
            vm.BrowseOutputCommand.Execute(null);

            Assert.False(Directory.Exists(suggested));
            Assert.Equal(chosen, vm.OutputFolder);
            // Neither the suggested nor the chosen folder exists yet, so both times it starts in their parent
            Assert.Equal(new[]
            {
                new FolderRequest("Choose where to save the images", _files.Folder),
                new FolderRequest("Choose where to save the images", _files.Folder)
            }, _dialogs.FolderRequests);
        }

        [Fact]
        public async Task PdfToImage_open_loads_the_picked_file()
        {
            var vm = new PdfToImageViewModel(new PdfService(), new WindowsPageRenderer(), _dialogs);
            string source = _files.CreatePages("doc.pdf", 2);
            _dialogs.PickedFile = source;

            vm.OpenFileCommand.Execute(null);

            await Eventually(() => vm.SourcePath == source);
        }

        // --- Image to PDF ---

        private ImagesToPdfViewModel NewImagesToPdf(Action<IList<string>, string>? create = null) =>
            new ImagesToPdfViewModel(new FakePdfService { CreateFromImages = create ?? ((_, _) => { }) }, _dialogs);

        private string Png(string name)
        {
            string path = _files.PathFor(name);
            var source = BitmapSource.Create(4, 2, 96, 96, PixelFormats.Gray8, null, new byte[8], 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(path))
                encoder.Save(stream);
            return path;
        }

        // Two images listed; the output defaults to Images.pdf next to them
        private ImagesToPdfViewModel ImagesToPdfOfTwo(Action<IList<string>, string>? create = null)
        {
            var vm = NewImagesToPdf(create);
            vm.AddPaths(new[] { Png("a.png"), Png("b.png") });
            return vm;
        }

        [Fact]
        public async Task ImagesToPdf_lists_the_images_that_no_longer_exist()
        {
            bool created = false;
            var vm = ImagesToPdfOfTwo((_, _) => created = true);
            File.Delete(_files.PathFor("b.png"));

            await vm.CreatePdfAsync();

            Assert.Equal(new DialogCall(false, "The following images no longer exist:\nb.png", "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(created);
        }

        [Fact]
        public async Task ImagesToPdf_refuses_an_output_folder_that_does_not_exist()
        {
            bool created = false;
            var vm = ImagesToPdfOfTwo((_, _) => created = true);
            vm.OutputPath = _files.PathFor(@"gone\Images.pdf");

            await vm.CreatePdfAsync();

            Assert.Equal(new DialogCall(false, "The output folder does not exist. Please choose another output file.", "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(created);
        }

        [Fact]
        public async Task ImagesToPdf_asks_before_overwriting_and_No_leaves_the_output_alone()
        {
            bool created = false;
            var vm = ImagesToPdfOfTwo((_, _) => created = true);
            string output = CreateFile("Images.pdf", "keep me");

            await vm.CreatePdfAsync();

            Assert.Equal(new DialogCall(true, $"\"{output}\" already exists and will be overwritten.\n\nContinue?", "Confirm Overwrite", DialogIcon.Warning), SingleCall());
            Assert.False(created);
            Assert.Equal("keep me", File.ReadAllText(output));
        }

        [Fact]
        public async Task ImagesToPdf_success_offers_the_folder_and_Yes_selects_the_PDF()
        {
            (IList<string> Images, string Output)? created = null;
            var vm = ImagesToPdfOfTwo((images, output) => created = (images, output));
            _dialogs.Answer = message => message.StartsWith("PDF created");

            await vm.CreatePdfAsync();

            string output = _files.PathFor("Images.pdf");
            Assert.Equal(new DialogCall(true, "PDF created with 2 pages!\n\nOpen the output folder?", "Success", DialogIcon.Information), SingleCall());
            Assert.Equal(new[] { output }, _dialogs.ShownInExplorer);
            Assert.Equal(new[] { _files.PathFor("a.png"), _files.PathFor("b.png") }, created?.Images);
            Assert.Equal(output, created?.Output);
            Assert.False(vm.IsBusy);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ImagesToPdf_failures_get_a_message_and_only_unexpected_ones_are_logged(bool unexpected)
        {
            Exception error = unexpected ? new NullReferenceException("boom") : new InvalidOperationException("b.png could not be read.");
            var vm = ImagesToPdfOfTwo((_, _) => throw error);

            await vm.CreatePdfAsync();

            string message = unexpected ? "Error creating PDF: boom" : "b.png could not be read.";
            Assert.Equal(new DialogCall(false, message, "Error", DialogIcon.Error), SingleCall());
            Assert.Equal(unexpected, AnythingLogged);
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public void ImagesToPdf_add_button_adds_the_picked_images_and_a_cancelled_pick_adds_nothing()
        {
            var vm = NewImagesToPdf();
            string a = Png("a.png");
            _dialogs.PickedFiles = new[] { a };

            vm.AddImagesCommand.Execute(null);
            _dialogs.PickedFiles = Array.Empty<string>();
            vm.AddImagesCommand.Execute(null);

            Assert.Equal(new[] { a }, vm.Images.Select(i => i.Path));
        }

        [Fact]
        public void ImagesToPdf_browse_suggests_a_name_and_a_cancelled_pick_keeps_the_output()
        {
            var vm = NewImagesToPdf();
            string chosen = _files.PathFor("Album.pdf");
            _dialogs.PickedSaveFile = chosen;

            vm.BrowseOutputCommand.Execute(null);
            _dialogs.PickedSaveFile = null;
            vm.BrowseOutputCommand.Execute(null);

            Assert.Equal(chosen, vm.OutputPath);
            Assert.Equal(new[]
            {
                new SaveFileRequest("PDF Files (*.pdf)|*.pdf", ".pdf", "Images.pdf"),
                new SaveFileRequest("PDF Files (*.pdf)|*.pdf", ".pdf", "Album.pdf")
            }, _dialogs.SaveFileRequests);
        }
    }
}
