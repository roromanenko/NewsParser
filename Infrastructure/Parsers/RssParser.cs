using CodeHollow.FeedReader;
using CodeHollow.FeedReader.Feeds;
using CodeHollow.FeedReader.Feeds.MediaRSS;
using Core.DomainModels;
using Core.Interfaces.Parsers;
using Infrastructure.Configuration;
using Infrastructure.Persistence.Mappers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Xml.Linq;

namespace Infrastructure.Parsers;

public class RssParser(
    IArticleContentScraper scraper,
    IOptions<ArticleScraperOptions> options,
    ILogger<RssParser> logger) : ISourceParser
{
    private readonly ArticleScraperOptions _options = options.Value;

    public SourceType SourceType => SourceType.Rss;

    private static readonly XNamespace MediaNs = "http://search.yahoo.com/mrss/";

    public async Task<List<Article>> ParseAsync(Source source, CancellationToken cancellationToken = default)
    {
        var feed = await FeedReader.ReadAsync(source.Url, cancellationToken);

        var articles = feed.Items.Select(item => new Article
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            Title = item.Title ?? string.Empty,
            OriginalContent = item.Content ?? item.Description ?? string.Empty,
            OriginalUrl = item.Link ?? string.Empty,
            ExternalId = item.Id ?? item.Link,
            PublishedAt = item.PublishingDate ?? DateTimeOffset.UtcNow,
            Language = string.Empty,
            Status = ArticleStatus.Pending,
            ProcessedAt = DateTimeOffset.UtcNow,
            MediaReferences = ExtractMediaReferences(item),
        }).ToList();

        if (_options.Enabled)
            await ScrapeAndMergeAllAsync(articles, cancellationToken);

        return articles;
    }

    private async Task ScrapeAndMergeAllAsync(List<Article> articles, CancellationToken cancellationToken)
    {
        var sem = new SemaphoreSlim(_options.MaxConcurrencyPerFeed);
        var tasks = articles.Select(article => ScrapeAndMergeOneAsync(article, sem, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task ScrapeAndMergeOneAsync(Article article, SemaphoreSlim sem, CancellationToken cancellationToken)
    {
        await sem.WaitAsync(cancellationToken);
        try
        {
            var scraped = await scraper.ScrapeAsync(article.OriginalUrl!, cancellationToken);
            article.MergeScraped(scraped, _options.MaxTagsPerArticle);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Scrape failed for {Url}; keeping RSS data.", article.OriginalUrl);
        }
        finally
        {
            sem.Release();
        }
    }

    private static List<MediaReference> ExtractMediaReferences(FeedItem item)
    {
        if (item.SpecificItem is MediaRssFeedItem mediaItem)
            return ExtractFromMediaRssFeedItem(mediaItem);

        var refs = new List<MediaReference>();

        if (item.SpecificItem is Rss20FeedItem rssItem)
            AddEnclosureReference(rssItem.Enclosure, refs);

        AddXmlMediaElements(item.SpecificItem?.Element, refs);

        return refs;
    }

    private static List<MediaReference> ExtractFromMediaRssFeedItem(MediaRssFeedItem mediaItem)
    {
        var refs = new List<MediaReference>();

        foreach (var media in mediaItem.Media)
        {
            if (string.IsNullOrEmpty(media.Url)) continue;

            var kind = ResolveKindFromTypeAndMedium(media.Type, media.Medium);
            if (kind is null) continue;

            refs.Add(new MediaReference(media.Url, kind.Value, media.Type));
        }

        AddXmlMediaElements(mediaItem.Element, refs);

        return refs.DistinctBy(r => r.Url).ToList();
    }

    private static void AddEnclosureReference(FeedItemEnclosure? enclosure, List<MediaReference> refs)
    {
        if (enclosure is null || string.IsNullOrEmpty(enclosure.Url)) return;
        if (string.IsNullOrEmpty(enclosure.MediaType)) return;

        if (!enclosure.MediaType.StartsWith("image/") && !enclosure.MediaType.StartsWith("video/"))
            return;

        var kind = enclosure.MediaType.StartsWith("video/") ? MediaKind.Video : MediaKind.Image;
        refs.Add(new MediaReference(enclosure.Url, kind, enclosure.MediaType));
    }

    private static void AddXmlMediaElements(XElement? element, List<MediaReference> refs)
    {
        if (element is null) return;

        foreach (var el in element.Descendants(MediaNs + "content"))
        {
            var url = (string?)el.Attribute("url");
            if (string.IsNullOrEmpty(url)) continue;

            var type = (string?)el.Attribute("type");
            var medium = (string?)el.Attribute("medium");
            var kind = ResolveKindFromStrings(type, medium);
            if (kind is null) continue;

            refs.Add(new MediaReference(url, kind.Value, type));
        }

        foreach (var el in element.Descendants(MediaNs + "thumbnail"))
        {
            var url = (string?)el.Attribute("url");
            if (string.IsNullOrEmpty(url)) continue;

            refs.Add(new MediaReference(url, MediaKind.Image, null));
        }
    }

    private static MediaKind? ResolveKindFromTypeAndMedium(string? type, Medium medium)
    {
        if (!string.IsNullOrEmpty(type))
        {
            if (type.StartsWith("video/")) return MediaKind.Video;
            if (type.StartsWith("image/")) return MediaKind.Image;
        }

        return medium switch
        {
            Medium.Video => MediaKind.Video,
            Medium.Image => MediaKind.Image,
            _ => null
        };
    }

    private static MediaKind? ResolveKindFromStrings(string? type, string? medium)
    {
        if (!string.IsNullOrEmpty(type))
        {
            if (type.StartsWith("video/")) return MediaKind.Video;
            if (type.StartsWith("image/")) return MediaKind.Image;
        }

        return medium switch
        {
            "video" => MediaKind.Video,
            "image" => MediaKind.Image,
            _ => null
        };
    }
}
