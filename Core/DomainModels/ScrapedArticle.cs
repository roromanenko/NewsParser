namespace Core.DomainModels;

public sealed record ScrapedArticle(
    string? FullContent,
    IReadOnlyList<string> Tags,
    IReadOnlyList<MediaReference> DiscoveredMedia);
