using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        public SplitViewModel Split { get; }
        public MergeViewModel Merge { get; }
        public RotateViewModel Rotate { get; }
        public ExtractViewModel Extract { get; }
        public MetadataViewModel Metadata { get; }
        public ProtectViewModel Protect { get; }
        public UnlockViewModel Unlock { get; }
        public WatermarkViewModel Watermark { get; }
        public BatchViewModel Batch { get; }
        public HomeViewModel HomeView { get; }

        // The view model shown in the main area; its DataTemplate supplies the view
        private object _currentView;
        public object CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        public MainViewModel(IPdfService pdfService, IPageRenderer pageRenderer)
        {
            Split = new SplitViewModel(pdfService, pageRenderer);
            Merge = new MergeViewModel(pdfService);
            Rotate = new RotateViewModel(pdfService, pageRenderer);
            Extract = new ExtractViewModel(pdfService, pageRenderer);
            Metadata = new MetadataViewModel(pdfService);
            Protect = new ProtectViewModel(pdfService);
            Unlock = new UnlockViewModel(pdfService);
            Watermark = new WatermarkViewModel(pdfService, pageRenderer);
            Batch = new BatchViewModel(pdfService);
            HomeView = new HomeViewModel();
            _currentView = HomeView;
        }
    }
}