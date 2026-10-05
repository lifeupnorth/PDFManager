#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class ProtectViewModel : ObservableObject
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

        private string _confirmPassword = string.Empty;
        public string ConfirmPassword
        {
            get => _confirmPassword;
            set => SetProperty(ref _confirmPassword, value);
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
        public ICommand RunProtectCommand { get; }

        public ProtectViewModel(IPdfService pdfService, IDialogService dialogs)
        {
            _pdfService = pdfService;
            _dialogs = dialogs;
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput);
            RunProtectCommand = new RelayCommand(ExecuteRunProtect, CanExecuteRunProtect);
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
                ? Path.GetFileNameWithoutExtension(SourcePath) + "_protected.pdf"
                : Path.GetFileName(OutputPath));
            if (path != null)
                OutputPath = path;
        }

        private bool CanExecuteRunProtect(object? parameter)
        {
            return !IsBusy && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(OutputPath);
        }

        private async void ExecuteRunProtect(object? parameter) => await ProtectAsync();

        // The Protect button's work, including its messages and confirmations
        public async Task ProtectAsync()
        {
            // Keep the unprotected original: if the password is forgotten, it is the only readable copy
            if (string.Equals(Path.GetFullPath(SourcePath), Path.GetFullPath(OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                _dialogs.Show("The output file cannot be the same as the source file. Please choose a different output path.", "Validation Error", DialogIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(Password))
            {
                _dialogs.Show("Please enter a password.", "Validation Error", DialogIcon.Warning);
                return;
            }

            if (Password != ConfirmPassword)
            {
                _dialogs.Show("The passwords do not match. Please type the same password in both boxes.", "Validation Error", DialogIcon.Warning);
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
                await Task.Run(() => _pdfService.ProtectPdf(sourcePath, outputPath, password));

                Password = ConfirmPassword = string.Empty;
                if (_dialogs.Confirm("PDF protected successfully!\n\nOpen the output folder?", "Success", DialogIcon.Information))
                    _dialogs.ShowInExplorer(outputPath);
            }
            catch (InvalidOperationException ex)
            {
                // The service's own explanations, such as a file that is already protected
                _dialogs.Show(ex.Message, "Error", DialogIcon.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                _dialogs.Show("This file is not a valid PDF or is corrupted.", "Error", DialogIcon.Error);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "Protect");
                _dialogs.Show($"Error protecting PDF: {ex.Message}", "Error", DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task UpdateFileAsync(string path)
        {
            SourcePath = path;
            OutputPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_protected.pdf");
            StatusInfo = "Checking the file...";

            string info = await Task.Run(() =>
            {
                try
                {
                    return _pdfService.GetProtection(path) == PdfProtection.None
                        ? "The password cannot be recovered if it is forgotten, so keep it somewhere safe."
                        : "This PDF is already protected. Remove its password with Unlock PDF first.";
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
