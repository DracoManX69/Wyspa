using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class TextCleanupServiceTests
{
    [Fact]
    public void Clean_TrimsCapitalizesAndConvertsPunctuation()
    {
        var service = new TextCleanupService();

        var result = service.Clean("  hello comma new line world question mark  ");

        Assert.Equal($"Hello,{Environment.NewLine}world?", result);
    }

    [Fact]
    public void Clean_DoesNotAggressivelyCapitalizeCodeLikeText()
    {
        var service = new TextCleanupService();

        var result = service.Clean("  const value = () => 1;  ");

        Assert.Equal("const value = () => 1;", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("...")]
    [InlineData("?!")]
    [InlineData("Please provide the dictated speech, and I will rewrite it into polished written text.")]
    public void HasTranscribableText_RejectsEmptyNoiseAndAssistantPlaceholders(string input)
    {
        Assert.False(TextCleanupService.HasTranscribableText(input));
    }

    [Theory]
    [InlineData("hello there")]
    [InlineData("Press Enter")]
    [InlineData("yes")]
    public void HasTranscribableText_AllowsShortRealSpeech(string input)
    {
        Assert.True(TextCleanupService.HasTranscribableText(input));
    }
}
