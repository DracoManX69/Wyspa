using System.Windows.Input;

namespace Wyspa.App;

public static class AppNavigationCommands
{
    public static RoutedUICommand OpenGroqSettings { get; } = new("Groq settings", nameof(OpenGroqSettings), typeof(AppNavigationCommands));
    public static RoutedUICommand OpenAudioSettings { get; } = new("Audio & Capture settings", nameof(OpenAudioSettings), typeof(AppNavigationCommands));
    public static RoutedUICommand OpenConversationSettings { get; } = new("Conversation settings", nameof(OpenConversationSettings), typeof(AppNavigationCommands));
    public static RoutedUICommand OpenRepository { get; } = new("About Wyspa on GitHub", nameof(OpenRepository), typeof(AppNavigationCommands));
    public static Uri RepositoryUri { get; } = new("https://github.com/DracoManX69/Wyspa");
}
