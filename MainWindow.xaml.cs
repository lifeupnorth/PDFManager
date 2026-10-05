using System.Windows;
using PDFManager.ViewModels;
using PDFManager.Services;

namespace PDFManager
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new MainViewModel(new PdfService());
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (tabControl == null || DataContext is MainViewModel vm)
            {
                var navItems = new object[] { vm.HomeView, vm.Split, vm.Merge, vm.Rotate };
                if (sender is System.Windows.Controls.RadioButton rb && int.TryParse(rb.Tag?.ToString(), out int index))
                {
                    if (index >= 0 && index < navItems.Length)
                        tabControl.SelectedIndex = index;
                }
            }
        }
    }
}