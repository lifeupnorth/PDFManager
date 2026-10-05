using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using PDFManager;
using PDFManager.Harness;
using PDFManager.Services;

// Loaded into the real PDFManager.exe through DOTNET_STARTUP_HOOKS by run.ps1; the runtime looks
// for exactly this class name, outside any namespace. It does nothing unless run.ps1 set
// PDFMANAGER_HARNESS_REPORT. Before Main runs it points the settings and error-log folders at
// a temp folder, so the user's own are never read or written. Once the main window is up it runs
// the steps in PDFMANAGER_HARNESS_STEPS through the real buttons and dialogs, then posts WM_CLOSE
// to the window, which is what the title-bar X does. The app then exits on its own and run.ps1
// records how. No mouse or keyboard input is ever injected.
internal static class StartupHook
{
    public static void Initialize()
    {
        string? reportPath = Environment.GetEnvironmentVariable("PDFMANAGER_HARNESS_REPORT");
        if (string.IsNullOrEmpty(reportPath))
            return;

        string folder = Path.Combine(Path.GetTempPath(), "PDFManager.Harness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        AppSettings.Folder = Path.Combine(folder, "settings");
        ErrorLog.Folder = Path.Combine(folder, "logs");

        var report = new Report(reportPath);
        report.Log($"loaded into {Environment.ProcessPath} (pid {Environment.ProcessId}); working folder {folder}");
        // Reached when the app closes normally; a crash after this line is in native teardown
        AppDomain.CurrentDomain.ProcessExit += (_, _) => report.Log("ProcessExit raised");

        string steps = Environment.GetEnvironmentVariable("PDFMANAGER_HARNESS_STEPS") ?? "";
        new Thread(() => Drive(report, folder, steps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            IsBackground = true,
            Name = "Harness driver"
        }.Start();
    }

    private static void Drive(Report report, string folder, string[] steps)
    {
        // Startup fires before the StartupUri window exists, so wait for it to load
        Window? window = null;
        var waited = Stopwatch.StartNew();
        while (window == null && waited.Elapsed < TimeSpan.FromSeconds(30))
        {
            Thread.Sleep(200);
            window = Application.Current?.Dispatcher.Invoke(() => Application.Current.MainWindow is { IsLoaded: true } w ? w : null);
        }
        if (window == null)
        {
            report.Check(false, "the main window appeared");
            return;
        }
        IntPtr hwnd = window.Dispatcher.Invoke(() => new WindowInteropHelper(window).Handle);
        Thread.Sleep(1000);

        try
        {
            using var dialogs = new DialogAnswerer(report);
            // The window and its view models belong to the UI thread
            var session = window.Dispatcher.Invoke(() => new Session((MainWindow)window, dialogs, report, folder));
            window.Dispatcher.Invoke(() => session.RunAsync(steps)).Wait();
            Thread.Sleep(1000); // a dialog shown at the very end still gets answered
            dialogs.Stop();

            report.Check(dialogs.Pending == 0, "every expected dialog appeared");
            report.Check(dialogs.Unexpected == 0, "no unexpected dialogs");
            if (!session.ErrorLogExpected)
                report.Check(!Directory.Exists(ErrorLog.Folder) || Directory.GetFiles(ErrorLog.Folder).Length == 0, "nothing was written to the error log");
        }
        catch (Exception ex)
        {
            report.Check(false, "the driver threw: " + ex);
        }

        // Close the window even after a failure, so the run ends and its exit is recorded
        report.Log(report.Failures == 0 ? "ALL PASSED" : $"{report.Failures} FAILED");
        report.Log("closing the main window");
        Native.PostMessage(hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }
}

namespace PDFManager.Harness
{
    // The report file run.ps1 reads: one timestamped line per event, PASS/FAIL per check
    internal sealed class Report
    {
        private readonly string _path;
        private readonly object _sync = new object();

        public Report(string path) => _path = path;

        public int Failures { get; private set; }

        public void Log(string line)
        {
            lock (_sync)
                File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }

        public void Check(bool ok, string what)
        {
            lock (_sync)
            {
                if (!ok)
                    Failures++;
                Log((ok ? "PASS " : "FAIL ") + what);
            }
        }
    }
}
