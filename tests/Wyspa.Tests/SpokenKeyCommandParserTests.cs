using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class SpokenKeyCommandParserTests
{
    [Fact]
    public void TryParse_ReadsPressEnter()
    {
        var parsed = SpokenKeyCommandParser.TryParse("Press Enter", out var command);

        Assert.True(parsed);
        Assert.Equal("Enter", command.Key);
        Assert.Empty(command.Modifiers);
    }

    [Fact]
    public void TryParse_ReadsModifiedLetter()
    {
        var parsed = SpokenKeyCommandParser.TryParse("press ctrl c", out var command);

        Assert.True(parsed);
        Assert.Equal("C", command.Key);
        Assert.Contains("Ctrl", command.Modifiers);
    }

    [Fact]
    public void TryParse_IgnoresRegularDictation()
    {
        var parsed = SpokenKeyCommandParser.TryParse("Please press enter when you are ready", out _);

        Assert.False(parsed);
    }
}
