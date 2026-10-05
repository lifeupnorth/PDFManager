using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PDFManager.Services;
using PDFManager.ViewModels;
using Path = System.IO.Path;

namespace PDFManager.Tests
{
    public class BatchTests : IDisposable
    {
        private readonly PdfService _service = new PdfService();
        private readonly TestPdfs _pdfs = new TestPdfs();

        public void Dispose() => _pdfs.Dispose();

        private BatchViewModel NewBatch() => new BatchViewModel(_service, new FakeDialogService());

        private string Subfolder(string name)
        {
            string path = _pdfs.PathFor(name);
            Directory.CreateDirectory(path);
            return path;
        }

        // --- Adding files ---

        [Fact]
        public void AddPaths_takes_PDFs_and_the_PDFs_directly_inside_folders()
        {
            string folder = Subfolder("in");
            File.WriteAllText(Path.Combine(folder, "b.PDF"), "");
            File.WriteAllText(Path.Combine(folder, "a.pdf"), "");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "");
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            File.WriteAllText(Path.Combine(folder, "nested", "deep.pdf"), "");
            string loose = _pdfs.CreateNotAPdf("loose.pdf");
            var batch = NewBatch();

            int added = batch.AddPaths(new[] { loose, folder, _pdfs.CreateNotAPdf("readme.txt") });

            Assert.Equal(3, added);
            Assert.Equal(new[] { "loose.pdf", "a.pdf", "b.PDF" }, batch.Files.Select(f => f.FileName));
        }

        [Fact]
        public void AddPaths_skips_files_already_listed_ignoring_case()
        {
            string file = _pdfs.CreatePages("doc.pdf", 1);
            var batch = NewBatch();
            batch.AddPaths(new[] { file });

            Assert.Equal(0, batch.AddPaths(new[] { file.ToUpperInvariant() }));
            Assert.Single(batch.Files);
        }

        // --- Planning outputs ---

        [Fact]
        public void PlanOutputs_puts_each_output_next_to_its_original_by_default()
        {
            string a = Path.Combine(Subfolder("one"), "a.pdf"), b = Path.Combine(Subfolder("two"), "b.pdf");
            var batch = NewBatch();
            batch.AddPaths(new[] { a, b });
            batch.Action = BatchAction.Watermark;

            Assert.Equal(new[] { Path.Combine(_pdfs.PathFor("one"), "a_watermarked.pdf"), Path.Combine(_pdfs.PathFor("two"), "b_watermarked.pdf") }, batch.PlanOutputs());
        }

        [Fact]
        public void PlanOutputs_numbers_names_that_would_collide_in_one_folder()
        {
            string out_ = Subfolder("out");
            var batch = NewBatch();
            batch.AddPaths(new[] { Path.Combine(Subfolder("one"), "Report.pdf"), Path.Combine(Subfolder("two"), "report.pdf") });
            batch.OutputFolder = out_;

            Assert.Equal(new[] { Path.Combine(out_, "Report_rotated.pdf"), Path.Combine(out_, "report_rotated (2).pdf") }, batch.PlanOutputs());
        }

        [Fact]
        public void PlanOutputs_never_writes_over_a_file_in_the_batch()
        {
            string folder = Subfolder("in");
            var batch = NewBatch();
            batch.AddPaths(new[] { Path.Combine(folder, "a.pdf"), Path.Combine(folder, "a_rotated.pdf") });

            Assert.Equal(new[] { Path.Combine(folder, "a_rotated (2).pdf"), Path.Combine(folder, "a_rotated_rotated.pdf") }, batch.PlanOutputs());
        }

        // --- Processing ---

        [Fact]
        public async Task ProcessAsync_handles_each_file_and_reports_failures_on_their_rows()
        {
            string good1 = _pdfs.CreatePages("one.pdf", 2), corrupt = _pdfs.CreateNotAPdf("broken.pdf"), locked = _pdfs.CreateEncrypted("locked.pdf"), good2 = _pdfs.CreatePages("two.pdf", 1);
            var batch = NewBatch();
            batch.AddPaths(new[] { good1, corrupt, locked, good2 });
            batch.RotationAngle = 180;

            await batch.ProcessAsync();

            Assert.Equal(new[] { BatchFileStatus.Done, BatchFileStatus.Failed, BatchFileStatus.Failed, BatchFileStatus.Done }, batch.Files.Select(f => f.Status));
            Assert.Equal("Saved as one_rotated.pdf", batch.Files[0].StatusText);
            Assert.Equal("Not a valid PDF, or the file is corrupted.", batch.Files[1].StatusText);
            Assert.Equal("This PDF is password-protected. Remove its password with Unlock PDF first.", batch.Files[2].StatusText);
            Assert.Equal(new[] { 180, 180 }, TestPdfs.PageRotations(_pdfs.PathFor("one_rotated.pdf")));
            Assert.Equal(new[] { 0, 0 }, TestPdfs.PageRotations(good1));
            Assert.False(File.Exists(_pdfs.PathFor("broken_rotated.pdf")));
            Assert.False(batch.IsBusy);
            Assert.Equal(100, batch.Progress);
        }

        [Fact]
        public async Task ProcessAsync_extracts_the_same_pages_from_every_file()
        {
            var batch = NewBatch();
            batch.AddPaths(new[] { _pdfs.Create("long.pdf", 101, 102, 103), _pdfs.Create("short.pdf", 201) });
            batch.Action = BatchAction.Extract;
            batch.PageSpec = "2-3";

            await batch.ProcessAsync();

            Assert.Equal(new[] { 102f, 103f }, TestPdfs.PageWidths(_pdfs.PathFor("long_extracted.pdf")));
            Assert.Equal(BatchFileStatus.Failed, batch.Files[1].Status);
            Assert.Equal("Page range \"2-3\" is out of range. The document has 1 pages.", batch.Files[1].StatusText);
        }

        [Fact]
        public async Task ProcessAsync_unlocks_the_files_the_password_opens()
        {
            string plain = _pdfs.CreatePages("plain.pdf", 1);
            _service.ProtectPdf(plain, _pdfs.PathFor("a.pdf"), "secret");
            _service.ProtectPdf(plain, _pdfs.PathFor("b.pdf"), "different");
            var batch = NewBatch();
            batch.AddPaths(new[] { _pdfs.PathFor("a.pdf"), _pdfs.PathFor("b.pdf"), plain });
            batch.Action = BatchAction.Unlock;
            batch.Password = "secret";

            await batch.ProcessAsync();

            Assert.Equal(BatchFileStatus.Done, batch.Files[0].Status);
            Assert.Equal(PdfProtection.None, _service.GetProtection(_pdfs.PathFor("a_unlocked.pdf")));
            Assert.Equal("The password is incorrect.", batch.Files[1].StatusText);
            Assert.Equal("This PDF is not password-protected.", batch.Files[2].StatusText);
        }

        [Fact]
        public async Task ProcessAsync_protects_and_watermarks_into_a_chosen_folder()
        {
            string out_ = Subfolder("out");
            var batch = NewBatch();
            batch.AddPaths(new[] { _pdfs.CreatePages("a.pdf", 1), _pdfs.CreatePages("b.pdf", 2) });
            batch.OutputFolder = out_;

            batch.Action = BatchAction.Protect;
            batch.Password = "pw";
            await batch.ProcessAsync();
            batch.Action = BatchAction.Watermark;
            await batch.ProcessAsync();

            Assert.All(new[] { "a_protected.pdf", "b_protected.pdf" }, name =>
                Assert.Equal(PdfProtection.PasswordToOpen, _service.GetProtection(Path.Combine(out_, name))));
            Assert.All(new[] { "a_watermarked.pdf", "b_watermarked.pdf" }, name => Assert.True(File.Exists(Path.Combine(out_, name))));
            Assert.All(batch.Files, f => Assert.Equal(BatchFileStatus.Done, f.Status));
        }

        [Fact]
        public async Task Cancelling_finishes_the_current_file_and_skips_the_rest()
        {
            using var firstStarted = new ManualResetEventSlim();
            using var firstMayFinish = new ManualResetEventSlim();
            int calls = 0;
            var batch = new BatchViewModel(new FakePdfService
            {
                Rotate = (source, output) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        firstStarted.Set();
                        firstMayFinish.Wait();
                    }
                }
            }, new FakeDialogService());
            batch.AddPaths(new[] { @"C:\Docs\1.pdf", @"C:\Docs\2.pdf", @"C:\Docs\3.pdf" });

            Task run = batch.ProcessAsync();
            firstStarted.Wait();
            Assert.True(batch.IsBusy);
            batch.CancelCommand.Execute(null);
            firstMayFinish.Set();
            await run;

            Assert.Equal(1, calls);
            Assert.Equal(new[] { BatchFileStatus.Done, BatchFileStatus.Skipped, BatchFileStatus.Skipped }, batch.Files.Select(f => f.Status));
            Assert.Equal("Not processed: the run was cancelled.", batch.Files[2].StatusText);
        }

        [Fact]
        public async Task Running_again_clears_the_previous_results()
        {
            var batch = NewBatch();
            batch.AddPaths(new[] { _pdfs.CreateNotAPdf("broken.pdf") });
            await batch.ProcessAsync();
            Assert.Equal(BatchFileStatus.Failed, batch.Files[0].Status);

            File.Delete(batch.Files[0].Path);
            File.Copy(_pdfs.CreatePages("good.pdf", 1), batch.Files[0].Path);
            await batch.ProcessAsync();

            Assert.Equal(BatchFileStatus.Done, batch.Files[0].Status);
        }

        [Fact]
        public void Run_needs_at_least_one_file()
        {
            var batch = NewBatch();
            Assert.False(batch.RunBatchCommand.CanExecute(null));

            batch.AddPaths(new[] { @"C:\Docs\a.pdf" });
            Assert.True(batch.RunBatchCommand.CanExecute(null));
        }
    }
}
