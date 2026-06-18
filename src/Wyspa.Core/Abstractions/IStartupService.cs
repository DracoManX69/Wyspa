namespace Wyspa.Core.Abstractions;

public interface IStartupService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}
