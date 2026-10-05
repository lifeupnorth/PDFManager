using System;
using System.Windows;
using Microsoft.Win32;
using PDFManager.Services;

namespace PDFManager
{
    /// <summary>
    /// Switches the app between its light and dark palettes while it runs. Every colour in the
    /// XAML is a DynamicResource, so replacing the merged palette dictionary repaints everything.
    /// With AppTheme.System the palette follows Windows' "app mode" and changes along with it.
    /// </summary>
    public static class ThemeManager
    {
        private static AppTheme _choice = AppTheme.Dark;
        private static bool? _appliedDark;

        static ThemeManager()
        {
            // Windows reports a change of app mode as a General preference change
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (_choice == AppTheme.System && e.Category == UserPreferenceCategory.General)
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(AppTheme.System)));
            };
        }

        public static bool IsDark => _appliedDark ?? true;

        public static bool ResolveIsDark(AppTheme choice, bool windowsUsesLightApps) => choice switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => !windowsUsesLightApps
        };

        // Windows' Settings > Personalization > Colors > "Choose your default app mode"
        public static bool WindowsUsesLightApps()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is not int value || value != 0; // Windows' default is light
            }
            catch
            {
                return true;
            }
        }

        public static void Apply(AppTheme choice)
        {
            _choice = choice;
            bool dark = ResolveIsDark(choice, WindowsUsesLightApps());
            if (dark == _appliedDark || Application.Current == null)
                return;

            // App.xaml merges exactly one dictionary: the palette
            var palette = new ResourceDictionary
            {
                Source = new Uri(dark ? "/PDFManager;component/Themes/Dark.xaml" : "/PDFManager;component/Themes/Light.xaml", UriKind.Relative)
            };
            Application.Current.Resources.MergedDictionaries[0] = palette;
            _appliedDark = dark;

            foreach (Window window in Application.Current.Windows)
                SetTitleBar(window, dark);
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // Asks DWM to draw the title bar dark or light to match. Ignored on Windows versions without it,
        // and for windows that have no handle yet (they call this again from OnSourceInitialized)
        public static void SetTitleBar(Window window, bool dark)
        {
            try
            {
                const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;
                int enabled = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
            }
            catch { }
        }
    }
}
