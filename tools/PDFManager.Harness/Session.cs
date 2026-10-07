using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Harness
{
    // The steps run.ps1 can ask for, run in order on the UI thread against the real window.
    // Every step works on its own; each tool step first opens that tool's page.
    internal sealed class Session
    {
        private readonly MainWindow _window;
        private readonly MainViewModel _vm;
        private readonly DialogAnswerer _dialogs;
        private readonly Report _report;
        private readonly string _folder;
        private readonly Dictionary<string, (string Description, Func<Task> Run)> _steps;

        // Sample PDFs, made fresh for every run
        private readonly string _a, _b;

        public Session(MainWindow window, DialogAnswerer dialogs, Report report, string folder)
        {
            _window = window;
            _vm = (MainViewModel)window.DataContext;
            _dialogs = dialogs;
            _report = report;
            _folder = folder;
            _a = Pdfs.Create(Path.Combine(folder, "a.pdf"), 4);
            _b = Pdfs.Create(Path.Combine(folder, "b.pdf"), 2);

            _steps = new Dictionary<string, (string, Func<Task>)>
            {
                ["idle"] = ("wait three seconds", () => Task.Delay(3000)),
                ["split_open"] = ("Split: pick a.pdf in the Open dialog and wait for its page previews", SplitOpen),
                ["split_load"] = ("Split: load a.pdf without a dialog and wait for its page previews", SplitLoad),
                ["split_run"] = ("Split: overwrite No, then Yes, then the success prompt", SplitRun),
                ["merge_open"] = ("Merge: pick a.pdf and b.pdf in the Open dialog", MergeOpen),
                ["merge_saveas"] = ("Merge: pick the output in the Save As dialog", MergeSaveAs),
                ["merge_run"] = ("Merge: the same-path warning, overwrite No, then Yes", MergeRun),
                ["rotate_flow"] = ("Rotate: Open, Save As, overwrite No, then Yes, then the success prompt", RotateFlow),
                ["batch_flow"] = ("Batch: pick both PDFs in the Open dialog, run, answer the summary", BatchFlow),
                ["reorder_flow"] = ("Reorder: Open, press page 4 and Move to start, overwrite No, then Yes, then the success prompt", ReorderFlow),
                ["crash_ui"] = ("throw on the UI thread: App logs it, shows the error and keeps running", CrashOnUiThread),
                ["theme_light"] = ("switch to the light theme with ThemeManager.Apply", ThemeLight),
                ["theme_ui"] = ("switch to the light theme through Home's appearance choice", ThemeThroughHome),
                ["nav_home"] = ("open the Home page", () => Open("navHome")),
                ["nav_split"] = ("open the Split page", () => Open("navSplit")),
                ["nav_reorder"] = ("open the Reorder page", () => Open("navReorder")),
                ["screenshot"] = ("save what the window shows as a PNG next to the report", Screenshot),
                ["sidebar_fits"] = ("every sidebar entry fits without scrolling at the default window size, all sections expanded", SidebarFits),
                ["gc"] = ("force a full garbage collection", CollectGarbage),
            };
        }

        // Set by a step that writes to the error log on purpose
        public bool ErrorLogExpected { get; private set; }

        public async Task RunAsync(IEnumerable<string> steps)
        {
            _report.Check(AppSettings.Folder.StartsWith(_folder), "settings point at the working folder");
            foreach (string step in steps)
            {
                if (!_steps.TryGetValue(step, out var found))
                {
                    _report.Check(false, $"unknown step \"{step}\"; the steps are: {string.Join(", ", _steps.Keys)}");
                    continue;
                }
                _report.Log($"step {step}: {found.Description}");
                try
                {
                    await found.Run();
                }
                catch (Exception ex)
                {
                    _report.Check(false, $"step {step} threw: {ex}");
                }
            }
            await Ui.Idle();
        }

        private async Task Open(string navItem)
        {
            Ui.Navigate(_window, navItem);
            await Ui.Idle();
        }

        private async Task WaitFor(Func<bool> condition, string what) =>
            _report.Check(await Ui.WaitFor(condition), what);

        // --- Split ---

        private async Task SplitOpen()
        {
            await Open("navSplit");
            _dialogs.Expect("Open dialog (Split)", (title, _) => title == "Open", Answer.FileName(_a));
            await Ui.ClickAsync(_window, _vm.Split.OpenFileCommand);
            await WaitFor(() => _vm.Split.SourcePath == _a && _vm.Split.PageCountInfo.StartsWith("Enter a page"), "Split: the picked file is read");
            await _vm.Split.Thumbnails.Loading;
            _report.Check(_vm.Split.Thumbnails.Pages.Count == 4 && _vm.Split.Thumbnails.Pages.All(p => p.Image != null), "Split: 4 page previews rendered");
        }

        private async Task SplitLoad()
        {
            await Open("navSplit");
            await _vm.Split.UpdateFileAsync(_a);
            await _vm.Split.Thumbnails.Loading;
            _report.Check(_vm.Split.Thumbnails.Pages.Count == 4, "Split: 4 page previews rendered");
        }

        private async Task SplitRun()
        {
            await Open("navSplit");
            if (_vm.Split.SourcePath != _a)
                await _vm.Split.UpdateFileAsync(_a);
            _vm.Split.SplitPageNum = "2";
            string part1 = Path.Combine(_folder, "a_part1.pdf"), part2 = Path.Combine(_folder, "a_part2.pdf");
            File.WriteAllText(part2, "keep me");

            _dialogs.Expect("Split overwrite -> No", (title, text) => title == "Confirm Overwrite" && text.Contains("already exist"), Answer.Button(Answer.IDNO));
            await Ui.ClickAsync(_window, _vm.Split.RunSplitCommand);
            await _dialogs.AllHandled();
            await WaitFor(() => !_vm.Split.IsBusy, "Split: idle again");
            _report.Check(File.ReadAllText(part2) == "keep me" && !File.Exists(part1), "Split: No writes nothing");

            _dialogs.Expect("Split overwrite -> Yes", (title, _) => title == "Confirm Overwrite", Answer.Button(Answer.IDYES));
            _dialogs.Expect("Split success -> No", (title, text) => title == "Success" && text.StartsWith("PDF split successfully!"), Answer.Button(Answer.IDNO));
            await Ui.ClickAsync(_window, _vm.Split.RunSplitCommand);
            await _dialogs.AllHandled();
            await WaitFor(() => !_vm.Split.IsBusy, "Split: idle again");
            _report.Check(Pdfs.PageCount(part1) == 2 && Pdfs.PageCount(part2) == 2, "Split: Yes writes both parts");
        }

        // --- Merge ---

        private string MergedPath => Path.Combine(_folder, "Merged.pdf");

        private async Task MergeOpen()
        {
            await Open("navMerge");
            _dialogs.Expect("Open dialog (Merge)", (title, _) => title == "Open", Answer.FileName($"\"{_a}\" \"{_b}\""));
            await Ui.ClickAsync(_window, _vm.Merge.AddFilesCommand);
            await WaitFor(() => _vm.Merge.MergeFiles.Count == 2 && !_vm.Merge.IsAddingFiles, "Merge: both picked files are listed");
        }

        private async Task MergeSaveAs()
        {
            await Open("navMerge");
            _dialogs.Expect("Save As dialog (Merge)", (title, _) => title == "Save As", Answer.FileName(MergedPath));
            await Ui.ClickAsync(_window, _vm.Merge.BrowseOutputCommand);
            await WaitFor(() => _vm.Merge.OutputPath == MergedPath, "Merge: the output is picked");
        }

        private async Task MergeRun()
        {
            await Open("navMerge");
            if (_vm.Merge.MergeFiles.Count == 0)
                await _vm.Merge.AddFilesAsync(new[] { _a, _b });

            _vm.Merge.OutputPath = _a.ToUpperInvariant();
            _dialogs.Expect("Merge same-path -> OK", (_, text) => text.Contains("cannot be one of the files being merged"), Answer.Button(Answer.IDCANCEL));
            await Ui.ClickAsync(_window, _vm.Merge.RunMergeCommand);
            await _dialogs.AllHandled();
            _report.Check(Pdfs.PageCount(_a) == 4, "Merge: an input chosen as the output is untouched");

            _vm.Merge.OutputPath = MergedPath;
            File.WriteAllText(MergedPath, "keep me");
            _dialogs.Expect("Merge overwrite -> No", (title, _) => title == "Confirm Overwrite", Answer.Button(Answer.IDNO));
            await Ui.ClickAsync(_window, _vm.Merge.RunMergeCommand);
            await _dialogs.AllHandled();
            await WaitFor(() => !_vm.Merge.IsBusy, "Merge: idle again");
            _report.Check(File.ReadAllText(MergedPath) == "keep me", "Merge: No keeps the existing output");

            _dialogs.Expect("Merge overwrite -> Yes", (title, _) => title == "Confirm Overwrite", Answer.Button(Answer.IDYES));
            _dialogs.Expect("Merge success -> No", (title, text) => title == "Success" && text.StartsWith("PDFs merged successfully!"), Answer.Button(Answer.IDNO));
            await Ui.ClickAsync(_window, _vm.Merge.RunMergeCommand);
            await _dialogs.AllHandled();
            await WaitFor(() => !_vm.Merge.IsBusy, "Merge: idle again");
            _report.Check(Pdfs.PageCount(MergedPath) == 6, "Merge: Yes writes all 6 pages");
        }

        // --- Rotate and Batch ---

        private async Task RotateFlow()
        {
            await Open("navRotate");
            _dialogs.Expect("Open dialog (Rotate)", (title, _) => title == "Open", Answer.FileName(_a));
            await Ui.ClickAsync(_window, _vm.Rotate.OpenFileCommand);
            await WaitFor(() => _vm.Rotate.SourcePath == _a, "Rotate: the picked file is read");

            string output = Path.Combine(_folder, "turned.pdf");
            _dialogs.Expect("Save As dialog (Rotate)", (title, _) => title == "Save As", Answer.FileName(output));
            await Ui.ClickAsync(_window, _vm.Rotate.BrowseOutputCommand);
            await WaitFor(() => _vm.Rotate.OutputPath == output, "Rotate: the output is picked");

            File.WriteAllText(output, "keep me");
            _dialogs.Expect("Rotate overwrite -> No", (title, _) => title == "Confirm Overwrite", Answer.Button(Answer.IDNO));
            await Ui.ClickAsync(_window, _vm.Rotate.RunRotateCommand);
            await _dialogs.AllHandled();
            _report.Check(File.ReadAllText(output) == "keep me", "Rotate: No keeps the existing output");

            _dialogs.Expect("Rotate overwrite -> Yes", (title, _) => title == "Confirm Overwrite", Answer.Button(Answer.IDYES));
            _dialogs.Expect("Rotate success -> No", (title, text) => title == "Success" && text.StartsWith("PDF rotated successfully!"), Answer.Button(Answer.IDNO));
            await Ui.ClickAsync(_window, _vm.Rotate.RunRotateCommand);
            await _dialogs.AllHandled();
            await WaitFor(() => !_vm.Rotate.IsBusy, "Rotate: idle again");
            _report.Check(Pdfs.PageCount(output) == 4, "Rotate: Yes writes the rotated file");
        }

        private async Task BatchFlow()
        {
            await Open("navBatch");
            _dialogs.Expect("Open dialog (Batch)", (title, _) => title == "Open", Answer.FileName($"\"{_a}\" \"{_b}\""));
            await Ui.ClickAsync(_window, _vm.Batch.AddFilesCommand);
            await WaitFor(() => _vm.Batch.Files.Count == 2, "Batch: both picked files are listed");

            // An earlier run's outputs would add an overwrite question
            File.Delete(Path.Combine(_folder, "a_rotated.pdf"));
            File.Delete(Path.Combine(_folder, "b_rotated.pdf"));
            _dialogs.Expect("Batch finished -> No", (title, text) => title == "Batch Finished" && text.StartsWith("2 of 2 files processed successfully."), Answer.Button(Answer.IDNO));
            await Ui.ClickAsync(_window, _vm.Batch.RunBatchCommand);
            await _dialogs.AllHandled();
            await WaitFor(() => !_vm.Batch.IsBusy, "Batch: idle again");
            _report.Check(Pdfs.PageCount(Path.Combine(_folder, "a_rotated.pdf")) == 4 && Pdfs.PageCount(Path.Combine(_folder, "b_rotated.pdf")) == 2, "Batch: both files rotated");
        }

        // --- Reorder ---

        private async Task ReorderFlow()
        {
            await Open("navReorder");
            // Each page has its own width, so the saved order can be read back
            string source = Pdfs.CreateWithWidths(Path.Combine(_folder, "c.pdf"), 300, 301, 302, 303);
            _dialogs.Expect("Open dialog (Reorder)", (title, _) => title == "Open", Answer.FileName(source));
            await Ui.ClickAsync(_window, _vm.Reorder.OpenFileCommand);
            await WaitFor(() => _vm.Reorder.SourcePath == source && _vm.Reorder.PageCountInfo == "Document has 4 pages.", "Reorder: the picked file is read");
            await _vm.Reorder.Thumbnails.Loading;
            _report.Check(_vm.Reorder.Thumbnails.Pages.Count == 4 && _vm.Reorder.Thumbnails.Pages.All(p => p.Image != null), "Reorder: 4 page previews rendered");

            await Ui.ClickNamedAsync(_window, "Page 4");
            await WaitFor(() => _vm.Reorder.Thumbnails.SelectedPages.SequenceEqual(new[] { 4 }), "Reorder: pressing page 4 selects it");
            await Ui.ClickAsync(_window, _vm.Reorder.MoveToStartCommand);
            await WaitFor(() => _vm.Reorder.OrderSummary == "New order: 4, 1-3", "Reorder: Move to start puts page 4 first");

            string output = Path.Combine(_folder, "c_reordered.pdf");
            _report.Check(_vm.Reorder.OutputPath == output, "Reorder: the output defaults to c_reordered.pdf");
            File.WriteAllText(output, "keep me");
            _dialogs.Expect("Reorder overwrite -> No", (title, _) => title == "Confirm Overwrite", Answer.Button(Answer.IDNO));
            await Ui.ClickAsync(_window, _vm.Reorder.RunReorderCommand);
            await _dialogs.AllHandled();
            _report.Check(File.ReadAllText(output) == "keep me", "Reorder: No keeps the existing output");

            _dialogs.Expect("Reorder overwrite -> Yes", (title, _) => title == "Confirm Overwrite", Answer.Button(Answer.IDYES));
            _dialogs.Expect("Reorder success -> No", (title, text) => title == "Success" && text.StartsWith("Pages reordered successfully!"), Answer.Button(Answer.IDNO));
            await Ui.ClickAsync(_window, _vm.Reorder.RunReorderCommand);
            await _dialogs.AllHandled();
            await WaitFor(() => !_vm.Reorder.IsBusy, "Reorder: idle again");
            _report.Check(Pdfs.PageWidths(output).SequenceEqual(new float[] { 303, 300, 301, 302 }), "Reorder: Yes writes the pages in the new order");
        }

        // --- App-wide ---

        private async Task Screenshot()
        {
            await Ui.Idle();
            string path = _report.SiblingPath($"shot{++_screenshots}.png");
            Ui.SaveScreenshot(_window, path);
            _report.Log("screenshot: " + path);
        }

        private int _screenshots;

        // The sidebar scrolls when it has to, but at the window's default size every tool should
        // show at once. Adding a tool takes about 34 px, so this says how much room is left
        private async Task SidebarFits()
        {
            var sidebar = _vm.Sidebar;
            sidebar.OrganizeExpanded = sidebar.EditExpanded = sidebar.SecurityExpanded = sidebar.ConvertExpanded = sidebar.BatchExpanded = true;
            _window.WindowState = System.Windows.WindowState.Normal;
            _window.Width = 1100;
            _window.Height = 720;
            await Ui.Idle();
            var nav = Ui.ScrollViewerOf((System.Windows.FrameworkElement)_window.FindName("navHome"));
            _report.Log($"sidebar: content {nav.ExtentHeight:0} px, room {nav.ViewportHeight:0} px");
            _report.Check(nav.ScrollableHeight <= 0, $"the sidebar fits at 1100x720 ({nav.ViewportHeight - nav.ExtentHeight:0} px to spare)");
        }

        private async Task CrashOnUiThread()
        {
            ErrorLogExpected = true;
            _dialogs.Expect("Crash report -> OK",
                (title, text) => title == "Error" && text.StartsWith("An unexpected error occurred: harness crash") && text.Contains("Details were saved to:"),
                Answer.Button(Answer.IDCANCEL));
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("harness crash")));
            await _dialogs.AllHandled();
            string[] logs = Directory.Exists(ErrorLog.Folder) ? Directory.GetFiles(ErrorLog.Folder) : Array.Empty<string>();
            _report.Check(logs.Length == 1 && File.ReadAllText(logs[0]).Contains("harness crash"), "crash: the exception is in the working folder's error log");
            _report.Check(_window.IsVisible, "crash: the window is still open");
        }

        private async Task ThemeLight()
        {
            ThemeManager.Apply(AppTheme.Light);
            await Ui.Idle();
            _report.Check(!ThemeManager.IsDark, "the light theme is applied");
        }

        private async Task ThemeThroughHome()
        {
            await Open("navHome");
            _vm.HomeView.Appearance.Theme = AppTheme.Light;
            await Ui.Idle();
            _report.Check(!ThemeManager.IsDark, "the light theme is applied");
            _report.Check(AppSettings.Load().ThemeChoice == AppTheme.Light, "the choice is saved in the working folder's settings");
        }

        private async Task CollectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Ui.Idle();
        }
    }
}
