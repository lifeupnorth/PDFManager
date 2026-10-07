using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    // The run, open and browse paths of the seven tools that turn one PDF into another: Rotate,
    // Extract, Reorder, Watermark, Properties, Protect and Unlock, checked through FakeDialogService.
    // They share one shape, so each behaviour is a theory over the tools, and what only one tool
    // does is tested on its own at the end. Some paths write to ErrorLog, so this class shares
    // the ErrorLog.Folder redirect (and its collection) with ErrorLogTests.
    [Collection("Error log")]
    public class ToolDialogFlowTests : IDisposable
    {
        private const string PdfFilter = "PDF Files (*.pdf)|*.pdf";

        private readonly string _originalLogFolder = ErrorLog.Folder;
        private readonly TestPdfs _files = new TestPdfs();
        private readonly FakeDialogService _dialogs = new FakeDialogService();

        public ToolDialogFlowTests() => ErrorLog.Folder = Path.Combine(_files.Folder, "logs");

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

        // --- The tools, seen through what they share ---

        private sealed record Tool(Func<Task> Run, Func<bool> IsBusy, Func<string> Source, Action<string> SetSource,
            Func<string> Output, Action<string> SetOutput, ICommand Open, ICommand Browse);

        // PasswordProtected is null for Protect and Unlock: they show the service's own
        // explanations as they are, and log any other error
        private sealed record Texts(string Success, string ErrorPrefix, string? PasswordProtected, string OutputSuffix);

        private static readonly Dictionary<string, Texts> Expected = new Dictionary<string, Texts>
        {
            ["Rotate"] = new Texts("PDF rotated successfully!", "Error rotating PDF", "This PDF is password-protected and cannot be rotated.", "_rotated"),
            ["Extract"] = new Texts("Pages extracted successfully!", "Error extracting pages", "This PDF is password-protected and pages cannot be extracted from it.", "_extracted"),
            ["Reorder"] = new Texts("Pages reordered successfully!", "Error reordering pages", "This PDF is password-protected and its pages cannot be reordered.", "_reordered"),
            ["Watermark"] = new Texts("Watermark added successfully!", "Error adding watermark", "This PDF is password-protected and cannot be watermarked.", "_watermarked"),
            ["Metadata"] = new Texts("Properties saved successfully!", "Error saving properties", "This PDF is password-protected and its properties cannot be changed.", "_edited"),
            ["Protect"] = new Texts("PDF protected successfully!", "Error protecting PDF", null, "_protected"),
            ["Unlock"] = new Texts("Password removed successfully!", "Error removing password", null, "_unlocked"),
        };

        public static TheoryData<string> AllTools() => new TheoryData<string>(Expected.Keys);

        // Properties may save over the source after asking; the others refuse
        public static TheoryData<string> ToolsThatRefuseTheSource() => new TheoryData<string>(Expected.Keys.Where(name => name != "Metadata"));

        // A tool whose work is `work`, with settings that pass its own checks
        private Tool NewTool(string name, Action<string, string>? work = null)
        {
            work ??= (_, _) => { };
            switch (name)
            {
                case "Rotate":
                {
                    var vm = new RotateViewModel(new FakePdfService { Rotate = work }, new FakePageRenderer(), _dialogs);
                    return new Tool(vm.RotateAsync, () => vm.IsBusy, () => vm.SourcePath, s => vm.SourcePath = s,
                        () => vm.OutputPath, o => vm.OutputPath = o, vm.OpenFileCommand, vm.BrowseOutputCommand);
                }
                case "Extract":
                {
                    var vm = new ExtractViewModel(new FakePdfService { Extract = work }, new FakePageRenderer(), _dialogs) { PageSpec = "1" };
                    return new Tool(vm.ExtractAsync, () => vm.IsBusy, () => vm.SourcePath, s => vm.SourcePath = s,
                        () => vm.OutputPath, o => vm.OutputPath = o, vm.OpenFileCommand, vm.BrowseOutputCommand);
                }
                case "Reorder":
                {
                    var vm = new ReorderViewModel(new FakePdfService { Reorder = work }, new FakePageRenderer(), _dialogs);
                    // The order comes from the page thumbnails and must differ from the original,
                    // so a run loads them and reverses the pages first
                    async Task Run()
                    {
                        await vm.Thumbnails.LoadAsync(vm.SourcePath);
                        vm.Reverse();
                        await vm.ReorderAsync();
                    }
                    return new Tool(Run, () => vm.IsBusy, () => vm.SourcePath, s => vm.SourcePath = s,
                        () => vm.OutputPath, o => vm.OutputPath = o, vm.OpenFileCommand, vm.BrowseOutputCommand);
                }
                case "Watermark":
                {
                    var vm = new WatermarkViewModel(new FakePdfService { Watermark = work }, new FakePageRenderer(), _dialogs);
                    return new Tool(vm.WatermarkAsync, () => vm.IsBusy, () => vm.SourcePath, s => vm.SourcePath = s,
                        () => vm.OutputPath, o => vm.OutputPath = o, vm.OpenFileCommand, vm.BrowseOutputCommand);
                }
                case "Metadata":
                {
                    var vm = new MetadataViewModel(new FakePdfService { SaveMetadata = work }, _dialogs);
                    return new Tool(vm.SavePropertiesAsync, () => vm.IsBusy, () => vm.SourcePath, s => vm.SourcePath = s,
                        () => vm.OutputPath, o => vm.OutputPath = o, vm.OpenFileCommand, vm.BrowseOutputCommand);
                }
                case "Protect":
                {
                    var vm = new ProtectViewModel(new FakePdfService { Protect = work }, _dialogs) { Password = "secret", ConfirmPassword = "secret" };
                    return new Tool(vm.ProtectAsync, () => vm.IsBusy, () => vm.SourcePath, s => vm.SourcePath = s,
                        () => vm.OutputPath, o => vm.OutputPath = o, vm.OpenFileCommand, vm.BrowseOutputCommand);
                }
                case "Unlock":
                {
                    var vm = new UnlockViewModel(new FakePdfService { Unlock = work }, _dialogs) { Password = "secret" };
                    return new Tool(vm.UnlockAsync, () => vm.IsBusy, () => vm.SourcePath, s => vm.SourcePath = s,
                        () => vm.OutputPath, o => vm.OutputPath = o, vm.OpenFileCommand, vm.BrowseOutputCommand);
                }
                default:
                    throw new ArgumentException("No such tool: " + name);
            }
        }

        // A tool with a source file and an output that does not exist yet
        private Tool Prepared(string name, Action<string, string>? work = null)
        {
            var tool = NewTool(name, work);
            tool.SetSource(CreateFile("Report.pdf"));
            tool.SetOutput(_files.PathFor("Result.pdf"));
            return tool;
        }

        // --- What all seven do ---

        [Theory]
        [MemberData(nameof(ToolsThatRefuseTheSource))]
        public async Task Refuses_to_write_over_the_source(string name)
        {
            bool worked = false;
            var tool = Prepared(name, (_, _) => worked = true);
            tool.SetOutput(Path.Combine(_files.Folder, "sub", "..", "REPORT.PDF"));

            await tool.Run();

            Assert.Equal(new DialogCall(false, "The output file cannot be the same as the source file. Please choose a different output path.", "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(worked);
            Assert.Equal("original", File.ReadAllText(tool.Source()));
        }

        [Theory]
        [MemberData(nameof(AllTools))]
        public async Task Asks_before_overwriting_and_No_leaves_the_output_alone(string name)
        {
            bool worked = false;
            var tool = Prepared(name, (_, _) => worked = true);
            string output = CreateFile("Result.pdf", "keep me");

            await tool.Run();

            Assert.Equal(new DialogCall(true, $"\"{output}\" already exists and will be overwritten.\n\nContinue?", "Confirm Overwrite", DialogIcon.Warning), SingleCall());
            Assert.False(worked);
            Assert.Equal("keep me", File.ReadAllText(output));
        }

        [Theory]
        [MemberData(nameof(AllTools))]
        public async Task Overwrites_on_Yes_with_the_chosen_paths(string name)
        {
            (string Source, string Output)? worked = null;
            var tool = Prepared(name, (source, output) => worked = (source, output));
            string output = CreateFile("Result.pdf");
            _dialogs.Answer = message => message.Contains("already exists");

            await tool.Run();

            Assert.Equal((tool.Source(), output), worked);
        }

        [Theory]
        [MemberData(nameof(AllTools))]
        public async Task Success_offers_the_folder_and_Yes_selects_the_output(string name)
        {
            bool busyWhileWorking = false;
            Tool? tool = null;
            tool = Prepared(name, (_, _) => busyWhileWorking = tool!.IsBusy());
            string success = Expected[name].Success;
            _dialogs.Answer = message => message.StartsWith(success);

            await tool.Run();

            Assert.Equal(new DialogCall(true, success + "\n\nOpen the output folder?", "Success", DialogIcon.Information), SingleCall());
            Assert.Equal(new[] { _files.PathFor("Result.pdf") }, _dialogs.ShownInExplorer);
            Assert.True(busyWhileWorking);
            Assert.False(tool.IsBusy());
        }

        [Theory]
        [MemberData(nameof(AllTools))]
        public async Task Success_with_No_opens_nothing(string name)
        {
            var tool = Prepared(name);

            await tool.Run();

            Assert.True(SingleCall().IsQuestion);
            Assert.Empty(_dialogs.ShownInExplorer);
        }

        public static IEnumerable<object[]> Failures() =>
            from name in Expected.Keys
            from kind in new[] { "password", "corrupt", "service", "unexpected" }
            where kind != "password" || Expected[name].PasswordProtected != null
            select new object[] { name, kind };

        [Theory]
        [MemberData(nameof(Failures))]
        public async Task Failures_get_a_message_and_only_unexpected_ones_are_logged(string name, string kind)
        {
            var texts = Expected[name];
            (Exception Error, string Message, bool Logged) failure = kind switch
            {
                "password" => (new iText.Kernel.Exceptions.BadPasswordException("locked"), texts.PasswordProtected!, false),
                "corrupt" => (new iText.IO.Exceptions.IOException("bad header"), "This file is not a valid PDF or is corrupted.", false),
                "service" => (new InvalidOperationException("The service explains."),
                    texts.PasswordProtected == null ? "The service explains." : $"{texts.ErrorPrefix}: The service explains.", false),
                _ => (new NullReferenceException("boom"), $"{texts.ErrorPrefix}: boom", true)
            };
            var tool = Prepared(name, (_, _) => throw failure.Error);

            await tool.Run();

            Assert.Equal(new DialogCall(false, failure.Message, "Error", DialogIcon.Error), SingleCall());
            Assert.Equal(failure.Logged, AnythingLogged);
            Assert.False(tool.IsBusy());
        }

        [Theory]
        [MemberData(nameof(AllTools))]
        public async Task Open_loads_the_picked_file_and_a_cancelled_pick_changes_nothing(string name)
        {
            var tool = NewTool(name);
            string first = CreateFile("First.pdf");
            _dialogs.PickedFile = first;

            tool.Open.Execute(null);
            await Eventually(() => tool.Source() == first);
            _dialogs.PickedFile = null;
            tool.Open.Execute(null);

            Assert.Equal(first, tool.Source());
        }

        [Theory]
        [MemberData(nameof(AllTools))]
        public void Browse_suggests_a_name_and_a_cancelled_pick_keeps_the_output(string name)
        {
            var tool = NewTool(name);
            tool.SetSource(_files.PathFor("Report.pdf"));
            string chosen = _files.PathFor("Chosen.pdf");
            _dialogs.PickedSaveFile = chosen;

            tool.Browse.Execute(null);
            _dialogs.PickedSaveFile = null;
            tool.Browse.Execute(null);

            Assert.Equal(chosen, tool.Output());
            Assert.Equal(new[]
            {
                new SaveFileRequest(PdfFilter, ".pdf", "Report" + Expected[name].OutputSuffix + ".pdf"),
                new SaveFileRequest(PdfFilter, ".pdf", "Chosen.pdf")
            }, _dialogs.SaveFileRequests);
        }

        // --- What only one tool does ---

        [Fact]
        public async Task Rotate_of_specific_pages_asks_for_them()
        {
            bool rotated = false;
            var vm = new RotateViewModel(new FakePdfService { Rotate = (_, _) => rotated = true }, new FakePageRenderer(), _dialogs)
            {
                SourcePath = CreateFile("Report.pdf"),
                OutputPath = _files.PathFor("Result.pdf"),
                RotateAllPages = false,
                RotatePageSpec = " "
            };

            await vm.RotateAsync();

            Assert.Equal(new DialogCall(false, "Please enter the pages to rotate (for example: 1, 3, 5-7), or choose \"All pages\".", "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(rotated);
        }

        [Fact]
        public async Task Extract_asks_for_the_pages()
        {
            bool extracted = false;
            var vm = new ExtractViewModel(new FakePdfService { Extract = (_, _) => extracted = true }, new FakePageRenderer(), _dialogs)
            {
                SourcePath = CreateFile("Report.pdf"),
                OutputPath = _files.PathFor("Result.pdf"),
                PageSpec = ""
            };

            await vm.ExtractAsync();

            Assert.Equal(new DialogCall(false, "Please enter the pages to extract (for example: 1, 3, 5-7).", "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(extracted);
        }

        [Theory]
        [InlineData(" ", true, "", "Please enter the watermark text.")]
        [InlineData("DRAFT", false, " ", "Please enter the pages to watermark (for example: 1, 3, 5-7), or choose \"All pages\".")]
        public async Task Watermark_asks_for_its_text_and_pages(string text, bool allPages, string pageSpec, string message)
        {
            bool watermarked = false;
            var vm = new WatermarkViewModel(new FakePdfService { Watermark = (_, _) => watermarked = true }, new FakePageRenderer(), _dialogs)
            {
                SourcePath = CreateFile("Report.pdf"),
                OutputPath = _files.PathFor("Result.pdf"),
                WatermarkText = text,
                AllPages = allPages,
                PageSpec = pageSpec
            };

            await vm.WatermarkAsync();

            Assert.Equal(new DialogCall(false, message, "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(watermarked);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Properties_saved_over_the_source_ask_first(bool yes)
        {
            (string Source, string Output)? saved = null;
            var vm = new MetadataViewModel(new FakePdfService { SaveMetadata = (source, output) => saved = (source, output) }, _dialogs);
            string source = vm.SourcePath = CreateFile("Report.pdf");
            vm.OutputPath = source;
            _dialogs.Answer = message => yes && message.StartsWith("This will update");

            await vm.SavePropertiesAsync();

            Assert.Equal(new DialogCall(true, $"This will update the original file:\n{source}\n\nContinue?", "Confirm Overwrite", DialogIcon.Warning), _dialogs.Calls[0]);
            if (yes)
                Assert.Equal((source, source), saved);
            else
                Assert.Null(saved);
        }

        [Theory]
        [InlineData("", "", "Please enter a password.")]
        [InlineData("secret", "Secret", "The passwords do not match. Please type the same password in both boxes.")]
        public async Task Protect_checks_the_password_and_its_confirmation(string password, string confirm, string message)
        {
            bool protectedIt = false;
            var vm = new ProtectViewModel(new FakePdfService { Protect = (_, _) => protectedIt = true }, _dialogs)
            {
                SourcePath = CreateFile("Report.pdf"),
                OutputPath = _files.PathFor("Result.pdf"),
                Password = password,
                ConfirmPassword = confirm
            };

            await vm.ProtectAsync();

            Assert.Equal(new DialogCall(false, message, "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(protectedIt);
        }

        [Fact]
        public async Task Protect_forgets_the_password_once_it_is_used()
        {
            var vm = new ProtectViewModel(new FakePdfService { Protect = (_, _) => { } }, _dialogs)
            {
                SourcePath = CreateFile("Report.pdf"),
                OutputPath = _files.PathFor("Result.pdf"),
                Password = "secret",
                ConfirmPassword = "secret"
            };

            await vm.ProtectAsync();

            Assert.Equal("", vm.Password);
            Assert.Equal("", vm.ConfirmPassword);
        }

        [Fact]
        public async Task Unlock_asks_for_the_password_and_forgets_it_once_used()
        {
            bool unlocked = false;
            var vm = new UnlockViewModel(new FakePdfService { Unlock = (_, _) => unlocked = true }, _dialogs)
            {
                SourcePath = CreateFile("Report.pdf"),
                OutputPath = _files.PathFor("Result.pdf")
            };

            await vm.UnlockAsync();
            Assert.Equal(new DialogCall(false, "Please enter the PDF's password.", "Validation Error", DialogIcon.Warning), SingleCall());
            Assert.False(unlocked);

            vm.Password = "secret";
            await vm.UnlockAsync();
            Assert.True(unlocked);
            Assert.Equal("", vm.Password);
        }
    }
}
