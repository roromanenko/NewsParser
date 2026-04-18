using Core.DomainModels;
using Core.Interfaces.Parsers;
using HtmlAgilityPack;
using Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Infrastructure.Parsers;

public class HtmlArticleContentScraper(
    IHttpClientFactory httpClientFactory,
    IOptions<ArticleScraperOptions> options) : IArticleContentScraper
{
    private readonly ArticleScraperOptions _options = options.Value;

    private static readonly Dictionary<string, string> ExtensionToMime =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" },
            { ".png", "image/png" }, { ".gif", "image/gif" },
            { ".webp", "image/webp" },
        };

    public async Task<ScrapedArticle?> ScrapeAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return null;

        var client = httpClientFactory.CreateClient("ArticleContentScraper");
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode) return null;

        var html = await ReadBodyUpToLimitAsync(response, cancellationToken);
        if (string.IsNullOrEmpty(html)) return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var articleUri = response.RequestMessage?.RequestUri ?? new Uri(url);

        return new ScrapedArticle(
            FullContent: ExtractContent(doc),
            Tags: ExtractTags(doc, _options.MaxTagsPerArticle),
            DiscoveredMedia: ExtractOpenGraphMedia(doc, articleUri));
    }

    private async Task<string?> ReadBodyUpToLimitAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            ms.Write(buffer, 0, bytesRead);
            if (ms.Length > _options.MaxHtmlSizeBytes) return null;
        }
        return ms.Length == 0 ? null : System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string? ExtractContent(HtmlDocument doc)
    {
        var article = doc.DocumentNode.SelectSingleNode("//article");
        if (article is not null)
            return JoinParagraphs(article);

        var bestDiv = FindLargestDivWithParagraphs(doc);
        return bestDiv is not null ? JoinParagraphs(bestDiv) : null;
    }

    private static HtmlNode? FindLargestDivWithParagraphs(HtmlDocument doc)
    {
        return doc.DocumentNode
            .SelectNodes("//div")
            ?.Where(div => div.SelectNodes(".//p") is { Count: > 0 })
            .OrderByDescending(div => div.SelectNodes(".//p")?.Count ?? 0)
            .FirstOrDefault();
    }

    private static string JoinParagraphs(HtmlNode container)
    {
        var paragraphs = container.SelectNodes(".//p");
        if (paragraphs is null) return string.Empty;
        return string.Join(" ", paragraphs.Select(p => p.InnerText.Trim()));
    }

    private static IReadOnlyList<string> ExtractTags(HtmlDocument doc, int maxTags)
    {
        var tags = new List<string>();

        var articleTagNodes = doc.DocumentNode.SelectNodes("//meta[@property='article:tag']");
        if (articleTagNodes is not null)
            tags.AddRange(articleTagNodes.Select(n => n.GetAttributeValue("content", string.Empty)));

        var keywords = doc.DocumentNode
            .SelectSingleNode("//meta[@name='keywords']")?
            .GetAttributeValue("content", null);
        if (!string.IsNullOrWhiteSpace(keywords))
            tags.AddRange(keywords.Split(',').Select(k => k.Trim()));

        tags.AddRange(ExtractTagsFromJsonLd(doc));

        return tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxTags)
            .ToList();
    }

    private static IEnumerable<string> ExtractTagsFromJsonLd(HtmlDocument doc)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts is null) return [];

        var result = new List<string>();
        foreach (var script in scripts)
            result.AddRange(ParseKeywordsFromScript(script.InnerText));

        return result;
    }

    private static IEnumerable<string> ParseKeywordsFromScript(string json)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(json);
            return ExtractKeywords(jsonDoc.RootElement).ToList();
        }
        catch (JsonException) { return []; }
    }

    private static IEnumerable<string> ExtractKeywords(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().SelectMany(ExtractKeywords);

        if (element.ValueKind != JsonValueKind.Object)
            return [];

        if (element.TryGetProperty("@graph", out var graph))
            return ExtractKeywords(graph);

        if (!element.TryGetProperty("keywords", out var keywords))
            return [];

        return keywords.ValueKind switch
        {
            JsonValueKind.String => (keywords.GetString() ?? string.Empty)
                .Split(',')
                .Select(t => t.Trim()),
            JsonValueKind.Array => keywords.EnumerateArray()
                .Where(i => i.ValueKind == JsonValueKind.String)
                .Select(i => i.GetString()!),
            _ => []
        };
    }

    private static IReadOnlyList<MediaReference> ExtractOpenGraphMedia(HtmlDocument doc, Uri articleUri)
    {
        var properties = new[] { "og:image", "og:image:secure_url", "twitter:image", "twitter:image:src" };
        var media = new List<MediaReference>();

        foreach (var property in properties)
        {
            var node = doc.DocumentNode.SelectSingleNode(
                $"//meta[@property='{property}' or @name='{property}']");
            var rawValue = node?.GetAttributeValue("content", null);

            if (string.IsNullOrWhiteSpace(rawValue)) continue;
            if (rawValue.StartsWith("data:")) continue;
            if (!TryResolveUrl(rawValue, articleUri, out var absUri)) continue;

            var extension = Path.GetExtension(absUri.AbsolutePath);
            var declaredContentType = ExtensionToMime.GetValueOrDefault(extension);

            media.Add(new MediaReference(absUri.ToString(), MediaKind.Image, declaredContentType, MediaSourceKind.Http));
        }

        return media;
    }

    private static bool TryResolveUrl(string rawValue, Uri articleUri, out Uri resolved)
    {
        if (Uri.TryCreate(rawValue, UriKind.Absolute, out var abs)
            && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
        {
            resolved = abs;
            return true;
        }

        if (Uri.TryCreate(articleUri, rawValue, out var rel))
        {
            resolved = rel;
            return true;
        }

        resolved = null!;
        return false;
    }
}
