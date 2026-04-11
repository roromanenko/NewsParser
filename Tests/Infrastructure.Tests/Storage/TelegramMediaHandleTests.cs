using FluentAssertions;
using Infrastructure.Storage;
using NUnit.Framework;

namespace Infrastructure.Tests.Storage;

[TestFixture]
public class TelegramMediaHandleTests
{
    // ------------------------------------------------------------------
    // P0 — Encode produces the expected colon-delimited format
    // ------------------------------------------------------------------

    [Test]
    public void Encode_WithAllParameters_ReturnsColonDelimitedString()
    {
        // Arrange
        const long channelId = 1234567890L;
        const long accessHash = 9876543210L;
        const int messageId = 42;
        const int mediaIndex = 1;

        // Act
        var result = TelegramMediaHandle.Encode(channelId, accessHash, messageId, mediaIndex);

        // Assert
        result.Should().Be("1234567890:9876543210:42:1");
    }

    [Test]
    public void Encode_WithDefaultMediaIndex_UsesZero()
    {
        // Arrange
        const long channelId = 100L;
        const long accessHash = 200L;
        const int messageId = 7;

        // Act
        var result = TelegramMediaHandle.Encode(channelId, accessHash, messageId);

        // Assert
        result.Should().Be("100:200:7:0");
    }

    // ------------------------------------------------------------------
    // P0 — TryDecode on a handle produced by Encode round-trips all values
    // ------------------------------------------------------------------

    [Test]
    public void TryDecode_WhenHandleIsValid_ReturnsTrueAndCorrectValues()
    {
        // Arrange
        const long channelId = 1234567890L;
        const long accessHash = 9876543210L;
        const int messageId = 55;
        const int mediaIndex = 2;

        var handle = TelegramMediaHandle.Encode(channelId, accessHash, messageId, mediaIndex);

        // Act
        var success = TelegramMediaHandle.TryDecode(handle,
            out var decodedChannelId,
            out var decodedAccessHash,
            out var decodedMessageId,
            out var decodedMediaIndex);

        // Assert
        success.Should().BeTrue();
        decodedChannelId.Should().Be(channelId);
        decodedAccessHash.Should().Be(accessHash);
        decodedMessageId.Should().Be(messageId);
        decodedMediaIndex.Should().Be(mediaIndex);
    }

    // ------------------------------------------------------------------
    // P1 — TryDecode returns false and zeroes out-params for invalid inputs
    // ------------------------------------------------------------------

    [TestCase(null, Description = "null handle")]
    [TestCase("", Description = "empty handle")]
    public void TryDecode_WhenHandleIsNullOrEmpty_ReturnsFalseAndZeroOutParams(string? handle)
    {
        // Act
        var success = TelegramMediaHandle.TryDecode(handle,
            out var channelId,
            out var accessHash,
            out var messageId,
            out var mediaIndex);

        // Assert
        success.Should().BeFalse();
        channelId.Should().Be(0L);
        accessHash.Should().Be(0L);
        messageId.Should().Be(0);
        mediaIndex.Should().Be(0);
    }

    [TestCase("100:200:7", Description = "only 3 parts")]
    [TestCase("100:200:7:0:extra", Description = "5 parts")]
    public void TryDecode_WhenSegmentCountIsWrong_ReturnsFalse(string handle)
    {
        // Act
        var success = TelegramMediaHandle.TryDecode(handle,
            out _,
            out _,
            out _,
            out _);

        // Assert
        success.Should().BeFalse();
    }

    [TestCase("abc:200:7:0", Description = "channelId is not a number")]
    [TestCase("100:xyz:7:0", Description = "accessHash is not a number")]
    [TestCase("100:200:abc:0", Description = "messageId is not a number")]
    [TestCase("100:200:7:abc", Description = "mediaIndex is not a number")]
    public void TryDecode_WhenAnySegmentIsNotNumeric_ReturnsFalse(string handle)
    {
        // Act
        var success = TelegramMediaHandle.TryDecode(handle,
            out _,
            out _,
            out _,
            out _);

        // Assert
        success.Should().BeFalse();
    }
}
