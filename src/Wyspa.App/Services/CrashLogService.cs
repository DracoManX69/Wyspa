using System.IO;

namespace Wyspa.App.Services;

public static class CrashLogService
{
    public static void Log(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Wyspa");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "crash.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:u}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
