using Core.DomainModels;
using Core.Interfaces.Repositories;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class SourceRepository : ISourceRepository
{
	private readonly NewsParserDbContext _context;

	public SourceRepository(NewsParserDbContext context)
	{
		_context = context;
	}

	public async Task<List<Source>> GetActiveAsync(SourceType type, CancellationToken cancellationToken = default)
	{
		var entities = await _context.Sources
			.Where(s => s.IsActive && s.Type == type.ToString())
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task UpdateLastFetchedAtAsync(Guid sourceId, DateTimeOffset fetchedAt, CancellationToken cancellationToken = default)
	{
		await _context.Sources
			.Where(s => s.Id == sourceId)
			.ExecuteUpdateAsync(s => s.SetProperty(x => x.LastFetchedAt, fetchedAt), cancellationToken);
	}

	public async Task<List<Source>> GetAllAsync(CancellationToken cancellationToken = default)
	{
		var entities = await _context.Sources
			.OrderBy(s => s.Name)
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public async Task<Source?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var entity = await _context.Sources
			.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

		return entity?.ToDomain();
	}

	public async Task<bool> ExistsByUrlAsync(string url, CancellationToken cancellationToken = default)
	{
		return await _context.Sources
			.AnyAsync(s => s.Url == url, cancellationToken);
	}

	public async Task<Source> CreateAsync(Source source, CancellationToken cancellationToken = default)
	{
		var entity = source.ToEntity();
		await _context.Sources.AddAsync(entity, cancellationToken);
		await _context.SaveChangesAsync(cancellationToken);
		return entity.ToDomain();
	}

	public async Task UpdateAsync(Source source, CancellationToken cancellationToken = default)
	{
		await _context.Sources
			.Where(s => s.Id == source.Id)
			.ExecuteUpdateAsync(s => s
				.SetProperty(x => x.Name, source.Name)
				.SetProperty(x => x.Url, source.Url)
				.SetProperty(x => x.IsActive, source.IsActive),
			cancellationToken);
	}

	public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		await _context.Sources
			.Where(s => s.Id == id)
			.ExecuteDeleteAsync(cancellationToken);
	}
}