using Core.DomainModels;

namespace Core.Interfaces.Services;

public interface IArticleApprovalService
{
	Task<Article> ApproveAsync(Guid articleId, Guid editorId, List<Guid> publishTargetIds, CancellationToken cancellationToken = default);
	Task<Article> RejectAsync(Guid articleId, Guid editorId, string reason, CancellationToken cancellationToken = default);
}