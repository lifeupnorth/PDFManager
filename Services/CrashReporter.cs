using System;

namespace PDFManager.Services
{
    // What happens to exceptions nothing else caught. App.OnStartup hooks these up to the
    // dispatcher, the AppDomain and the task scheduler. Each one logs first, so the details
    // are kept even if showing the message fails.
    public sealed class CrashReporter
    {
        private readonly IDialogService _dialogs;

        public CrashReporter(IDialogService dialogs) => _dialogs = dialogs;

        // UI-thread exceptions, including ones rethrown from async void commands; the app keeps running
        public void ReportUiException(Exception exception)
        {
            string? logPath = ErrorLog.Write(exception, "Unhandled UI exception");
            _dialogs.Show($"An unexpected error occurred: {exception.Message}{DescribeLog(logPath)}", "Error", DialogIcon.Error);
        }

        // Exceptions on other threads end the process; record them before it goes
        public void ReportFatalException(object exceptionObject)
        {
            if (exceptionObject is not Exception exception)
                return;
            string? logPath = ErrorLog.Write(exception, "Unhandled background exception");
            _dialogs.Show($"PDFManager has to close because of an unexpected error: {exception.Message}{DescribeLog(logPath)}", "Error", DialogIcon.Error);
        }

        // Faulted tasks nobody awaited would otherwise vanish silently
        public void ReportUnobservedTask(Exception exception) => ErrorLog.Write(exception, "Unobserved task exception");

        private static string DescribeLog(string? logPath) =>
            logPath == null ? string.Empty : $"\n\nDetails were saved to:\n{logPath}";
    }
}
