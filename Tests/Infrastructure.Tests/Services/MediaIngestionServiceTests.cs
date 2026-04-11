using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Storage;
using FluentAssertions;
using Infrastructure.Configuration;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using System.Net;

namespace Infrastructure.Tests.Services;

[TestFixture]
public class MediaIngestionServiceTests
{
    private Mock<IMediaStorage> _storageMock = null!;
    private Mock<IMediaFileRepository> _repositoryMock = null!;
    private Mock<IMediaContentDownloader> _httpDownloaderMock = null!;
    private IOptions<CloudflareR2Options> _options = null!;
    private MediaIngestionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _storageMock = new Mock<IMediaStorage>();
        _repositoryMock = new Mock<IMediaFileRepository>();
        _httpDownloaderMock = new Mock<IMediaContentDownloader>();
        _httpDownloaderMock.Setup(d => d.Kind).Returns(MediaSourceKind.Http);

        _options = Options.Create(new CloudflareR2Options
        {
            MaxFileSizeBytes = 10 * 1024 * 1024, // 10 MB
            DownloadTimeoutSeconds = 30,
        });

        _repositoryMock
            .Setup(r => r.ExistsByArticleAndUrlAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _sut = new MediaIngestionService(
            _storageMock.Object,
            _repositoryMock.Object,
            [_httpDownloaderMock.Object],
            _options,
            NullLogger<MediaIngestionService>.Instance);
    }

    // ------------------------------------------------------------------
    // P0 — Empty references list → early return, storage and repo not called
    // ------------------------------------------------------------------

    [Test]
    public async Task IngestForArticleAsync_WhenReferencesIsEmpty_DoesNotCallStorageOrRepository()
    {
        // Arrange
        var articleId = Guid.NewGuid();

        // Act
        await _sut.IngestForArticleAsync(articleId, [], CancellationToken.None);

        // Assert
        _storageMock.Verify(
            s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            r => r.AddAsync(It.IsAny<MediaFile>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P0 — Successful path → UploadAsync and AddAsync called with correct data
    // ------------------------------------------------------------------

    [Test]
    public async Task IngestForArticleAsync_WhenDownloadSucceeds_CallsStorageAndRepositoryWithCorrectArticleId()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var reference = new MediaReference("https://cdn.example.com/image.jpg", MediaKind.Image, "image/jpeg");
        var downloadStream = new MemoryStream("fake-image-bytes"u8.ToArray());

        _httpDownloaderMock
            .Setup(d => d.DownloadAsync(reference, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaDownloadResult(downloadStream, "image/jpeg", downloadStream.Length));

        MediaFile? capturedMediaFile = null;
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<MediaFile>(), It.IsAny<CancellationToken>()))
            .Callback<MediaFile, CancellationToken>((mf, _) => capturedMediaFile = mf)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.IngestForArticleAsync(articleId, [reference], CancellationToken.None);

        // Assert
        _storageMock.Verify(
            s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), "image/jpeg", It.IsAny<CancellationToken>()),
            Times.Once);

        capturedMediaFile.Should().NotBeNull();
        capturedMediaFile!.ArticleId.Should().Be(articleId);
        capturedMediaFile.OriginalUrl.Should().Be("https://cdn.example.com/image.jpg");
        capturedMediaFile.ContentType.Should().Be("image/jpeg");
        capturedMediaFile.Kind.Should().Be(MediaKind.Image);
    }

    // ------------------------------------------------------------------
    // P1 — Downloader returns null (e.g. HTTP 404) → caught, AddAsync never called
    // ------------------------------------------------------------------

    [Test]
    public async Task IngestForArticleAsync_WhenDownloaderReturnsNull_DoesNotCallAddAsyncAndDoesNotThrow()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var reference = new MediaReference("https://cdn.example.com/missing.jpg", MediaKind.Image, null);

        _httpDownloaderMock
            .Setup(d => d.DownloadAsync(reference, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MediaDownloadResult?)null);

        // Act
        var act = async () => await _sut.IngestForArticleAsync(articleId, [reference], CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        _repositoryMock.Verify(
            r => r.AddAsync(It.IsAny<MediaFile>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P1 — SizeBytes from downloader exceeds MaxFileSizeBytes → storage not called
    // ------------------------------------------------------------------

    [Test]
    public async Task IngestForArticleAsync_WhenDownloadSizeExceedsLimit_DoesNotCallStorage()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var reference = new MediaReference("https://cdn.example.com/huge.jpg", MediaKind.Image, "image/jpeg");
        var oversizedBytes = _options.Value.MaxFileSizeBytes + 1;
        var downloadStream = new MemoryStream(new byte[1024]);

        _httpDownloaderMock
            .Setup(d => d.DownloadAsync(reference, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaDownloadResult(downloadStream, "image/jpeg", oversizedBytes));

        // Act
        await _sut.IngestForArticleAsync(articleId, [reference], CancellationToken.None);

        // Assert
        _storageMock.Verify(
            s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P1 — ExistsByArticleAndUrlAsync returns true → URL skipped, storage not called
    // ------------------------------------------------------------------

    [Test]
    public async Task IngestForArticleAsync_WhenUrlAlreadyExistsForArticle_SkipsUpload()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var reference = new MediaReference("https://cdn.example.com/existing.jpg", MediaKind.Image, "image/jpeg");

        _repositoryMock
            .Setup(r => r.ExistsByArticleAndUrlAsync(articleId, reference.Url, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.IngestForArticleAsync(articleId, [reference], CancellationToken.None);

        // Assert
        _storageMock.Verify(
            s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            r => r.AddAsync(It.IsAny<MediaFile>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P1 — IMediaStorage.UploadAsync throws → caught, method does not throw
    // ------------------------------------------------------------------

    [Test]
    public async Task IngestForArticleAsync_WhenStorageUploadThrows_DoesNotPropagate()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var reference = new MediaReference("https://cdn.example.com/photo.jpg", MediaKind.Image, "image/jpeg");
        var downloadStream = new MemoryStream("some-bytes"u8.ToArray());

        _httpDownloaderMock
            .Setup(d => d.DownloadAsync(reference, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaDownloadResult(downloadStream, "image/jpeg", downloadStream.Length));

        _storageMock
            .Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("R2 upload failed"));

        // Act
        var act = async () => await _sut.IngestForArticleAsync(articleId, [reference], CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------
    // P1 — No downloader registered for the source kind → logs warning, storage not called
    // ------------------------------------------------------------------

    [Test]
    public async Task IngestForArticleAsync_WhenNoDownloaderForSourceKind_SkipsUploadAndDoesNotThrow()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        // Telegram reference but no Telegram downloader registered
        var reference = new MediaReference(
            "https://t.me/channel/1#media-0",
            MediaKind.Image,
            "image/jpeg",
            MediaSourceKind.Telegram,
            "123:456:1:0");

        // Act
        var act = async () => await _sut.IngestForArticleAsync(articleId, [reference], CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        _storageMock.Verify(
            s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P1 — Dispatches to correct downloader by SourceKind
    // ------------------------------------------------------------------

    [Test]
    public async Task IngestForArticleAsync_DispatchesToDownloaderWhoseKindMatchesSourceKind()
    {
        // Arrange
        var articleId = Guid.NewGuid();

        var telegramDownloaderMock = new Mock<IMediaContentDownloader>();
        telegramDownloaderMock.Setup(d => d.Kind).Returns(MediaSourceKind.Telegram);

        var sut = new MediaIngestionService(
            _storageMock.Object,
            _repositoryMock.Object,
            [_httpDownloaderMock.Object, telegramDownloaderMock.Object],
            _options,
            NullLogger<MediaIngestionService>.Instance);

        var httpReference = new MediaReference("https://cdn.example.com/image.jpg", MediaKind.Image, "image/jpeg");
        var telegramReference = new MediaReference(
            "https://t.me/channel/1#media-0",
            MediaKind.Image,
            "image/jpeg",
            MediaSourceKind.Telegram,
            "123:456:1:0");

        var httpStream = new MemoryStream("http-bytes"u8.ToArray());
        var telegramStream = new MemoryStream("tg-bytes"u8.ToArray());

        _httpDownloaderMock
            .Setup(d => d.DownloadAsync(httpReference, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaDownloadResult(httpStream, "image/jpeg", httpStream.Length));

        telegramDownloaderMock
            .Setup(d => d.DownloadAsync(telegramReference, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaDownloadResult(telegramStream, "image/jpeg", telegramStream.Length));

        // Act
        await sut.IngestForArticleAsync(articleId, [httpReference, telegramReference], CancellationToken.None);

        // Assert
        _httpDownloaderMock.Verify(d => d.DownloadAsync(httpReference, It.IsAny<CancellationToken>()), Times.Once);
        telegramDownloaderMock.Verify(d => d.DownloadAsync(telegramReference, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // P2 — Duplicate URLs in a single batch → deduplicated, storage called once
    // ------------------------------------------------------------------

    [Test]
    public async Task IngestForArticleAsync_WhenBatchContainsDuplicateUrls_ProcessesUrlOnlyOnce()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        const string url = "https://cdn.example.com/dup.jpg";
        var references = new List<MediaReference>
        {
            new(url, MediaKind.Image, "image/jpeg"),
            new(url, MediaKind.Image, "image/jpeg"),
        };

        var downloadStream = new MemoryStream("img-bytes"u8.ToArray());
        _httpDownloaderMock
            .Setup(d => d.DownloadAsync(It.IsAny<MediaReference>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaDownloadResult(downloadStream, "image/jpeg", downloadStream.Length));

        // Act
        await _sut.IngestForArticleAsync(articleId, references, CancellationToken.None);

        // Assert
        _storageMock.Verify(
            s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
