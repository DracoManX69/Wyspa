namespace Wyspa.Core.Models;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault = false)
{
    public override string ToString() => Name;
}
