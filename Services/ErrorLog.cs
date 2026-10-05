#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PDFManager.Services
{
    // Appends unexpected exceptions, with their stack traces, to a daily log file so problems
    // users hit can be diagnosed later. Writing the log must never throw: it runs inside the
    // crash handlers, where a second exception would hide the first.
    public static class ErrorLog
    {
        private const int DaysToKeep = 14;
        private static readonly object Sync = new object();

        public static string Folder { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PDFManager", "logs");

        public static string CurrentFilePath => Path.Combine(Folder, $"errors-{DateTime.Now:yyyy-MM-dd}.log");

        // Returns the file written to, or null if the log could not be written
        public static string? Write(Exception exception, string source)
        {
            try
            {
                var entry = new StringBuilder()
                    .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}")
                    .AppendLine($"PDFManager {typeof(ErrorLog).Assembly.GetName().Version}, {Environment.OSVersion}, .NET {Environment.Version}")
                    .AppendLine(exception.ToString())
                    .AppendLine();

                lock (Sync)
                {
                    Directory.CreateDirectory(Folder);
                    string path = CurrentFilePath;
                    bool isNewFile = !File.Exists(path);
                    File.AppendAllText(path, entry.ToString());
                    if (isNewFile)
                        DeleteOldLogs();
                    return path;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void DeleteOldLogs()
        {
            // File names sort by date, so everything before the newest DaysToKeep files is old
            foreach (var oldLog in Directory.GetFiles(Folder, "errors-*.log").OrderByDescending(f => f).Skip(DaysToKeep))
            {
                try { File.Delete(oldLog); } catch { }
            }
        }
    }
}
