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

            // Unhandled exceptions are logged and reported; see CrashReporter
            var crashes = new CrashReporter(new WindowsDialogService());
            DispatcherUnhandledException += (sender, args) =>
            {
                crashes.ReportUiException(args.Exception);
                args.Handled = true; // keep running
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => crashes.ReportFatalException(args.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                crashes.ReportUnobservedTask(args.Exception);
                args.SetObserved();
            };
        }
    }
}
