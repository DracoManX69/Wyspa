using System.Diagnostics;
using System.IO;
using System.Windows;
using Wyspa.App.ViewModels;
namespace Wyspa.App;
public partial class VideoView : System.Windows.Controls.UserControl
{
    public VideoView() => InitializeComponent();
    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is NotesViewModel vm && vm.SelectedNote is not null &&
            System.Windows.MessageBox.Show("Delete this video transcript and its text copy from this PC?", "Delete video transcript", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            vm.DeleteCommand.Execute(null);
    }
    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is NotesViewModel vm && vm.ExportSelected() is { Length: > 0 } text)
            try { System.Windows.Clipboard.SetText(text); } catch (System.Runtime.InteropServices.COMException) { System.Windows.MessageBox.Show("The clipboard is busy. Try again.", "Wyspa"); }
    }
    private void Folder_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NotesViewModel vm) return;
        try { Directory.CreateDirectory(vm.FolderPath); Process.Start(new ProcessStartInfo(vm.FolderPath) { UseShellExecute = true }); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Wyspa"); }
    }
}
