namespace Wyspa.Core.Services;

public static class WakeVerificationWindow
{
    public static float[] Extract(float[] recent, long acceptedSamples, float startTime, IReadOnlyList<float> timestamps, IReadOnlyList<string> tokens)
    {
        if (timestamps.Count == 0 || timestamps.Count != tokens.Count || !float.IsFinite(startTime) || startTime < 0 ||
            timestamps.Any(t => !float.IsFinite(t) || t < 0) || timestamps.Zip(timestamps.Skip(1)).Any(t => t.First > t.Second)) return recent;
        var start = (long)Math.Floor((startTime + timestamps[0] - .20) * 16000);
        // Last token timestamps mark its start, not end. Longer BPE pieces need more room.
        var letters = tokens[^1].Count(char.IsLetter);
        var allowance = Math.Clamp(.25 + letters * .05, .40, .70);
        var end = (long)Math.Ceiling((startTime + timestamps[^1] + allowance) * 16000);
        var origin = acceptedSamples - recent.Length;
        if (start >= acceptedSamples || end <= origin || start > end) return recent;
        var from = (int)Math.Clamp(start - origin, 0, recent.Length);
        var to = (int)Math.Clamp(end - origin, 0, recent.Length);
        if (to - from < 6400) return recent;
        var result = new float[to - from + 1600]; recent.AsSpan(from, to - from).CopyTo(result);
        return result;
    }
}
