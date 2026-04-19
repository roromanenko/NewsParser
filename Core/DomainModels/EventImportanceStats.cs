namespace Core.DomainModels;

public record EventImportanceStats(
    int ArticleCount,
    int DistinctSourceCount,
    int ArticlesLastHour,
    DateTimeOffset? LastArticleAt);
