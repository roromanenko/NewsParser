using CodeHollow.FeedReader;
using Core.DomainModels;
using Core.Interfaces.Parsers;
using FluentAssertions;
using Infrastructure.Configuration;
using Infrastructure.Parsers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using System.Reflection;

namespace Infrastructure.Tests.Parsers;

/// <summary>
/// Tests for <see cref="RssParser"/>'s media-reference extraction logic and scrape
/// orchestration.
///
/// <para>
/// <c>RssParser.ParseAsync</c> calls <c>FeedReader.ReadAsync</c>, which performs a live
/// HTTP request and cannot run in isolation. The business-critical
/// <c>ExtractMediaReferences</c> method is <c>private static</c> and is tested here via
/// reflection, using <c>FeedReader.ReadFromString</c> to build real <see cref="FeedItem"/>
/// instances from inline RSS XML — no network access required.
/// </para>
///
/// <para>
/// FeedReader parses any item containing <c>media:</c>-namespaced elements as a
/// <c>MediaRssFeedItem</c>. For that type, the production code iterates
/// <c>MediaRssFeedItem.Media</c> (populated from <c>media:content</c> tags only).
/// The XML fallback path (<c>ExtractXmlMediaElements</c>) runs only when the item is
/// NOT a <c>MediaRssFeedItem</c>, which happens for plain RSS 2.0 items without
/// the media namespace. Tests reflect this actual parsing behavior.
/// </para>
///
/// <para>
/// The scrape-orchestration tests cover the per-article scrape + merge pipeline
/// exposed by the private <c>ScrapeAndMergeAllAsync</c> method, invoked via reflection
/// against pre-built <see cref="Article"/> lists. This avoids the live
/// <c>FeedReader.ReadAsync</c> network call without modifying the production API.
/// </para>
/// </summary>
[TestFixture]
public class RssParserTests
{
	private static readonly MethodInfo ExtractMediaReferencesMethod =
		typeof(RssParser)
			.GetMethod("ExtractMediaReferences", BindingFlags.NonPublic | BindingFlags.Static)
		?? throw new MissingMethodException(nameof(RssParser), "ExtractMediaReferences");

	private static readonly MethodInfo ScrapeAndMergeAllMethod =
		typeof(RssParser)
			.GetMethod("ScrapeAndMergeAllAsync", BindingFlags.NonPublic | BindingFlags.Instance)
		?? throw new MissingMethodException(nameof(RssParser), "ScrapeAndMergeAllAsync");

	// ------------------------------------------------------------------
	// P0 — RSS 2.0 <enclosure> → one Image MediaReference
	// ------------------------------------------------------------------

	[Test]
	public void ExtractMediaReferences_WhenRss20ItemHasImageEnclosure_ReturnsOneImageReference()
	{
		// Arrange
		var feed = FeedReader.ReadFromString(BuildRss20Feed("""
            <enclosure url="https://cdn.example.com/photo.jpg" type="image/jpeg" length="50000" />
            """));
		var item = feed.Items.First();

		// Act
		var refs = InvokeExtract(item);

		// Assert
		refs.Should().HaveCount(1);
		refs[0].Url.Should().Be("https://cdn.example.com/photo.jpg");
		refs[0].Kind.Should().Be(MediaKind.Image);
		refs[0].DeclaredContentType.Should().Be("image/jpeg");
	}

	// ------------------------------------------------------------------
	// P0 — media:content element → correct Url and Kind (Video)
	// ------------------------------------------------------------------

	[Test]
	public void ExtractMediaReferences_WhenItemHasMediaContentElement_ReturnsCorrectUrlAndKind()
	{
		// Arrange — FeedReader parses media:-namespaced items as MediaRssFeedItem;
		// media:content elements are populated into MediaRssFeedItem.Media
		var feed = FeedReader.ReadFromString(BuildMediaRssFeed("""
            <media:content url="https://cdn.example.com/clip.mp4" type="video/mp4" />
            """));
		var item = feed.Items.First();

		// Act
		var refs = InvokeExtract(item);

		// Assert
		refs.Should().HaveCount(1);
		refs[0].Url.Should().Be("https://cdn.example.com/clip.mp4");
		refs[0].Kind.Should().Be(MediaKind.Video);
	}

	// ------------------------------------------------------------------
	// P0 — media:content element with medium="image" → Image kind
	// ------------------------------------------------------------------

	[Test]
	public void ExtractMediaReferences_WhenMediaContentHasMediumImage_ReturnsImageReference()
	{
		// Arrange
		var feed = FeedReader.ReadFromString(BuildMediaRssFeed("""
            <media:content url="https://cdn.example.com/photo.png" medium="image" />
            """));
		var item = feed.Items.First();

		// Act
		var refs = InvokeExtract(item);

		// Assert
		refs.Should().HaveCount(1);
		refs[0].Url.Should().Be("https://cdn.example.com/photo.png");
		refs[0].Kind.Should().Be(MediaKind.Image);
	}

	// ------------------------------------------------------------------
	// P1 — No media elements → empty list
	// ------------------------------------------------------------------

	[Test]
	public void ExtractMediaReferences_WhenItemHasNoMediaElements_ReturnsEmptyList()
	{
		// Arrange
		var feed = FeedReader.ReadFromString(BuildRss20Feed(string.Empty));
		var item = feed.Items.First();

		// Act
		var refs = InvokeExtract(item);

		// Assert
		refs.Should().BeEmpty();
	}

	// ------------------------------------------------------------------
	// P1 — Unsupported MIME type in enclosure (application/pdf) → excluded
	// ------------------------------------------------------------------

	[Test]
	public void ExtractMediaReferences_WhenEnclosureMimeTypeIsUnsupported_ExcludesFromResults()
	{
		// Arrange
		var feed = FeedReader.ReadFromString(BuildRss20Feed("""
            <enclosure url="https://cdn.example.com/report.pdf" type="application/pdf" length="99000" />
            """));
		var item = feed.Items.First();

		// Act
		var refs = InvokeExtract(item);

		// Assert — PDF is neither image/* nor video/*
		refs.Should().BeEmpty();
	}

	// ------------------------------------------------------------------
	// P0 — media:thumbnail alone in MediaRssFeedItem → one Image reference
	//       (XML fallback is now called inside ExtractFromMediaRssFeedItem)
	// ------------------------------------------------------------------

	[Test]
	public void ExtractMediaReferences_WhenMediaRssFeedItemHasThumbnailOnly_ReturnsImageReference()
	{
		// Arrange
		var feed = FeedReader.ReadFromString(BuildMediaRssFeed("""
            <media:thumbnail url="https://cdn.example.com/thumb.jpg" />
            """));
		var item = feed.Items.First();

		// Act
		var refs = InvokeExtract(item);

		// Assert
		refs.Should().HaveCount(1);
		refs[0].Url.Should().Be("https://cdn.example.com/thumb.jpg");
		refs[0].Kind.Should().Be(MediaKind.Image);
	}

	// ------------------------------------------------------------------
	// P0 — media:content + media:thumbnail at the same URL → deduped to one
	// ------------------------------------------------------------------

	[Test]
	public void ExtractMediaReferences_WhenMediaRssFeedItemHasContentAndThumbnail_DedupesByUrl()
	{
		// Arrange
		var feed = FeedReader.ReadFromString(BuildMediaRssFeed("""
            <media:content url="https://cdn.example.com/img.jpg" type="image/jpeg" />
            <media:thumbnail url="https://cdn.example.com/img.jpg" />
            """));
		var item = feed.Items.First();

		// Act
		var refs = InvokeExtract(item);

		// Assert — DistinctBy keeps the first entry (media:content wins)
		refs.Should().HaveCount(1);
		refs[0].Url.Should().Be("https://cdn.example.com/img.jpg");
		refs[0].DeclaredContentType.Should().Be("image/jpeg");
	}

	// ------------------------------------------------------------------
	// P2 — Two media:content elements in the same item → both returned
	// ------------------------------------------------------------------

	[Test]
	public void ExtractMediaReferences_WhenItemHasTwoMediaContentElements_ReturnsBothReferences()
	{
		// Arrange
		var feed = FeedReader.ReadFromString(BuildMediaRssFeed("""
            <media:content url="https://cdn.example.com/video.mp4" type="video/mp4" />
            <media:content url="https://cdn.example.com/photo.jpg" type="image/jpeg" />
            """));
		var item = feed.Items.First();

		// Act
		var refs = InvokeExtract(item);

		// Assert
		refs.Should().HaveCount(2);
		refs.Should().ContainSingle(r => r.Url == "https://cdn.example.com/video.mp4" && r.Kind == MediaKind.Video);
		refs.Should().ContainSingle(r => r.Url == "https://cdn.example.com/photo.jpg" && r.Kind == MediaKind.Image);
	}

	// ==================================================================
	// Scrape-orchestration tests (private ScrapeAndMergeAllAsync path)
	// ==================================================================

	// ------------------------------------------------------------------
	// P0 — Scraped FullContent longer than RSS content → replaces
	// ------------------------------------------------------------------

	[Test]
	public async Task ParseAsync_WhenScraperReturnsLongerContent_ReplacesRssContent()
	{
		// Arrange
		var scraperMock = new Mock<IArticleContentScraper>();
		const string rssContent = "short rss";
		const string scrapedContent = "this is a significantly longer scraped content body";

		scraperMock
			.Setup(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ScrapedArticle(
				FullContent: scrapedContent,
				Tags: [],
				DiscoveredMedia: []));

		var sut = CreateSut(scraperMock.Object, enabled: true);
		var article = CreateArticle(originalContent: rssContent);

		// Act
		await InvokeScrapeAndMergeAll(sut, [article]);

		// Assert
		article.OriginalContent.Should().Be(scrapedContent);
	}

	// ------------------------------------------------------------------
	// P0 — Scraped FullContent shorter than RSS content → keeps RSS
	// ------------------------------------------------------------------

	[Test]
	public async Task ParseAsync_WhenScraperReturnsShortContent_KeepsRssContent()
	{
		// Arrange
		var scraperMock = new Mock<IArticleContentScraper>();
		const string rssContent = "long rss content from the original feed description";
		const string scrapedContent = "short";

		scraperMock
			.Setup(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ScrapedArticle(
				FullContent: scrapedContent,
				Tags: [],
				DiscoveredMedia: []));

		var sut = CreateSut(scraperMock.Object, enabled: true);
		var article = CreateArticle(originalContent: rssContent);

		// Act
		await InvokeScrapeAndMergeAll(sut, [article]);

		// Assert
		article.OriginalContent.Should().Be(rssContent);
	}

	// ------------------------------------------------------------------
	// P1 — Scraper throws HttpRequestException → article kept with RSS content,
	//      no exception propagates to the caller
	// ------------------------------------------------------------------

	[Test]
	public async Task ParseAsync_WhenScraperThrows_ReturnsArticleWithRssContent()
	{
		// Arrange
		var scraperMock = new Mock<IArticleContentScraper>();
		const string rssContent = "rss content preserved after scrape failure";

		scraperMock
			.Setup(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new HttpRequestException("boom"));

		var sut = CreateSut(scraperMock.Object, enabled: true);
		var article = CreateArticle(originalContent: rssContent);
		var articles = new List<Article> { article };

		// Act
		var act = async () => await InvokeScrapeAndMergeAll(sut, articles);

		// Assert
		await act.Should().NotThrowAsync();
		articles.Should().ContainSingle();
		articles[0].OriginalContent.Should().Be(rssContent);
	}

	// ------------------------------------------------------------------
	// P0 — Scraper returns og:image → appended to existing MediaReferences
	// ------------------------------------------------------------------

	[Test]
	public async Task ParseAsync_WhenScraperReturnsOgImage_AppendsToMediaReferences()
	{
		// Arrange
		var scraperMock = new Mock<IArticleContentScraper>();
		const string rssImageUrl = "https://cdn.example.com/rss.jpg";
		const string ogImageUrl = "https://cdn.example.com/og.jpg";

		var scrapedMedia = new MediaReference(ogImageUrl, MediaKind.Image, null, MediaSourceKind.Http);

		scraperMock
			.Setup(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ScrapedArticle(
				FullContent: null,
				Tags: [],
				DiscoveredMedia: [scrapedMedia]));

		var sut = CreateSut(scraperMock.Object, enabled: true);
		var article = CreateArticle(originalContent: "rss");
		article.MediaReferences.Add(new MediaReference(rssImageUrl, MediaKind.Image, "image/jpeg"));

		// Act
		await InvokeScrapeAndMergeAll(sut, [article]);

		// Assert
		article.MediaReferences.Should().HaveCount(2);
		article.MediaReferences.Should().ContainSingle(m => m.Url == rssImageUrl);
		article.MediaReferences.Should().ContainSingle(m => m.Url == ogImageUrl);
	}

	// ------------------------------------------------------------------
	// P1 — Options.Enabled=false → scraper is never invoked
	//
	// Note: the Enabled=false short-circuit lives inside the public ParseAsync,
	// which cannot be driven without triggering FeedReader.ReadAsync (live HTTP).
	// Restructuring production code to inject a pre-built feed is out of scope
	// per the task rules ("do NOT change production code signatures to enable
	// tests"). The kill-switch is also double-guarded inside
	// HtmlArticleContentScraper.ScrapeAsync, which is covered by
	// HtmlArticleContentScraperTests.ScrapeAsync_WhenEnabledFalse_ReturnsNull.
	// ------------------------------------------------------------------

	[Test]
	[Ignore("ParseAsync's Enabled=false branch lives behind FeedReader.ReadAsync " +
			"(live HTTP) and cannot be reached without restructuring the production " +
			"API to accept an injectable feed source. The kill-switch behaviour is " +
			"covered at the scraper level by " +
			"HtmlArticleContentScraperTests.ScrapeAsync_WhenEnabledFalse_ReturnsNull.")]
	public Task ParseAsync_WhenOptionsEnabledFalse_DoesNotCallScraper()
	{
		return Task.CompletedTask;
	}

	// ------------------------------------------------------------------
	// Helpers
	// ------------------------------------------------------------------

	private static List<MediaReference> InvokeExtract(FeedItem item)
	{
		var result = ExtractMediaReferencesMethod.Invoke(null, [item]);
		return (List<MediaReference>)result!;
	}

	private static Task InvokeScrapeAndMergeAll(RssParser sut, List<Article> articles)
	{
		var result = ScrapeAndMergeAllMethod.Invoke(sut, [articles, CancellationToken.None]);
		return (Task)result!;
	}

	private static RssParser CreateSut(IArticleContentScraper scraper, bool enabled) =>
		new(
			scraper,
			Options.Create(new ArticleScraperOptions
			{
				Enabled = enabled,
				MaxConcurrencyPerFeed = 4,
				MaxTagsPerArticle = 20,
			}),
			NullLogger<RssParser>.Instance);

	private static Article CreateArticle(string originalContent) => new()
	{
		Id = Guid.NewGuid(),
		SourceId = Guid.NewGuid(),
		Title = "Test",
		OriginalContent = originalContent,
		OriginalUrl = "https://example.com/article",
		ExternalId = "ext-1",
		PublishedAt = DateTimeOffset.UtcNow,
		Language = string.Empty,
		Status = ArticleStatus.Pending,
		ProcessedAt = DateTimeOffset.UtcNow,
		MediaReferences = [],
	};

	private static string BuildRss20Feed(string itemExtra) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>Test Feed</title>
            <link>https://example.com</link>
            <description>Test</description>
            <item>
              <title>Test Article</title>
              <link>https://example.com/article1</link>
              <guid>article-1</guid>
              {itemExtra}
            </item>
          </channel>
        </rss>
        """;

	private static string BuildMediaRssFeed(string itemExtra) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:media="http://search.yahoo.com/mrss/">
          <channel>
            <title>Test Feed</title>
            <link>https://example.com</link>
            <description>Test</description>
            <item>
              <title>Media Article</title>
              <link>https://example.com/article2</link>
              <guid>article-2</guid>
              {itemExtra}
            </item>
          </channel>
        </rss>
        """;
}
