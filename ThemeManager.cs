using System;
using System.Windows;
using Microsoft.Win32;
using PDFManager.Services;

namespace PDFManager
{
    /// <summary>
    /// Switches the app between light and dark while it runs. The look is WPF's Fluent theme (as in
    /// ScanManager): setting Application.ThemeMode swaps its light and dark resources, repaints every
    /// DynamicResource colour and recolours the title bar. With AppTheme.System, Fluent follows
    /// Windows' "app mode" by itself.
    /// </summary>
    public static class ThemeManager
    {
        private static AppTheme _choice = AppTheme.System;
        private static bool? _appliedDark;

        static ThemeManager()
        {
            // Windows reports a change of app mode as a General preference change. Fluent repaints
            // on its own; this only keeps IsDark true to what is on screen
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (_choice == AppTheme.System && e.Category == UserPreferenceCategory.General)
                    _appliedDark = ResolveIsDark(AppTheme.System, WindowsUsesLightApps());
            };
        }

        public static bool IsDark => _appliedDark ?? ResolveIsDark(_choice, WindowsUsesLightApps());

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

        public static ThemeMode ModeFor(AppTheme choice) => choice switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System
        };

        public static void Apply(AppTheme choice)
        {
            _choice = choice;
            _appliedDark = ResolveIsDark(choice, WindowsUsesLightApps());
            if (Application.Current is Application app)
                app.ThemeMode = ModeFor(choice);
        }
    }
}
