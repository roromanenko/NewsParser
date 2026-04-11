using Api.Mappers;
using Api.Models;
using Core.DomainModels;
using FluentAssertions;
using NUnit.Framework;

namespace Api.Tests.Mappers;

/// <summary>
/// Tests for <see cref="MediaFileMapper.ToDto"/> and the private <c>BuildUrl</c> helper.
///
/// <c>BuildUrl</c> is private but its behavior is observable through <c>ToDto</c>.
/// The four slash-edge-case tests exercise every combination of trailing slash on the
/// base URL and leading slash on the R2 key, verifying a single clean separator is
/// always produced.
/// </summary>
[TestFixture]
public class MediaFileMapperTests
{
    // ------------------------------------------------------------------
    // BuildUrl — P0: trailing slash on base only
    // ------------------------------------------------------------------

    [Test]
    public void ToDto_WhenBaseHasTrailingSlashAndKeyHasNone_ProducesSingleSeparator()
    {
        // Arrange
        var media = CreateMediaFile(r2Key: "articles/abc/photo.jpg");
        const string baseUrl = "https://cdn.example.com/";

        // Act
        var result = media.ToDto(baseUrl);

        // Assert
        result.Url.Should().Be("https://cdn.example.com/articles/abc/photo.jpg");
    }

    // ------------------------------------------------------------------
    // BuildUrl — P0: leading slash on key only
    // ------------------------------------------------------------------

    [Test]
    public void ToDto_WhenKeyHasLeadingSlashAndBaseHasNone_ProducesSingleSeparator()
    {
        // Arrange
        var media = CreateMediaFile(r2Key: "/articles/abc/photo.jpg");
        const string baseUrl = "https://cdn.example.com";

        // Act
        var result = media.ToDto(baseUrl);

        // Assert
        result.Url.Should().Be("https://cdn.example.com/articles/abc/photo.jpg");
    }

    // ------------------------------------------------------------------
    // BuildUrl — P0: both base and key have slashes
    // ------------------------------------------------------------------

    [Test]
    public void ToDto_WhenBothBaseAndKeyHaveSlashes_ProducesSingleSeparator()
    {
        // Arrange
        var media = CreateMediaFile(r2Key: "/articles/abc/photo.jpg");
        const string baseUrl = "https://cdn.example.com/";

        // Act
        var result = media.ToDto(baseUrl);

        // Assert
        result.Url.Should().Be("https://cdn.example.com/articles/abc/photo.jpg");
    }

    // ------------------------------------------------------------------
    // BuildUrl — P0: neither base nor key has a slash at the join point
    // ------------------------------------------------------------------

    [Test]
    public void ToDto_WhenNeitherBaseNorKeyHasSlash_ProducesSingleSeparator()
    {
        // Arrange
        var media = CreateMediaFile(r2Key: "articles/abc/photo.jpg");
        const string baseUrl = "https://cdn.example.com";

        // Act
        var result = media.ToDto(baseUrl);

        // Assert
        result.Url.Should().Be("https://cdn.example.com/articles/abc/photo.jpg");
    }

    // ------------------------------------------------------------------
    // ToDto — P0: full round-trip — all fields are mapped correctly
    // ------------------------------------------------------------------

    [Test]
    public void ToDto_WhenCalled_MapsAllFieldsCorrectly()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var media = new MediaFile
        {
            Id = mediaId,
            ArticleId = articleId,
            R2Key = "articles/events/image.png",
            OriginalUrl = "https://source.example.com/image.png",
            ContentType = "image/png",
            SizeBytes = 204_800,
            Kind = MediaKind.Image,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        const string baseUrl = "https://cdn.example.com";

        // Act
        var result = media.ToDto(baseUrl);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(mediaId);
        result.ArticleId.Should().Be(articleId);
        result.Url.Should().Be("https://cdn.example.com/articles/events/image.png");
        result.Kind.Should().Be(MediaKind.Image.ToString());
        result.ContentType.Should().Be("image/png");
        result.SizeBytes.Should().Be(204_800);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static MediaFile CreateMediaFile(string r2Key) => new()
    {
        Id = Guid.NewGuid(),
        ArticleId = Guid.NewGuid(),
        R2Key = r2Key,
        OriginalUrl = "https://source.example.com/photo.jpg",
        ContentType = "image/jpeg",
        SizeBytes = 102_400,
        Kind = MediaKind.Image,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
