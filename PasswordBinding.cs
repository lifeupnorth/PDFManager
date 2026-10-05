using System.Windows;
using System.Windows.Controls;

namespace PDFManager
{
    /// <summary>
    /// Lets a PasswordBox's password bind to a view-model property. PasswordBox.Password is not a
    /// dependency property, so this attached property mirrors it in both directions. Views are
    /// rebuilt from their DataTemplate on every visit, and the binding restores the box each time.
    /// </summary>
    public static class PasswordBinding
    {
        // The default is null so the first bound value (usually "") still counts as a change and hooks the box up
        public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
            "Password", typeof(string), typeof(PasswordBinding),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

        private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
            "IsUpdating", typeof(bool), typeof(PasswordBinding));

        public static string GetPassword(DependencyObject d) => (string)d.GetValue(PasswordProperty);
        public static void SetPassword(DependencyObject d, string value) => d.SetValue(PasswordProperty, value);

        private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not PasswordBox box)
                return;

            box.PasswordChanged -= Box_PasswordChanged;
            // Skip writing back the value the box itself just reported, which would move the caret
            if (!(bool)box.GetValue(IsUpdatingProperty))
                box.Password = (string)e.NewValue ?? string.Empty;
            box.PasswordChanged += Box_PasswordChanged;
        }

        private static void Box_PasswordChanged(object sender, RoutedEventArgs e)
        {
            var box = (PasswordBox)sender;
            box.SetValue(IsUpdatingProperty, true);
            SetPassword(box, box.Password);
            box.SetValue(IsUpdatingProperty, false);
        }
    }
}
