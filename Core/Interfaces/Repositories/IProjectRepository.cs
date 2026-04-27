using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IProjectRepository
{
	Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<List<Project>> GetAllAsync(CancellationToken cancellationToken = default);
	Task<Project?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
	Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default);
	Task UpdateAsync(Project project, CancellationToken cancellationToken = default);
	Task UpdateActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
