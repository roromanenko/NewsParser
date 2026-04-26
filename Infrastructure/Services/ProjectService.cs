using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Services;

internal class ProjectService(
	IProjectRepository repository,
	ILogger<ProjectService> logger) : IProjectService
{
	public Task<List<Project>> GetAllAsync(CancellationToken cancellationToken = default)
		=> repository.GetAllAsync(cancellationToken);

	public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
		=> repository.GetByIdAsync(id, cancellationToken);

	public Task<Project?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
		=> repository.GetBySlugAsync(slug, cancellationToken);

	public async Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(project.Slug))
			project.Slug = DeriveSlug(project.Name);

		var existing = await repository.GetBySlugAsync(project.Slug, cancellationToken);
		if (existing is not null)
			throw new InvalidOperationException("Slug already in use");

		var newProject = new Project
		{
			Id = Guid.NewGuid(),
			Name = project.Name,
			Slug = project.Slug,
			AnalyzerPromptText = project.AnalyzerPromptText,
			Categories = project.Categories,
			OutputLanguage = project.OutputLanguage,
			OutputLanguageName = project.OutputLanguageName,
			IsActive = project.IsActive,
			CreatedAt = DateTimeOffset.UtcNow,
		};

		return await repository.CreateAsync(newProject, cancellationToken);
	}

	public Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
		=> repository.UpdateAsync(project, cancellationToken);

	public async Task UpdateActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
	{
		if (id == ProjectConstants.DefaultProjectId && !isActive)
			logger.LogWarning("Deactivating the Default project (id {Id})", id);

		await repository.UpdateActiveAsync(id, isActive, cancellationToken);
	}

	public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		if (id == ProjectConstants.DefaultProjectId)
			throw new InvalidOperationException("The Default project cannot be deleted");

		try
		{
			await repository.DeleteAsync(id, cancellationToken);
		}
		catch (PostgresException ex) when (ex.SqlState == "23503")
		{
			throw new InvalidOperationException("Project has children; archive it instead of deleting.", ex);
		}
	}

	private static string DeriveSlug(string name)
		=> name.ToLowerInvariant().Replace(' ', '-');
}
