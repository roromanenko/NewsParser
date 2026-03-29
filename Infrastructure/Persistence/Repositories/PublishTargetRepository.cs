using Core.DomainModels;
using Core.Interfaces.Repositories;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class PublishTargetRepository(NewsParserDbContext db) : IPublishTargetRepository
{
	public async Task<List<PublishTarget>> GetAllAsync(CancellationToken cancellationToken = default)
	{
		var entities = await db.PublishTargets
			.OrderBy(t => t.Name)
			.ToListAsync(cancellationToken);

		return entities.Select(t => t.ToDomain()).ToList();
	}

	public async Task<List<PublishTarget>> GetActiveAsync(CancellationToken cancellationToken = default)
	{
		var entities = await db.PublishTargets
			.Where(t => t.IsActive)
			.OrderBy(t => t.Name)
			.ToListAsync(cancellationToken);

		return entities.Select(t => t.ToDomain()).ToList();
	}

	public async Task<PublishTarget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var entity = await db.PublishTargets
			.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

		return entity?.ToDomain();
	}

	public async Task<PublishTarget> CreateAsync(PublishTarget target, CancellationToken cancellationToken = default)
	{
		var entity = target.ToEntity();
		await db.PublishTargets.AddAsync(entity, cancellationToken);
		await db.SaveChangesAsync(cancellationToken);
		return entity.ToDomain();
	}

	public async Task UpdateAsync(PublishTarget target, CancellationToken cancellationToken = default)
	{
		await db.PublishTargets
			.Where(t => t.Id == target.Id)
			.ExecuteUpdateAsync(s => s
				.SetProperty(t => t.Name, target.Name)
				.SetProperty(t => t.Identifier, target.Identifier)
				.SetProperty(t => t.SystemPrompt, target.SystemPrompt)
				.SetProperty(t => t.IsActive, target.IsActive),
			cancellationToken);
	}

	public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		await db.PublishTargets
			.Where(t => t.Id == id)
			.ExecuteDeleteAsync(cancellationToken);
	}
}