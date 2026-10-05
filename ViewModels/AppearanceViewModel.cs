#nullable enable
using System;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    /// <summary>The theme choice offered on the Home page: applied at once and remembered.</summary>
    public class AppearanceViewModel : ObservableObject
    {
        private readonly Action<AppTheme> _apply;

        public AppearanceViewModel(Action<AppTheme> apply)
        {
            _apply = apply;
            _theme = AppSettings.Load().ThemeChoice;
        }

        private AppTheme _theme;
        public AppTheme Theme
        {
            get => _theme;
            set
            {
                if (!SetProperty(ref _theme, value))
                    return;
                _apply(value);
                // Re-read so only this setting changes, whatever else the file holds
                var settings = AppSettings.Load();
                settings.ThemeChoice = value;
                AppSettings.Save(settings);
            }
        }
    }
}
