using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        public SplitViewModel Split { get; }
        public MergeViewModel Merge { get; }
        public RotateViewModel Rotate { get; }
        public object HomeView { get; }

        public MainViewModel(IPdfService pdfService)
        {
            Split = new SplitViewModel(pdfService);
            Merge = new MergeViewModel(pdfService);
            Rotate = new RotateViewModel(pdfService);
            HomeView = new object(); // Just a placeholder for Home
        }
    }
}