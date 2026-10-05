using System;
using System.IO;
using PDFManager.Services;

namespace PDFManager.Tests
{
    // What App's handlers for unhandled exceptions log and tell the user. They redirect the
    // static ErrorLog.Folder, so they share a collection with ErrorLogTests.
    [Collection("Error log")]
    public class CrashReporterTests : IDisposable
    {
        private readonly string _originalLogFolder = ErrorLog.Folder;
        private readonly TestPdfs _files = new TestPdfs();
        private readonly FakeDialogService _dialogs = new FakeDialogService();
        private readonly CrashReporter _reporter;

        public CrashReporterTests()
        {
            ErrorLog.Folder = Path.Combine(_files.Folder, "logs");
            _reporter = new CrashReporter(_dialogs);
        }

        public void Dispose()
        {
            ErrorLog.Folder = _originalLogFolder;
            _files.Dispose();
        }

        private static string Log => File.ReadAllText(ErrorLog.CurrentFilePath);

        // A log folder that cannot be created, because a file is in the way
        private void BreakTheLog()
        {
            string blocker = _files.PathFor("blocker");
            File.WriteAllText(blocker, "");
            ErrorLog.Folder = Path.Combine(blocker, "logs");
        }

        [Fact]
        public void A_UI_exception_is_logged_and_the_message_says_where()
        {
            _reporter.ReportUiException(new InvalidOperationException("boom"));

            Assert.Equal(new DialogCall(false, $"An unexpected error occurred: boom\n\nDetails were saved to:\n{ErrorLog.CurrentFilePath}", "Error", DialogIcon.Error), Assert.Single(_dialogs.Calls));
            Assert.Contains("Unhandled UI exception", Log);
            Assert.Contains("System.InvalidOperationException: boom", Log);
        }

        [Fact]
        public void A_UI_exception_is_still_reported_when_the_log_cannot_be_written()
        {
            BreakTheLog();

            _reporter.ReportUiException(new InvalidOperationException("boom"));

            Assert.Equal(new DialogCall(false, "An unexpected error occurred: boom", "Error", DialogIcon.Error), Assert.Single(_dialogs.Calls));
        }

        [Fact]
        public void A_fatal_exception_is_logged_and_the_user_is_told_the_app_will_close()
        {
            _reporter.ReportFatalException(new NullReferenceException("gone"));

            Assert.Equal(new DialogCall(false, $"PDFManager has to close because of an unexpected error: gone\n\nDetails were saved to:\n{ErrorLog.CurrentFilePath}", "Error", DialogIcon.Error), Assert.Single(_dialogs.Calls));
            Assert.Contains("Unhandled background exception", Log);
        }

        [Fact]
        public void A_fatal_exception_is_still_reported_when_the_log_cannot_be_written()
        {
            BreakTheLog();

            _reporter.ReportFatalException(new NullReferenceException("gone"));

            Assert.Equal("PDFManager has to close because of an unexpected error: gone", Assert.Single(_dialogs.Calls).Message);
        }

        [Fact]
        public void Something_thrown_that_is_not_an_exception_is_left_alone()
        {
            _reporter.ReportFatalException("not an exception");

            Assert.Empty(_dialogs.Calls);
            Assert.False(Directory.Exists(ErrorLog.Folder));
        }

        [Fact]
        public void An_unobserved_task_exception_is_logged_without_a_message()
        {
            _reporter.ReportUnobservedTask(new AggregateException(new TimeoutException("late")));

            Assert.Empty(_dialogs.Calls);
            Assert.Contains("Unobserved task exception", Log);
            Assert.Contains("late", Log);
        }
    }
}
