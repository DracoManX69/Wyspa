using System.Net;
using System.Text.Json;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class NotesTests
{
    [Theory]
    [InlineData("https://youtu.be/jNQXAC9IVRw?t=5")]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw&list=ignore-this-playlist")]
    [InlineData("https://www.youtube.com/shorts/jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/live/jNQXAC9IVRw")]
    public void SingleVideoUrlsAreCanonicalized(string input)
        => Assert.Equal("https://www.youtube.com/watch?v=jNQXAC9IVRw", VideoImporter.NormalizeUrl(input));

    [Theory]
    [InlineData("https://youtube.com/playlist?list=PL123")]
    [InlineData("https://youtube.com.evil.test/watch?v=jNQXAC9IVRw")]
    [InlineData("https://youtube.com@evil.test/watch?v=jNQXAC9IVRw")]
    [InlineData("file:///etc/passwd")]
    [InlineData("--exec malicious")]
    [InlineData("https://youtube.com/watch?v=../../payload")]
    public void RejectsNonVideoAndUnsafeUrls(string input)
        => Assert.Throws<ArgumentException>(() => VideoImporter.NormalizeUrl(input));

    [Fact]
    public async Task NotesSurviveReopenAndExportSpeakerIdentityAndSummary()
    {
        var directory = Directory.CreateTempSubdirectory("wyspa-notes-").FullName;
        try
        {
            var note = new NoteSession { Title = "Planning", MySpeaker = "Speaker 2", Summary = "Agreed to meet Friday", SummaryModel = "my-model" };
            note.Entries.Add(new(Guid.NewGuid(), 2, 5, 6, "Speaker 2", "Friday works"));
            note.Entries.Add(new(Guid.NewGuid(), 1, 1, 3, "Speaker 1", "When can we meet?"));
            await new NoteStore(directory).SaveAsync(note);
            var store = new NoteStore(directory);
            var loaded = Assert.Single(await store.LoadAsync());
            Assert.Equal(note.Id, loaded.Id); Assert.Equal("Speaker 2", loaded.MySpeaker); Assert.Equal(note.Summary, loaded.Summary);
            var text = await File.ReadAllTextAsync(Path.Combine(directory, note.Id + ".txt"));
            Assert.True(text.IndexOf("When can we meet?", StringComparison.Ordinal) < text.IndexOf("Friday works", StringComparison.Ordinal));
            Assert.Contains("You (Speaker 2)", text);
            Assert.DoesNotContain(Directory.EnumerateFiles(directory), p => p.EndsWith(".tmp"));
            await store.DeleteAsync(note.Id); Assert.Empty(await store.LoadAsync()); Assert.Empty(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OneCorruptNoteDoesNotHideTheOthers()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var store = new NoteStore(directory);
            await store.SaveAsync(new NoteSession());
            await File.WriteAllTextAsync(Path.Combine(directory, "broken.json"), "{");
            Assert.Single(await store.LoadAsync()); Assert.True(File.Exists(Path.Combine(directory, "broken.json")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RoomTurnsSplitWordsAndPreserveUncertainOverlap()
    {
        var chunk = new AudioChunk(1, "Room", 10, new float[64000]);
        var speech = new SpeechResult("Hello there yes", [new(.1, .3, "Hello"), new(.6, 1, "there"), new(1.5, 2, "yes")]);
        var entries = TranscriptAssembler.Assemble(chunk, speech, [new(0, 1.3, "Speaker 1"), new(1.2, 3, "Speaker 2")]);
        Assert.Equal(2, entries.Count); Assert.Equal("Hello there", entries[0].Text); Assert.Equal(10.1, entries[0].Start);
        Assert.Equal("Speaker 2", entries[1].Speaker);
        var overlapping = TranscriptAssembler.Assemble(chunk, speech, [new(0, 4, "Speaker 1"), new(0, 4, "Speaker 2")]);
        Assert.Equal("Overlapping voices", Assert.Single(overlapping).Speaker);
    }

    [Fact]
    public void BoundaryOverlapIsNotPrintedAgain()
    {
        var chunk = new AudioChunk(2, "You", 3.3, new float[32000], .2);
        var result = TranscriptAssembler.Assemble(chunk, new("old new", [new(0, .1, "old"), new(.2, .4, "new")]));
        Assert.Equal("new", Assert.Single(result).Text);
    }

    [Fact]
    public void ChunkerSkipsSilenceAndFlushesSpeechWhenLoopbackStopsSendingPackets()
    {
        var clips = new List<(double Start, float[] Samples, double Overlap)>();
        var chunker = new SpeechChunker((start, samples, overlap) => clips.Add((start, samples, overlap)));
        chunker.Add(new float[16000], 0); chunker.Tick(2); Assert.Empty(clips);
        chunker.Add(Enumerable.Repeat(.1f, 16000).ToArray(), 2);
        chunker.Tick(3.6);
        var clip = Assert.Single(clips); Assert.InRange(clip.Start, 1.8, 2); Assert.True(clip.Samples.Length >= 16000);
        chunker.Flush(); Assert.Single(clips);
    }

    [Fact]
    public void ChunkerBoundsContinuousSpeechAndMarksOverlap()
    {
        var clips = new List<(double Start, float[] Samples, double Overlap)>();
        var chunker = new SpeechChunker((start, samples, overlap) => clips.Add((start, samples, overlap)), 2);
        chunker.Add(Enumerable.Repeat(.1f, 16000 * 6).ToArray(), 0); chunker.Flush();
        Assert.True(clips.Count >= 3); Assert.All(clips, c => Assert.True(c.Samples.Length <= 32000));
        Assert.Equal(0, clips[0].Overlap); Assert.Equal(.2, clips[1].Overlap);
        Assert.InRange(clips[1].Start, 1.79, 1.81);
    }

    [Fact]
    public void GroqSilenceMetadataFiltersHallucinatedText()
    {
        var result = GroqNoteIntelligence.Parse("""{"text":"Thank you","words":[{"word":"Thank you","start":0,"end":1}],"segments":[{"start":0,"end":2,"no_speech_prob":0.99}]}""");
        Assert.Empty(result.Words); Assert.Empty(result.Text);
    }

    [Fact]
    public async Task VerboseTranscriptionRequestsWordTimestamps()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var request = GroqTranscriptionClient.CreateTranscriptionRequest("fake-test", path, new("whisper-large-v3-turbo", null, null, "verbose_json"));
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("timestamp_granularities[]", body); Assert.Contains("word", body); Assert.Contains("segment", body);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LongSummaryUsesSelectedModelAndIncludesAllTranscriptParts()
    {
        var bodies = new List<string>();
        using var http = new HttpClient(new Handler(async request =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"choices":[{"finish_reason":"stop","message":{"content":"A concise summary."}}]}""") };
        }));
        var result = await new GroqNoteIntelligence(http).SummariseAsync("fake-key", "BEGIN " + new string('x', 40000) + " END", "custom-model", default);
        Assert.Equal("A concise summary.", result); Assert.True(bodies.Count >= 4);
        Assert.All(bodies, b => Assert.Equal("custom-model", JsonDocument.Parse(b).RootElement.GetProperty("model").GetString()));
        Assert.Contains(bodies, b => b.Contains("BEGIN")); Assert.Contains(bodies, b => b.Contains("END"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "API key")]
    [InlineData(HttpStatusCode.Forbidden, "model")]
    [InlineData(HttpStatusCode.BadRequest, "model ID")]
    public async Task SummaryReportsErrorsWithoutReplacingTranscript(HttpStatusCode status, string message)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(status))));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new GroqNoteIntelligence(http).SummariseAsync("fake-key", "Conversation", "test-model", default));
        Assert.Contains(message, exception.Message);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
    }
}
