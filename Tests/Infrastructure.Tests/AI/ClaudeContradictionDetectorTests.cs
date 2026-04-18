using Core.DomainModels;
using Core.DomainModels.AI;
using FluentAssertions;
using Infrastructure.AI;
using NUnit.Framework;
using System.Reflection;

namespace Infrastructure.Tests.AI;

/// <summary>
/// Tests for ClaudeContradictionDetector parsing logic and prompt construction.
///
/// The AnthropicClient is instantiated internally — there is no DI seam for it.
/// ParseResult (private static) is the business-critical method: it strips
/// markdown fences and deserializes the JSON array the model returns.
/// All ParseResult tests exercise it via reflection; no live HTTP calls are made.
///
/// BuildUserPrompt (private static) constructs the user prompt sent to the model.
/// It must include article titles, key facts, and event updates from the target event
/// so the model has complete context for contradiction detection.
/// </summary>
[TestFixture]
public class ClaudeContradictionDetectorTests
{
	// Reflection target: private static List<ContradictionInput> ParseResult(string json)
	private static readonly MethodInfo ParseResultMethod =
		typeof(ClaudeContradictionDetector)
			.GetMethod("ParseResult", BindingFlags.NonPublic | BindingFlags.Static)!;

	// Reflection target: private static string BuildUserPrompt(Article article, Event targetEvent)
	private static readonly MethodInfo BuildUserPromptMethod =
		typeof(ClaudeContradictionDetector)
			.GetMethod("BuildUserPrompt", BindingFlags.NonPublic | BindingFlags.Static)!;

	private static List<ContradictionInput> InvokeParseResult(string json)
	{
		var result = ParseResultMethod.Invoke(null, [json]);
		return (List<ContradictionInput>)result!;
	}

	private static string InvokeBuildUserPrompt(Article article, Event targetEvent)
	{
		var result = BuildUserPromptMethod.Invoke(null, [article, targetEvent]);
		return (string)result!;
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
                "article_ids": ["{{articleId}}"],
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
                "article_ids": ["{{articleId}}"],
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

	// ------------------------------------------------------------------
	// P0 — BuildUserPrompt includes article titles and key facts from
	//       targetEvent.Articles when Articles is non-empty
	// ------------------------------------------------------------------

	[Test]
	public void BuildUserPrompt_WhenTargetEventHasArticlesWithKeyFacts_IncludesEachArticleTitleAndKeyFacts()
	{
		// Arrange
		var article1Id = Guid.NewGuid();
		var article2Id = Guid.NewGuid();

		var article1 = new Article
		{
			Id = article1Id,
			Title = "Rescue teams deployed to flood zone",
			KeyFacts = ["200 families displaced", "3 casualties confirmed"],
		};

		var article2 = new Article
		{
			Id = article2Id,
			Title = "Government declares state of emergency",
			KeyFacts = ["emergency declared in 4 provinces"],
		};

		var targetEvent = new Event
		{
			Id = Guid.NewGuid(),
			Title = "Flood Disaster",
			Summary = "A severe flood has struck the region.",
			Status = EventStatus.Active,
			FirstSeenAt = DateTimeOffset.UtcNow,
			LastUpdatedAt = DateTimeOffset.UtcNow,
			Articles = [article1, article2],
			EventUpdates = [],
		};

		var incomingArticle = new Article
		{
			Id = Guid.NewGuid(),
			Title = "New article about floods",
			Summary = "Another report on the flood.",
			KeyFacts = ["5 casualties now confirmed"],
		};

		// Act
		var prompt = InvokeBuildUserPrompt(incomingArticle, targetEvent);

		// Assert
		prompt.Should().Contain("Rescue teams deployed to flood zone",
			"article title must appear in the prompt");
		prompt.Should().Contain("200 families displaced",
			"first key fact of the first article must appear");
		prompt.Should().Contain("3 casualties confirmed",
			"second key fact of the first article must appear");
		prompt.Should().Contain("Government declares state of emergency",
			"second article title must appear");
		prompt.Should().Contain("emergency declared in 4 provinces",
			"key fact of the second article must appear");
	}

	// ------------------------------------------------------------------
	// P1 — BuildUserPrompt emits "No articles recorded." when
	//       targetEvent.Articles is empty
	// ------------------------------------------------------------------

	[Test]
	public void BuildUserPrompt_WhenTargetEventHasNoArticles_ContainsNoArticlesRecordedSentinel()
	{
		// Arrange
		var targetEvent = new Event
		{
			Id = Guid.NewGuid(),
			Title = "Event Without Articles",
			Summary = "An event that has no articles yet.",
			Status = EventStatus.Active,
			FirstSeenAt = DateTimeOffset.UtcNow,
			LastUpdatedAt = DateTimeOffset.UtcNow,
			Articles = [],
			EventUpdates = [],
		};

		var incomingArticle = new Article
		{
			Id = Guid.NewGuid(),
			Title = "First article for this event",
			Summary = "Breaking news.",
			KeyFacts = ["initial report"],
		};

		// Act
		var prompt = InvokeBuildUserPrompt(incomingArticle, targetEvent);

		// Assert
		prompt.Should().Contain("No articles recorded.",
			"the prompt must contain the sentinel string when targetEvent.Articles is empty");
	}
}
