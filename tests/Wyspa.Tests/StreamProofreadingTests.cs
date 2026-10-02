using System.Net;
using System.Text.Json;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class StreamProofreadingTests
{
    [Theory]
    [InlineData("um I I think this is a test", "I think this is a test.")]
    [InlineData("these is the results", "These are the results.")]
    [InlineData("this is uh a test", "This is a test.")]
    public void ConservativeEdits_AcceptHesitationStutterCasePunctuationAndAgreement(string original, string replacement)
    {
        var result = StreamProofreading.Apply(original, Edits((original, replacement)));
        Assert.Equal(replacement, result.Text);
        Assert.False(result.RejectedEdits);
    }

    [Theory]
    [InlineData("I do not agree", "I agree")]
    [InlineData("I might send it", "I will send it")]
    [InlineData("Send 15 mg to Daniel", "Send 50 mg to Daniel")]
    [InlineData("Wyspa uses Groq", "Whisper uses Groq")]
    [InlineData("very very good", "very good")]
    [InlineData("I think it is fine", "It is fine")]
    [InlineData("Is this correct?", "This is correct.")]
    [InlineData("I like this", "I this")]
    [InlineData("a cat", "a dog")]
    [InlineData("2026-10-02", "2026-02-10")]
    [InlineData("-15", "15")]
    public void RejectsChangedMeaningFactsUncertaintyNamesAndEmphasis(string original, string replacement)
    {
        var result = StreamProofreading.Apply(original, Edits((original, replacement)));
        Assert.Equal(original, result.Text);
        Assert.True(result.RejectedEdits);
    }

    [Fact]
    public void RejectsAmbiguousOverlappingPartialWordAndMalformedEdits()
    {
        Assert.Equal("um test um", StreamProofreading.Apply("um test um", Edits(("um", ""))).Text);
        Assert.Equal("human", StreamProofreading.Apply("human", Edits(("um", ""))).Text);
        Assert.Equal("Hello", StreamProofreading.Apply("hello", Edits(("hello", "Hello"), ("hello", "Hello."))).Text);
        foreach (var invalid in new[] { "garbage", "{}", "{\"edits\":42}", "{\"edits\":[null]}" })
            Assert.Equal("Original", StreamProofreading.Apply("Original", invalid).Text);
    }

    [Fact]
    public async Task RealClient_UsesDedicatedJsonPrompt_AndValidatesModelEdits()
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
            Assert.Equal(StreamProofreading.Prompt, body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = Edits(("um I do not agree", "I agree")) } } } })) };
        }));
        var result = await new GroqTranscriptionClient(http).ProofreadStreamAsync("test", "um I do not agree", "test-model", default);
        Assert.Equal("um I do not agree", result.Text);
        Assert.True(result.RejectedEdits);
    }

    [Theory]
    [InlineData("This is a test", "This is a test. The final words.", ". The final words.")]
    [InlineData("This is a pest", "This is a test. The final words.", "")]
    [InlineData("", "Complete dictation.", "Complete dictation.")]
    public void FinalTail_IsAppendedOnlyAfterConfirmedLiveWords(string live, string final, string tail) =>
        Assert.Equal(tail, StreamTextReconciliation.AppendOnlyTail(live, final));

    private static string Edits(params (string Original, string Replacement)[] values) => JsonSerializer.Serialize(new { edits = values.Select(v => new { original = v.Original, replacement = v.Replacement }) });
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request); }
}
