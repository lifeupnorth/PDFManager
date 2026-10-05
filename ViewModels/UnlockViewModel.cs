using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class UnlockViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;
        private readonly IDialogService _dialogs;

        private string _sourcePath = string.Empty;
        public string SourcePath
        {
            get => _sourcePath;
            set => SetProperty(ref _sourcePath, value);
        }

        private string _outputPath = string.Empty;
        public string OutputPath
        {
            get => _outputPath;
            set => SetProperty(ref _outputPath, value);
        }

        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        private string _statusInfo = string.Empty;
        public string StatusInfo
        {
            get => _statusInfo;
            set => SetProperty(ref _statusInfo, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ICommand OpenFileCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand RunUnlockCommand { get; }

        public UnlockViewModel(IPdfService pdfService, IDialogService dialogs)
        {
            _pdfService = pdfService;
            _dialogs = dialogs;
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput);
            RunUnlockCommand = new RelayCommand(ExecuteRunUnlock, CanExecuteRunUnlock);
        }

        private async void ExecuteOpenFile(object? parameter)
        {
            string? path = _dialogs.PickFile("PDF Files (*.pdf)|*.pdf");
            if (path != null)
                await UpdateFileAsync(path);
        }

        private void ExecuteBrowseOutput(object? parameter)
        {
            string? path = _dialogs.PickSaveFile("PDF Files (*.pdf)|*.pdf", ".pdf", string.IsNullOrWhiteSpace(OutputPath)
                ? Path.GetFileNameWithoutExtension(SourcePath) + "_unlocked.pdf"
                : Path.GetFileName(OutputPath));
            if (path != null)
                OutputPath = path;
        }

        private bool CanExecuteRunUnlock(object? parameter)
        {
            return !IsBusy && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(OutputPath);
        }

        private async void ExecuteRunUnlock(object? parameter) => await UnlockAsync();

        // The Unlock button's work, including its messages and confirmations
        public async Task UnlockAsync()
        {
            if (string.Equals(Path.GetFullPath(SourcePath), Path.GetFullPath(OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                _dialogs.Show("The output file cannot be the same as the source file. Please choose a different output path.", "Validation Error", DialogIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(Password))
            {
                _dialogs.Show("Please enter the PDF's password.", "Validation Error", DialogIcon.Warning);
                return;
            }

            if (File.Exists(OutputPath))
            {
                if (!_dialogs.Confirm(
                        $"\"{OutputPath}\" already exists and will be overwritten.\n\nContinue?",
                        "Confirm Overwrite", DialogIcon.Warning))
                    return;
            }

            // Read the inputs here: the work runs on a background thread
            string sourcePath = SourcePath, outputPath = OutputPath, password = Password;
            try
            {
                IsBusy = true;
                await Task.Run(() => _pdfService.UnlockPdf(sourcePath, outputPath, password));

                Password = string.Empty;
                if (_dialogs.Confirm("Password removed successfully!\n\nOpen the output folder?", "Success", DialogIcon.Information))
                    _dialogs.ShowInExplorer(outputPath);
            }
            catch (InvalidOperationException ex)
            {
                // The service's own explanations: a wrong password, or a file with no password
                _dialogs.Show(ex.Message, "Error", DialogIcon.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                _dialogs.Show("This file is not a valid PDF or is corrupted.", "Error", DialogIcon.Error);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "Unlock");
                _dialogs.Show($"Error removing password: {ex.Message}", "Error", DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task UpdateFileAsync(string path)
        {
            SourcePath = path;
            OutputPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_unlocked.pdf");
            Password = string.Empty;
            StatusInfo = "Checking the file...";

            string info = await Task.Run(() =>
            {
                try
                {
                    return _pdfService.GetProtection(path) switch
                    {
                        PdfProtection.PasswordToOpen => "Enter the password that opens this PDF.",
                        PdfProtection.PermissionsOnly => "This PDF opens without a password but restricts printing, copying or editing. Enter its owner password to remove the restrictions.",
                        _ => "This PDF is not password-protected."
                    };
                }
                catch
                {
                    return "Unable to read this file.";
                }
            });

            // Another file may have been picked while this one was being read
            if (SourcePath == path)
                StatusInfo = info;
        }
    }
}
