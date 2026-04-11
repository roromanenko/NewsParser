using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IMediaFileRepository
{
	Task AddAsync(MediaFile mediaFile, CancellationToken cancellationToken = default);
	Task<List<MediaFile>> GetByArticleIdAsync(Guid articleId, CancellationToken cancellationToken = default);
	Task<bool> ExistsByArticleAndUrlAsync(Guid articleId, string originalUrl, CancellationToken cancellationToken = default);
}
