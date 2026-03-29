using Core.DomainModels;

namespace Core.Interfaces.Services;

public interface IPublishTargetService
{
	Task<List<PublishTarget>> GetAllAsync(CancellationToken cancellationToken = default);
	Task<List<PublishTarget>> GetActiveAsync(CancellationToken cancellationToken = default);
	Task<PublishTarget> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<PublishTarget> CreateAsync(string name, Platform platform, string identifier, string systemPrompt, CancellationToken cancellationToken = default);
	Task<PublishTarget> UpdateAsync(Guid id, string name, string identifier, string systemPrompt, bool isActive, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}