using Core.Interfaces.AI;
using FluentAssertions;
using Infrastructure.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using System.Reflection;

namespace Infrastructure.Tests.AI;

/// <summary>
/// Tests for HaikuEventTitleGenerator.
///
/// The AnthropicClient is instantiated internally — there is no DI seam for it.
/// Unlike HaikuKeyFactsExtractor, this class has no private parsing helper suitable
/// for reflection testing. The testable boundaries without real HTTP calls are:
///   1. The constructor accepts arbitrary strings and a logger without throwing.
///   2. The injected systemPrompt is stored verbatim in the cached _systemPrompt
///      field (verified via reflection). Language substitution now happens in
///      PromptsOptions.ReadPrompt before the string reaches this constructor.
///   3. OperationCanceledException is NOT swallowed — it propagates to the caller.
/// </summary>
[TestFixture]
public class HaikuEventTitleGeneratorTests
{
	// Reflection target: private readonly string _systemPrompt
	private static readonly FieldInfo SystemPromptField =
		typeof(HaikuEventTitleGenerator)
			.GetField("_systemPrompt", BindingFlags.NonPublic | BindingFlags.Instance)!;

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
			systemPrompt: "You are a news headline writer.",
			logger: NullLogger<HaikuEventTitleGenerator>.Instance,
			aiRequestLogger: new Mock<IAiRequestLogger>().Object);

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
			systemPrompt: string.Empty,
			logger: NullLogger<HaikuEventTitleGenerator>.Instance,
			aiRequestLogger: new Mock<IAiRequestLogger>().Object);

		// Assert
		act.Should().NotThrow();
	}

	// ------------------------------------------------------------------
	// P0 — the passed-in systemPrompt is stored verbatim in _systemPrompt.
	// Note: language substitution ({OUTPUT_LANGUAGE} → language name) now
	// happens in PromptsOptions.ReadPrompt before the string reaches this
	// constructor. This test is a pass-through smoke-test confirming the
	// class does not alter the prompt it receives.
	// ------------------------------------------------------------------

	[Test]
	public void Constructor_WhenSystemPromptContainsEnglish_StoresPromptWithEnglishAndWithoutUkrainian()
	{
		// Arrange — simulate what PromptsOptions.HaikuEventTitle returns after substitution
		const string systemPrompt = "You are a news headline writer. Generate a concise English-language news headline.";
		const string forbiddenLanguage = "Ukrainian";

		// Act
		var sut = new HaikuEventTitleGenerator(
			apiKey: "dummy-key",
			model: "claude-haiku-4-5-20251001",
			systemPrompt: systemPrompt,
			logger: NullLogger<HaikuEventTitleGenerator>.Instance,
			aiRequestLogger: new Mock<IAiRequestLogger>().Object);

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
	public async Task GenerateTitleAsync_WhenCancellationTokenAlreadyCancelled_ThrowsOperationCanceledException()
	{
		// Arrange
		var sut = new HaikuEventTitleGenerator(
			apiKey: "dummy-key",
			model: "claude-haiku-4-5-20251001",
			systemPrompt: "You are a news headline writer.",
			logger: NullLogger<HaikuEventTitleGenerator>.Instance,
			aiRequestLogger: new Mock<IAiRequestLogger>().Object);

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
