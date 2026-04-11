using CodeHollow.FeedReader;
using Core.DomainModels;
using FluentAssertions;
using Infrastructure.Parsers;
using NUnit.Framework;
using System.Reflection;

namespace Infrastructure.Tests.Parsers;

/// <summary>
/// Tests for <see cref="RssParser"/>'s media-reference extraction logic.
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
/// </summary>
[TestFixture]
public class RssParserTests
{
    private static readonly MethodInfo ExtractMediaReferencesMethod =
        typeof(RssParser)
            .GetMethod("ExtractMediaReferences", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(RssParser), "ExtractMediaReferences");

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
    // P1 — media:thumbnail alone in MediaRssFeedItem → empty
    //       (FeedReader does not populate Media from thumbnail-only items;
    //        the XML fallback path is not reached for MediaRssFeedItem)
    // ------------------------------------------------------------------

    [Test]
    public void ExtractMediaReferences_WhenMediaRssFeedItemHasThumbnailOnly_ReturnsEmpty()
    {
        // Arrange — thumbnail-only item is still parsed as MediaRssFeedItem by FeedReader,
        // but MediaRssFeedItem.Media is empty; the XML fallback (ExtractXmlMediaElements)
        // is only reached for non-MediaRssFeedItem types.
        var feed = FeedReader.ReadFromString(BuildMediaRssFeed("""
            <media:thumbnail url="https://cdn.example.com/thumb.jpg" />
            """));
        var item = feed.Items.First();

        // Act
        var refs = InvokeExtract(item);

        // Assert — documents known production behavior for standalone thumbnails
        refs.Should().BeEmpty();
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

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static List<MediaReference> InvokeExtract(FeedItem item)
    {
        var result = ExtractMediaReferencesMethod.Invoke(null, [item]);
        return (List<MediaReference>)result!;
    }

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
