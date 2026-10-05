using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PDFManager;
using PDFManager.Services;
using PDFManager.ViewModels;

namespace PDFManager.Tests
{
    // Runs the real PDFManager.exe, shows page thumbnails, lets them be garbage collected and
    // closes the window. Once rendered pages were released, the process used to crash on exit in
    // the graphics driver (Qualcomm Adreno on ARM64); see WindowsPageRenderer. The window is
    // on screen for a few seconds while this runs.
    public class ExitCrashTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "PDFManagerExitTest_" + Guid.NewGuid().ToString("N"));

        public ExitCrashTests() => Directory.CreateDirectory(_folder);

        public void Dispose()
        {
            try { Directory.Delete(_folder, true); } catch { }
        }

        [Fact]
        public void App_exits_cleanly_after_page_previews_are_released()
        {
            string report = Path.Combine(_folder, "report.txt");
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "PDFManager.exe")) { UseShellExecute = false };
            start.Environment["DOTNET_STARTUP_HOOKS"] = typeof(ExitCrashTests).Assembly.Location;
            start.Environment[StartupHook.FolderVariable] = _folder;

            using var app = Process.Start(start)!;
            bool exited = app.WaitForExit(60_000);
            if (!exited)
                app.Kill();

            string log = File.Exists(report) ? File.ReadAllText(report) : "(no report)";
            Assert.True(exited, "the app did not close: " + log);
            Assert.Contains("rendered 4 of 4 page previews", log);
            Assert.Contains("closing", log);
            Assert.Equal(0, app.ExitCode);
        }
    }
}

// Loaded into PDFManager.exe through DOTNET_STARTUP_HOOKS by ExitCrashTests; the runtime looks
// for exactly this class name, outside any namespace. It does nothing unless the test set the
// folder variable. Settings and error logs go to that folder, never to the user's own.
internal static class StartupHook
{
    public const string FolderVariable = "PDFMANAGER_EXIT_TEST_FOLDER";

    private static string _folder = "";

    public static void Initialize()
    {
        _folder = Environment.GetEnvironmentVariable(FolderVariable) ?? "";
        if (_folder.Length == 0)
            return;
        AppSettings.Folder = Path.Combine(_folder, "settings");
        ErrorLog.Folder = Path.Combine(_folder, "logs");
        new Thread(Drive) { IsBackground = true }.Start();
    }

    private static void Report(string line) => File.AppendAllText(Path.Combine(_folder, "report.txt"), line + Environment.NewLine);

    private static void Drive()
    {
        try
        {
            // Wait for the StartupUri window
            Window? window = null;
            for (int i = 0; i < 150 && window == null; i++)
            {
                Thread.Sleep(200);
                window = Application.Current?.Dispatcher.Invoke(() => Application.Current.MainWindow is { IsLoaded: true } w ? w : null);
            }
            if (window == null)
            {
                Report("the main window never appeared");
                return;
            }

            window.Dispatcher.Invoke(async () =>
            {
                try
                {
                    var vm = (MainViewModel)window.DataContext;
                    ((RadioButton)window.FindName("navSplit")).IsChecked = true;

                    string pdf = Path.Combine(_folder, "four pages.pdf");
                    using (var doc = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfWriter(pdf)))
                        for (int i = 0; i < 4; i++)
                            doc.AddNewPage();

                    await vm.Split.UpdateFileAsync(pdf);
                    await vm.Split.Thumbnails.Loading;
                    var pages = vm.Split.Thumbnails.Pages;
                    Report($"rendered {pages.Count(p => p.Image != null)} of {pages.Count} page previews");

                    // Release every rendered document, as any later garbage collection would
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                }
                catch (Exception ex)
                {
                    Report("failed: " + ex);
                }
                Report("closing");
                window.Close();
            }).Wait();
        }
        catch (Exception ex)
        {
            Report("failed: " + ex);
        }
    }
}
