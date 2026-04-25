using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IAiRequestLogRepository
{
    Task AddAsync(AiRequestLog entry, CancellationToken cancellationToken = default);

    Task<AiRequestLogMetrics> GetMetricsAsync(AiRequestLogFilter filter, CancellationToken cancellationToken = default);

    Task<List<AiRequestLog>> GetPagedAsync(AiRequestLogFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> CountAsync(AiRequestLogFilter filter, CancellationToken cancellationToken = default);

    Task<AiRequestLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
