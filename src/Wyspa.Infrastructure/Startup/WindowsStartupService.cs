using Microsoft.Win32;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Services;

namespace Wyspa.Infrastructure.Startup;

public sealed class WindowsStartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Wyspa";
    private readonly string _executablePath;

    public WindowsStartupService(string executablePath)
    {
        _executablePath = executablePath;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return string.Equals(value, StartupCommandBuilder.BuildRunCommand(_executablePath), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
                        Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, StartupCommandBuilder.BuildRunCommand(_executablePath));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
