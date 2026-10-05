using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class ProtectViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;

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

        public ProtectViewModel(IPdfService pdfService)
        {
            _pdfService = pdfService;
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput);
            RunProtectCommand = new RelayCommand(ExecuteRunProtect, CanExecuteRunProtect);
        }

        private async void ExecuteOpenFile(object? parameter)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf"
            };

            if (openFileDialog.ShowDialog() == true)
                await UpdateFileAsync(openFileDialog.FileName);
        }

        private void ExecuteBrowseOutput(object? parameter)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                FileName = string.IsNullOrWhiteSpace(OutputPath)
                    ? Path.GetFileNameWithoutExtension(SourcePath) + "_protected.pdf"
                    : Path.GetFileName(OutputPath)
            };

            if (saveFileDialog.ShowDialog() == true)
                OutputPath = saveFileDialog.FileName;
        }

        private bool CanExecuteRunProtect(object? parameter)
        {
            return !IsBusy && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(OutputPath);
        }

        private async void ExecuteRunProtect(object? parameter)
        {
            // Keep the unprotected original: if the password is forgotten, it is the only readable copy
            if (string.Equals(Path.GetFullPath(SourcePath), Path.GetFullPath(OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("The output file cannot be the same as the source file. Please choose a different output path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(Password))
            {
                MessageBox.Show("Please enter a password.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Password != ConfirmPassword)
            {
                MessageBox.Show("The passwords do not match. Please type the same password in both boxes.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (File.Exists(OutputPath))
            {
                var result = MessageBox.Show(
                    $"\"{OutputPath}\" already exists and will be overwritten.\n\nContinue?",
                    "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            // Read the inputs here: the work runs on a background thread
            string sourcePath = SourcePath, outputPath = OutputPath, password = Password;
            try
            {
                IsBusy = true;
                await Task.Run(() => _pdfService.ProtectPdf(sourcePath, outputPath, password));

                Password = ConfirmPassword = string.Empty;
                var openFolder = MessageBox.Show("PDF protected successfully!\n\nOpen the output folder?", "Success", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (openFolder == MessageBoxResult.Yes)
                    ShowInExplorer(outputPath);
            }
            catch (InvalidOperationException ex)
            {
                // The service's own explanations, such as a file that is already protected
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (iText.IO.Exceptions.IOException)
            {
                MessageBox.Show("This file is not a valid PDF or is corrupted.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex, "Protect");
                MessageBox.Show($"Error protecting PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void ShowInExplorer(string filePath)
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
    }
}
