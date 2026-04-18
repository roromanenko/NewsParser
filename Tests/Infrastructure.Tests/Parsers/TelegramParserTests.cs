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
	// Album tests — P0: album of 3 photos with caption on first → 1 Article, 3 MediaReferences
	// ------------------------------------------------------------------

	[Test]
	public async Task ParseAsync_WhenAlbumHas3PhotosWithCaptionOnFirst_Returns1ArticleWith3MediaReferences()
	{
		// Arrange
		const long albumGroupId = 12345L;
		const int firstId = 100;
		const int secondId = 101;
		const int thirdId = 102;
		const string captionText = "Album caption on first";

		var messages = new List<TelegramChannelMessage>
		{
			new(BuildMessage(firstId, captionText, new MessageMediaPhoto { photo = new Photo() }, groupedId: albumGroupId), ChannelId, AccessHash),
			new(BuildMessage(secondId, "", new MessageMediaPhoto { photo = new Photo() }, groupedId: albumGroupId), ChannelId, AccessHash),
			new(BuildMessage(thirdId, "", new MessageMediaPhoto { photo = new Photo() }, groupedId: albumGroupId), ChannelId, AccessHash),
		};

		var source = BuildSource($"https://t.me/{ChannelUsername}");
		_channelReaderMock
			.Setup(r => r.GetChannelMessagesAsync(ChannelUsername, source.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(messages);

		// Act
		var articles = await _sut.ParseAsync(source, CancellationToken.None);

		// Assert
		articles.Should().HaveCount(1);
		var article = articles[0];
		article.ExternalId.Should().Be(firstId.ToString());
		article.OriginalContent.Should().Be(captionText);
		article.MediaReferences.Should().HaveCount(3);
		article.MediaReferences[0].Url.Should().EndWith("#media-0");
		article.MediaReferences[1].Url.Should().EndWith("#media-1");
		article.MediaReferences[2].Url.Should().EndWith("#media-2");
	}

	// ------------------------------------------------------------------
	// Album tests — P0: album with no captions → uses first message by ID as primary
	// ------------------------------------------------------------------

	[Test]
	public async Task ParseAsync_WhenAlbumHasNoCaptions_UsesFirstMessageByIdAsPrimary()
	{
		// Arrange
		const long albumGroupId = 22222L;
		const int lowerId = 200;
		const int higherId = 201;

		var messages = new List<TelegramChannelMessage>
		{
			new(BuildMessage(higherId, "", new MessageMediaPhoto { photo = new Photo() }, groupedId: albumGroupId), ChannelId, AccessHash),
			new(BuildMessage(lowerId, "", new MessageMediaPhoto { photo = new Photo() }, groupedId: albumGroupId), ChannelId, AccessHash),
		};

		var source = BuildSource($"https://t.me/{ChannelUsername}");
		_channelReaderMock
			.Setup(r => r.GetChannelMessagesAsync(ChannelUsername, source.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(messages);

		// Act
		var articles = await _sut.ParseAsync(source, CancellationToken.None);

		// Assert
		articles.Should().HaveCount(1);
		articles[0].ExternalId.Should().Be(lowerId.ToString());
		articles[0].Title.Should().Be(string.Empty);
		articles[0].MediaReferences.Should().HaveCount(2);
	}

	// ------------------------------------------------------------------
	// Album tests — P0: album with mixed photo and video → correct MediaKind per item
	// ------------------------------------------------------------------

	[Test]
	public async Task ParseAsync_WhenAlbumHasMixedPhotoAndVideo_MediaReferenceKindsAreCorrect()
	{
		// Arrange
		const long albumGroupId = 33333L;
		const int photoId = 300;
		const int videoId = 301;

		var videoDoc = new Document { mime_type = "video/mp4", size = 4096L };
		var messages = new List<TelegramChannelMessage>
		{
			new(BuildMessage(photoId, "Mixed album", new MessageMediaPhoto { photo = new Photo() }, groupedId: albumGroupId), ChannelId, AccessHash),
			new(BuildMessage(videoId, "", new MessageMediaDocument { document = videoDoc }, groupedId: albumGroupId), ChannelId, AccessHash),
		};

		var source = BuildSource($"https://t.me/{ChannelUsername}");
		_channelReaderMock
			.Setup(r => r.GetChannelMessagesAsync(ChannelUsername, source.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(messages);

		// Act
		var articles = await _sut.ParseAsync(source, CancellationToken.None);

		// Assert
		articles.Should().HaveCount(1);
		articles[0].MediaReferences.Should().HaveCount(2);
		articles[0].MediaReferences[0].Kind.Should().Be(MediaKind.Image);
		articles[0].MediaReferences[1].Kind.Should().Be(MediaKind.Video);
	}

	// ------------------------------------------------------------------
	// Regression — ungrouped message with media → 1 Article, 1 MediaReference
	// ------------------------------------------------------------------

	[Test]
	public async Task ParseAsync_WhenUngroupedMessageHasMedia_Returns1ArticleWith1MediaReference()
	{
		// Arrange
		const int messageId = 400;
		var message = BuildMessage(messageId, "Ungrouped with photo", new MessageMediaPhoto { photo = new Photo() }, groupedId: 0);
		var source = BuildSource($"https://t.me/{ChannelUsername}");

		_channelReaderMock
			.Setup(r => r.GetChannelMessagesAsync(ChannelUsername, source.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync([new TelegramChannelMessage(message, ChannelId, AccessHash)]);

		// Act
		var articles = await _sut.ParseAsync(source, CancellationToken.None);

		// Assert
		articles.Should().HaveCount(1);
		articles[0].MediaReferences.Should().HaveCount(1);
		articles[0].MediaReferences[0].Url.Should().EndWith("#media-0");
	}

	// ------------------------------------------------------------------
	// Regression — ungrouped text-only message → 1 Article, 0 MediaReferences
	// ------------------------------------------------------------------

	[Test]
	public async Task ParseAsync_WhenUngroupedTextOnlyMessage_Returns1ArticleWithNoMediaReferences()
	{
		// Arrange
		const int messageId = 500;
		var message = BuildMessage(messageId, "Just text, no media at all", media: null, groupedId: 0);
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
	// Album tests — caption on later message → that message is primary
	// ------------------------------------------------------------------

	[Test]
	public async Task ParseAsync_WhenAlbumCaptionIsOnLaterMessage_ThatMessageIsPrimary()
	{
		// Arrange
		const long albumGroupId = 44444L;
		const int lowerId = 600;
		const int higherId = 601;
		const string captionText = "Caption on second message";

		var messages = new List<TelegramChannelMessage>
		{
			new(BuildMessage(lowerId, "", new MessageMediaPhoto { photo = new Photo() }, groupedId: albumGroupId), ChannelId, AccessHash),
			new(BuildMessage(higherId, captionText, new MessageMediaPhoto { photo = new Photo() }, groupedId: albumGroupId), ChannelId, AccessHash),
		};

		var source = BuildSource($"https://t.me/{ChannelUsername}");
		_channelReaderMock
			.Setup(r => r.GetChannelMessagesAsync(ChannelUsername, source.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(messages);

		// Act
		var articles = await _sut.ParseAsync(source, CancellationToken.None);

		// Assert
		articles.Should().HaveCount(1);
		articles[0].ExternalId.Should().Be(higherId.ToString());
		articles[0].OriginalContent.Should().Be(captionText);
		articles[0].MediaReferences.Should().HaveCount(2);
	}

	// ------------------------------------------------------------------
	// Helpers
	// ------------------------------------------------------------------

	private static Message BuildMessage(int id, string text, MessageMedia? media, long groupedId = 0)
	{
		return new Message
		{
			id = id,
			message = text,
			date = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
			media = media,
			grouped_id = groupedId,
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
