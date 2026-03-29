using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IPublishTargetRepository
{
	Task<List<PublishTarget>> GetAllAsync(CancellationToken cancellationToken = default);
	Task<List<PublishTarget>> GetActiveAsync(CancellationToken cancellationToken = default);
	Task<PublishTarget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<PublishTarget> CreateAsync(PublishTarget target, CancellationToken cancellationToken = default);
	Task UpdateAsync(PublishTarget target, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}