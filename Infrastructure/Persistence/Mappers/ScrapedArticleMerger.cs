using Core.DomainModels;

namespace Infrastructure.Persistence.Mappers;

public static class ScrapedArticleMerger
{
    public static void MergeScraped(this Article article, ScrapedArticle? scraped, int maxTags)
    {
        if (scraped is null) return;

        MergeContent(article, scraped);
        MergeTags(article, scraped, maxTags);
        MergeMedia(article, scraped);
    }

    private static void MergeContent(Article article, ScrapedArticle scraped)
    {
        var scrapedIsLonger = !string.IsNullOrEmpty(scraped.FullContent)
            && scraped.FullContent.Length > (article.OriginalContent?.Length ?? 0);

        if (scrapedIsLonger)
            article.OriginalContent = scraped.FullContent;
    }

    private static void MergeTags(Article article, ScrapedArticle scraped, int maxTags)
    {
        article.Tags = article.Tags
            .Concat(scraped.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxTags)
            .ToList();
    }

    private static void MergeMedia(Article article, ScrapedArticle scraped)
    {
        article.MediaReferences.AddRange(scraped.DiscoveredMedia);
    }
}
