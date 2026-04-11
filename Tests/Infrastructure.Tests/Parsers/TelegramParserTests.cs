using Core.DomainModels;
using FluentAssertions;
using Infrastructure.Models;
using Infrastructure.Parsers;
using Infrastructure.Storage;
using Moq;
using NUnit.Framework;
using TL;

namespace Infrastructure.Tests.Parsers;

[TestFixture]
public class TelegramParserTests
{
    private Mock<ITelegramChannelReader> _channelReaderMock = null!;
    private TelegramParser _sut = null!;

    private const string ChannelUsername = "testchannel";
    private const long ChannelId = 100L;
    private const long AccessHash = 999L;

    [SetUp]
    public void SetUp()
    {
        _channelReaderMock = new Mock<ITelegramChannelReader>();
        _channelReaderMock.Setup(r => r.IsReady).Returns(true);
        _sut = new TelegramParser(_channelReaderMock.Object);
    }

    // ------------------------------------------------------------------
    // P0 — MessageMediaPhoto → one MediaReference with Telegram source kind,
    //       Image kind, image/jpeg content type, correct ExternalHandle and Url
    // ------------------------------------------------------------------

    [Test]
    public async Task ParseAsync_WhenMessageHasPhoto_ReturnsArticleWithCorrectImageMediaReference()
    {
        // Arrange
        const int messageId = 42;
        var photo = new Photo();
        var message = BuildMessage(messageId, "Photo message text", new MessageMediaPhoto { photo = photo });
        var source = BuildSource($"https://t.me/{ChannelUsername}");

        _channelReaderMock
            .Setup(r => r.GetChannelMessagesAsync(ChannelUsername, source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TelegramChannelMessage(message, ChannelId, AccessHash)]);

        // Act
        var articles = await _sut.ParseAsync(source, CancellationToken.None);

        // Assert
        articles.Should().HaveCount(1);
        var article = articles[0];

        article.OriginalUrl.Should().Be($"https://t.me/{ChannelUsername}/{messageId}");
        article.MediaReferences.Should().HaveCount(1);

        var mediaRef = article.MediaReferences[0];
        mediaRef.SourceKind.Should().Be(MediaSourceKind.Telegram);
        mediaRef.Kind.Should().Be(MediaKind.Image);
        mediaRef.DeclaredContentType.Should().Be("image/jpeg");
        mediaRef.Url.Should().Be($"https://t.me/{ChannelUsername}/{messageId}#media-0");
        mediaRef.ExternalHandle.Should().Be(
            TelegramMediaHandle.Encode(ChannelId, AccessHash, messageId, 0));
    }

    // ------------------------------------------------------------------
    // P0 — OriginalUrl must NOT have the #media-0 suffix
    // ------------------------------------------------------------------

    [Test]
    public async Task ParseAsync_WhenMessageHasPhoto_OriginalUrlDoesNotContainMediaSuffix()
    {
        // Arrange
        const int messageId = 55;
        var message = BuildMessage(messageId, "Text with photo", new MessageMediaPhoto { photo = new Photo() });
        var source = BuildSource($"https://t.me/{ChannelUsername}");

        _channelReaderMock
            .Setup(r => r.GetChannelMessagesAsync(ChannelUsername, source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TelegramChannelMessage(message, ChannelId, AccessHash)]);

        // Act
        var articles = await _sut.ParseAsync(source, CancellationToken.None);

        // Assert
        articles[0].OriginalUrl.Should().Be($"https://t.me/{ChannelUsername}/{messageId}");
        articles[0].OriginalUrl.Should().NotContain("#media");
    }

    // ------------------------------------------------------------------
    // P0 — MessageMediaDocument with video/mp4 → one MediaReference with Video kind
    // ------------------------------------------------------------------

    [Test]
    public async Task ParseAsync_WhenMessageHasVideoDocument_ReturnsArticleWithVideoMediaReference()
    {
        // Arrange
        const int messageId = 77;
        var doc = new Document { mime_type = "video/mp4", size = 2048L };
        var message = BuildMessage(messageId, "Video message", new MessageMediaDocument { document = doc });
        var source = BuildSource($"https://t.me/{ChannelUsername}");

        _channelReaderMock
            .Setup(r => r.GetChannelMessagesAsync(ChannelUsername, source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TelegramChannelMessage(message, ChannelId, AccessHash)]);

        // Act
        var articles = await _sut.ParseAsync(source, CancellationToken.None);

        // Assert
        articles.Should().HaveCount(1);
        var mediaRef = articles[0].MediaReferences.Should().ContainSingle().Subject;
        mediaRef.Kind.Should().Be(MediaKind.Video);
        mediaRef.DeclaredContentType.Should().Be("video/mp4");
        mediaRef.SourceKind.Should().Be(MediaSourceKind.Telegram);
    }

    // ------------------------------------------------------------------
    // P1 — MessageMediaDocument with unsupported mime (application/octet-stream)
    //       → MediaReferences is empty
    // ------------------------------------------------------------------

    [Test]
    public async Task ParseAsync_WhenDocumentHasUnsupportedMime_MediaReferencesIsEmpty()
    {
        // Arrange
        const int messageId = 88;
        var doc = new Document { mime_type = "application/octet-stream", size = 500L };
        var message = BuildMessage(messageId, "Binary file", new MessageMediaDocument { document = doc });
        var source = BuildSource($"https://t.me/{ChannelUsername}");

        _channelReaderMock
            .Setup(r => r.GetChannelMessagesAsync(ChannelUsername, source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TelegramChannelMessage(message, ChannelId, AccessHash)]);

        // Act
        var articles = await _sut.ParseAsync(source, CancellationToken.None);

        // Assert
        articles.Should().HaveCount(1);
        articles[0].MediaReferences.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // P1 — Message with no media field → MediaReferences is empty
    // ------------------------------------------------------------------

    [Test]
    public async Task ParseAsync_WhenMessageHasNoMedia_MediaReferencesIsEmpty()
    {
        // Arrange
        const int messageId = 10;
        var message = BuildMessage(messageId, "Plain text message, no media", media: null);
        var source = BuildSource($"https://t.me/{ChannelUsername}");

        _channelReaderMock
            .Setup(r => r.GetChannelMessagesAsync(ChannelUsername, source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TelegramChannelMessage(message, ChannelId, AccessHash)]);

        // Act
        var articles = await _sut.ParseAsync(source, CancellationToken.None);

        // Assert
        articles.Should().HaveCount(1);
        articles[0].MediaReferences.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // P1 — Sticker (MessageMediaDocument, mime image/webp) — parser relies
    //       on mime type only, so image/webp IS included as an Image reference.
    //       This test documents the chosen behavior: stickers are treated as images
    //       because the parser has no sticker-attribute check, only a mime-prefix check.
    // ------------------------------------------------------------------

    [Test]
    public async Task ParseAsync_WhenDocumentIsStickerWithImageWebpMime_ReturnsImageMediaReference()
    {
        // Arrange — sticker documents use mime_type "image/webp";
        // the parser allows anything starting with "image/" or "video/".
        const int messageId = 99;
        var doc = new Document { mime_type = "image/webp", size = 100L };
        var message = BuildMessage(messageId, "Sticker message", new MessageMediaDocument { document = doc });
        var source = BuildSource($"https://t.me/{ChannelUsername}");

        _channelReaderMock
            .Setup(r => r.GetChannelMessagesAsync(ChannelUsername, source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TelegramChannelMessage(message, ChannelId, AccessHash)]);

        // Act
        var articles = await _sut.ParseAsync(source, CancellationToken.None);

        // Assert — image/webp starts with "image/" so it passes the allow-list
        articles.Should().HaveCount(1);
        var mediaRef = articles[0].MediaReferences.Should().ContainSingle().Subject;
        mediaRef.Kind.Should().Be(MediaKind.Image);
        mediaRef.DeclaredContentType.Should().Be("image/webp");
    }

    // ------------------------------------------------------------------
    // P1 — IsReady == false → returns empty list without calling channel reader
    // ------------------------------------------------------------------

    [Test]
    public async Task ParseAsync_WhenClientIsNotReady_ReturnsEmptyList()
    {
        // Arrange
        _channelReaderMock.Setup(r => r.IsReady).Returns(false);
        var source = BuildSource($"https://t.me/{ChannelUsername}");

        // Act
        var articles = await _sut.ParseAsync(source, CancellationToken.None);

        // Assert
        articles.Should().BeEmpty();
        _channelReaderMock.Verify(
            r => r.GetChannelMessagesAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Message BuildMessage(int id, string text, MessageMedia? media)
    {
        return new Message
        {
            id = id,
            message = text,
            date = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            media = media,
        };
    }

    private static Source BuildSource(string url) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Telegram Source",
        Url = url,
        Type = SourceType.Telegram,
        IsActive = true,
    };
}
