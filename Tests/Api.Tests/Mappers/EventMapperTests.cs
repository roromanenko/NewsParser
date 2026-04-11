using Api.Mappers;
using Api.Models;
using Core.DomainModels;
using FluentAssertions;
using NUnit.Framework;

namespace Api.Tests.Mappers;

[TestFixture]
public class EventMapperTests
{
    private const string PublicBaseUrl = "https://cdn.example.com";

    // ------------------------------------------------------------------
    // ToEventArticleDto — P0: Media is populated from article's MediaFiles
    // ------------------------------------------------------------------

    [Test]
    public void ToEventArticleDto_WhenArticleHasMediaFiles_ReturnsDtoWithPopulatedMedia()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = CreateArticle(
            id: articleId,
            mediaFiles:
            [
                CreateMediaFile(articleId, r2Key: "articles/img1.jpg"),
                CreateMediaFile(articleId, r2Key: "articles/img2.jpg"),
            ]);

        // Act
        var result = article.ToEventArticleDto(PublicBaseUrl);

        // Assert
        result.Media.Should().HaveCount(2);
        result.Media.Should().ContainSingle(m => m.Url == "https://cdn.example.com/articles/img1.jpg");
        result.Media.Should().ContainSingle(m => m.Url == "https://cdn.example.com/articles/img2.jpg");
    }

    // ------------------------------------------------------------------
    // ToEventArticleDto — P1: article with no media returns empty Media list
    // ------------------------------------------------------------------

    [Test]
    public void ToEventArticleDto_WhenArticleHasNoMediaFiles_ReturnsDtoWithEmptyMedia()
    {
        // Arrange
        var article = CreateArticle(mediaFiles: []);

        // Act
        var result = article.ToEventArticleDto(PublicBaseUrl);

        // Assert
        result.Media.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // ToDetailDto — P0: aggregates Media across all articles in the event
    // ------------------------------------------------------------------

    [Test]
    public void ToDetailDto_WhenEventHasTwoArticlesWithMedia_AggregatesMediaOnEachArticleDto()
    {
        // Arrange
        var articleId1 = Guid.NewGuid();
        var articleId2 = Guid.NewGuid();

        var article1 = CreateArticle(id: articleId1, mediaFiles:
        [
            CreateMediaFile(articleId1, r2Key: "a1/photo.jpg"),
        ]);
        var article2 = CreateArticle(id: articleId2, mediaFiles:
        [
            CreateMediaFile(articleId2, r2Key: "a2/video.mp4"),
        ]);

        var evt = CreateEvent(articles: [article1, article2]);

        // Act
        var result = evt.ToDetailDto(PublicBaseUrl);

        // Assert
        result.Articles.Should().HaveCount(2);

        var dto1 = result.Articles.Single(a => a.ArticleId == articleId1);
        dto1.Media.Should().HaveCount(1);
        dto1.Media[0].Url.Should().Be("https://cdn.example.com/a1/photo.jpg");

        var dto2 = result.Articles.Single(a => a.ArticleId == articleId2);
        dto2.Media.Should().HaveCount(1);
        dto2.Media[0].Url.Should().Be("https://cdn.example.com/a2/video.mp4");
    }

    // ------------------------------------------------------------------
    // ToListItemDto — P0: signature unchanged — no Media field on the DTO
    // ------------------------------------------------------------------

    [Test]
    public void ToListItemDto_WhenCalled_ReturnsDtoWithoutMediaField()
    {
        // Arrange
        var evt = CreateEvent(articles: []);

        // Act
        var result = evt.ToListItemDto();

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be(evt.Title);

        // EventListItemDto must not expose a Media property
        var mediaProperty = typeof(EventListItemDto).GetProperty("Media");
        mediaProperty.Should().BeNull("ToListItemDto must not include a Media field");
    }

    // ------------------------------------------------------------------
    // ToListItemDto — P0: signature takes no publicBaseUrl parameter
    // ------------------------------------------------------------------

    [Test]
    public void ToListItemDto_WhenCalledWithoutBaseUrl_CompilesAndReturnsCorrectCounts()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = CreateArticle(id: articleId, mediaFiles:
        [
            CreateMediaFile(articleId, r2Key: "img.jpg"),
        ]);
        var evt = CreateEvent(articles: [article]);

        // Act — no publicBaseUrl argument; signature must remain unchanged
        var result = evt.ToListItemDto();

        // Assert
        result.ArticleCount.Should().Be(1);
        result.UnresolvedContradictions.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Event CreateEvent(List<Article>? articles = null) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Event",
        Summary = "Summary of the event",
        Status = EventStatus.Active,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        Articles = articles ?? [],
        EventUpdates = [],
        Contradictions = [],
    };

    private static Article CreateArticle(
        Guid? id = null,
        List<MediaFile>? mediaFiles = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Title = "Test Article",
        Category = "Tech",
        Tags = [],
        Sentiment = Sentiment.Neutral,
        Language = "en",
        Summary = "Summary",
        KeyFacts = [],
        ProcessedAt = DateTimeOffset.UtcNow,
        ModelVersion = "claude-3-haiku",
        Status = ArticleStatus.AnalysisDone,
        MediaFiles = mediaFiles ?? [],
    };

    private static MediaFile CreateMediaFile(Guid articleId, string r2Key) => new()
    {
        Id = Guid.NewGuid(),
        ArticleId = articleId,
        R2Key = r2Key,
        OriginalUrl = "https://source.example.com/img.jpg",
        ContentType = "image/jpeg",
        SizeBytes = 50_000,
        Kind = MediaKind.Image,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
