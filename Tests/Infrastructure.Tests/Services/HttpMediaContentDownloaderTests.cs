using Core.DomainModels;
using FluentAssertions;
using Infrastructure.Configuration;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Headers;

namespace Infrastructure.Tests.Services;

[TestFixture]
public class HttpMediaContentDownloaderTests
{
    private Mock<HttpMessageHandler> _httpHandlerMock = null!;
    private Mock<IHttpClientFactory> _httpClientFactoryMock = null!;
    private IOptions<CloudflareR2Options> _options = null!;
    private HttpMediaContentDownloader _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();

        // Return a NEW HttpClient instance per call so that `using var` disposal
        // in HttpMediaContentDownloader.DownloadAsync does not affect subsequent calls.
        _httpClientFactoryMock
            .Setup(f => f.CreateClient("MediaDownloader"))
            .Returns(() => new HttpClient(_httpHandlerMock.Object));

        _options = Options.Create(new CloudflareR2Options
        {
            MaxFileSizeBytes = 10 * 1024 * 1024, // 10 MB
            DownloadTimeoutSeconds = 30,
        });

        _sut = new HttpMediaContentDownloader(
            _httpClientFactoryMock.Object,
            _options,
            NullLogger<HttpMediaContentDownloader>.Instance);
    }

    // ------------------------------------------------------------------
    // P0 — 200 with image/jpeg content type → MediaDownloadResult with correct fields
    // ------------------------------------------------------------------

    [Test]
    public async Task DownloadAsync_WhenResponseIsSuccessWithImageJpeg_ReturnsResultWithCorrectContentTypeAndNonZeroSize()
    {
        // Arrange
        const string url = "https://cdn.example.com/photo.jpg";
        var reference = new MediaReference(url, MediaKind.Image, "image/jpeg");
        var imageBytes = "fake-jpeg-content"u8.ToArray();

        SetupHttpResponse(HttpStatusCode.OK, imageBytes, "image/jpeg");

        // Act
        var result = await _sut.DownloadAsync(reference, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.ContentType.Should().Be("image/jpeg");
        result.SizeBytes.Should().BeGreaterThan(0);
        result.Content.Should().NotBeNull();
        result.Content.Position.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // P0 — Kind property returns Http
    // ------------------------------------------------------------------

    [Test]
    public void Kind_ReturnsHttp()
    {
        // Act & Assert
        _sut.Kind.Should().Be(MediaSourceKind.Http);
    }

    // ------------------------------------------------------------------
    // P1 — HTTP 404 → returns null, does not throw
    // ------------------------------------------------------------------

    [Test]
    public async Task DownloadAsync_WhenResponseIs404_ReturnsNull()
    {
        // Arrange
        var reference = new MediaReference("https://cdn.example.com/missing.jpg", MediaKind.Image, null);
        SetupHttpResponse(HttpStatusCode.NotFound, [], "image/jpeg");

        // Act
        var result = await _sut.DownloadAsync(reference, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P1 — Content-Length header exceeds MaxFileSizeBytes → returns null
    // ------------------------------------------------------------------

    [Test]
    public async Task DownloadAsync_WhenContentLengthHeaderExceedsLimit_ReturnsNull()
    {
        // Arrange
        var reference = new MediaReference("https://cdn.example.com/huge.jpg", MediaKind.Image, "image/jpeg");
        var oversizedLength = _options.Value.MaxFileSizeBytes + 1;

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([])
                };
                response.Content.Headers.ContentLength = oversizedLength;
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                return response;
            });

        // Act
        var result = await _sut.DownloadAsync(reference, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P1 — Downloaded stream exceeds MaxFileSizeBytes → returns null
    // ------------------------------------------------------------------

    [Test]
    public async Task DownloadAsync_WhenDownloadedBytesExceedLimit_ReturnsNull()
    {
        // Arrange
        var reference = new MediaReference("https://cdn.example.com/large.jpg", MediaKind.Image, "image/jpeg");
        // Lower the limit so the test doesn't need to allocate large byte arrays
        var lowLimitOptions = Options.Create(new CloudflareR2Options { MaxFileSizeBytes = 5 });
        var sut = new HttpMediaContentDownloader(
            _httpClientFactoryMock.Object,
            lowLimitOptions,
            NullLogger<HttpMediaContentDownloader>.Instance);

        SetupHttpResponse(HttpStatusCode.OK, "123456789"u8.ToArray(), "image/jpeg");

        // Act
        var result = await sut.DownloadAsync(reference, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P1 — Unsupported mime type (application/pdf) → returns null
    // ------------------------------------------------------------------

    [Test]
    public async Task DownloadAsync_WhenContentTypeIsUnsupportedMime_ReturnsNull()
    {
        // Arrange
        var reference = new MediaReference("https://cdn.example.com/report.pdf", MediaKind.Image, null);
        SetupHttpResponse(HttpStatusCode.OK, "pdf-content"u8.ToArray(), "application/pdf");

        // Act
        var result = await _sut.DownloadAsync(reference, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P1 — Header content-type absent; DeclaredContentType used as fallback
    // ------------------------------------------------------------------

    [Test]
    public async Task DownloadAsync_WhenHeaderContentTypeAbsentAndDeclaredIsValid_UsesDeclaredContentType()
    {
        // Arrange
        var reference = new MediaReference("https://cdn.example.com/photo", MediaKind.Image, "image/png");
        SetupHttpResponse(HttpStatusCode.OK, "png-bytes"u8.ToArray(), contentType: null);

        // Act
        var result = await _sut.DownloadAsync(reference, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.ContentType.Should().Be("image/png");
    }

    // ------------------------------------------------------------------
    // P1 — Header and DeclaredContentType both absent; URL extension used as fallback
    // ------------------------------------------------------------------

    [Test]
    public async Task DownloadAsync_WhenHeaderAndDeclaredContentTypeAbsent_ResolvesContentTypeFromUrlExtension()
    {
        // Arrange
        var reference = new MediaReference("https://cdn.example.com/clip.mp4", MediaKind.Video, null);
        SetupHttpResponse(HttpStatusCode.OK, "mp4-bytes"u8.ToArray(), contentType: null);

        // Act
        var result = await _sut.DownloadAsync(reference, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.ContentType.Should().Be("video/mp4");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void SetupHttpResponse(HttpStatusCode statusCode, byte[] content, string? contentType)
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(statusCode)
                {
                    Content = new ByteArrayContent(content)
                };
                if (contentType is not null)
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                return response;
            });
    }
}
