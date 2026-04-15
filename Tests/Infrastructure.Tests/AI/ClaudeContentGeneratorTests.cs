using Core.DomainModels;
using FluentAssertions;
using Infrastructure.AI;
using NUnit.Framework;
using System.Reflection;

namespace Infrastructure.Tests.AI;

/// <summary>
/// Tests for ClaudeContentGenerator prompt-building logic.
///
/// The AnthropicClient is instantiated internally; GenerateForPlatformAsync
/// makes a real HTTP call and cannot be tested without a live API key.
/// The private static methods BuildEventPrompt, BuildUpdatePrompt, and
/// BuildArticlesSection contain the business-critical logic specified in the
/// feature requirements. They are tested via reflection.
/// </summary>
[TestFixture]
public class ClaudeContentGeneratorTests
{
	private ClaudeContentGenerator _sut = null!;
	private PublishTarget _telegramTarget = null!;

	private static readonly MethodInfo BuildEventPromptMethod =
		typeof(ClaudeContentGenerator)
			.GetMethod("BuildEventPrompt", BindingFlags.NonPublic | BindingFlags.Static)!;

	private static readonly MethodInfo BuildUpdatePromptMethod =
		typeof(ClaudeContentGenerator)
			.GetMethod("BuildUpdatePrompt", BindingFlags.NonPublic | BindingFlags.Static)!;

	private static readonly MethodInfo BuildArticlesSectionMethod =
		typeof(ClaudeContentGenerator)
			.GetMethod("BuildArticlesSection", BindingFlags.NonPublic | BindingFlags.Static)!;

	[SetUp]
	public void SetUp()
	{
		_sut = new ClaudeContentGenerator(
			apiKey: "dummy-key",
			model: "claude-sonnet-4-5",
			basePrompts: new Dictionary<Platform, string>
			{
				{ Platform.Telegram, "You are a Telegram news bot." }
			});

		_telegramTarget = new PublishTarget
		{
			Id = Guid.NewGuid(),
			Name = "News Channel",
			Platform = Platform.Telegram,
			Identifier = "@news",
			IsActive = true
		};
	}

	// ------------------------------------------------------------------
	// P0 — Event prompt includes Event.Title and Event.Summary
	// ------------------------------------------------------------------

	[Test]
	public void BuildEventPrompt_WhenCalled_IncludesEventTitleAndSummary()
	{
		// Arrange
		var evt = CreateEvent(title: "Major Flood Warning", summary: "Floods are expected in three regions.");

		// Act
		var prompt = (string)BuildEventPromptMethod.Invoke(null, [evt, _telegramTarget])!;

		// Assert
		prompt.Should().Contain("Major Flood Warning");
		prompt.Should().Contain("Floods are expected in three regions.");
	}

	// ------------------------------------------------------------------
	// P0 — Event prompt includes each article's Summary and KeyFacts
	// ------------------------------------------------------------------

	[Test]
	public void BuildEventPrompt_WhenArticleHasKeyFacts_IncludesAllKeyFactsInPrompt()
	{
		// Arrange
		var article = CreateArticle(
			summary: "Emergency services are on standby.",
			keyFacts: ["Rivers overflowing.", "Three towns at risk.", "Evacuation orders issued."]);
		var evt = CreateEvent(articles: [article]);

		// Act
		var prompt = (string)BuildEventPromptMethod.Invoke(null, [evt, _telegramTarget])!;

		// Assert
		prompt.Should().Contain("Emergency services are on standby.");
		prompt.Should().Contain("Rivers overflowing.");
		prompt.Should().Contain("Three towns at risk.");
		prompt.Should().Contain("Evacuation orders issued.");
	}

	// ------------------------------------------------------------------
	// P0 — Update-context prompt uses the initiator article's data
	// ------------------------------------------------------------------

	[Test]
	public void BuildUpdatePrompt_WhenEventHasInitiatorArticle_UsesInitiatorCategory()
	{
		// Arrange
		var initiator = CreateArticle(role: ArticleRole.Initiator);
		initiator.Category = "Environment";
		var update = CreateArticle(role: ArticleRole.Update);
		update.Category = "Politics";
		var evt = CreateEvent(articles: [update, initiator]); // initiator is not first

		const string updateContext = "Water levels have risen by 2 metres.";

		// Act
		var prompt = (string)BuildUpdatePromptMethod.Invoke(null, [evt, _telegramTarget, updateContext])!;

		// Assert — the initiator's Category should appear, not the update's
		prompt.Should().Contain("Environment");
		prompt.Should().Contain("Water levels have risen by 2 metres.");
	}

	// ------------------------------------------------------------------
	// P2 — Event with empty articles list does not throw
	// ------------------------------------------------------------------

	[Test]
	public void BuildEventPrompt_WhenArticlesListIsEmpty_DoesNotThrowAndIncludesPlaceholder()
	{
		// Arrange
		var evt = CreateEvent(articles: []);

		// Act
		var act = () => BuildEventPromptMethod.Invoke(null, [evt, _telegramTarget]);

		// Assert
		act.Should().NotThrow();
		var prompt = (string)act()!;
		prompt.Should().Contain("(no articles)");
	}

	// ------------------------------------------------------------------
	// P2 — Articles section shows "(none)" when article has no key facts
	// ------------------------------------------------------------------

	[Test]
	public void BuildArticlesSection_WhenArticleHasNoKeyFacts_ShowsNonePlaceholder()
	{
		// Arrange
		var article = CreateArticle(summary: "Breaking news summary.", keyFacts: []);
		var articles = new List<Article> { article };

		// Act
		var section = (string)BuildArticlesSectionMethod.Invoke(null, [articles])!;

		// Assert
		section.Should().Contain("Breaking news summary.");
		section.Should().Contain("(none)");
	}

	// ------------------------------------------------------------------
	// P1 — GenerateForPlatformAsync throws when no prompt configured for platform
	// ------------------------------------------------------------------

	[Test]
	public async Task GenerateForPlatformAsync_WhenNoPlatformPromptConfigured_ThrowsInvalidOperationException()
	{
		// Arrange — sut has no Instagram prompt
		var instagramTarget = new PublishTarget
		{
			Id = Guid.NewGuid(),
			Name = "Instagram",
			Platform = Platform.Instagram,
			Identifier = "@insta",
			IsActive = true
		};
		var evt = CreateEvent();

		// Act
		var act = async () => await _sut.GenerateForPlatformAsync(evt, instagramTarget);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*Instagram*");
	}

	// ------------------------------------------------------------------
	// Helpers
	// ------------------------------------------------------------------

	private static Event CreateEvent(
		string title = "Test Event",
		string summary = "Event summary.",
		List<Article>? articles = null) => new()
		{
			Id = Guid.NewGuid(),
			Title = title,
			Summary = summary,
			Status = EventStatus.Active,
			FirstSeenAt = DateTimeOffset.UtcNow,
			LastUpdatedAt = DateTimeOffset.UtcNow,
			Articles = articles ?? []
		};

	private static Article CreateArticle(
		string summary = "Article summary.",
		List<string>? keyFacts = null,
		ArticleRole role = ArticleRole.Initiator)
	{
		var article = new Article
		{
			Id = Guid.NewGuid(),
			Title = "Article Title",
			Summary = summary,
			KeyFacts = keyFacts ?? [],
			Role = role
		};
		return article;
	}
}
