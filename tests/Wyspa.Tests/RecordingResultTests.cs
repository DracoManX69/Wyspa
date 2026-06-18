using Wyspa.Core.Models;

namespace Wyspa.Tests;

public sealed class RecordingResultTests
{
    [Fact]
    public void LooksSilent_FlagsEmptyOrVeryQuietRecording()
    {
        var recording = new RecordingResult("clip.wav", TimeSpan.FromSeconds(2), 64000, 0.001f);

        Assert.True(recording.LooksSilent);
    }

    [Fact]
    public void LooksSilent_AllowsRecordingWithAudiblePeak()
    {
        var recording = new RecordingResult("clip.wav", TimeSpan.FromSeconds(2), 64000, 0.08f);

        Assert.False(recording.LooksSilent);
    }
}
