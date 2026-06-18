using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class StartupCommandBuilderTests
{
    [Fact]
    public void BuildRunCommand_QuotesExecutableAndAddsMinimizedFlag()
    {
        var command = StartupCommandBuilder.BuildRunCommand(@"C:\Program Files\Wyspa\Wyspa.exe");

        Assert.Equal("\"C:\\Program Files\\Wyspa\\Wyspa.exe\" --minimized", command);
    }
}
