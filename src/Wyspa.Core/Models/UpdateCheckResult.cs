namespace Wyspa.Core.Models;

public sealed record UpdateCheckResult(
    bool Success,
    bool UpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    string? InstallerUrl,
    string UserMessage)
{
    public static UpdateCheckResult Failed(string currentVersion, string message) =>
        new(false, false, currentVersion, null, null, null, message);
}
