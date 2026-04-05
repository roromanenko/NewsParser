using Core.DomainModels;
using Core.DomainModels.AI;
using Infrastructure.Persistence.Entity;
using Pgvector;

namespace Infrastructure.Persistence.Mappers;

public static class ArticleMapper
{
	public static Article ToDomain(this ArticleEntity entity) => new()
	{
		Id = entity.Id,
		OriginalContent = entity.OriginalContent,
		SourceId = entity.SourceId,
		OriginalUrl = entity.OriginalUrl,
		PublishedAt = entity.PublishedAt,
		ExternalId = entity.ExternalId,
		Embedding = entity.Embedding?.ToArray(),
		Title = entity.Title,
		Tags = entity.Tags,
		Category = entity.Category,
		Sentiment = Enum.Parse<Sentiment>(entity.Sentiment),
		ProcessedAt = entity.ProcessedAt,
		Status = Enum.Parse<ArticleStatus>(entity.Status),
		ModelVersion = entity.ModelVersion,
		Language = entity.Language,
		Summary = entity.Summary,
		KeyFacts = entity.KeyFacts,
		RejectionReason = entity.RejectionReason,
		RetryCount = entity.RetryCount,
		EventId = entity.EventId,
		Role = entity.Role != null ? Enum.Parse<ArticleRole>(entity.Role) : null,
		WasReclassified = entity.WasReclassified,
		AddedToEventAt = entity.AddedToEventAt,
	};

	public static ArticleEntity ToEntity(this Article domain) => new()
	{
		Id = domain.Id,
		OriginalContent = domain.OriginalContent,
		SourceId = domain.SourceId,
		OriginalUrl = domain.OriginalUrl,
		PublishedAt = domain.PublishedAt,
		ExternalId = domain.ExternalId,
		Embedding = domain.Embedding != null ? new Vector(domain.Embedding) : null,
		Title = domain.Title,
		Tags = domain.Tags,
		Category = domain.Category,
		Sentiment = domain.Sentiment.ToString(),
		ProcessedAt = domain.ProcessedAt,
		Status = domain.Status.ToString(),
		ModelVersion = domain.ModelVersion,
		Language = domain.Language,
		Summary = domain.Summary,
		KeyFacts = domain.KeyFacts,
		RejectionReason = domain.RejectionReason,
		RetryCount = domain.RetryCount,
		EventId = domain.EventId,
		Role = domain.Role?.ToString(),
		WasReclassified = domain.WasReclassified,
		AddedToEventAt = domain.AddedToEventAt,
	};

	public static Article FromAnalysisResult(
		Article pendingArticle,
		ArticleAnalysisResult analysis,
		string modelVersion) => new()
	{
		Id = Guid.NewGuid(),
		Title = pendingArticle.Title,
		OriginalContent = pendingArticle.OriginalContent,
		OriginalUrl = pendingArticle.OriginalUrl,
		PublishedAt = pendingArticle.PublishedAt,
		SourceId = pendingArticle.SourceId,
		ExternalId = pendingArticle.ExternalId,
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
