using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IMediaFileRepository
{
	Task AddAsync(MediaFile mediaFile, CancellationToken cancellationToken = default);
	Task<List<MediaFile>> GetByArticleIdAsync(Guid articleId, CancellationToken cancellationToken = default);
	Task<bool> ExistsByArticleAndUrlAsync(Guid articleId, string originalUrl, CancellationToken cancellationToken = default);
	Task<List<MediaFile>> GetByIdsAsync(List<Guid> ids, CancellationToken cancellationToken = default);
	Task<List<MediaFile>> GetByPublicationIdAsync(Guid publicationId, CancellationToken cancellationToken = default);
	Task<MediaFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
