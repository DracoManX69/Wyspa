using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class SpeechEndpointTests
{
    [Fact]
    public void ZeroThresholdNeverCountsSilenceAsSpeech_AndNoiseHasAnAbsoluteFloor()
    {
        Assert.False(SpeechEndpoint.FallbackSpeech(0, 0)); Assert.False(SpeechEndpoint.FallbackSpeech(.005f, 0));
        Assert.True(SpeechEndpoint.FallbackSpeech(.03f, 0));
        Assert.False(SpeechEndpoint.FallbackSpeech(.02f, .12f));
    }
    [Fact]
    public void SilenceExpiresWithoutNewAudioCallbacks_AndSpeechExtendsIt()
    {
        var endpoint = new SpeechEndpoint(); var generation = endpoint.Begin(TimeSpan.Zero);
        endpoint.Speech(generation, TimeSpan.FromMilliseconds(200));
        Assert.False(endpoint.ShouldStop(TimeSpan.FromMilliseconds(1199), 1000, 250));
        Assert.True(endpoint.ShouldStop(TimeSpan.FromMilliseconds(1200), 1000, 250));
        endpoint.Speech(generation, TimeSpan.FromMilliseconds(1150));
        Assert.False(endpoint.ShouldStop(TimeSpan.FromMilliseconds(1200), 1000, 250));
        Assert.True(endpoint.ShouldStop(TimeSpan.FromMilliseconds(2150), 1000, 250));
    }
    [Fact]
    public void StaleSpeechFramesCannotHoldTheNextRecordingOpen()
    {
        var endpoint = new SpeechEndpoint(); var old = endpoint.Begin(TimeSpan.Zero); endpoint.End();
        endpoint.Begin(TimeSpan.FromSeconds(2)); endpoint.Speech(old, TimeSpan.FromSeconds(100));
        Assert.True(endpoint.ShouldStop(TimeSpan.FromSeconds(3), 800, 250));
        endpoint.End(); Assert.False(endpoint.ShouldStop(TimeSpan.FromSeconds(4), 800, 250));
    }
    [Theory]
    [InlineData("Hey, Whisper.", "hey whisper", true)]
    [InlineData("hay whisper", "hey whisper", true)]
    [InlineData("we should whisper quietly", "hey whisper", false)]
    [InlineData("hey mister", "hey whisper", false)]
    [InlineData("hello computer", "hello computer", true)]
    [InlineData("hey whisper", "hello computer", false)]
    public void VerificationRequiresWordsInOrder(string text, string phrase, bool match) => Assert.Equal(match, WakePhraseVerifier.Matches(text, phrase));
}
