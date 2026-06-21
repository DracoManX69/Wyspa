using System.Net;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class GitHubUpdateServiceTests
{
    [Fact]
    public async Task CheckLatestAsync_ReturnsUpdateWhenGitHubReleaseIsNewer()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "tag_name": "v0.5.3",
              "html_url": "https://github.com/DracoManX69/Wyspa/releases/tag/v0.5.3",
              "assets": [
                {
                  "name": "WyspaSetup-0.5.3-win-x64.exe",
                  "browser_download_url": "https://github.com/DracoManX69/Wyspa/releases/download/v0.5.3/WyspaSetup-0.5.3-win-x64.exe"
                }
              ]
            }
            """)
        }));
        var service = new GitHubUpdateService(http);

        var result = await service.CheckLatestAsync(new Version(0, 5, 2), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("0.5.2", result.CurrentVersion);
        Assert.Equal("0.5.3", result.LatestVersion);
        Assert.EndsWith("WyspaSetup-0.5.3-win-x64.exe", result.InstallerUrl);
    }

    [Fact]
    public async Task CheckLatestAsync_ReportsUpToDateWhenVersionsMatch()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "tag_name": "v0.5.2",
              "html_url": "https://github.com/DracoManX69/Wyspa/releases/tag/v0.5.2",
              "assets": []
            }
            """)
        }));
        var service = new GitHubUpdateService(http);

        var result = await service.CheckLatestAsync(new Version(0, 5, 2), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.UpdateAvailable);
        Assert.Contains("up to date", result.UserMessage);
    }

    [Theory]
    [InlineData("v0.5.3", "0.5.3")]
    [InlineData("0.6.0", "0.6.0")]
    public void TryParseVersion_ReadsReleaseTags(string tag, string expected)
    {
        var parsed = GitHubUpdateService.TryParseVersion(tag, out var version);

        Assert.True(parsed);
        Assert.Equal(expected, $"{version.Major}.{version.Minor}.{version.Build}");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Contains("Wyspa", request.Headers.UserAgent.ToString());
            return Task.FromResult(_handler(request));
        }
    }
}
