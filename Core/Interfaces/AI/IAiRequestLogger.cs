using Core.DomainModels.AI;

namespace Core.Interfaces.AI;

public interface IAiRequestLogger
{
    Task LogAsync(AiRequestLogEntry entry, CancellationToken cancellationToken = default);
}
