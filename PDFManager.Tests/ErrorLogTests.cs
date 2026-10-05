using System;
using System.IO;
using System.Linq;
using PDFManager.Services;

namespace PDFManager.Tests
{
    // ErrorLog.Folder is static, so every class that changes it joins the "Error log"
    // collection: xUnit runs the tests within a collection one at a time
    [Collection("Error log")]
    public class ErrorLogTests : IDisposable
    {
        private readonly string _originalFolder = ErrorLog.Folder;
        private readonly TestPdfs _temp = new TestPdfs();

        public ErrorLogTests() => ErrorLog.Folder = Path.Combine(_temp.Folder, "logs");

        public void Dispose()
        {
            ErrorLog.Folder = _originalFolder;
            _temp.Dispose();
        }

        [Fact]
        public void Write_records_the_source_and_the_full_exception_in_todays_file()
        {
            Exception thrown = CatchThrown(() => throw new NullReferenceException("boom"));

            string? path = ErrorLog.Write(thrown, "Split");

            Assert.Equal(Path.Combine(ErrorLog.Folder, $"errors-{DateTime.Now:yyyy-MM-dd}.log"), path);
            string text = File.ReadAllText(path!);
            Assert.Contains("] Split", text);
            Assert.Contains("System.NullReferenceException: boom", text);
            Assert.Contains(nameof(CatchThrown), text); // the stack trace
        }

        [Fact]
        public void Write_appends_rather_than_replacing_earlier_entries()
        {
            ErrorLog.Write(new InvalidCastException("first"), "Merge");
            string? path = ErrorLog.Write(new FormatException("second"), "Rotate");

            string text = File.ReadAllText(path!);
            Assert.Contains("first", text);
            Assert.Contains("second", text);
            Assert.True(text.IndexOf("first") < text.IndexOf("second"));
        }

        [Fact]
        public void Write_keeps_only_the_newest_two_weeks_of_files()
        {
            Directory.CreateDirectory(ErrorLog.Folder);
            var oldLogs = Enumerable.Range(1, 20).Select(day => Path.Combine(ErrorLog.Folder, $"errors-2020-01-{day:00}.log")).ToList();
            foreach (var log in oldLogs)
                File.WriteAllText(log, "old");
            string unrelated = Path.Combine(ErrorLog.Folder, "notes.txt");
            File.WriteAllText(unrelated, "keep me");

            string? path = ErrorLog.Write(new Exception("new"), "Test");

            var remaining = Directory.GetFiles(ErrorLog.Folder, "errors-*.log").OrderBy(f => f).ToList();
            Assert.Equal(14, remaining.Count);
            Assert.Equal(path, remaining.Last());
            Assert.Equal(oldLogs.Skip(7), remaining.Take(13));
            Assert.True(File.Exists(unrelated));
        }

        [Fact]
        public void Write_returns_null_instead_of_throwing_when_the_folder_cannot_be_created()
        {
            string file = Path.Combine(_temp.Folder, "a-file");
            File.WriteAllText(file, "");
            ErrorLog.Folder = Path.Combine(file, "logs");

            Assert.Null(ErrorLog.Write(new Exception("lost"), "Test"));
        }

        private static Exception CatchThrown(Action action)
        {
            try { action(); }
            catch (Exception ex) { return ex; }
            throw new InvalidOperationException("The action did not throw.");
        }
    }
}
