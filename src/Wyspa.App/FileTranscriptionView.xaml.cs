using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows;
using Wyspa.App.ViewModels;
using Wyspa.Core.Services;

namespace Wyspa.App;

public partial class FileTranscriptionView : System.Windows.Controls.UserControl
{
    public FileTranscriptionView() => InitializeComponent();

    private void ChooseFiles_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FileTranscriptionViewModel viewModel || viewModel.IsBusy) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose audio files to transcribe", Multiselect = true, CheckFileExists = true,
            Filter = "Audio files|*.wav;*.flac;*.mp3;*.m4a;*.ogg;*.webm;*.mpga;*.aif;*.aiff"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) viewModel.AddFiles(dialog.FileNames);
    }

    private void CopyTranscript_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FileTranscriptionViewModel viewModel || viewModel.SelectedFile?.HasTranscript != true) return;
        try
        {
            System.Windows.Clipboard.SetText(viewModel.SelectedFile.TimestampedTranscript);
            viewModel.Status = "Transcript copied.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            viewModel.Status = "The clipboard is busy. Try copying again.";
        }
    }

    private async void SaveTranscript_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FileTranscriptionViewModel viewModel || viewModel.SelectedFile?.HasTranscript != true) return;
        var file = viewModel.SelectedFile;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save transcript", Filter = "Text file (*.txt)|*.txt", DefaultExt = ".txt",
            FileName = Path.GetFileNameWithoutExtension(file.Name) + ".txt", AddExtension = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, file.TimestampedTranscript);
            viewModel.Status = "Transcript saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            viewModel.Status = "Could not save the transcript. Choose a writable folder and try again.";
        }
    }

    private void OpenCsv_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FileTranscriptionViewModel viewModel || viewModel.SelectedFile?.CsvPath is not { } path) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            viewModel.Status = "Could not open the CSV. Use Save CSV… to save a copy, then open it in a spreadsheet app.";
        }
    }

    private async void SaveCsv_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FileTranscriptionViewModel viewModel || viewModel.SelectedFile?.CanSaveCsv != true) return;
        var file = viewModel.SelectedFile;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save timestamped CSV", Filter = "CSV file (*.csv)|*.csv", DefaultExt = ".csv",
            FileName = Path.GetFileNameWithoutExtension(file.Name) + ".csv", AddExtension = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, FileTranscriptExport.ToCsv(file.Segments), new UTF8Encoding(true));
            file.CsvPath = dialog.FileName;
            file.Status = "Complete — CSV saved";
            viewModel.Status = "Timestamped CSV saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            viewModel.Status = "Could not save the CSV. Choose a writable folder and try again.";
        }
    }
}
