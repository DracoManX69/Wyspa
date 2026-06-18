using System.Windows;
using Wyspa.App.Services;

namespace Wyspa.App;

public partial class ScratchpadWindow : Window
{
    public ScratchpadWindow(bool isDarkMode)
    {
        InitializeComponent();
        IsDarkMode = isDarkMode;
    }

    public bool IsDarkMode { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindowStyler.Apply(this, IsDarkMode);
    }

    public void ApplyTheme(bool darkMode)
    {
        IsDarkMode = darkMode;
        NativeWindowStyler.Apply(this, darkMode);
    }
}
