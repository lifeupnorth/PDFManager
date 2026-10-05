using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PDFManager.Base;
using PDFManager.Services;

namespace PDFManager.ViewModels
{
    public class MergeViewModel : ObservableObject
    {
        private readonly IPdfService _pdfService;

        public ObservableCollection<string> MergeFiles { get; } = new ObservableCollection<string>();

        private string _selectedFile = string.Empty;
        public string SelectedFile
        {
            get => _selectedFile;
            set => SetProperty(ref _selectedFile, value);
        }

        private string _outputPath = string.Empty;
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

        public MergeViewModel(IPdfService pdfService)
        {
            _pdfService = pdfService;

            AddFilesCommand = new RelayCommand(ExecuteAddFiles);
            RemoveFileCommand = new RelayCommand(ExecuteRemoveFile, CanExecuteRemoveFile);
            MoveUpCommand = new RelayCommand(ExecuteMoveUp, CanExecuteMoveUp);
            MoveDownCommand = new RelayCommand(ExecuteMoveDown, CanExecuteMoveDown);
            BrowseOutputCommand = new RelayCommand(ExecuteBrowseOutput);
            RunMergeCommand = new RelayCommand(ExecuteRunMerge, CanExecuteRunMerge);
        }

        private void ExecuteAddFiles(object? parameter)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (var file in openFileDialog.FileNames)
                {
                    if (MergeFiles.Any(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    try
                    {
                        using (var reader = new iText.Kernel.Pdf.PdfReader(file))
                        using (var readerDoc = new iText.Kernel.Pdf.PdfDocument(reader)) { }
                        MergeFiles.Add(file);
                    }
                    catch { }
                }
            }
        }

        private bool CanExecuteRemoveFile(object? parameter) => !string.IsNullOrEmpty(SelectedFile);

        private void ExecuteRemoveFile(object? parameter)
        {
            if (!string.IsNullOrEmpty(SelectedFile))
                MergeFiles.Remove(SelectedFile);
        }

        private bool CanExecuteMoveUp(object? parameter) => MergeFiles.Count > 1 && !string.IsNullOrEmpty(SelectedFile) && MergeFiles.IndexOf(SelectedFile) > 0;
        private void ExecuteMoveUp(object? parameter)
        {
            int index = MergeFiles.IndexOf(SelectedFile);
            if (index > 0)
            {
                MergeFiles.Move(index, index - 1);
            }
        }

        private bool CanExecuteMoveDown(object? parameter) => MergeFiles.Count > 1 && !string.IsNullOrEmpty(SelectedFile) && MergeFiles.IndexOf(SelectedFile) < MergeFiles.Count - 1;
        private void ExecuteMoveDown(object? parameter)
        {
            int index = MergeFiles.IndexOf(SelectedFile);
            if (index < MergeFiles.Count - 1)
            {
                MergeFiles.Move(index, index + 1);
            }
        }

        private void ExecuteBrowseOutput(object? parameter)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = "Merged File.pdf",
                DefaultExt = ".pdf"
            };

            if (saveFileDialog.ShowDialog() == true)
                OutputPath = saveFileDialog.FileName;
        }

        private bool CanExecuteRunMerge(object? parameter)
        {
            if (IsBusy || MergeFiles.Count == 0 || string.IsNullOrWhiteSpace(OutputPath))
                return false;

            if (MergeFiles.Any(f => string.Equals(f, OutputPath, StringComparison.OrdinalIgnoreCase)))
                return false;

            if (!Directory.Exists(Path.GetDirectoryName(OutputPath)))
                return false;

            return true;
        }

        private async void ExecuteRunMerge(object? parameter)
        {
            try
            {
                IsBusy = true;
                await Task.Run(() => _pdfService.MergePdfs(OutputPath, MergeFiles.ToList()));

                var openFolder = MessageBox.Show("PDFs merged successfully!\n\nOpen the output folder?", "Success", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (openFolder == MessageBoxResult.Yes)
                    ShowInExplorer(OutputPath);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(OutputPath)) File.Delete(OutputPath); } catch { }
                MessageBox.Show($"Error merging PDFs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ShowInExplorer(string filePath)
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
    }
}