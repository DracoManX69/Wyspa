using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using NAudio.Wave;
using Wyspa.Core.Models;
using Wyspa.Core.Services;
using Wyspa.Infrastructure.Settings;

internal static class StreamingGroqSmoke
{
    public static async Task RunAsync(string output, string wave)
    {
        var key = await new DpapiSecretStore().GetApiKeyAsync(default);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No saved Groq key is available for the synthetic fixture test.");
        using var reader = new WaveFileReader(wave);
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16)
            throw new InvalidOperationException("Fixture must be 16 kHz mono 16-bit PCM.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var http = new HttpClient();
        var groq = new GroqTranscriptionClient(http);
        var watch = Stopwatch.StartNew();
        var updates = new List<object>();
        var delivered = "";
        double? firstUpdate = null;
        using var session = new StreamingDictationSession(groq, key, new TranscriptionOptions("whisper-large-v3-turbo", "en", "Wyspa Stream Mode"), (delta, text, _) =>
        {
            if (delta.Length > 0)
            {
                firstUpdate ??= watch.Elapsed.TotalSeconds;
                delivered += delta;
                updates.Add(new { seconds = watch.Elapsed.TotalSeconds, delta, clipboard = text });
            }
            // The authoritative final pass may correct the live hypothesis.
            if (delta.Length == 0) updates.Add(new { seconds = watch.Elapsed.TotalSeconds, delta, clipboard = text });
            return Task.CompletedTask;
        });
        using var live = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var worker = RunLiveAsync();
        try
        {
            var buffer = new byte[1280];
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                session.AddAudio(null, buffer.AsMemory(0, count));
                await Task.Delay(40, timeout.Token);
            }
        }
        finally { live.Cancel(); await worker; }
        var stoppedAt = watch.Elapsed.TotalSeconds;
        await session.ProcessAsync(true, timeout.Token);
        File.WriteAllText(Path.Combine(output, "live-stream.json"), JsonSerializer.Serialize(new
        {
            audioSeconds = reader.TotalTime.TotalSeconds, firstUpdateSeconds = firstUpdate,
            stoppedAtSeconds = stoppedAt, finishedAtSeconds = watch.Elapsed.TotalSeconds,
            transcript = session.Text, deliveredPreview = delivered, updates
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (firstUpdate is null || firstUpdate >= stoppedAt || updates.Count < 2)
            throw new Exception("No meaningful live updates arrived before the fixture stopped.");
        foreach (var word in new[] { "test", "stream", "clipboard", "delivered" })
            if (!session.Text.Contains(word, StringComparison.OrdinalIgnoreCase)) throw new Exception("Fixture transcript omitted expected word: " + word);
        var proofread = await groq.ProofreadStreamAsync(key, "um I I think this is a test and the results is ready", "openai/gpt-oss-20b", timeout.Token);
        File.WriteAllText(Path.Combine(output, "proofread.json"), JsonSerializer.Serialize(proofread));
        if (!proofread.Text.Contains("I think", StringComparison.OrdinalIgnoreCase) || !proofread.Text.Contains("test", StringComparison.OrdinalIgnoreCase)) throw new Exception("Proofreading changed meaning.");
        File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: real Groq transcription of a synthetic speech fixture, updates before stopping, authoritative final transcript includes the complete audio, final tail delivered.");

        async Task RunLiveAsync()
        {
            try
            {
                var delay = StreamingCadence.Interval;
                while (true)
                {
                    await Task.Delay(delay, live.Token);
                    var started = Stopwatch.GetTimestamp();
                    await session.ProcessAsync(false, live.Token);
                    delay = StreamingCadence.After(Stopwatch.GetElapsedTime(started));
                }
            }
            catch (OperationCanceledException) when (live.IsCancellationRequested) { }
        }
    }
}
