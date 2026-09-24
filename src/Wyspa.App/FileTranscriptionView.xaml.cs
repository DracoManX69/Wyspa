using System.IO;
using System.Windows;
using Wyspa.App.ViewModels;

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
            System.Windows.Clipboard.SetText(viewModel.SelectedFile.Transcript);
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
            await File.WriteAllTextAsync(dialog.FileName, file.Transcript);
            viewModel.Status = "Transcript saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            viewModel.Status = "Could not save the transcript. Choose a writable folder and try again.";
        }
    }
}
