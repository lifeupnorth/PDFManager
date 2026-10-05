using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;

namespace PDFManager
{
    public class FileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string path ? Path.GetFileName(path) : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Shows an element when a string is non-empty (or, with VisibleWhenEmpty, when it is empty).
    /// Used to switch a tool's workspace between its "pick a file" and "file selected" states.
    /// </summary>
    public class StringVisibilityConverter : IValueConverter
    {
        public bool VisibleWhenEmpty { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEmpty = string.IsNullOrWhiteSpace(value?.ToString());
            return isEmpty == VisibleWhenEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Checks a radio button when the bound value equals its ConverterParameter, and writes the
    /// parameter back when the button is checked. Used for the rotation angle options.
    /// </summary>
    public class RadioValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true
                ? System.Convert.ChangeType(parameter, targetType, CultureInfo.InvariantCulture)
                : Binding.DoNothing; // an option being unchecked by its group says nothing about the value
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
