using Core.DomainModels;
using Core.Interfaces.Repositories;
using Dapper;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Infrastructure.Persistence.Repositories.Sql;
using Infrastructure.Persistence.UnitOfWork;

namespace Infrastructure.Persistence.Repositories;

internal class ProjectRepository(IDbConnectionFactory factory, IUnitOfWork uow) : IProjectRepository
{
    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entity = await conn.QuerySingleOrDefaultAsync<ProjectEntity>(
            new CommandDefinition(ProjectSql.GetById, new { id }, cancellationToken: cancellationToken));
        return entity?.ToDomain();
    }

    public async Task<List<Project>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entities = await conn.QueryAsync<ProjectEntity>(
            new CommandDefinition(ProjectSql.GetAll, cancellationToken: cancellationToken));
        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<Project?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entity = await conn.QuerySingleOrDefaultAsync<ProjectEntity>(
            new CommandDefinition(ProjectSql.GetBySlug, new { slug }, cancellationToken: cancellationToken));
        return entity?.ToDomain();
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(ProjectSql.Exists, new { id }, cancellationToken: cancellationToken));
    }

    public async Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default)
    {
        var entity = project.ToEntity();
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(ProjectSql.Insert, new
        {
            entity.Id,
            entity.Name,
            entity.Slug,
            entity.AnalyzerPromptText,
            Categories = entity.Categories,
            entity.OutputLanguage,
            entity.OutputLanguageName,
            entity.IsActive,
            entity.CreatedAt,
        }, cancellationToken: cancellationToken));
        return entity.ToDomain();
    }

    public async Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        var entity = project.ToEntity();
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(ProjectSql.Update, new
        {
            entity.Id,
            entity.Name,
            entity.Slug,
            entity.AnalyzerPromptText,
            Categories = entity.Categories,
            entity.OutputLanguage,
            entity.OutputLanguageName,
            entity.IsActive,
        }, cancellationToken: cancellationToken));
    }

    public async Task UpdateActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(ProjectSql.UpdateActive,
            new { id, isActive },
            cancellationToken: cancellationToken));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(ProjectSql.Delete,
            new { id },
            cancellationToken: cancellationToken));
    }
}
