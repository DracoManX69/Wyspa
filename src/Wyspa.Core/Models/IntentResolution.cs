namespace Wyspa.Core.Models;

public enum IntentDecisionKind
{
    InsertText,
    Action,
    Ignore
}

public enum VoxAction
{
    Copy,
    Paste,
    Cut,
    SelectAll,
    Undo,
    Redo,
    Enter,
    Tab,
    Escape,
    Backspace,
    Delete,
    TaskView
}

public sealed record IntentResolution(
    IntentDecisionKind Kind,
    string Text,
    VoxAction? Action,
    double Confidence)
{
    public static IntentResolution Insert(string text) => new(IntentDecisionKind.InsertText, text, null, 1);
}
