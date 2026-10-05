using System;
using System.IO;
using System.Runtime.CompilerServices;
using PDFManager.Services;

namespace PDFManager.Tests
{
    // AppSettings.Folder and ErrorLog.Folder are static and point at %LocalAppData%\PDFManager by
    // default. Before any test runs, they are moved to a temp folder for this run, so no test can
    // read the user's settings.json or write to their error log. Classes that need a folder of
    // their own still redirect them, and put this one back afterwards.
    internal static class TestFolders
    {
        public static string Root { get; } = Path.Combine(Path.GetTempPath(), "PDFManager.Tests", "run-" + Guid.NewGuid().ToString("N"));

        [ModuleInitializer]
        internal static void PointAppFoldersAtTemp()
        {
            AppSettings.Folder = Path.Combine(Root, "settings");
            ErrorLog.Folder = Path.Combine(Root, "logs");
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { Directory.Delete(Root, true); } catch { }
            };
        }
    }

    public class TestFoldersTests
    {
        private static readonly string UsersOwnFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PDFManager");

        private static bool IsTheUsersOwn(string folder)
        {
            string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            return full.Equals(UsersOwnFolder, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(UsersOwnFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        // Other classes may have redirected them for a moment, but never to the user's own folder
        [Fact]
        public void Tests_never_use_the_users_own_settings_or_error_log()
        {
            Assert.False(IsTheUsersOwn(AppSettings.Folder), AppSettings.Folder);
            Assert.False(IsTheUsersOwn(ErrorLog.Folder), ErrorLog.Folder);
        }
    }
}
