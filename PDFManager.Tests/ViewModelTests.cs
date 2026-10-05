using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    // Covers the view-model paths that never show a MessageBox: the page-count hints,
    // default output paths, metadata loading, protection checks, the watermark preview and
    // merge-list de-duplication
    public class ViewModelTests
    {
        private const string Folder = @"C:\Docs";

        [Theory]
        [InlineData(5, "Enter a page from 1 to 4. The document has 5 pages.")]
        [InlineData(2, "Enter a page from 1 to 1. The document has 2 pages.")]
        [InlineData(1, "This document has only 1 page and cannot be split.")]
        public async Task Split_hint_describes_the_valid_split_pages(int pages, string hint)
        {
            var vm = new SplitViewModel(new FakePdfService { PageCount = _ => pages }, new FakePageRenderer());

            await vm.UpdateFileAsync(Path.Combine(Folder, "Report.pdf"));

            Assert.Equal(hint, vm.PageCountInfo);
            Assert.Equal(Folder, vm.OutputFolder);
        }

        [Fact]
        public async Task Split_hint_flags_a_password_protected_file()
        {
            var vm = new SplitViewModel(new FakePdfService { PageCount = _ => throw new iText.Kernel.Exceptions.BadPasswordException("locked") }, new FakePageRenderer());

            await vm.UpdateFileAsync(Path.Combine(Folder, "Locked.pdf"));

            Assert.Equal("This PDF is password-protected and cannot be split.", vm.PageCountInfo);
        }

        [Fact]
        public async Task Split_hint_reports_an_unreadable_file()
        {
            var vm = new SplitViewModel(new FakePdfService { PageCount = _ => throw new IOException("gone") }, new FakePageRenderer());

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
            }, new FakePageRenderer());

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
            var vm = new RotateViewModel(new FakePdfService { PageCount = _ => pages }, new FakePageRenderer());

            await vm.UpdateFileAsync(Path.Combine(Folder, "Scan.pdf"));

            Assert.Equal(hint, vm.PageCountInfo);
        }

        [Fact]
        public async Task Rotate_suggests_an_output_next_to_the_source()
        {
            var vm = new RotateViewModel(new FakePdfService { PageCount = _ => 1 }, new FakePageRenderer());

            await vm.UpdateFileAsync(Path.Combine(Folder, "Scan.pdf"));

            Assert.Equal(Path.Combine(Folder, "Scan_rotated.pdf"), vm.OutputPath);
        }

        [Fact]
        public async Task Rotate_hint_flags_a_password_protected_file()
        {
            var vm = new RotateViewModel(new FakePdfService { PageCount = _ => throw new iText.Kernel.Exceptions.BadPasswordException("locked") }, new FakePageRenderer());

            await vm.UpdateFileAsync(Path.Combine(Folder, "Locked.pdf"));

            Assert.Equal("This PDF is password-protected and cannot be rotated.", vm.PageCountInfo);
        }

        [Theory]
        [InlineData(1, "Document has 1 page. Example: 1, 3, 5-7")]
        [InlineData(12, "Document has 12 pages. Example: 1, 3, 5-7")]
        public async Task Extract_hint_gives_the_page_count_and_an_example(int pages, string hint)
        {
            var vm = new ExtractViewModel(new FakePdfService { PageCount = _ => pages }, new FakePageRenderer());

            await vm.UpdateFileAsync(Path.Combine(Folder, "Book.pdf"));

            Assert.Equal(hint, vm.PageCountInfo);
            Assert.Equal(Path.Combine(Folder, "Book_extracted.pdf"), vm.OutputPath);
        }

        [Fact]
        public async Task Extract_hint_flags_a_password_protected_file()
        {
            var vm = new ExtractViewModel(new FakePdfService { PageCount = _ => throw new iText.Kernel.Exceptions.BadPasswordException("locked") }, new FakePageRenderer());

            await vm.UpdateFileAsync(Path.Combine(Folder, "Locked.pdf"));

            Assert.Equal("This PDF is password-protected and pages cannot be extracted from it.", vm.PageCountInfo);
        }

        [Fact]
        public async Task Extract_hint_ignores_a_slow_result_for_a_file_that_was_replaced()
        {
            using var firstMayFinish = new ManualResetEventSlim();
            string first = Path.Combine(Folder, "Slow.pdf"), second = Path.Combine(Folder, "Fast.pdf");
            var vm = new ExtractViewModel(new FakePdfService
            {
                PageCount = path =>
                {
                    if (path == first)
                        firstMayFinish.Wait();
                    return path == first ? 9 : 3;
                }
            }, new FakePageRenderer());

            Task slow = vm.UpdateFileAsync(first);
            await vm.UpdateFileAsync(second);
            firstMayFinish.Set();
            await slow;

            Assert.Equal("Document has 3 pages. Example: 1, 3, 5-7", vm.PageCountInfo);
            Assert.Equal(Path.Combine(Folder, "Fast_extracted.pdf"), vm.OutputPath);
        }

        [Fact]
        public async Task Metadata_loads_the_properties_and_suggests_an_output_next_to_the_source()
        {
            var vm = new MetadataViewModel(new FakePdfService { Metadata = _ => new PdfMetadata("T", "A", "S", "K") });

            await vm.UpdateFileAsync(Path.Combine(Folder, "Paper.pdf"));

            Assert.Equal(("T", "A", "S", "K"), (vm.Title, vm.Author, vm.Subject, vm.Keywords));
            Assert.True(vm.IsLoaded);
            Assert.True(vm.RunSaveCommand.CanExecute(null));
            Assert.Equal("Leave a field empty to remove that property.", vm.StatusInfo);
            Assert.Equal(Path.Combine(Folder, "Paper_edited.pdf"), vm.OutputPath);
        }

        [Fact]
        public async Task Metadata_cannot_be_saved_while_a_new_file_is_loading()
        {
            using var mayFinish = new ManualResetEventSlim();
            string first = Path.Combine(Folder, "First.pdf"), second = Path.Combine(Folder, "Second.pdf");
            var vm = new MetadataViewModel(new FakePdfService
            {
                Metadata = path =>
                {
                    if (path == second)
                        mayFinish.Wait();
                    return new PdfMetadata(Path.GetFileNameWithoutExtension(path), "", "", "");
                }
            });
            await vm.UpdateFileAsync(first);

            Task loading = vm.UpdateFileAsync(second);

            // The first file's values are gone and saving is off until the second file is read
            Assert.Equal("", vm.Title);
            Assert.False(vm.IsLoaded);
            Assert.False(vm.RunSaveCommand.CanExecute(null));
            Assert.Equal("Reading document properties...", vm.StatusInfo);

            mayFinish.Set();
            await loading;
            Assert.Equal("Second", vm.Title);
            Assert.True(vm.RunSaveCommand.CanExecute(null));
        }

        [Fact]
        public async Task Metadata_ignores_a_slow_result_for_a_file_that_was_replaced()
        {
            using var firstMayFinish = new ManualResetEventSlim();
            string first = Path.Combine(Folder, "Slow.pdf"), second = Path.Combine(Folder, "Fast.pdf");
            var vm = new MetadataViewModel(new FakePdfService
            {
                Metadata = path =>
                {
                    if (path == first)
                        firstMayFinish.Wait();
                    return new PdfMetadata(Path.GetFileNameWithoutExtension(path), "", "", "");
                }
            });

            Task slow = vm.UpdateFileAsync(first);
            await vm.UpdateFileAsync(second);
            firstMayFinish.Set();
            await slow;

            Assert.Equal("Fast", vm.Title);
            Assert.True(vm.IsLoaded);
        }

        [Theory]
        [InlineData(true, "This PDF is password-protected and its properties cannot be changed.")]
        [InlineData(false, "Unable to read this file's properties.")]
        public async Task Metadata_that_cannot_be_read_leaves_saving_off_and_says_why(bool passwordProtected, string status)
        {
            var vm = new MetadataViewModel(new FakePdfService
            {
                Metadata = _ => passwordProtected
                    ? throw new iText.Kernel.Exceptions.BadPasswordException("locked")
                    : throw new IOException("unreadable")
            });

            await vm.UpdateFileAsync(Path.Combine(Folder, "Locked.pdf"));

            Assert.Equal(status, vm.StatusInfo);
            Assert.False(vm.IsLoaded);
            Assert.False(vm.RunSaveCommand.CanExecute(null));
        }

        [Theory]
        [InlineData(PdfProtection.None, "The password cannot be recovered if it is forgotten, so keep it somewhere safe.")]
        [InlineData(PdfProtection.PasswordToOpen, "This PDF is already protected. Remove its password with Unlock PDF first.")]
        [InlineData(PdfProtection.PermissionsOnly, "This PDF is already protected. Remove its password with Unlock PDF first.")]
        public async Task Protect_hint_depends_on_whether_the_file_is_protected(PdfProtection protection, string hint)
        {
            var vm = new ProtectViewModel(new FakePdfService { Protection = _ => protection });

            await vm.UpdateFileAsync(Path.Combine(Folder, "Contract.pdf"));

            Assert.Equal(hint, vm.StatusInfo);
            Assert.Equal(Path.Combine(Folder, "Contract_protected.pdf"), vm.OutputPath);
        }

        [Fact]
        public async Task Protect_hint_reports_an_unreadable_file()
        {
            var vm = new ProtectViewModel(new FakePdfService { Protection = _ => throw new IOException("gone") });

            await vm.UpdateFileAsync(Path.Combine(Folder, "Broken.pdf"));

            Assert.Equal("Unable to read this file.", vm.StatusInfo);
        }

        [Theory]
        [InlineData(PdfProtection.PasswordToOpen, "Enter the password that opens this PDF.")]
        [InlineData(PdfProtection.PermissionsOnly, "This PDF opens without a password but restricts printing, copying or editing. Enter its owner password to remove the restrictions.")]
        [InlineData(PdfProtection.None, "This PDF is not password-protected.")]
        public async Task Unlock_hint_says_which_password_is_needed(PdfProtection protection, string hint)
        {
            var vm = new UnlockViewModel(new FakePdfService { Protection = _ => protection });

            await vm.UpdateFileAsync(Path.Combine(Folder, "Contract.pdf"));

            Assert.Equal(hint, vm.StatusInfo);
            Assert.Equal(Path.Combine(Folder, "Contract_unlocked.pdf"), vm.OutputPath);
        }

        [Fact]
        public async Task Unlock_clears_the_password_when_another_file_is_picked()
        {
            var vm = new UnlockViewModel(new FakePdfService { Protection = _ => PdfProtection.PasswordToOpen });
            await vm.UpdateFileAsync(Path.Combine(Folder, "First.pdf"));
            vm.Password = "for the first file";

            await vm.UpdateFileAsync(Path.Combine(Folder, "Second.pdf"));

            Assert.Equal("", vm.Password);
        }

        [Fact]
        public async Task Unlock_hint_ignores_a_slow_result_for_a_file_that_was_replaced()
        {
            using var firstMayFinish = new ManualResetEventSlim();
            string first = Path.Combine(Folder, "Slow.pdf"), second = Path.Combine(Folder, "Fast.pdf");
            var vm = new UnlockViewModel(new FakePdfService
            {
                Protection = path =>
                {
                    if (path == first)
                        firstMayFinish.Wait();
                    return path == first ? PdfProtection.None : PdfProtection.PasswordToOpen;
                }
            });

            Task slow = vm.UpdateFileAsync(first);
            await vm.UpdateFileAsync(second);
            firstMayFinish.Set();
            await slow;

            Assert.Equal("Enter the password that opens this PDF.", vm.StatusInfo);
        }

        [Theory]
        [InlineData(1, "Document has 1 page. Example: 1, 3, 5-7")]
        [InlineData(3, "Document has 3 pages. Example: 1, 3, 5-7")]
        public async Task Watermark_hint_gives_the_page_count_and_an_example(int pages, string hint)
        {
            var vm = new WatermarkViewModel(new FakePdfService { PageCount = _ => pages }, new FakePageRenderer());

            await vm.UpdateFileAsync(Path.Combine(Folder, "Report.pdf"));

            Assert.Equal(hint, vm.PageCountInfo);
            Assert.Equal(Path.Combine(Folder, "Report_watermarked.pdf"), vm.OutputPath);
        }

        [Fact]
        public async Task Watermark_hint_flags_a_password_protected_file()
        {
            var vm = new WatermarkViewModel(new FakePdfService { PageCount = _ => throw new iText.Kernel.Exceptions.BadPasswordException("locked") }, new FakePageRenderer());

            await vm.UpdateFileAsync(Path.Combine(Folder, "Locked.pdf"));

            Assert.Equal("This PDF is password-protected and cannot be watermarked.", vm.PageCountInfo);
        }

        [Fact]
        public void Watermark_defaults_to_a_medium_diagonal_CONFIDENTIAL_on_all_pages()
        {
            var vm = new WatermarkViewModel(new FakePdfService(), new FakePageRenderer());

            Assert.Equal(("CONFIDENTIAL", true, 30, true), (vm.WatermarkText, vm.IsDiagonal, vm.OpacityPercent, vm.AllPages));
            Assert.False(vm.IsPageSpecEnabled);
        }

        [Fact]
        public void Watermark_preview_follows_the_direction_and_opacity()
        {
            var vm = new WatermarkViewModel(new FakePdfService(), new FakePageRenderer());
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            // Diagonal: corner to corner of the A4 preview, rotated counter-clockwise on screen
            Assert.Equal(-Math.Atan2(297, 210) * 180 / Math.PI, vm.PreviewAngle, 6);
            Assert.Equal(0.75 * Math.Sqrt(210 * 210 + 297 * 297), vm.PreviewTextWidth, 6);
            Assert.Equal(105, vm.PreviewTextLeft + vm.PreviewTextWidth / 2, 6); // centred

            vm.IsDiagonal = false;
            vm.OpacityPercent = 50;

            Assert.Equal(0, vm.PreviewAngle);
            Assert.Equal(0.75 * 210, vm.PreviewTextWidth, 6);
            Assert.Equal(105, vm.PreviewTextLeft + vm.PreviewTextWidth / 2, 6);
            Assert.Equal(0.5, vm.PreviewOpacity);
            Assert.Subset(new HashSet<string?>(changed), new HashSet<string?> { "PreviewAngle", "PreviewTextWidth", "PreviewTextLeft", "PreviewOpacity" });
        }

        [Fact]
        public void Watermark_page_box_is_enabled_only_for_specific_pages()
        {
            var vm = new WatermarkViewModel(new FakePdfService(), new FakePageRenderer());
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            vm.AllPages = false;

            Assert.True(vm.IsPageSpecEnabled);
            Assert.Contains("IsPageSpecEnabled", changed);
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
    }
}
