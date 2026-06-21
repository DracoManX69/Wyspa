using System.Net;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class GroqTranscriptionClientTests
{
    [Fact]
    public async Task TestConnection_ConfirmsModelAvailability()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"whisper-large-v3-turbo"}]}""")
        }));
        var client = new GroqTranscriptionClient(http);

        var result = await client.TestConnectionAsync("gsk_test", CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.ModelAvailable);
        Assert.Contains("whisper-large-v3-turbo", result.AvailableModels);
    }

    [Fact]
    public async Task CreateTranscriptionRequest_UsesExpectedMultipartShape()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wyspa-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(path, [0, 1, 2, 3]);
        using var request = GroqTranscriptionClient.CreateTranscriptionRequest(
            "gsk_test",
            path,
            new TranscriptionOptions("whisper-large-v3-turbo", "en", "Wyspa", "text", 0));

        var body = await request.Content!.ReadAsStringAsync();

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("audio/transcriptions", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("gsk_test", request.Headers.Authorization.Parameter);
        Assert.Contains("name=model", body);
        Assert.Contains("whisper-large-v3-turbo", body);
        Assert.Contains("name=response_format", body);
        Assert.Contains("text", body);
        Assert.Contains("name=temperature", body);
        Assert.Contains("name=language", body);
        Assert.Contains("en", body);
        Assert.Contains("name=prompt", body);
        Assert.Contains("Wyspa", body);
        Assert.Contains("name=file", body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "API key")]
    [InlineData(HttpStatusCode.Forbidden, "access")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "too large")]
    [InlineData((HttpStatusCode)429, "rate limiting")]
    [InlineData(HttpStatusCode.BadGateway, "Groq is having trouble")]
    public void MapStatusToMessage_ReturnsFriendlyErrors(HttpStatusCode statusCode, string expected)
    {
        var message = GroqTranscriptionClient.MapStatusToMessage(statusCode);

        Assert.Contains(expected, message);
    }

    [Fact]
    public async Task CreateIntentRequest_UsesChatCompletionsJsonMode()
    {
        using var request = GroqTranscriptionClient.CreateIntentRequest(
            "gsk_test",
            "open task view",
            "llama-3.3-70b-versatile");

        var body = await request.Content!.ReadAsStringAsync();

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("gsk_test", request.Headers.Authorization.Parameter);
        Assert.Contains("\"model\":\"llama-3.3-70b-versatile\"", body);
        Assert.Contains("\"response_format\":{\"type\":\"json_object\"}", body);
        Assert.Contains("open task view", body);
    }

    [Fact]
    public async Task CreateCleanupRequest_UsesToneSpecificChatPrompt()
    {
        using var request = GroqTranscriptionClient.CreateCleanupRequest(
            "gsk_test",
            "like this is basically a message",
            "llama-3.1-8b-instant",
            WritingCleanupTone.Technical);

        var body = await request.Content!.ReadAsStringAsync();

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("gsk_test", request.Headers.Authorization.Parameter);
        Assert.Contains("\"model\":\"llama-3.1-8b-instant\"", body);
        Assert.Contains("clear technical writing", body);
        Assert.Contains("like this is basically a message", body);
    }

    [Fact]
    public async Task CreateCleanupRequest_UsesCustomPromptWhenProvided()
    {
        using var request = GroqTranscriptionClient.CreateCleanupRequest(
            "gsk_test",
            "clean this",
            "llama-3.1-8b-instant",
            WritingCleanupTone.Casual,
            "Custom prompt text.");

        var body = await request.Content!.ReadAsStringAsync();

        Assert.Contains("Custom prompt text.", body);
        Assert.DoesNotContain("friendly natural voice", body);
    }

    [Fact]
    public void ParseIntentResponse_ReturnsWhitelistedAction()
    {
        var response = """
        {
          "choices": [
            {
              "message": {
                "content": "{\"kind\":\"action\",\"text\":\"\",\"action\":\"task_view\",\"confidence\":0.92}"
              }
            }
          ]
        }
        """;

        var intent = GroqTranscriptionClient.ParseIntentResponse(response, "open task view");

        Assert.Equal(IntentDecisionKind.Action, intent.Kind);
        Assert.Equal(VoxAction.TaskView, intent.Action);
        Assert.Equal(0.92, intent.Confidence, precision: 2);
    }

    [Fact]
    public void ParseIntentResponse_FallsBackToInsertOnMalformedJson()
    {
        var intent = GroqTranscriptionClient.ParseIntentResponse("not json", "hello there");

        Assert.Equal(IntentDecisionKind.InsertText, intent.Kind);
        Assert.Equal("hello there", intent.Text);
    }

    [Fact]
    public void ParseTextResponse_ReturnsAssistantText()
    {
        var response = """
        {
          "choices": [
            {
              "message": {
                "content": "This is a clean message."
              }
            }
          ]
        }
        """;

        var cleaned = GroqTranscriptionClient.ParseTextResponse(response, "fallback");

        Assert.Equal("This is a clean message.", cleaned);
    }

    [Fact]
    public void ParseTextResponse_FallsBackOnMalformedJson()
    {
        var cleaned = GroqTranscriptionClient.ParseTextResponse("not json", " original text ");

        Assert.Equal("original text", cleaned);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
