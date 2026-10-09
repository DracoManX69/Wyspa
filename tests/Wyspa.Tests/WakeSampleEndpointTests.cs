using Wyspa.Core.Services;
namespace Wyspa.Tests;
public sealed class WakeSampleEndpointTests
{
    private static float[] Frame(float value, int count = 1600) => Enumerable.Repeat(value, count).ToArray();
    [Fact]
    public void QuietWithoutSpeechDoesNotFinish_AndNaturalPhrasePauseDoes()
    {
        var endpoint = new WakeSampleEndpoint(.001, true);
        for (var i = 0; i < 15; i++) Assert.False(endpoint.Feed(Frame(0)));
        for (var i = 0; i < 6; i++) Assert.False(endpoint.Feed(Frame(.05f)));
        for (var i = 0; i < 5; i++) Assert.False(endpoint.Feed(Frame(.001f)));
        Assert.True(endpoint.Feed(Frame(.001f)));
    }
    [Fact]
    public void SentencePausesAllowMoreTime_AndRoomFloorCannotBecomeSpeech()
    {
        var endpoint = new WakeSampleEndpoint(.01, false);
        for (var i = 0; i < 20; i++) Assert.False(endpoint.Feed(Frame(.012f)));
        for (var i = 0; i < 12; i++) Assert.False(endpoint.Feed(Frame(.08f)));
        for (var i = 0; i < 9; i++) Assert.False(endpoint.Feed(Frame(.012f)));
        Assert.True(endpoint.Feed(Frame(.012f)));
    }
}
