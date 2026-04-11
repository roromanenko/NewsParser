using Api.Mappers;
using Api.Models;
using Core.DomainModels;
using FluentAssertions;
using NUnit.Framework;

namespace Api.Tests.Mappers;

[TestFixture]
public class ArticleMapperTests
{
    private const string PublicBaseUrl = "https://cdn.example.com";

    // ------------------------------------------------------------------
    // ToDetailDto — P0: empty MediaFiles returns Media = []
    // ------------------------------------------------------------------

    [Test]
    public void ToDetailDto_WhenMediaFilesIsEmpty_ReturnsDtoWithEmptyMediaList()
    {
        // Arrange
        var article = CreateArticle(mediaFiles: []);

        // Act
        var result = article.ToDetailDto(PublicBaseUrl);

        // Assert
        result.Media.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // ToDetailDto — P0: two MediaFile objects produce correct URLs
    // ------------------------------------------------------------------

    [Test]
    public void ToDetailDto_WhenTwoMediaFilesExist_ReturnsDtoWithCorrectUrls()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var mediaFiles = new List<MediaFile>
        {
            CreateMediaFile(articleId, r2Key: "articles/photo1.jpg"),
            CreateMediaFile(articleId, r2Key: "articles/photo2.png"),
        };
        var article = CreateArticle(id: articleId, mediaFiles: mediaFiles);

        // Act
        var result = article.ToDetailDto(PublicBaseUrl);

        // Assert
        result.Media.Should().HaveCount(2);
        result.Media.Should().ContainSingle(m => m.Url == "https://cdn.example.com/articles/photo1.jpg");
        result.Media.Should().ContainSingle(m => m.Url == "https://cdn.example.com/articles/photo2.png");
    }

    // ------------------------------------------------------------------
    // ToDetailDto — P1: publicBaseUrl is threaded through to each MediaFileDto
    // ------------------------------------------------------------------

    [Test]
    public void ToDetailDto_WhenCustomBaseUrlProvided_UsesItForAllMediaItems()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var mediaFiles = new List<MediaFile>
        {
            CreateMediaFile(articleId, r2Key: "img/a.jpg"),
        };
        var article = CreateArticle(id: articleId, mediaFiles: mediaFiles);
        const string customBase = "https://assets.custom-cdn.net";

        // Act
        var result = article.ToDetailDto(customBase);

        // Assert
        result.Media[0].Url.Should().StartWith("https://assets.custom-cdn.net/");
    }

    // ------------------------------------------------------------------
    // ToListItemDto — P0: unaffected — no Media field on the DTO
    // ------------------------------------------------------------------

    [Test]
    public void ToListItemDto_WhenCalled_ReturnsDtoWithoutMediaField()
    {
        // Arrange
        var article = CreateArticle();
        article.Title = "Some Article";
        article.Category = "Politics";

        // Act
        var result = article.ToListItemDto();

        // Assert
        // ArticleListItemDto has no Media property — verified by type compile and field count
        result.Should().NotBeNull();
        result.Title.Should().Be("Some Article");
        result.Category.Should().Be("Politics");

        // Confirm the DTO record type does not expose a Media property
        var mediaProperty = typeof(ArticleListItemDto).GetProperty("Media");
        mediaProperty.Should().BeNull("ToListItemDto must not include a Media field");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Article CreateArticle(
        Guid? id = null,
        List<MediaFile>? mediaFiles = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Title = "Test Article",
        Category = "Tech",
        Tags = ["tag1"],
        Sentiment = Sentiment.Neutral,
        Language = "en",
        Summary = "Summary text",
        KeyFacts = ["Fact one."],
        ProcessedAt = DateTimeOffset.UtcNow,
        ModelVersion = "claude-3-haiku",
        OriginalUrl = "https://source.example.com/article",
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
