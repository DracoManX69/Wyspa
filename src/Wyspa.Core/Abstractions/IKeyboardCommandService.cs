using Wyspa.Core.Models;

namespace Wyspa.Core.Abstractions;

public interface IKeyboardCommandService
{
    Task SendAsync(KeyPressCommand command, CancellationToken cancellationToken);
}
