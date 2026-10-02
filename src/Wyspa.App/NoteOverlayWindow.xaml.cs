using System.Windows;
using Wyspa.App.Services;
namespace Wyspa.App;
public partial class NoteOverlayWindow : Window
{
    public NoteOverlayWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Left = SystemParameters.WorkArea.Right - ActualWidth - 20;
            Top = SystemParameters.WorkArea.Bottom - ActualHeight - 20;
        };
    }
}
