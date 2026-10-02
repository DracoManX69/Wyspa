using System.Text.Json;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class StreamingDictationTests
{
    [Fact]
    public async Task SubsecondSnapshots_StillRequireAgreement_AndDoNotReuploadUnchangedAudio()
    {
        var groq = new FakeGroq(); var updates = new List<(string Delta, string Full)>();
        using var session = Session(groq, updates);
        groq.Responses.Enqueue(Json("This is", 0, .3));
        groq.Responses.Enqueue(Json("This is a test", 0, .3));
        session.AddAudio(null, Pcm(.85)); await session.ProcessAsync(false, default);
        Assert.Single(groq.Paths); Assert.Empty(updates);
        await session.ProcessAsync(false, default);
        Assert.Single(groq.Paths);
        session.AddAudio(null, Pcm(.85)); await session.ProcessAsync(false, default);
        Assert.Equal("This is", updates.Single().Full);
    }

    [Theory]
    [InlineData(300, 700)]
    [InlineData(1800, 100)]
    public void Cadence_AccountsForRequestTime_WithoutBusyLoop(int requestMs, int delayMs) =>
        Assert.Equal(TimeSpan.FromMilliseconds(delayMs), StreamingCadence.After(TimeSpan.FromMilliseconds(requestMs)));

    [Fact]
    public void Agreement_HoldsUncertainTail_ThenAppendsFinalWordsWithoutReplaying()
    {
        var transcript = new StreamingTranscript();
        Assert.Equal("", transcript.Accept(Speech("This is a pest"), 0, 2, false, false));
        Assert.Equal("This is a", transcript.Accept(Speech("This is a test of"), 0, 3, false, false));
        Assert.Equal(" test of", transcript.Accept(Speech("This is a test of Wyspa Stream"), 0, 4, false, false));
        Assert.Equal(" Wyspa Stream mode.", transcript.Accept(Speech("This is a test of Wyspa Stream mode."), 0, 5, true, false));
        Assert.Equal("This is a test of Wyspa Stream mode.", transcript.Text);
    }

    [Fact]
    public void RepeatedWords_AreNotDeduplicatedAsText_AndOverlappingAudioIsNotReplayed()
    {
        var transcript = new StreamingTranscript();
        transcript.Accept(Speech("very very good"), 0, 3, true, false);
        // Retained audio includes the last word, followed by a genuinely repeated word.
        transcript.EndWindow();
        var next = new SpeechResult("good good morning", [new(0, .2, "good"), new(.3, .5, "good"), new(.6, .9, "morning")]);
        transcript.Accept(next, .65, 2, true, false);
        Assert.Equal("very very good good morning", transcript.Text);
    }

    [Fact]
    public void HardWindowBoundary_HoldsBackCutWord()
    {
        var transcript = new StreamingTranscript();
        var result = new SpeechResult("first cut", [new(.1, .5, "first"), new(19.3, 19.9, "cut")]);
        Assert.Equal("first", transcript.Accept(result, 0, 20, false, true));
        Assert.Equal(.5, transcript.CommittedThrough);
    }

    [Fact]
    public void LaterRevision_DoesNotEraseAlreadyCommittedText()
    {
        var transcript = new StreamingTranscript();
        transcript.Accept(Speech("a cat sat"), 0, 2, false, false);
        transcript.Accept(Speech("a cat sat down"), 0, 3, false, false);
        transcript.Accept(Speech("the cat sat down"), 0, 3, true, false);
        Assert.Equal("a cat sat down", transcript.Text);
    }

    [Fact]
    public void DelayedPunctuation_CanBeAppendedToLastWord_WithoutReplayingIt()
    {
        var transcript = new StreamingTranscript();
        transcript.Accept(Speech("Stream mode"), 0, 2, true, false);
        Assert.Equal(". Next", transcript.Accept(Speech("Stream mode. Next"), 0, 3, true, false));
        Assert.Equal(" sentence.", transcript.Accept(Speech("Stream mode. Next sentence."), 0, 4, true, false));
        Assert.Equal("Stream mode. Next sentence.", transcript.Text);
    }

    [Fact]
    public void Buffer_BoundsSnapshots_AndRetainsPausesAndQuietTails()
    {
        using var buffer = new StreamingAudioBuffer();
        buffer.Append(Pcm(2, false)); buffer.Append(Pcm(1));
        buffer.Append(Pcm(1.6, false)); buffer.Append(Pcm(30));
        var first = buffer.Read(0, false);
        Assert.False(first.Final);
        Assert.True(first.HasSpeech);
        Assert.Equal(0, first.StartSeconds);
        Assert.Equal(20, first.Duration);
        Assert.True(first.AtLimit);
        var final = buffer.Read(0, true, 120 * 16000);
        Assert.True(final.Final);
        Assert.Equal(34.6, final.Duration, 2);
    }

    [Fact]
    public async Task Session_UpdatesBeforeStop_ClipboardContainsEntireDictation_ThenResets()
    {
        var groq = new FakeGroq();
        var updates = new List<(string Delta, string Full)>();
        using var session = Session(groq, updates);
        groq.Responses.Enqueue(Json("This is a test"));
        groq.Responses.Enqueue(Json("This is a test of Wyspa"));
        groq.Responses.Enqueue(Json("This is a test of Wyspa Stream mode"));
        session.AddAudio(null, Pcm(1.6));
        await session.ProcessAsync(false, default);
        Assert.Empty(updates);
        session.AddAudio(null, Pcm(1.1));
        await session.ProcessAsync(false, default);
        Assert.Equal("This is a test", updates.Single().Full);
        session.AddAudio(null, Pcm(1));
        await session.ProcessAsync(true, default);
        Assert.Equal("This is a test of Wyspa Stream mode", updates.Last().Full);
        Assert.Equal(updates.Last().Full, string.Concat(updates.Select(u => u.Delta)));
        Assert.All(groq.Paths, path => Assert.False(File.Exists(path)));
        using var second = Session(groq, updates);
        groq.Responses.Enqueue(Json("New dictation"));
        second.AddAudio(null, Pcm(1));
        await second.ProcessAsync(true, default);
        Assert.Equal("New dictation", updates.Last().Full);
    }

    [Fact]
    public async Task Silence_DoesNotUpload_AndShortFinalPhraseIsFlushed()
    {
        var groq = new FakeGroq();
        var updates = new List<(string, string)>();
        using var session = Session(groq, updates);
        session.AddAudio(null, Pcm(2, false));
        await session.ProcessAsync(false, default);
        Assert.Empty(groq.Paths);
        session.AddAudio(null, Pcm(.3));
        groq.Responses.Enqueue(Json("Yes", .2, .1));
        await session.ProcessAsync(true, default);
        Assert.Equal("Yes", session.Text);
        Assert.Single(groq.Paths);
    }

    [Fact]
    public async Task Stop_RecognizesAllPhrasesAndPausesTogether()
    {
        var groq = new FakeGroq();
        using var session = Session(groq, []);
        var expected = string.Join(" ", Enumerable.Range(0, 25).Select(i => "word" + i));
        session.AddAudio(null, Pcm(.04)); session.AddAudio(null, Pcm(.6, false));
        for (var i = 0; i < 25; i++)
        {
            session.AddAudio(null, Pcm(.8)); session.AddAudio(null, Pcm(1.6, false));
        }
        groq.Responses.Enqueue(Json(expected, .64, 2.4));
        await session.ProcessAsync(true, default);
        Assert.Equal(expected, session.Text);
        Assert.Single(groq.Paths);
        Assert.Equal(44 + (long)(60.64 * 16000) * 2, groq.UploadLengths.Single());
    }

    [Fact]
    public async Task InFlightRequest_IsCancelled_NoLateInsertion_FinalFlushRecoversAudio()
    {
        var groq = new FakeGroq();
        var updates = new List<(string, string)>();
        using var session = Session(groq, updates);
        session.AddAudio(null, Pcm(2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        groq.Handler = async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return ""; };
        using var cancel = new CancellationTokenSource();
        var request = session.ProcessAsync(false, cancel.Token);
        await entered.Task;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Empty(updates);
        Assert.False(File.Exists(groq.Paths.Single()));
        groq.Handler = null;
        groq.Responses.Enqueue(Json("Recovered words"));
        await session.ProcessAsync(true, default);
        Assert.Equal("Recovered words", session.Text);
    }

    [Fact]
    public async Task SlowRequests_Serialize_AndNewAudioRemainsAvailable()
    {
        var groq = new FakeGroq();
        using var session = Session(groq, []);
        session.AddAudio(null, Pcm(2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        groq.Handler = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); return Json("one two"); };
        var first = session.ProcessAsync(false, default);
        await entered.Task;
        session.AddAudio(null, Pcm(2));
        var second = session.ProcessAsync(true, default);
        Assert.Single(groq.Paths);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(2, groq.Paths.Count);
        Assert.Equal("one two", session.Text);
    }

    [Fact]
    public async Task FailedUpload_DoesNotAdvanceCursor_OrLeaveSnapshotFiles()
    {
        var groq = new FakeGroq { Handler = _ => throw new HttpRequestException("offline") };
        using var session = Session(groq, []);
        session.AddAudio(null, Pcm(2));
        await Assert.ThrowsAsync<HttpRequestException>(() => session.ProcessAsync(false, default));
        Assert.False(File.Exists(groq.Paths.Single()));
        groq.Handler = null;
        groq.Responses.Enqueue(Json("Recovered"));
        await session.ProcessAsync(true, default);
        Assert.Equal("Recovered", session.Text);
    }

    [Fact]
    public async Task LongFinalRecording_UsesBoundedOverlappingWindows_WithoutLosingRepeatedWords()
    {
        var groq = new FakeGroq();
        using var session = Session(groq, []);
        session.AddAudio(null, Pcm(131));
        groq.Responses.Enqueue(Json(string.Join(" ", Enumerable.Range(0, 120).Select(i => "word" + i)), .1, 1));
        groq.Responses.Enqueue(Json(string.Join(" ", Enumerable.Range(116, 15).Select(i => "word" + i)), .1, 1));
        await session.ProcessAsync(true, default);
        Assert.Equal(string.Join(" ", Enumerable.Range(0, 131).Select(i => "word" + i)), session.Text);
        Assert.Equal(2, groq.Paths.Count);
        Assert.All(groq.UploadLengths, length => Assert.InRange(length, 44, 3840044));
    }

    [Fact]
    public async Task ClipboardFailure_RetriesUndeliveredPrefixOnStop()
    {
        var groq = new FakeGroq();
        groq.Responses.Enqueue(Json("Retry this text"));
        groq.Responses.Enqueue(Json("Retry this text"));
        groq.Responses.Enqueue(Json("Retry this text"));
        var attempts = 0; var delivered = "";
        using var session = new StreamingDictationSession(groq, "test", new("whisper-large-v3-turbo", null, null), (delta, _, _) =>
        {
            if (++attempts == 1) throw new IOException("Clipboard busy");
            delivered += delta;
            return Task.CompletedTask;
        });
        session.AddAudio(null, Pcm(1));
        await session.ProcessAsync(false, default);
        session.AddAudio(null, Pcm(1));
        await Assert.ThrowsAsync<IOException>(() => session.ProcessAsync(false, default));
        await session.ProcessAsync(true, default);
        Assert.Equal("Retry this text", delivered);
    }

    internal static SpeechResult Speech(string text, double start = 0, double step = .3) => new(text,
        text.Split(' ').Select((word, i) => new TimedWord(start + i * step, start + (i + 1) * step - .03, word)).ToArray());

    internal static string Json(string text, double start = 0, double step = .3) => JsonSerializer.Serialize(new
    {
        text, words = Speech(text, start, step).Words.Select(w => new { start = w.Start, end = w.End, word = w.Text })
    });

    internal static byte[] Pcm(double seconds, bool voiced = true)
    {
        var bytes = new byte[(int)(seconds * 16000) * 2];
        if (voiced)
            for (var i = 0; i < bytes.Length; i += 2)
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i, 2), (short)(Math.Sin(i * .1) * 5000));
        return bytes;
    }

    private static StreamingDictationSession Session(FakeGroq groq, List<(string Delta, string Full)> updates) =>
        new(groq, "test", new("whisper-large-v3-turbo", "en", "Wyspa"), (delta, full, _) =>
        { updates.Add((delta, full)); return Task.CompletedTask; });

    internal sealed class FakeGroq : IGroqTranscriptionClient, IStreamProofreader
    {
        public Queue<string> Responses { get; } = new();
        public List<string> Paths { get; } = [];
        public List<long> UploadLengths { get; } = [];
        public Func<CancellationToken, Task<string>>? Handler { get; set; }
        public Func<string, Task<StreamFixResult>>? ProofreadHandler;
        public int ProofreadCalls;
        public string ExpectedFormat = "verbose_json";
        public Task<StreamFixResult> ProofreadStreamAsync(string key, string text, string model, CancellationToken token)
        { ProofreadCalls++; return ProofreadHandler is null ? throw new Exception("Unexpected proofreading") : ProofreadHandler(text); }
        public Task<string> TranscribeAsync(string key, string path, TranscriptionOptions options, CancellationToken token)
        {
            Assert.Equal(ExpectedFormat, options.ResponseFormat);
            Paths.Add(path);
            UploadLengths.Add(new FileInfo(path).Length);
            return Handler is null ? Task.FromResult(Responses.Dequeue()) : Handler(token);
        }
        public Task<ConnectionTestResult> TestConnectionAsync(string key, CancellationToken token) => throw new NotSupportedException();
        public Task<IntentResolution> InterpretIntentAsync(string key, string text, string model, CancellationToken token) => throw new Exception("Streaming must not run intent actions.");
        public Task<string> CleanupTranscriptAsync(string key, string text, string model, WritingCleanupTone tone, string? prompt, CancellationToken token) => throw new Exception("Streaming must not run rewriting.");
    }
}
