namespace Wyspa.Core.Services;

public static class StartupCommandBuilder
{
    public static string BuildRunCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(executablePath));
        }

        return $"\"{executablePath}\" --minimized";
    }
}
