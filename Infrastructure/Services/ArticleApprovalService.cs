using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;

namespace Infrastructure.Services;

public class ArticleApprovalService(
	IArticleRepository articleRepository,
	IPublicationRepository publicationRepository,
	IPublishTargetRepository publishTargetRepository) : IArticleApprovalService
{
	public async Task<Article> ApproveAsync(
		Guid articleId,
		Guid editorId,
		List<Guid> publishTargetIds,
		CancellationToken cancellationToken = default)
	{
		var article = await articleRepository.GetByIdAsync(articleId, cancellationToken)
			?? throw new KeyNotFoundException($"Article {articleId} not found");

		if (article.Status != ArticleStatus.Pending)
			throw new InvalidOperationException(
				$"Article {articleId} cannot be approved: status is {article.Status}");

		var publishTargets = new List<PublishTarget>();
		foreach (var targetId in publishTargetIds)
		{
			var target = await publishTargetRepository.GetByIdAsync(targetId, cancellationToken)
				?? throw new KeyNotFoundException($"PublishTarget {targetId} not found");

			if (!target.IsActive)
				throw new InvalidOperationException(
					$"PublishTarget {targetId} is not active");

			publishTargets.Add(target);
		}

		var publications = publishTargets.Select(target => new Publication
		{
			Id = Guid.NewGuid(),
			Article = article,
			PublishTargetId = target.Id,
			PublishTarget = target,
			Status = PublicationStatus.Pending,
			CreatedAt = DateTimeOffset.UtcNow,
			ApprovedAt = DateTimeOffset.UtcNow,
		}).ToList();

		await publicationRepository.AddRangeAsync(articleId, editorId, publications, cancellationToken);
		await articleRepository.UpdateStatusAsync(articleId, ArticleStatus.Approved, cancellationToken);

		article.Status = ArticleStatus.Approved;
		article.Publications = publications;
		return article;
	}

	public async Task<Article> RejectAsync(
		Guid articleId,
		Guid editorId,
		string reason,
		CancellationToken cancellationToken = default)
	{
		var article = await articleRepository.GetByIdAsync(articleId, cancellationToken)
			?? throw new KeyNotFoundException($"Article {articleId} not found");

		if (article.Status != ArticleStatus.Pending)
			throw new InvalidOperationException(
				$"Article {articleId} cannot be rejected: status is {article.Status}");

		await articleRepository.UpdateRejectionAsync(articleId, editorId, reason, cancellationToken);

		article.Status = ArticleStatus.Rejected;
		article.RejectedByEditorId = editorId;
		article.RejectionReason = reason;
		return article;
	}
}