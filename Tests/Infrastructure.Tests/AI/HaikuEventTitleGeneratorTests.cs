using Core.Interfaces.AI;
using FluentAssertions;
using Infrastructure.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Infrastructure.Tests.AI;

/// <summary>
/// Tests for HaikuEventTitleGenerator.
///
/// The AnthropicClient is instantiated internally — there is no DI seam for it.
/// Unlike HaikuKeyFactsExtractor, this class has no private parsing helper suitable
/// for reflection testing. The testable boundary without real HTTP calls is:
///   1. The constructor accepts arbitrary strings and a logger without throwing.
///   2. OperationCanceledException is NOT swallowed — it propagates to the caller.
/// </summary>
[TestFixture]
public class HaikuEventTitleGeneratorTests
{
    // ------------------------------------------------------------------
    // P0 — Constructor can be created with arbitrary arguments without throwing
    // ------------------------------------------------------------------

    [Test]
    public void Constructor_WithArbitraryApiKeyAndModel_DoesNotThrow()
    {
        // Arrange & Act
        var act = () => new HaikuEventTitleGenerator(
            apiKey: "any-api-key-value",
            model: "claude-haiku-4-5-20251001",
            logger: NullLogger<HaikuEventTitleGenerator>.Instance);

        // Assert
        act.Should().NotThrow();
    }

    [Test]
    public void Constructor_WithEmptyStrings_DoesNotThrow()
    {
        // Arrange & Act
        var act = () => new HaikuEventTitleGenerator(
            apiKey: string.Empty,
            model: string.Empty,
            logger: NullLogger<HaikuEventTitleGenerator>.Instance);

        // Assert
        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------
    // P1 — OperationCanceledException propagates (is NOT swallowed)
    // ------------------------------------------------------------------

    [Test]
    public async Task GenerateTitleAsync_WhenCancellationTokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var sut = new HaikuEventTitleGenerator(
            apiKey: "dummy-key",
            model: "claude-haiku-4-5-20251001",
            logger: NullLogger<HaikuEventTitleGenerator>.Instance);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = async () => await sut.GenerateTitleAsync(
            eventSummary: "Some event summary text.",
            articleTitles: ["Article One", "Article Two"],
            cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
