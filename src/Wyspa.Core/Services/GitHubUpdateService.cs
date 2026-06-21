using System.Net.Http.Headers;
using System.Text.Json;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public sealed class GitHubUpdateService
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/DracoManX69/Wyspa/releases/latest";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public GitHubUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<UpdateCheckResult> CheckLatestAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        var currentText = FormatVersion(currentVersion);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Wyspa", currentText));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed(currentText, "Could not check for updates. GitHub did not return a release.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagProperty) ? tagProperty.GetString() : null;
            var releaseUrl = root.TryGetProperty("html_url", out var urlProperty) ? urlProperty.GetString() : null;
            var installerUrl = FindInstallerUrl(root);

            if (!TryParseVersion(tag, out var latestVersion))
            {
                return UpdateCheckResult.Failed(currentText, "Could not read the latest Wyspa version from GitHub.");
            }

            var latestText = FormatVersion(latestVersion);
            if (latestVersion.CompareTo(NormalizeVersion(currentVersion)) <= 0)
            {
                return new UpdateCheckResult(
                    true,
                    false,
                    currentText,
                    latestText,
                    releaseUrl,
                    installerUrl,
                    $"Wyspa is up to date. Current version: {currentText}.");
            }

            return new UpdateCheckResult(
                true,
                true,
                currentText,
                latestText,
                releaseUrl,
                installerUrl,
                $"Wyspa {latestText} is available. Current version: {currentText}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateCheckResult.Failed(currentText, "The update check timed out. Try again later.");
        }
        catch (HttpRequestException)
        {
            return UpdateCheckResult.Failed(currentText, "Could not reach GitHub. Check your internet connection.");
        }
        catch (JsonException)
        {
            return UpdateCheckResult.Failed(currentText, "GitHub returned an update response Wyspa could not read.");
        }
    }

    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var clean = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(clean, out var parsed))
        {
            return false;
        }

        version = NormalizeVersion(parsed);
        return true;
    }

    private static string? FindInstallerUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) ||
                !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                !name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return asset.TryGetProperty("browser_download_url", out var urlProperty) ? urlProperty.GetString() : null;
        }

        return null;
    }

    private static Version NormalizeVersion(Version version) =>
        new(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));

    private static string FormatVersion(Version version)
    {
        var normalized = NormalizeVersion(version);
        return $"{normalized.Major}.{normalized.Minor}.{normalized.Build}";
    }
}
