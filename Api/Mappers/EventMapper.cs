using Api.Models;
using Core.DomainModels;

namespace Api.Mappers;

public static class EventMapper
{
    public static EventListItemDto ToListItemDto(this Event evt) => new(
        evt.Id,
        evt.Title,
        evt.Summary,
        evt.Status.ToString(),
        evt.FirstSeenAt,
        evt.LastUpdatedAt,
        evt.Articles.Count,
        evt.Contradictions.Count(c => !c.IsResolved),
        evt.ImportanceTier?.ToString(),
        evt.ImportanceBaseScore,
        evt.DistinctSourceCount
    );

    public static EventDetailDto ToDetailDto(this Event evt, string publicBaseUrl) => new(
        evt.Id,
        evt.Title,
        evt.Summary,
        evt.Status.ToString(),
        evt.FirstSeenAt,
        evt.LastUpdatedAt,
        evt.Articles.Select(a => a.ToEventArticleDto(publicBaseUrl)).ToList(),
        evt.EventUpdates.Select(eu => eu.ToDto()).OrderBy(u => u.CreatedAt).ToList(),
        evt.Contradictions.Select(c => c.ToDto()).ToList(),
        evt.Articles.Count(a => a.WasReclassified),
        evt.ImportanceTier?.ToString(),
        evt.ImportanceBaseScore,
        evt.DistinctSourceCount
    );

    public static EventArticleDto ToEventArticleDto(this Article article, string publicBaseUrl) => new(
        article.Id,
        article.Title,
        article.Summary,
        article.KeyFacts ?? [],
        article.Role?.ToString() ?? string.Empty,
        article.AddedToEventAt ?? article.ProcessedAt,
        article.MediaFiles.Select(m => m.ToDto(publicBaseUrl)).ToList()
    );

    public static EventUpdateDto ToDto(this EventUpdate eu) => new(
        eu.Id,
        eu.FactSummary,
        eu.IsPublished,
        eu.CreatedAt
    );

    public static ContradictionDto ToDto(this Contradiction c) => new(
        c.Id,
        c.Description,
        c.IsResolved,
        c.CreatedAt,
        c.ContradictionArticles.Select(ca => ca.ArticleId).ToList()
    );
}
