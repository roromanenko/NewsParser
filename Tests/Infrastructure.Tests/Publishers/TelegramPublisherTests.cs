using Core.DomainModels;
using FluentAssertions;
using Infrastructure.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using System.Net;
using System.Text.Json;

namespace Infrastructure.Tests.Publishers;

/// <summary>
/// Unit tests for <see cref="TelegramPublisher"/> HTTP dispatch logic.
/// The Telegram API is replaced by a mock <see cref="HttpMessageHandler"/> so
/// no real network calls are made.
/// </summary>
[TestFixture]
public class TelegramPublisherTests
{
    private const string BotToken = "test-bot-token-123";
    private const string ChannelId = "-100123456789";

    private Mock<HttpMessageHandler> _handlerMock = null!;
    private TelegramPublisher _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _handlerMock = new Mock<HttpMessageHandler>();

        var httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = null
        };

        _sut = new TelegramPublisher(
            BotToken,
            NullLogger<TelegramPublisher>.Instance,
            httpClient);
    }

    // ------------------------------------------------------------------
    // P0 — Empty media → sendMessage endpoint called
    // ------------------------------------------------------------------

    [Test]
    public async Task PublishAsync_WhenMediaIsEmpty_CallsSendMessage()
    {
        // Arrange
        var capturedUrl = string.Empty;
        SetupHandlerCapture(
            messageId: 1,
            isArray: false,
            captureUrl: url => capturedUrl = url);

        var publication = CreatePublication("Hello world");

        // Act
        var result = await _sut.PublishAsync(publication, [], CancellationToken.None);

        // Assert
        capturedUrl.Should().EndWith("/sendMessage");
        result.Should().Be("1");
    }

    // ------------------------------------------------------------------
    // P0 — Single image, caption ≤ 1024 chars → sendPhoto called with caption;
    //       returned ID is the photo message ID
    // ------------------------------------------------------------------

    [Test]
    public async Task PublishAsync_WhenSingleImageAndCaptionWithinLimit_CallsSendPhotoWithCaption()
    {
        // Arrange
        var capturedUrl = string.Empty;
        SetupHandlerCapture(
            messageId: 10,
            isArray: false,
            captureUrl: url => capturedUrl = url);

        var shortCaption = new string('A', 1024);
        var publication = CreatePublication(shortCaption);
        var media = new List<ResolvedMedia> { new("https://cdn.example.com/photo.jpg", "image/jpeg", MediaKind.Image) };

        // Act
        var result = await _sut.PublishAsync(publication, media, CancellationToken.None);

        // Assert
        capturedUrl.Should().EndWith("/sendPhoto");
        result.Should().Be("10");
        // Handler was called exactly once — no follow-up sendMessage
        _handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // P1 — Single image, caption > 1024 chars → sendPhoto captionless,
    //       then sendMessage; returned ID is the text message ID (NOT the photo's)
    // ------------------------------------------------------------------

    [Test]
    public async Task PublishAsync_WhenSingleImageAndCaptionExceedsLimit_SendsPhotoThenMessageAndReturnsTextMessageId()
    {
        // Arrange
        var callCount = 0;
        var capturedUrls = new List<string>();

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                callCount++;
                capturedUrls.Add(req.RequestUri!.ToString());

                // First call → sendPhoto returns message_id 5
                // Second call → sendMessage returns message_id 99
                var messageId = callCount == 1 ? 5 : 99;
                return OkJsonResponse(messageId, isArray: false);
            });

        var longCaption = new string('X', 1025); // exceeds 1024
        var publication = CreatePublication(longCaption);
        var media = new List<ResolvedMedia> { new("https://cdn.example.com/photo.jpg", "image/jpeg", MediaKind.Image) };

        // Act
        var result = await _sut.PublishAsync(publication, media, CancellationToken.None);

        // Assert
        capturedUrls.Should().HaveCount(2);
        capturedUrls[0].Should().EndWith("/sendPhoto");
        capturedUrls[1].Should().EndWith("/sendMessage");
        result.Should().Be("99"); // returned ID must be the text message, not the photo
    }

    // ------------------------------------------------------------------
    // P0 — Single video → sendVideo endpoint called
    // ------------------------------------------------------------------

    [Test]
    public async Task PublishAsync_WhenSingleVideo_CallsSendVideo()
    {
        // Arrange
        var capturedUrl = string.Empty;
        SetupHandlerCapture(
            messageId: 20,
            isArray: false,
            captureUrl: url => capturedUrl = url);

        var publication = CreatePublication("Video caption");
        var media = new List<ResolvedMedia> { new("https://cdn.example.com/clip.mp4", "video/mp4", MediaKind.Video) };

        // Act
        var result = await _sut.PublishAsync(publication, media, CancellationToken.None);

        // Assert
        capturedUrl.Should().EndWith("/sendVideo");
        result.Should().Be("20");
    }

    // ------------------------------------------------------------------
    // P0 — Multiple media items (≤ 10) → sendMediaGroup called once
    // ------------------------------------------------------------------

    [Test]
    public async Task PublishAsync_WhenMultipleMediaWithinGroupLimit_CallsSendMediaGroupOnce()
    {
        // Arrange
        var capturedUrl = string.Empty;
        SetupHandlerCapture(
            messageId: 30,
            isArray: true,
            captureUrl: url => capturedUrl = url);

        var publication = CreatePublication("Group caption");
        var media = Enumerable.Range(1, 5)
            .Select(i => new ResolvedMedia($"https://cdn.example.com/photo{i}.jpg", "image/jpeg", MediaKind.Image))
            .ToList();

        // Act
        var result = await _sut.PublishAsync(publication, media, CancellationToken.None);

        // Assert
        capturedUrl.Should().EndWith("/sendMediaGroup");
        result.Should().Be("30");
    }

    // ------------------------------------------------------------------
    // P1 — More than 10 media items → sendMediaGroup called with only first 10
    // ------------------------------------------------------------------

    [Test]
    public async Task PublishAsync_WhenMoreThan10MediaItems_CallsSendMediaGroupWithFirst10Only()
    {
        // Arrange
        // Capture the request synchronously via Callback; the JSON body is read
        // after PublishAsync completes (JsonContent stays readable after the send).
        HttpRequestMessage? capturedRequest = null;
        var capturedUrl = string.Empty;

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri!.ToString();
                capturedRequest = req;
            })
            .ReturnsAsync(OkJsonResponse(50, isArray: true));

        var publication = CreatePublication("Big group");
        var media = Enumerable.Range(1, 13)
            .Select(i => new ResolvedMedia($"https://cdn.example.com/photo{i}.jpg", "image/jpeg", MediaKind.Image))
            .ToList();

        // Act
        var result = await _sut.PublishAsync(publication, media, CancellationToken.None);

        // Assert
        capturedUrl.Should().EndWith("/sendMediaGroup");
        result.Should().Be("50");

        capturedRequest.Should().NotBeNull();
        var capturedBody = await capturedRequest!.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(capturedBody);
        var mediaArray = doc.RootElement.GetProperty("media");
        mediaArray.GetArrayLength().Should().Be(10);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Configures the handler mock to return a successful Telegram API response and
    /// optionally capture the request URL via <paramref name="captureUrl"/>.
    /// </summary>
    private void SetupHandlerCapture(int messageId, bool isArray, Action<string> captureUrl)
    {
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                captureUrl(req.RequestUri!.ToString());
                return OkJsonResponse(messageId, isArray);
            });
    }

    /// <summary>
    /// Builds a JSON response that mirrors the Telegram Bot API success envelope.
    /// For <c>sendMediaGroup</c> the result is an array; for all other methods it is an object.
    /// </summary>
    private static HttpResponseMessage OkJsonResponse(int messageId, bool isArray)
    {
        string json = isArray
            ? $$$"""{"ok":true,"result":[{"message_id":{{{messageId}}}}]}"""
            : $$$"""{"ok":true,"result":{"message_id":{{{messageId}}}}}""";

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private static Publication CreatePublication(string content) =>
        new()
        {
            Id = Guid.NewGuid(),
            GeneratedContent = content,
            Status = PublicationStatus.Approved,
            PublishTarget = new PublishTarget
            {
                Id = Guid.NewGuid(),
                Name = "Test Channel",
                Platform = Platform.Telegram,
                Identifier = ChannelId,
                IsActive = true,
            },
        };
}
