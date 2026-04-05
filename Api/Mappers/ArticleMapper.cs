using Api.Models;
using Core.DomainModels;

namespace Api.Mappers;

public static class ArticleMapper
{
    public static ArticleListItemDto ToListItemDto(this Article article) => new(
        article.Id, article.Title, article.Category, article.Tags,
        article.Sentiment.ToString(), article.Language, article.Summary, article.ProcessedAt
    );

    public static ArticleDetailDto ToDetailDto(this Article article, Event? evt = null)
    {
        ArticleEventDto? eventDto = null;
        if (evt is not null)
        {
            eventDto = new ArticleEventDto(
                evt.Id,
                evt.Title,
                evt.Status.ToString(),
                article.Role?.ToString() ?? string.Empty
            );
        }

        return new ArticleDetailDto(
            article.Id, article.Title, article.Category,
            article.Tags, article.Sentiment.ToString(), article.Language,
            article.Summary, article.KeyFacts ?? [],
            article.ProcessedAt, article.ModelVersion,
            article.OriginalUrl, article.PublishedAt,
            eventDto
        );
    }
}
