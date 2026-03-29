using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface ISourceRepository
{
	Task<List<Source>> GetAllAsync(CancellationToken cancellationToken = default);
	Task<List<Source>> GetActiveAsync(SourceType type, CancellationToken cancellationToken = default);
	Task<Source?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<bool> ExistsByUrlAsync(string url, CancellationToken cancellationToken = default);
	Task<Source> CreateAsync(Source source, CancellationToken cancellationToken = default);
	Task UpdateAsync(Source source, CancellationToken cancellationToken = default);
	Task UpdateLastFetchedAtAsync(Guid sourceId, DateTimeOffset fetchedAt, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}