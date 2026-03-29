using Core.DomainModels;
using Core.DomainModels.AI;
using Infrastructure.Persistence.Entity;

namespace Infrastructure.Persistence.Mappers;

public static class ArticleMapper
{
	public static Article ToDomain(this ArticleEntity entity) => new()
	{
		Id = entity.Id,
		RawArticle = entity.RawArticle?.ToDomain() ?? new RawArticle(),
		Title = entity.Title,
		Content = entity.Content,
		Tags = entity.Tags,
		Category = entity.Category,
		Sentiment = Enum.Parse<Sentiment>(entity.Sentiment),
		ProcessedAt = entity.ProcessedAt,
		Status = Enum.Parse<ArticleStatus>(entity.Status),
		ModelVersion = entity.ModelVersion,
		Language = entity.Language,
		Summary = entity.Summary,
		RejectedByEditorId = entity.RejectedByEditorId,
		RejectionReason = entity.RejectionReason,
		RetryCount = entity.RetryCount,
	};

	public static ArticleEntity ToEntity(this Article domain) => new()
	{
		Id = domain.Id,
		RawArticleId = domain.RawArticle.Id,
		Title = domain.Title,
		Content = domain.Content,
		Tags = domain.Tags,
		Category = domain.Category,
		Sentiment = domain.Sentiment.ToString(),
		ProcessedAt = domain.ProcessedAt,
		Status = domain.Status.ToString(),
		ModelVersion = domain.ModelVersion,
		Language = domain.Language,
		Summary = domain.Summary,
		RejectedByEditorId = domain.RejectedByEditorId,
		RejectionReason = domain.RejectionReason,
		RetryCount = domain.RetryCount,
	};

	public static Article FromAnalysisResult(
		RawArticle rawArticle,
		ArticleAnalysisResult analysis,
		string modelVersion) => new()
	{
		Id = Guid.NewGuid(),
		RawArticle = rawArticle,
		Title = rawArticle.Title,
		Category = analysis.Category,
		Tags = analysis.Tags,
		Sentiment = Enum.Parse<Sentiment>(analysis.Sentiment),
		Language = analysis.Language,
		Summary = analysis.Summary,
		ProcessedAt = DateTimeOffset.UtcNow,
		Status = ArticleStatus.AnalysisDone,
		ModelVersion = modelVersion
	};
}