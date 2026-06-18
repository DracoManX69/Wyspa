namespace Wyspa.Core.Models;

public sealed record ConnectionTestResult(
    bool Success,
    bool ModelAvailable,
    string UserMessage,
    IReadOnlyList<string> AvailableModels)
{
    public static ConnectionTestResult Failed(string message) => new(false, false, message, []);
}
