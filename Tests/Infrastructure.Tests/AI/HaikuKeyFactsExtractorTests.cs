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
/// The stored system prompt is verified via reflection on the private _systemPrompt
/// field — language substitution now happens in PromptsOptions.ReadPrompt before
/// the string reaches this constructor, so asserting the stored value is the
/// correct boundary without making a live AI call.
/// </summary>
[TestFixture]
public class HaikuKeyFactsExtractorTests
{
	// Reflection target: private static List<string> ParseFacts(string json)
	private static readonly MethodInfo ParseFactsMethod =
		typeof(HaikuKeyFactsExtractor)
			.GetMethod("ParseFacts", BindingFlags.NonPublic | BindingFlags.Static)!;

	// Reflection target: private readonly string _systemPrompt
	private static readonly FieldInfo SystemPromptField =
		typeof(HaikuKeyFactsExtractor)
			.GetField("_systemPrompt", BindingFlags.NonPublic | BindingFlags.Instance)!;

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
	// P0 — the passed-in systemPrompt is stored verbatim.
	// Note: language substitution ({OUTPUT_LANGUAGE} → language name) now
	// happens in PromptsOptions.ReadPrompt before the string reaches this
	// constructor. This test is a smoke-test confirming that _systemPrompt
	// holds exactly the value passed in, so that contract is not accidentally
	// broken by future refactoring.
	// ------------------------------------------------------------------

	[Test]
	public void Constructor_WhenSystemPromptContainsEnglish_StoresPromptWithEnglishAndWithoutUkrainian()
	{
		// Arrange — simulate what PromptsOptions.HaikuKeyFacts returns after substitution
		const string systemPrompt = "You are a factual extraction assistant. Respond in English, regardless of the input language.";
		const string forbiddenLanguage = "Ukrainian";

		// Act
		var sut = new HaikuKeyFactsExtractor(
			apiKey: "dummy-key",
			model: "claude-haiku-4-5",
			systemPrompt: systemPrompt);

		// Assert
		var stored = (string)SystemPromptField.GetValue(sut)!;
		stored.Should().Contain("English");
		stored.Should().NotContain(forbiddenLanguage);
	}

	// ------------------------------------------------------------------
	// P1 — OperationCanceledException propagates (is NOT swallowed)
	// ------------------------------------------------------------------

	[Test]
	[Explicit("Requires network; not deterministic in offline CI — see review 2026-04-15")]
	public async Task ExtractAsync_WhenCancellationTokenAlreadyCancelled_ThrowsOperationCanceledException()
	{
		// Arrange
		var sut = new HaikuKeyFactsExtractor(apiKey: "dummy-key", model: "claude-haiku-4-5", systemPrompt: "You are a factual extraction assistant.");
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
		OriginalContent = "Some article content for testing.",
		Summary = "A brief summary."
	};
}
