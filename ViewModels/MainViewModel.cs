using System;
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
        public PdfToImageViewModel PdfToImage { get; }
        public ImagesToPdfViewModel ImagesToPdf { get; }
        public HomeViewModel HomeView { get; }
        public SidebarViewModel Sidebar { get; } = new SidebarViewModel();

        // The view model shown in the main area; its DataTemplate supplies the view
        private object _currentView;
        public object CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        public MainViewModel(IPdfService pdfService, IPageRenderer pageRenderer, IDialogService dialogs, Action<AppTheme> applyTheme)
        {
            Split = new SplitViewModel(pdfService, pageRenderer, dialogs);
            Merge = new MergeViewModel(pdfService, dialogs);
            Rotate = new RotateViewModel(pdfService, pageRenderer, dialogs);
            Extract = new ExtractViewModel(pdfService, pageRenderer, dialogs);
            Metadata = new MetadataViewModel(pdfService, dialogs);
            Protect = new ProtectViewModel(pdfService, dialogs);
            Unlock = new UnlockViewModel(pdfService, dialogs);
            Watermark = new WatermarkViewModel(pdfService, pageRenderer, dialogs);
            Batch = new BatchViewModel(pdfService, dialogs);
            PdfToImage = new PdfToImageViewModel(pdfService, pageRenderer, dialogs);
            ImagesToPdf = new ImagesToPdfViewModel(pdfService, dialogs);
            HomeView = new HomeViewModel(new AppearanceViewModel(applyTheme));
            _currentView = HomeView;
        }
    }
}