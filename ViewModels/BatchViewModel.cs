using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public enum BatchAction { Rotate, Watermark, Extract, Protect, Unlock }

    public enum BatchFileStatus { Waiting, Working, Done, Failed, Skipped }

    /// <summary>One file in the batch list, with the outcome of the last run.</summary>
    public class BatchFile : ObservableObject
    {
        public BatchFile(string path) => Path = path;

        public string Path { get; }
        public string FileName => System.IO.Path.GetFileName(Path);
        public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

        private BatchFileStatus _status;
        public BatchFileStatus Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                    OnPropertyChanged(nameof(StatusText));
            }
        }

        private string _message = string.Empty;
        public string Message
        {
            get => _message;
            set
            {
                if (SetProperty(ref _message, value))
                    OnPropertyChanged(nameof(StatusText));
            }
        }

        public string StatusText => Status switch
        {
            BatchFileStatus.Working => "Working...",
            BatchFileStatus.Skipped => "Not processed: the run was cancelled.",
            _ => Message
        };

        // The list's row tooltip shows the item itself
        public override string ToString() => Path;
    }

    public class BatchViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;
        private CancellationTokenSource? _cancel;

        public ObservableCollection<BatchFile> Files { get; } = new ObservableCollection<BatchFile>();

        private BatchAction _action = BatchAction.Rotate;
        public BatchAction Action
        {
            get => _action;
            set => SetProperty(ref _action, value);
        }

        // --- Options for each action ---

        private int _rotationAngle = 90;
        public int RotationAngle
        {
            get => _rotationAngle;
            set => SetProperty(ref _rotationAngle, value);
        }

        private string _watermarkText = "CONFIDENTIAL";
        public string WatermarkText
        {
            get => _watermarkText;
            set => SetProperty(ref _watermarkText, value);
        }

        private bool _watermarkDiagonal = true;
        public bool WatermarkDiagonal
        {
            get => _watermarkDiagonal;
            set => SetProperty(ref _watermarkDiagonal, value);
        }

        private int _watermarkOpacity = 30;
        public int WatermarkOpacity
        {
            get => _watermarkOpacity;
            set => SetProperty(ref _watermarkOpacity, value);
        }

        private string _pageSpec = "1";
        public string PageSpec
        {
            get => _pageSpec;
            set => SetProperty(ref _pageSpec, value);
        }

        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        private string _confirmPassword = string.Empty;
        public string ConfirmPassword
        {
            get => _confirmPassword;
            set => SetProperty(ref _confirmPassword, value);
        }

        // --- Output ---

        // Empty means each output goes next to its original
        private string _outputFolder = string.Empty;
        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                if (SetProperty(ref _outputFolder, value))
                {
                    OnPropertyChanged(nameof(OutputFolderText));
                    OnPropertyChanged(nameof(HasOutputFolder));
                }
            }
        }

        public bool HasOutputFolder => !string.IsNullOrEmpty(OutputFolder);
        public string OutputFolderText => HasOutputFolder ? OutputFolder : "Next to each original file";

        // --- Progress ---

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private string _progressText = string.Empty;
        public string ProgressText
        {
            get => _progressText;
            set => SetProperty(ref _progressText, value);
        }

        public ICommand AddFilesCommand { get; }
        public ICommand AddFolderCommand { get; }
        public ICommand RemoveFilesCommand { get; }
        public ICommand ClearFilesCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand UseOriginalFoldersCommand { get; }
        public ICommand RunBatchCommand { get; }
        public ICommand CancelCommand { get; }

        public BatchViewModel(IPdfService pdfService)
        {
            _pdfService = pdfService;
            AddFilesCommand = new RelayCommand(ExecuteAddFiles, _ => !IsBusy);
            AddFolderCommand = new RelayCommand(ExecuteAddFolder, _ => !IsBusy);
            RemoveFilesCommand = new RelayCommand(ExecuteRemoveFiles, p => !IsBusy && p is System.Collections.IList { Count: > 0 });
            ClearFilesCommand = new RelayCommand(_ => Files.Clear(), _ => !IsBusy && Files.Count > 0);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput, _ => !IsBusy);
            UseOriginalFoldersCommand = new RelayCommand(_ => OutputFolder = string.Empty, _ => !IsBusy);
            RunBatchCommand = new RelayCommand(ExecuteRunBatch, _ => !IsBusy && Files.Count > 0);
            CancelCommand = new RelayCommand(_ => _cancel?.Cancel(), _ => IsBusy);

            // Collection changes are not property changes, so ask for a CanExecute requery here too
            Files.CollectionChanged += (s, e) => CommandManager.InvalidateRequerySuggested();
        }

        // --- Adding files ---

        private void ExecuteAddFiles(object? parameter)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
                AddPaths(openFileDialog.FileNames);
        }

        private void ExecuteAddFolder(object? parameter)
        {
            var folderDialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder of PDFs" };
            if (folderDialog.ShowDialog() == true && AddPaths(new[] { folderDialog.FolderName }) == 0)
                MessageBox.Show("That folder has no PDF files.", "No Files Added", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Adds PDF files, and the PDFs directly inside any folders. Files already listed are
        // skipped. Returns how many were added
        public int AddPaths(IEnumerable<string> paths)
        {
            var listed = new HashSet<string>(Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (string path in paths)
            {
                IEnumerable<string> candidates = Directory.Exists(path)
                    ? Directory.GetFiles(path).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    : new[] { path };
                foreach (string file in candidates)
                {
                    if (!string.Equals(System.IO.Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string full = System.IO.Path.GetFullPath(file);
                    if (listed.Add(full))
                    {
                        Files.Add(new BatchFile(full));
                        added++;
                    }
                }
            }
            return added;
        }

        private void ExecuteRemoveFiles(object? parameter)
        {
            // Snapshot the selection: removing items changes it
            if (parameter is System.Collections.IList selected)
                foreach (var file in selected.Cast<BatchFile>().ToList())
                    Files.Remove(file);
        }

        private void ExecuteBrowseOutput(object? parameter)
        {
            var folderDialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where to save the processed files" };
            if (HasOutputFolder)
                folderDialog.InitialDirectory = OutputFolder;
            if (folderDialog.ShowDialog() == true)
                OutputFolder = folderDialog.FolderName;
        }

        // --- Planning outputs ---

        private string Suffix => Action switch
        {
            BatchAction.Rotate => "_rotated",
            BatchAction.Watermark => "_watermarked",
            BatchAction.Extract => "_extracted",
            BatchAction.Protect => "_protected",
            _ => "_unlocked"
        };

        // Where each file's output will go: {name}{suffix}.pdf next to it or in the chosen folder.
        // Names that would collide, with each other or with a file in the batch, get " (2)", " (3)"...
        public IReadOnlyList<string> PlanOutputs()
        {
            var taken = new HashSet<string>(Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            var outputs = new List<string>();
            foreach (var file in Files)
            {
                string folder = HasOutputFolder ? OutputFolder : file.Folder;
                string name = System.IO.Path.GetFileNameWithoutExtension(file.Path) + Suffix;
                string output = System.IO.Path.Combine(folder, name + ".pdf");
                for (int n = 2; !taken.Add(output); n++)
                    output = System.IO.Path.Combine(folder, $"{name} ({n}).pdf");
                outputs.Add(output);
            }
            return outputs;
        }

        // --- Running ---

        private async void ExecuteRunBatch(object? parameter)
        {
            string? problem = Action switch
            {
                BatchAction.Watermark when string.IsNullOrWhiteSpace(WatermarkText) => "Please enter the watermark text.",
                BatchAction.Extract when string.IsNullOrWhiteSpace(PageSpec) || PageRanges.TryParse(PageSpec, int.MaxValue) == null
                    => "Please enter the pages to take from every file, for example: 1 or 1-3.",
                BatchAction.Protect when string.IsNullOrEmpty(Password) => "Please enter a password.",
                BatchAction.Protect when Password != ConfirmPassword => "The passwords do not match. Please type the same password in both boxes.",
                BatchAction.Unlock when string.IsNullOrEmpty(Password) => "Please enter the password of the PDFs.",
                _ when HasOutputFolder && !Directory.Exists(OutputFolder) => "The output folder no longer exists. Please choose another one.",
                _ => null
            };
            if (problem != null)
            {
                MessageBox.Show(problem, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existing = PlanOutputs().Where(File.Exists).ToList();
            if (existing.Count > 0)
            {
                string list = string.Join("\n", existing.Take(8).Select(System.IO.Path.GetFileName));
                if (existing.Count > 8)
                    list += $"\n...and {existing.Count - 8} more";
                var result = MessageBox.Show(
                    $"{existing.Count} output file{(existing.Count == 1 ? "" : "s")} already exist{(existing.Count == 1 ? "s" : "")} and will be overwritten:\n{list}\n\nContinue?",
                    "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            await ProcessAsync();

            int done = Files.Count(f => f.Status == BatchFileStatus.Done);
            int failed = Files.Count(f => f.Status == BatchFileStatus.Failed);
            int skipped = Files.Count(f => f.Status == BatchFileStatus.Skipped);
            string summary = $"{done} of {Files.Count} file{(Files.Count == 1 ? "" : "s")} processed successfully.";
            if (failed > 0)
                summary += $"\n{failed} failed. The list shows why.";
            if (skipped > 0)
                summary += $"\n{skipped} not processed because the run was cancelled.";

            if (FirstOutputPath != null)
            {
                var open = MessageBox.Show(summary + "\n\nOpen the output folder?", "Batch Finished", MessageBoxButton.YesNo, failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                if (open == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{FirstOutputPath}\"");
            }
            else
            {
                MessageBox.Show(summary, "Batch Finished", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // The first file saved in the last run, for "Open the output folder"
        private string? FirstOutputPath { get; set; }

        // Processes every listed file with the current settings, one at a time, recording each
        // outcome on its row. Shows no dialogs; validation and confirmation happen before
        public async Task ProcessAsync()
        {
            var files = Files.ToList();
            var outputs = PlanOutputs();
            // Read the settings here: the work runs on a background thread
            BatchAction action = Action;
            int angle = RotationAngle;
            var watermark = new WatermarkOptions(WatermarkText.Trim(), WatermarkDiagonal, WatermarkOpacity, null!);
            string pageSpec = PageSpec, password = Password;

            foreach (var file in files)
            {
                file.Status = BatchFileStatus.Waiting;
                file.Message = string.Empty;
            }
            FirstOutputPath = null;

            using var cancel = new CancellationTokenSource();
            _cancel = cancel;
            IsBusy = true;
            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    BatchFile file = files[i];
                    if (cancel.IsCancellationRequested)
                    {
                        file.Status = BatchFileStatus.Skipped;
                        continue;
                    }

                    ProgressText = $"Processing {i + 1} of {files.Count}: {file.FileName}";
                    Progress = 100.0 * i / files.Count;
                    file.Status = BatchFileStatus.Working;

                    string source = file.Path, output = outputs[i];
                    try
                    {
                        await Task.Run(() =>
                        {
                            switch (action)
                            {
                                case BatchAction.Rotate: _pdfService.RotatePdf(source, output, null!, angle); break;
                                case BatchAction.Watermark: _pdfService.AddWatermark(source, output, watermark); break;
                                case BatchAction.Extract: _pdfService.ExtractPages(source, output, pageSpec); break;
                                case BatchAction.Protect: _pdfService.ProtectPdf(source, output, password); break;
                                case BatchAction.Unlock: _pdfService.UnlockPdf(source, output, password); break;
                            }
                        });
                        file.Message = $"Saved as {System.IO.Path.GetFileName(output)}";
                        file.Status = BatchFileStatus.Done;
                        FirstOutputPath ??= output;
                    }
                    catch (Exception ex)
                    {
                        file.Message = Describe(ex);
                        file.Status = BatchFileStatus.Failed;
                    }
                }
            }
            finally
            {
                Progress = 100;
                ProgressText = string.Empty;
                IsBusy = false;
                _cancel = null;
            }
        }

        // A short reason for a file's row. Unexpected errors are logged for diagnosis
        private static string Describe(Exception ex)
        {
            switch (ex)
            {
                case iText.Kernel.Exceptions.BadPasswordException:
                    return "This PDF is password-protected. Remove its password with Unlock PDF first.";
                case iText.IO.Exceptions.IOException:
                    return "Not a valid PDF, or the file is corrupted.";
                case InvalidOperationException:
                    return ex.Message;
                case IOException or UnauthorizedAccessException:
                    return $"Could not save: {ex.Message}";
                default:
                    ErrorLog.Write(ex, "Batch");
                    return $"Error: {ex.Message}";
            }
        }
    }
}
