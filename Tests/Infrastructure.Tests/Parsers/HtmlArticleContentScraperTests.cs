using Core.DomainModels;
using FluentAssertions;
using Infrastructure.Configuration;
using Infrastructure.Parsers;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Headers;

namespace Infrastructure.Tests.Parsers;

/// <summary>
/// Tests for <see cref="HtmlArticleContentScraper"/>.
///
/// <para>
/// All HTTP is mocked via <c>HttpMessageHandler</c> (Moq + <c>Moq.Protected</c>) — no live
/// network calls. The <c>IHttpClientFactory</c> mock returns a new <see cref="HttpClient"/>
/// per call so that per-client configuration (timeout, default headers) can be applied by
/// the individual test without cross-test interference.
/// </para>
/// </summary>
[TestFixture]
public class HtmlArticleContentScraperTests
{
    private const string TestUrl = "https://example.com/article";

    private Mock<HttpMessageHandler> _httpHandlerMock = null!;
    private Mock<IHttpClientFactory> _httpClientFactoryMock = null!;
    private ArticleScraperOptions _optionsValue = null!;
    private HtmlArticleContentScraper _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();

        _optionsValue = new ArticleScraperOptions
        {
            Enabled = true,
            RequestTimeoutSeconds = 15,
            MaxHtmlSizeBytes = 2_097_152,
            MaxConcurrencyPerFeed = 4,
            UserAgent = "NewsParserBot/1.0",
            MaxTagsPerArticle = 20,
        };

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("ArticleContentScraper"))
            .Returns(() =>
            {
                var client = new HttpClient(_httpHandlerMock.Object)
                {
                    Timeout = TimeSpan.FromSeconds(_optionsValue.RequestTimeoutSeconds),
                };
                client.DefaultRequestHeaders.UserAgent.ParseAdd(_optionsValue.UserAgent);
                return client;
            });

        _sut = new HtmlArticleContentScraper(
            _httpClientFactoryMock.Object,
            Options.Create(_optionsValue),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HtmlArticleContentScraper>.Instance);
    }

    // ------------------------------------------------------------------
    // P1 — Kill-switch: Enabled=false returns null without issuing HTTP
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenEnabledFalse_ReturnsNull()
    {
        // Arrange
        _optionsValue.Enabled = false;

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        _httpHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // P1 — Non-2xx HTTP response returns null
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenResponseIsNon200_ReturnsNull()
    {
        // Arrange
        SetupHtmlResponse(HttpStatusCode.NotFound, string.Empty);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P1 — Empty 200 body returns null
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenResponseBodyIsEmpty_ReturnsNull()
    {
        // Arrange
        SetupHtmlResponse(HttpStatusCode.OK, string.Empty);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P0 — <article> element body text is extracted into FullContent
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenHtmlHasArticleElement_ExtractsFullContent()
    {
        // Arrange
        const string html = "<html><body><article><p>Full body text.</p></article></body></html>";
        SetupHtmlResponse(HttpStatusCode.OK, html);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.FullContent.Should().Contain("Full body text.");
    }

    // ------------------------------------------------------------------
    // P0 — Repeated <meta property="article:tag"> → Tags
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenHtmlHasArticleTagMeta_ExtractsTags()
    {
        // Arrange
        const string html = """
            <html><head>
                <meta property="article:tag" content="politics">
                <meta property="article:tag" content="economy">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponse(HttpStatusCode.OK, html);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Tags.Should().Contain(["politics", "economy"]);
    }

    // ------------------------------------------------------------------
    // P0 — <meta name="keywords"> is comma-split into Tags
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenHtmlHasKeywordsMeta_SplitsAndExtractsTags()
    {
        // Arrange
        const string html = """
            <html><head>
                <meta name="keywords" content="news,politics,world">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponse(HttpStatusCode.OK, html);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Tags.Should().Contain(["news", "politics", "world"]);
    }

    // ------------------------------------------------------------------
    // P0 — og:image → DiscoveredMedia with MediaKind.Image
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenHtmlHasOgImage_ReturnsMediaReference()
    {
        // Arrange
        const string imageUrl = "https://cdn.example.com/img.jpg";
        var html = $"""
            <html><head>
                <meta property="og:image" content="{imageUrl}">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponse(HttpStatusCode.OK, html);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DiscoveredMedia.Should().ContainSingle();
        result.DiscoveredMedia[0].Url.Should().Be(imageUrl);
        result.DiscoveredMedia[0].Kind.Should().Be(MediaKind.Image);
    }

    // ------------------------------------------------------------------
    // P0 — twitter:image → DiscoveredMedia with MediaKind.Image
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenHtmlHasTwitterImage_ReturnsMediaReference()
    {
        // Arrange
        const string imageUrl = "https://cdn.example.com/tw.jpg";
        var html = $"""
            <html><head>
                <meta name="twitter:image" content="{imageUrl}">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponse(HttpStatusCode.OK, html);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DiscoveredMedia.Should().ContainSingle(m =>
            m.Url == imageUrl && m.Kind == MediaKind.Image);
    }

    // ------------------------------------------------------------------
    // P1 — Body exceeds MaxHtmlSizeBytes → null
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenResponseExceedsMaxHtmlSizeBytes_ReturnsNull()
    {
        // Arrange
        _optionsValue.MaxHtmlSizeBytes = 10;
        var oversizedHtml = new string('a', 64); // 64 bytes > 10
        SetupHtmlResponse(HttpStatusCode.OK, oversizedHtml);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P1 — Handler delays beyond client timeout → null (TaskCanceledException path)
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenRequestTimesOut_ReturnsNull()
    {
        // Arrange — tight client timeout; handler respects the token raised by HttpClient
        _optionsValue.RequestTimeoutSeconds = 1;

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken ct) =>
            {
                // Wait until HttpClient cancels us (exceeds configured timeout)
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK); // unreachable
            });

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert — HttpClient surfaces timeout as TaskCanceledException; scraper swallows
        // it (external token was not cancelled) and returns null so one slow article cannot
        // fail the whole feed fetch.
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P0 — User-Agent header from options is sent on the outgoing request
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenCalled_SendsConfiguredUserAgentHeader()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                capturedRequest = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body><article><p>x</p></article></body></html>"),
                };
            });

        // Act
        await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.UserAgent.ToString()
            .Should().Be(_optionsValue.UserAgent);
    }

    // ------------------------------------------------------------------
    // P0 — og:image with relative path → resolved against article URL
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenOgImageIsRelative_ResolvesAgainstArticleUrl()
    {
        // Arrange
        const string html = """
            <html><head>
                <meta property="og:image" content="/images/photo.jpg">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponseWithRequestUri(HttpStatusCode.OK, html, new Uri(TestUrl));

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DiscoveredMedia.Should().ContainSingle();
        result.DiscoveredMedia[0].Url.Should().Be("https://example.com/images/photo.jpg");
    }

    // ------------------------------------------------------------------
    // P0 — og:image with scheme-relative URL → resolves to https
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenOgImageIsSchemeRelative_ResolvesToHttps()
    {
        // Arrange
        const string html = """
            <html><head>
                <meta property="og:image" content="//cdn.example.com/photo.jpg">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponseWithRequestUri(HttpStatusCode.OK, html, new Uri(TestUrl));

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DiscoveredMedia.Should().ContainSingle();
        result.DiscoveredMedia[0].Url.Should().Be("https://cdn.example.com/photo.jpg");
    }

    // ------------------------------------------------------------------
    // P0 — og:image with data: URI → skipped
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenOgImageIsDataUri_IsSkipped()
    {
        // Arrange
        const string html = """
            <html><head>
                <meta property="og:image" content="data:image/svg+xml;base64,abc">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponse(HttpStatusCode.OK, html);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DiscoveredMedia.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // P0 — og:image with empty content → skipped
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenOgImageIsEmpty_IsSkipped()
    {
        // Arrange
        const string html = """
            <html><head>
                <meta property="og:image" content="">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponse(HttpStatusCode.OK, html);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DiscoveredMedia.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // P0 — og:image with .jpg extension → DeclaredContentType = "image/jpeg"
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenOgImageHasJpgExtension_PopulatesDeclaredContentType()
    {
        // Arrange
        const string imageUrl = "https://cdn.example.com/photo.jpg";
        var html = $"""
            <html><head>
                <meta property="og:image" content="{imageUrl}">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponse(HttpStatusCode.OK, html);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DiscoveredMedia.Should().ContainSingle();
        result.DiscoveredMedia[0].DeclaredContentType.Should().Be("image/jpeg");
    }

    // ------------------------------------------------------------------
    // P0 — og:image without extension → DeclaredContentType = null
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenOgImageIsExtensionless_LeavesDeclaredContentTypeNull()
    {
        // Arrange
        const string imageUrl = "https://cdn.example.com/abc123";
        var html = $"""
            <html><head>
                <meta property="og:image" content="{imageUrl}">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponse(HttpStatusCode.OK, html);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DiscoveredMedia.Should().ContainSingle();
        result.DiscoveredMedia[0].DeclaredContentType.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P0 — Redirect: handler RequestUri differs from original URL →
    //      relative og:image resolved against final (RequestUri) URL
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenResponseRedirected_ResolvesRelativeAgainstFinalUrl()
    {
        // Arrange — original URL differs from the handler's RequestUri (simulates redirect)
        const string originalUrl = "https://example.com/redirect-source";
        var finalUri = new Uri("https://example.com/final-destination");
        const string html = """
            <html><head>
                <meta property="og:image" content="/images/banner.jpg">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponseWithRequestUri(HttpStatusCode.OK, html, finalUri);

        // Act
        var result = await _sut.ScrapeAsync(originalUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DiscoveredMedia.Should().ContainSingle();
        result.DiscoveredMedia[0].Url.Should().Be("https://example.com/images/banner.jpg");
    }

    // ------------------------------------------------------------------
    // P2 — Extension → MIME lookup covers every entry in ExtensionToMime
    //      (regression guard: if an entry is removed from the map, this fails)
    // ------------------------------------------------------------------

    [TestCase(".jpg", "image/jpeg")]
    [TestCase(".jpeg", "image/jpeg")]
    [TestCase(".png", "image/png")]
    [TestCase(".gif", "image/gif")]
    [TestCase(".webp", "image/webp")]
    public async Task ScrapeAsync_WhenOgImageHasKnownExtension_MapsToExpectedMime(
        string extension, string expectedMime)
    {
        // Arrange
        var imageUrl = $"https://cdn.example.com/photo{extension}";
        var html = $"""
            <html><head>
                <meta property="og:image" content="{imageUrl}">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponse(HttpStatusCode.OK, html);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DiscoveredMedia.Should().ContainSingle();
        result.DiscoveredMedia[0].DeclaredContentType.Should().Be(expectedMime);
    }

    // ------------------------------------------------------------------
    // P2 — Extension match is case-insensitive (.JPG → image/jpeg)
    //      (regression guard on StringComparer.OrdinalIgnoreCase)
    // ------------------------------------------------------------------

    [Test]
    public async Task ScrapeAsync_WhenOgImageHasUppercaseExtension_MatchesCaseInsensitively()
    {
        // Arrange
        const string imageUrl = "https://cdn.example.com/PHOTO.JPG";
        var html = $"""
            <html><head>
                <meta property="og:image" content="{imageUrl}">
            </head><body><article><p>x</p></article></body></html>
            """;
        SetupHtmlResponse(HttpStatusCode.OK, html);

        // Act
        var result = await _sut.ScrapeAsync(TestUrl, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DiscoveredMedia.Should().ContainSingle();
        result.DiscoveredMedia[0].DeclaredContentType.Should().Be("image/jpeg");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void SetupHtmlResponse(HttpStatusCode statusCode, string body)
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
                    Content = new StringContent(body),
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
                return response;
            });
    }

    private void SetupHtmlResponseWithRequestUri(HttpStatusCode statusCode, string body, Uri requestUri)
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                req.RequestUri = requestUri;
                var response = new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(body),
                    RequestMessage = req,
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
                return response;
            });
    }
}
