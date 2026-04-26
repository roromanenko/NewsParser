using Core.DomainModels;

namespace Core.Interfaces.Services;

public interface IProjectService
{
	Task<List<Project>> GetAllAsync(CancellationToken cancellationToken = default);
	Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<Project?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
	Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default);
	Task UpdateAsync(Project project, CancellationToken cancellationToken = default);
	Task UpdateActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
