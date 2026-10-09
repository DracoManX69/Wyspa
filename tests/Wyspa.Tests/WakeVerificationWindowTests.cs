using Wyspa.Core.Services;
namespace Wyspa.Tests;
public sealed class WakeVerificationWindowTests
{
    [Fact]
    public void KeywordTimesExcludeEarlierConversationAndLaterRequest_WithBoundedPadding()
    {
        var recent = Enumerable.Range(0,64000).Select(i => (float)i).ToArray();
        var clip = WakeVerificationWindow.Extract(recent,64000,0,[2.0f,2.5f],["▁HEY","▁WHISPER"]);
        Assert.InRange(clip[0],28790,28810); Assert.InRange(clip.Length,15000,24000); Assert.Equal(0,clip[^1]);
    }
    [Fact]
    public void InvalidMissingOrOutOfRingTimesKeepTheExistingVerificationPath()
    {
        var recent = new float[20000];
        Assert.Same(recent,WakeVerificationWindow.Extract(recent,20000,0,[],[]));
        Assert.Same(recent,WakeVerificationWindow.Extract(recent,20000,0,[float.NaN],["hey"]));
        Assert.Same(recent,WakeVerificationWindow.Extract(recent,20000,0,[5f,4f],["hey","whisper"]));
        Assert.Same(recent,WakeVerificationWindow.Extract(recent,20000,0,[10f,11f],["hey","whisper"]));
    }
}
