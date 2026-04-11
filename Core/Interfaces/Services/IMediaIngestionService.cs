using Core.DomainModels;

namespace Core.Interfaces.Services;

public interface IMediaIngestionService
{
	Task IngestForArticleAsync(
		Guid articleId,
		IReadOnlyList<MediaReference> references,
		CancellationToken cancellationToken = default);
}
