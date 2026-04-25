using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IAiRequestLogRepository
{
    Task AddAsync(AiRequestLog entry, CancellationToken cancellationToken = default);
}
