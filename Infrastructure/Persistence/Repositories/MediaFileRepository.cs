using Core.DomainModels;
using Core.Interfaces.Repositories;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class MediaFileRepository(NewsParserDbContext context) : IMediaFileRepository
{
	public async Task AddAsync(MediaFile mediaFile, CancellationToken cancellationToken = default)
	{
		var entity = mediaFile.ToEntity();
		context.MediaFiles.Add(entity);
		await context.SaveChangesAsync(cancellationToken);
	}

	public async Task<List<MediaFile>> GetByArticleIdAsync(Guid articleId, CancellationToken cancellationToken = default)
	{
		var entities = await context.MediaFiles
			.Where(m => m.ArticleId == articleId)
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}

	public Task<bool> ExistsByArticleAndUrlAsync(Guid articleId, string originalUrl, CancellationToken cancellationToken = default)
	{
		return context.MediaFiles
			.AnyAsync(m => m.ArticleId == articleId && m.OriginalUrl == originalUrl, cancellationToken);
	}

	public async Task<List<MediaFile>> GetByIdsAsync(List<Guid> ids, CancellationToken cancellationToken = default)
	{
		var entities = await context.MediaFiles
			.Where(m => ids.Contains(m.Id))
			.ToListAsync(cancellationToken);

		return entities.Select(e => e.ToDomain()).ToList();
	}
}
