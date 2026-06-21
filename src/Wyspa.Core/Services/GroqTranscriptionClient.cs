using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public sealed class GroqTranscriptionClient : IGroqTranscriptionClient
{
    public const string BaseUrl = "https://api.groq.com/openai/v1/";
    public const string DefaultModel = "whisper-large-v3-turbo";
    public const string DefaultIntentModel = "llama-3.3-70b-versatile";
    public const string DefaultCleanupModel = "llama-3.1-8b-instant";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public GroqTranscriptionClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(BaseUrl);
        }
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "models");
        AddAuth(request, apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ConnectionTestResult.Failed(MapStatusToMessage(response.StatusCode));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var models = document.RootElement.TryGetProperty("data", out var data)
                ? data.EnumerateArray()
                    .Select(model => model.TryGetProperty("id", out var id) ? id.GetString() : null)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .ToArray()
                : [];

            var available = models.Contains(DefaultModel, StringComparer.OrdinalIgnoreCase);
            var message = available
                ? "Connected. Groq Whisper Large v3 Turbo is available."
                : "Connected, but whisper-large-v3-turbo was not listed for this key.";
            return new ConnectionTestResult(true, available, message, models);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ConnectionTestResult.Failed("Groq did not respond in time. Check your connection and try again.");
        }
        catch (HttpRequestException)
        {
            return ConnectionTestResult.Failed("Could not reach Groq. Check your network connection.");
        }
    }

    public async Task<string> TranscribeAsync(string apiKey, string audioFilePath, TranscriptionOptions options, CancellationToken cancellationToken)
    {
        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException("The recording could not be found.", audioFilePath);
        }

        using var request = CreateTranscriptionRequest(apiKey, audioFilePath, options);
        using var response = await SendWithRetryAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(MapStatusToMessage(response.StatusCode));
        }

        return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
    }

    public async Task<IntentResolution> InterpretIntentAsync(string apiKey, string transcript, string modelId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return new IntentResolution(IntentDecisionKind.Ignore, string.Empty, null, 1);
        }

        using var request = CreateIntentRequest(apiKey, transcript, string.IsNullOrWhiteSpace(modelId) ? DefaultIntentModel : modelId);
        using var response = await SendWithRetryAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return IntentResolution.Insert(transcript);
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseIntentResponse(responseJson, transcript);
    }

    public async Task<string> CleanupTranscriptAsync(string apiKey, string transcript, string modelId, WritingCleanupTone tone, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        using var request = CreateCleanupRequest(apiKey, transcript, string.IsNullOrWhiteSpace(modelId) ? DefaultCleanupModel : modelId, tone);
        using var response = await SendWithRetryAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return transcript.Trim();
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseTextResponse(responseJson, transcript);
    }

    public static HttpRequestMessage CreateTranscriptionRequest(string apiKey, string audioFilePath, TranscriptionOptions options)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions");
        AddAuth(request, apiKey);

        var content = new MultipartFormDataContent
        {
            { new StringContent(options.ModelId), "model" },
            { new StringContent(options.ResponseFormat), "response_format" },
            { new StringContent(options.Temperature.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)), "temperature" }
        };

        if (!string.IsNullOrWhiteSpace(options.Language))
        {
            content.Add(new StringContent(options.Language), "language");
        }

        if (!string.IsNullOrWhiteSpace(options.Prompt))
        {
            content.Add(new StringContent(options.Prompt), "prompt");
        }

        var fileContent = new StreamContent(File.OpenRead(audioFilePath));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", Path.GetFileName(audioFilePath));
        request.Content = content;
        return request;
    }

    public static HttpRequestMessage CreateIntentRequest(string apiKey, string transcript, string modelId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        AddAuth(request, apiKey);

        var body = new
        {
            model = string.IsNullOrWhiteSpace(modelId) ? DefaultIntentModel : modelId,
            temperature = 0,
            max_completion_tokens = 140,
            response_format = new { type = "json_object" },
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = """
You classify a short voice transcript for a Windows dictation app.
Return only valid JSON with:
{"kind":"insert|action|ignore","text":"string","action":"copy|paste|cut|select_all|undo|redo|enter|tab|escape|backspace|delete|task_view|null","confidence":0.0}

Use "action" only when the speaker clearly wants the computer to perform one of the listed actions.
Use "insert" for ordinary dictation, even if it mentions action words.
Use "ignore" for empty filler, accidental noise, or no user intent.
For insert, put the final text to type in "text" and action must be null.
"""
                },
                new { role = "user", content = transcript }
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return request;
    }

    public static HttpRequestMessage CreateCleanupRequest(string apiKey, string transcript, string modelId, WritingCleanupTone tone)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        AddAuth(request, apiKey);

        var body = new
        {
            model = string.IsNullOrWhiteSpace(modelId) ? DefaultCleanupModel : modelId,
            temperature = 0.1,
            max_completion_tokens = EstimateCleanupTokenLimit(transcript),
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = BuildCleanupSystemPrompt(tone)
                },
                new { role = "user", content = transcript }
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return request;
    }

    public static IntentResolution ParseIntentResponse(string responseJson, string fallbackText)
    {
        try
        {
            using var response = JsonDocument.Parse(responseJson);
            var content = response.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                return IntentResolution.Insert(fallbackText);
            }

            using var decision = JsonDocument.Parse(content);
            var root = decision.RootElement;
            var kindText = root.TryGetProperty("kind", out var kindProperty) ? kindProperty.GetString() : "insert";
            var text = root.TryGetProperty("text", out var textProperty) ? textProperty.GetString() ?? string.Empty : string.Empty;
            var confidence = root.TryGetProperty("confidence", out var confidenceProperty) && confidenceProperty.TryGetDouble(out var parsedConfidence)
                ? Math.Clamp(parsedConfidence, 0, 1)
                : 0;

            return kindText?.ToLowerInvariant() switch
            {
                "action" when TryParseAction(root, out var action) => new IntentResolution(IntentDecisionKind.Action, text, action, confidence),
                "ignore" => new IntentResolution(IntentDecisionKind.Ignore, string.Empty, null, confidence),
                _ => IntentResolution.Insert(string.IsNullOrWhiteSpace(text) ? fallbackText : text)
            };
        }
        catch (JsonException)
        {
            return IntentResolution.Insert(fallbackText);
        }
        catch (KeyNotFoundException)
        {
            return IntentResolution.Insert(fallbackText);
        }
        catch (InvalidOperationException)
        {
            return IntentResolution.Insert(fallbackText);
        }
    }

    public static string ParseTextResponse(string responseJson, string fallbackText)
    {
        try
        {
            using var response = JsonDocument.Parse(responseJson);
            var content = response.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content) ? fallbackText.Trim() : content.Trim();
        }
        catch (JsonException)
        {
            return fallbackText.Trim();
        }
        catch (KeyNotFoundException)
        {
            return fallbackText.Trim();
        }
        catch (InvalidOperationException)
        {
            return fallbackText.Trim();
        }
    }

    public static string MapStatusToMessage(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "The Groq API key was rejected. Check that it was copied correctly.",
        HttpStatusCode.Forbidden => "This Groq key does not have access to the selected model.",
        HttpStatusCode.RequestEntityTooLarge => "The recording is too large to upload. Try a shorter dictation.",
        (HttpStatusCode)429 => "Groq is rate limiting requests. Wait a moment and try again.",
        >= HttpStatusCode.InternalServerError => "Groq is having trouble right now. Try again shortly.",
        _ => "Groq could not transcribe the recording. Try again."
    };

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var clone = await CloneRequestAsync(request, cancellationToken);
            var response = await _httpClient.SendAsync(clone, cancellationToken);
            if (response.StatusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429 ||
                response.StatusCode >= HttpStatusCode.InternalServerError)
            {
                if (attempt < 2)
                {
                    response.Dispose();
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)), cancellationToken);
                    continue;
                }
            }

            return response;
        }

        throw new InvalidOperationException("Groq did not return a response.");
    }

    private static void AddAuth(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static int EstimateCleanupTokenLimit(string transcript)
    {
        var roughInputTokens = Math.Max(80, transcript.Length / 4);
        return Math.Clamp(roughInputTokens + 180, 260, 1400);
    }

    private static string BuildCleanupSystemPrompt(WritingCleanupTone tone) => tone switch
    {
        WritingCleanupTone.Formal => """
You rewrite dictated speech into polished written text.
Use a formal, professional tone with complete sentences and clear paragraphing.
Remove filler words, false starts, repeated phrasing, and conversational clutter.
Preserve the speaker's meaning, names, facts, and intent.
Do not add new information.
Return only the rewritten text.
""",
        WritingCleanupTone.Technical => """
You rewrite dictated speech into clear technical writing.
Keep technical terms, product names, numbers, commands, acronyms, and code-like wording accurate.
Structure the result with concise paragraphs or bullets when that makes procedures or explanations easier to scan.
Remove filler words, false starts, repeated phrasing, and conversational clutter.
Preserve the speaker's meaning and do not add new information.
Return only the rewritten text.
""",
        _ => """
You rewrite dictated speech into a clean casual written message.
Keep the speaker's friendly natural voice while removing filler words, false starts, repetition, and rambling.
Use readable punctuation and short paragraphs when helpful.
Preserve the speaker's meaning, names, facts, and intent.
Do not make it overly formal and do not add new information.
Return only the rewritten text.
"""
    };

    private static bool TryParseAction(JsonElement root, out VoxAction action)
    {
        action = default;
        var value = root.TryGetProperty("action", out var actionProperty) ? actionProperty.GetString() : null;
        action = value?.ToLowerInvariant() switch
        {
            "copy" => VoxAction.Copy,
            "paste" => VoxAction.Paste,
            "cut" => VoxAction.Cut,
            "select_all" => VoxAction.SelectAll,
            "undo" => VoxAction.Undo,
            "redo" => VoxAction.Redo,
            "enter" => VoxAction.Enter,
            "tab" => VoxAction.Tab,
            "escape" => VoxAction.Escape,
            "backspace" => VoxAction.Backspace,
            "delete" => VoxAction.Delete,
            "task_view" => VoxAction.TaskView,
            _ => default
        };

        return value is "copy" or "paste" or "cut" or "select_all" or "undo" or "redo" or
            "enter" or "tab" or "escape" or "backspace" or "delete" or "task_view";
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (original.Content is not null)
        {
            var memory = new MemoryStream();
            await original.Content.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;
            var content = new StreamContent(memory);
            foreach (var header in original.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            clone.Content = content;
        }

        return clone;
    }
}
