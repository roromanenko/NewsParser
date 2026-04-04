using Core.DomainModels;
using FluentAssertions;
using Infrastructure.AI;
using NUnit.Framework;
using System.Reflection;

namespace Infrastructure.Tests.AI;

/// <summary>
/// Tests for HaikuKeyFactsExtractor.
///
/// The AnthropicClient is instantiated internally — there is no DI seam for it.
/// ParseFacts (private static) is the business-critical logic that can be tested
/// in isolation via reflection without making real HTTP calls.
/// The cancellation-propagation test passes an already-cancelled token; because
/// the client throws OperationCanceledException before any JSON parsing, the
/// extractor must NOT swallow it.
/// </summary>
[TestFixture]
public class HaikuKeyFactsExtractorTests
{
    // Reflection target: private static List<string> ParseFacts(string json)
    private static readonly MethodInfo ParseFactsMethod =
        typeof(HaikuKeyFactsExtractor)
            .GetMethod("ParseFacts", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static List<string> InvokeParseFacts(string json)
    {
        var result = ParseFactsMethod.Invoke(null, [json]);
        return (List<string>)result!;
    }

    // ------------------------------------------------------------------
    // P0 — valid JSON response returns 3–7 fact strings
    // ------------------------------------------------------------------

    [Test]
    public void ParseFacts_WhenValidJsonWithThreeFacts_ReturnsAllThreeFacts()
    {
        // Arrange
        const string json = """{"facts": ["Fact one.", "Fact two.", "Fact three."]}""";

        // Act
        var result = InvokeParseFacts(json);

        // Assert
        result.Should().HaveCount(3);
        result.Should().ContainInOrder("Fact one.", "Fact two.", "Fact three.");
    }

    [Test]
    public void ParseFacts_WhenJsonWrappedInMarkdownCodeFence_ReturnsFactsSuccessfully()
    {
        // Arrange — Claude sometimes wraps JSON in ```json...```
        const string json = "```json\n{\"facts\": [\"Fact A.\", \"Fact B.\"]}\n```";

        // Act
        var result = InvokeParseFacts(json);

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainInOrder("Fact A.", "Fact B.");
    }

    // ------------------------------------------------------------------
    // P1 — malformed / empty responses return empty list without throwing
    // ------------------------------------------------------------------

    [Test]
    public void ParseFacts_WhenJsonIsMalformed_ReturnsEmptyList()
    {
        // Arrange
        const string json = "{ this is not valid JSON }";

        // Act
        var result = InvokeParseFacts(json);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void ParseFacts_WhenJsonLacksFactsProperty_ReturnsEmptyList()
    {
        // Arrange
        const string json = """{"summary": "Some text"}""";

        // Act
        var result = InvokeParseFacts(json);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void ParseFacts_WhenInputIsEmptyString_ReturnsEmptyList()
    {
        // Arrange
        const string json = "";

        // Act
        var result = InvokeParseFacts(json);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void ParseFacts_WhenFactsArrayContainsNullOrWhitespaceEntries_FiltersThemOut()
    {
        // Arrange — null entries come back as JSON null, whitespace as ""
        const string json = """{"facts": ["Valid fact.", "", "  ", "Another valid fact."]}""";

        // Act
        var result = InvokeParseFacts(json);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain("Valid fact.").And.Contain("Another valid fact.");
    }

    // ------------------------------------------------------------------
    // P1 — OperationCanceledException propagates (is NOT swallowed)
    // ------------------------------------------------------------------

    [Test]
    public async Task ExtractAsync_WhenCancellationTokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var sut = new HaikuKeyFactsExtractor(apiKey: "dummy-key", model: "claude-haiku-4-5");
        var article = CreateArticle();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = async () => await sut.ExtractAsync(article, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Article CreateArticle() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Article",
        Content = "Some article content for testing.",
        Summary = "A brief summary."
    };
}
