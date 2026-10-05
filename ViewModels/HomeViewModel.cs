namespace PDFManager.ViewModels
{
    /// <summary>
    /// The home tool grid. Its DataTemplate in MainWindow.xaml supplies the view; the tool cards
    /// navigate by checking the matching sidebar item. It also offers the appearance choice.
    /// </summary>
    public class HomeViewModel
    {
        public HomeViewModel(AppearanceViewModel appearance) => Appearance = appearance;

        public AppearanceViewModel Appearance { get; }
    }
}
