using Core.DomainModels;
using Core.Interfaces.Storage;
using FluentAssertions;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Services;

[TestFixture]
public class TelegramMediaContentDownloaderTests
{
    private Mock<ITelegramMediaGateway> _gatewayMock = null!;
    private TelegramMediaContentDownloader _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _gatewayMock = new Mock<ITelegramMediaGateway>();
        _sut = new TelegramMediaContentDownloader(
            _gatewayMock.Object,
            NullLogger<TelegramMediaContentDownloader>.Instance);
    }

    // ------------------------------------------------------------------
    // P0 — Kind property returns Telegram
    // ------------------------------------------------------------------

    [Test]
    public void Kind_ReturnsTelegram()
    {
        // Act & Assert
        _sut.Kind.Should().Be(MediaSourceKind.Telegram);
    }

    // ------------------------------------------------------------------
    // P0 — Gateway returns payload → MediaDownloadResult with matching fields,
    //       Content.Position == 0
    // ------------------------------------------------------------------

    [Test]
    public async Task DownloadAsync_WhenGatewayReturnsPayload_ReturnsResultWithMatchingFieldsAndPositionZero()
    {
        // Arrange
        var reference = new MediaReference(
            "https://t.me/channel/42#media-0",
            MediaKind.Image,
            "image/jpeg",
            MediaSourceKind.Telegram,
            "123:456:42:0");

        _gatewayMock.Setup(g => g.IsReady).Returns(true);
        _gatewayMock
            .Setup(g => g.DownloadMediaAsync("123:456:42:0", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramMediaDownloadResult("image/jpeg", 1024));

        // Act
        var result = await _sut.DownloadAsync(reference, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.ContentType.Should().Be("image/jpeg");
        result.SizeBytes.Should().Be(1024);
        result.Content.Position.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // P1 — IsReady == false → returns null
    // ------------------------------------------------------------------

    [Test]
    public async Task DownloadAsync_WhenClientIsNotReady_ReturnsNull()
    {
        // Arrange
        var reference = new MediaReference(
            "https://t.me/channel/1#media-0",
            MediaKind.Image,
            "image/jpeg",
            MediaSourceKind.Telegram,
            "1:2:3:0");

        _gatewayMock.Setup(g => g.IsReady).Returns(false);

        // Act
        var result = await _sut.DownloadAsync(reference, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        _gatewayMock.Verify(g => g.DownloadMediaAsync(
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // P1 — ExternalHandle is null → returns null
    // ------------------------------------------------------------------

    [Test]
    public async Task DownloadAsync_WhenExternalHandleIsNull_ReturnsNull()
    {
        // Arrange
        var reference = new MediaReference(
            "https://t.me/channel/1#media-0",
            MediaKind.Image,
            "image/jpeg",
            MediaSourceKind.Telegram,
            ExternalHandle: null);

        _gatewayMock.Setup(g => g.IsReady).Returns(true);

        // Act
        var result = await _sut.DownloadAsync(reference, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        _gatewayMock.Verify(g => g.DownloadMediaAsync(
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // P1 — Gateway returns null (e.g. WTException handled inside service) → returns null
    // ------------------------------------------------------------------

    [Test]
    public async Task DownloadAsync_WhenGatewayReturnsNull_ReturnsNull()
    {
        // Arrange
        var reference = new MediaReference(
            "https://t.me/channel/5#media-0",
            MediaKind.Image,
            "image/jpeg",
            MediaSourceKind.Telegram,
            "1:2:5:0");

        _gatewayMock.Setup(g => g.IsReady).Returns(true);
        _gatewayMock
            .Setup(g => g.DownloadMediaAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TelegramMediaDownloadResult?)null);

        // Act
        var result = await _sut.DownloadAsync(reference, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // P1 — Gateway throws unexpectedly → DownloadAsync returns null and does not throw
    // ------------------------------------------------------------------

    [Test]
    public async Task DownloadAsync_WhenGatewayThrows_ReturnsNullAndDoesNotThrow()
    {
        // Arrange
        var reference = new MediaReference(
            "https://t.me/channel/7#media-0",
            MediaKind.Image,
            "image/jpeg",
            MediaSourceKind.Telegram,
            "1:2:7:0");

        _gatewayMock.Setup(g => g.IsReady).Returns(true);
        _gatewayMock
            .Setup(g => g.DownloadMediaAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("unexpected inner failure"));

        // Act
        var act = async () => await _sut.DownloadAsync(reference, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        var result = await _sut.DownloadAsync(reference, CancellationToken.None);
        result.Should().BeNull();
    }
}
