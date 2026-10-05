using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class MergeViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;
        private readonly IDialogService _dialogs;

        public ObservableCollection<string> MergeFiles { get; } = new ObservableCollection<string>();

        // Adds can overlap (a drop while picked files are still being checked), so count
        // them and keep the add buttons disabled until every validation has finished
        private int _validationsInFlight;
        public bool IsAddingFiles => _validationsInFlight > 0;

        private string _outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Merged File.pdf");
        public string OutputPath
        {
            get => _outputPath;
            set => SetProperty(ref _outputPath, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ICommand AddFilesCommand { get; }
        public ICommand RemoveFileCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand RunMergeCommand { get; }

        public MergeViewModel(IPdfService pdfService, IDialogService dialogs)
        {
            _pdfService = pdfService;
            _dialogs = dialogs;

            AddFilesCommand = new RelayCommand(ExecuteAddFiles, _ => !IsAddingFiles);
            RemoveFileCommand = new RelayCommand(ExecuteRemoveFiles, CanExecuteRemoveFiles);
            MoveUpCommand = new RelayCommand(ExecuteMoveUp, CanExecuteMoveUp);
            MoveDownCommand = new RelayCommand(ExecuteMoveDown, CanExecuteMoveDown);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput);
            RunMergeCommand = new RelayCommand(ExecuteRunMerge, CanExecuteRunMerge);

            // Collection changes are not property changes, so ask for a CanExecute requery here too
            MergeFiles.CollectionChanged += (s, e) => CommandManager.InvalidateRequerySuggested();
        }

        private async void ExecuteAddFiles(object? parameter) =>
            await AddFilesAsync(_dialogs.PickFiles("PDF Files (*.pdf)|*.pdf"));

        public async Task AddFilesAsync(IEnumerable<string> files)
        {
            var existing = new HashSet<string>(MergeFiles, StringComparer.OrdinalIgnoreCase);
            var candidates = files.Where(existing.Add).ToList();
            if (candidates.Count == 0)
                return;

            var accepted = new List<string>();
            var skipped = new List<string>();

            _validationsInFlight++;
            OnPropertyChanged(nameof(IsAddingFiles));
            try
            {
                // Reading each PDF is slow enough to freeze the window when several
                // large files are added at once, so validate them off the UI thread
                await Task.Run(() =>
                {
                    foreach (var file in candidates)
                    {
                        try
                        {
                            _pdfService.GetPageCount(file);
                            accepted.Add(file);
                        }
                        catch (iText.Kernel.Exceptions.BadPasswordException)
                        {
                            skipped.Add($"{Path.GetFileName(file)} (password-protected)");
                        }
                        catch
                        {
                            skipped.Add($"{Path.GetFileName(file)} (not a valid PDF)");
                        }
                    }
                });
            }
            finally
            {
                _validationsInFlight--;
                OnPropertyChanged(nameof(IsAddingFiles));
            }

            // An overlapping add may have listed some of the same files while this one was validating
            var current = new HashSet<string>(MergeFiles, StringComparer.OrdinalIgnoreCase);
            foreach (var file in accepted)
                if (current.Add(file))
                    MergeFiles.Add(file);

            if (skipped.Any())
                _dialogs.Show($"The following files were not added:\n{string.Join("\n", skipped)}", "Some Files Skipped", DialogIcon.Warning);
        }

        // Remove and the move commands get the list's SelectedItems as their parameter.
        // Removing works on any number of files; moving only on exactly one.
        private static string? SingleSelected(object? parameter) =>
            parameter is System.Collections.IList { Count: 1 } selected ? selected[0] as string : null;

        private bool CanExecuteRemoveFiles(object? parameter) => parameter is System.Collections.IList { Count: > 0 };

        private void ExecuteRemoveFiles(object? parameter)
        {
            if (parameter is not System.Collections.IList selectedItems)
                return;

            // Snapshot first: SelectedItems shrinks as items are removed
            var selected = new HashSet<string>(selectedItems.Cast<string>(), StringComparer.OrdinalIgnoreCase);
            for (int i = MergeFiles.Count - 1; i >= 0; i--)
                if (selected.Contains(MergeFiles[i]))
                    MergeFiles.RemoveAt(i);
        }

        private bool CanExecuteMoveUp(object? parameter)
        {
            string? file = SingleSelected(parameter);
            return file != null && MergeFiles.IndexOf(file) > 0;
        }

        private void ExecuteMoveUp(object? parameter)
        {
            if (SingleSelected(parameter) is not string file)
                return;
            int index = MergeFiles.IndexOf(file);
            if (index > 0)
                MergeFiles.Move(index, index - 1);
        }

        private bool CanExecuteMoveDown(object? parameter)
        {
            string? file = SingleSelected(parameter);
            int index = file == null ? -1 : MergeFiles.IndexOf(file);
            return index >= 0 && index < MergeFiles.Count - 1;
        }

        private void ExecuteMoveDown(object? parameter)
        {
            if (SingleSelected(parameter) is not string file)
                return;
            int index = MergeFiles.IndexOf(file);
            if (index >= 0 && index < MergeFiles.Count - 1)
                MergeFiles.Move(index, index + 1);
        }

        private void ExecuteBrowseOutput(object? parameter)
        {
            string? path = _dialogs.PickSaveFile("PDF Files (*.pdf)|*.pdf", ".pdf", "Merged File.pdf");
            if (path != null)
                OutputPath = path;
        }

        private bool CanExecuteRunMerge(object? parameter)
        {
            if (IsBusy || MergeFiles.Count < 2 || string.IsNullOrWhiteSpace(OutputPath))
                return false;

            if (!Directory.Exists(Path.GetDirectoryName(OutputPath)))
                return false;

            return true;
        }

        private async void ExecuteRunMerge(object? parameter) => await MergeAsync();

        // The Merge button's work, including its messages and confirmations
        public async Task MergeAsync()
        {
            var missingFiles = MergeFiles.Where(f => !File.Exists(f)).ToList();
            if (missingFiles.Any())
            {
                _dialogs.Show($"The following files no longer exist:\n{string.Join("\n", missingFiles)}", "Validation Error", DialogIcon.Warning);
                return;
            }

            // PdfWriter truncates the output as soon as it is created, and a failed merge
            // deletes it, so the output must never be one of the inputs
            string fullOutputPath = Path.GetFullPath(OutputPath);
            if (MergeFiles.Any(f => string.Equals(Path.GetFullPath(f), fullOutputPath, StringComparison.OrdinalIgnoreCase)))
            {
                _dialogs.Show("The output file cannot be one of the files being merged. Please choose a different output path.", "Validation Error", DialogIcon.Warning);
                return;
            }

            if (File.Exists(OutputPath) && !_dialogs.Confirm(
                    $"\"{OutputPath}\" already exists and will be overwritten.\n\nContinue?",
                    "Confirm Overwrite", DialogIcon.Warning))
                return;

            // Read the inputs here: the work runs on a background thread, and files can
            // still be dropped onto the list while it does
            string outputPath = OutputPath;
            var sourceFiles = MergeFiles.ToList();
            try
            {
                IsBusy = true;
                await Task.Run(() => _pdfService.MergePdfs(outputPath, sourceFiles));

                if (_dialogs.Confirm("PDFs merged successfully!\n\nOpen the output folder?", "Success", DialogIcon.Information))
                    _dialogs.ShowInExplorer(outputPath);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                // InvalidOperationException carries the service's own validation messages
                if (ex is not InvalidOperationException)
                    ErrorLog.Write(ex, "Merge");
                _dialogs.Show($"Error merging PDFs: {ex.Message}", "Error", DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}