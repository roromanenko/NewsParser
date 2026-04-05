using Core.DomainModels.AI;
using FluentAssertions;
using Infrastructure.AI;
using NUnit.Framework;
using System.Reflection;

namespace Infrastructure.Tests.AI;

/// <summary>
/// Tests for ClaudeContradictionDetector parsing logic.
///
/// The AnthropicClient is instantiated internally — there is no DI seam for it.
/// ParseResult (private static) is the business-critical method: it strips
/// markdown fences and deserializes the JSON array the model returns.
/// All three tests exercise it via reflection; no live HTTP calls are made.
/// </summary>
[TestFixture]
public class ClaudeContradictionDetectorTests
{
    // Reflection target: private static List<ContradictionInput> ParseResult(string json)
    private static readonly MethodInfo ParseResultMethod =
        typeof(ClaudeContradictionDetector)
            .GetMethod("ParseResult", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static List<ContradictionInput> InvokeParseResult(string json)
    {
        var result = ParseResultMethod.Invoke(null, [json]);
        return (List<ContradictionInput>)result!;
    }

    // ------------------------------------------------------------------
    // P0 — ParseResult returns one ContradictionInput with correct fields
    // ------------------------------------------------------------------

    [Test]
    public void ParseResult_WhenJsonHasOneEntry_ReturnsOneContradictionInputWithCorrectFields()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var json = $$"""
            [
              {
                "articleIds": ["{{articleId}}"],
                "description": "Article claims 5 casualties, but event summary says 2."
              }
            ]
            """;

        // Act
        var result = InvokeParseResult(json);

        // Assert
        result.Should().HaveCount(1);
        result[0].ArticleIds.Should().ContainSingle().Which.Should().Be(articleId);
        result[0].Description.Should().Be("Article claims 5 casualties, but event summary says 2.");
    }

    // ------------------------------------------------------------------
    // P0 — ParseResult returns empty list when model returns "[]"
    // ------------------------------------------------------------------

    [Test]
    public void ParseResult_WhenJsonIsEmptyArray_ReturnsEmptyList()
    {
        // Arrange
        var json = "[]";

        // Act
        var result = InvokeParseResult(json);

        // Assert
        result.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // P1 — ParseResult strips markdown fences before deserializing
    // ------------------------------------------------------------------

    [Test]
    public void ParseResult_WhenJsonIsWrappedInMarkdownFences_StripsThemAndDeserializesCorrectly()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var json = $$"""
            ```json
            [
              {
                "articleIds": ["{{articleId}}"],
                "description": "Contradicting death toll figures."
              }
            ]
            ```
            """;

        // Act
        var result = InvokeParseResult(json);

        // Assert
        result.Should().HaveCount(1);
        result[0].ArticleIds.Should().ContainSingle().Which.Should().Be(articleId);
        result[0].Description.Should().Be("Contradicting death toll figures.");
    }
}
