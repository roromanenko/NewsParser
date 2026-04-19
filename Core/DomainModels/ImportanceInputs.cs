namespace Core.DomainModels;

public record ImportanceInputs(
    int ArticleCount,
    int DistinctSourceCount,
    int ArticlesLastHour,
    string AiLabel,
    DateTimeOffset LastArticleAt,
    DateTimeOffset Now);
