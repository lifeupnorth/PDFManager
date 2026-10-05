using System;
using System.Threading.Tasks;
using System.Windows;
using PDFManager.Services;

namespace PDFManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Before StartupUri creates the window, so it never shows in the wrong colours
            ThemeManager.Apply(AppSettings.Load().ThemeChoice);

            // UI-thread exceptions, including ones rethrown from async void commands:
            // log them and keep the app running
            DispatcherUnhandledException += (sender, args) =>
            {
                string? logPath = ErrorLog.Write(args.Exception, "Unhandled UI exception");
                MessageBox.Show($"An unexpected error occurred: {args.Exception.Message}{DescribeLog(logPath)}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            // Exceptions on other threads end the process; record them before it goes
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (args.ExceptionObject is not Exception ex)
                    return;
                string? logPath = ErrorLog.Write(ex, "Unhandled background exception");
                MessageBox.Show($"PDFManager has to close because of an unexpected error: {ex.Message}{DescribeLog(logPath)}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // Faulted tasks nobody awaited would otherwise vanish silently
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                ErrorLog.Write(args.Exception, "Unobserved task exception");
                args.SetObserved();
            };
        }

        private static string DescribeLog(string? logPath) =>
            logPath == null ? string.Empty : $"\n\nDetails were saved to:\n{logPath}";
    }
}
