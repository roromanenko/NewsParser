using Core.DomainModels;

namespace Core.Interfaces.Parsers;

public interface IArticleContentScraper
{
    Task<ScrapedArticle?> ScrapeAsync(string url, CancellationToken cancellationToken = default);
}
