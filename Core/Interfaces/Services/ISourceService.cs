using Core.DomainModels;

namespace Core.Interfaces.Services;

public interface ISourceService
{
	Task<List<Source>> GetAllAsync(CancellationToken cancellationToken = default);
	Task<Source> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<Source> CreateAsync(string name, string url, SourceType type, CancellationToken cancellationToken = default);
	Task<Source> UpdateAsync(Guid id, string name, string url, bool isActive, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}