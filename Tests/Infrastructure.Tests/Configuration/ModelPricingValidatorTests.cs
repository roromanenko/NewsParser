using FluentAssertions;
using Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Configuration;

/// <summary>
/// Tests for <see cref="ModelPricingValidator"/>.
///
/// The validator iterates the configured Anthropic and Gemini model ids on
/// <see cref="AiOptions"/> and emits a single <see cref="LogLevel.Error"/> per
/// model id that has no entry in <see cref="ModelPricingOptions"/>. Null/empty
/// model ids are skipped. The method must never throw, even with default
/// (empty) options, so a startup misconfiguration cannot crash the host.
/// </summary>
[TestFixture]
public class ModelPricingValidatorTests
{
    private const string AnthropicAnalyzerId = "claude-haiku-4-5-20251001";
    private const string AnthropicGeneratorId = "claude-sonnet-4-5";
    private const string AnthropicContentGeneratorId = "claude-sonnet-4-5";
    private const string AnthropicClassifierId = "claude-haiku-4-5-20251001";
    private const string AnthropicContradictionDetectorId = "claude-haiku-4-5-20251001";
    private const string AnthropicSummaryUpdaterId = "claude-haiku-4-5-20251001";
    private const string AnthropicKeyFactsExtractorId = "claude-haiku-4-5-20251001";
    private const string AnthropicTitleGeneratorId = "claude-haiku-4-5-20251001";
    private const string GeminiAnalyzerId = "gemini-2.0-flash";
    private const string GeminiEmbeddingId = "gemini-embedding-001";

    private Mock<ILogger> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger>();
        _loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
    }

    // ------------------------------------------------------------------
    // P0 — happy path: every configured model id has a pricing entry.
    // ------------------------------------------------------------------

    [Test]
    public void ValidateOrLog_WhenAllConfiguredModelIdsHavePricing_DoesNotLogAnyError()
    {
        // Arrange
        var ai = BuildAiOptionsWithAllDefaults();
        var pricing = BuildPricingCovering(
            anthropicIds: new[]
            {
                AnthropicAnalyzerId,
                AnthropicGeneratorId,
                AnthropicContentGeneratorId,
                AnthropicClassifierId,
                AnthropicContradictionDetectorId,
                AnthropicSummaryUpdaterId,
                AnthropicKeyFactsExtractorId,
                AnthropicTitleGeneratorId,
            },
            geminiIds: new[] { GeminiAnalyzerId, GeminiEmbeddingId });

        // Act
        ModelPricingValidator.ValidateOrLog(ai, pricing, _loggerMock.Object);

        // Assert
        VerifyErrorLogCount(Times.Never());
    }

    // ------------------------------------------------------------------
    // P1 — exactly one Anthropic model id has no pricing entry: one error
    //       carrying the "Anthropic" provider tag and the missing id.
    // ------------------------------------------------------------------

    [Test]
    public void ValidateOrLog_WhenOneAnthropicModelIdIsMissingFromPricing_LogsErrorOnceForThatId()
    {
        // Arrange
        const string missingId = "claude-not-in-table";
        var ai = BuildAiOptionsWithAllDefaults();
        ai.Anthropic.AnalyzerModel = missingId;

        var pricing = BuildPricingCovering(
            anthropicIds: new[]
            {
                // Note: missingId is intentionally absent.
                AnthropicGeneratorId,
                AnthropicContentGeneratorId,
                AnthropicClassifierId,
                AnthropicContradictionDetectorId,
                AnthropicSummaryUpdaterId,
                AnthropicKeyFactsExtractorId,
                AnthropicTitleGeneratorId,
            },
            geminiIds: new[] { GeminiAnalyzerId, GeminiEmbeddingId });

        // Act
        ModelPricingValidator.ValidateOrLog(ai, pricing, _loggerMock.Object);

        // Assert
        VerifyErrorLoggedFor("Anthropic", missingId, Times.Once());
        VerifyErrorLogCount(Times.Once());
    }

    // ------------------------------------------------------------------
    // P1 — exactly one Gemini model id has no pricing entry.
    // ------------------------------------------------------------------

    [Test]
    public void ValidateOrLog_WhenOneGeminiModelIdIsMissingFromPricing_LogsErrorOnceForThatId()
    {
        // Arrange
        const string missingId = "gemini-not-in-table";
        var ai = BuildAiOptionsWithAllDefaults();
        ai.Gemini.AnalyzerModel = missingId;

        var pricing = BuildPricingCovering(
            anthropicIds: new[]
            {
                AnthropicAnalyzerId,
                AnthropicGeneratorId,
                AnthropicContentGeneratorId,
                AnthropicClassifierId,
                AnthropicContradictionDetectorId,
                AnthropicSummaryUpdaterId,
                AnthropicKeyFactsExtractorId,
                AnthropicTitleGeneratorId,
            },
            geminiIds: new[] { GeminiEmbeddingId }); // missingId intentionally absent

        // Act
        ModelPricingValidator.ValidateOrLog(ai, pricing, _loggerMock.Object);

        // Assert
        VerifyErrorLoggedFor("Gemini", missingId, Times.Once());
        VerifyErrorLogCount(Times.Once());
    }

    // ------------------------------------------------------------------
    // P1 — multiple missing ids across both providers: one error per id,
    //       each carrying its own provider tag.
    // ------------------------------------------------------------------

    [Test]
    public void ValidateOrLog_WhenMultipleModelIdsAreMissingAcrossProviders_LogsErrorOncePerMissingId()
    {
        // Arrange
        const string missingAnthropic1 = "claude-missing-A";
        const string missingAnthropic2 = "claude-missing-B";
        const string missingGemini = "gemini-missing";

        var ai = BuildAiOptionsWithAllDefaults();
        ai.Anthropic.AnalyzerModel = missingAnthropic1;
        ai.Anthropic.GeneratorModel = missingAnthropic2;
        ai.Gemini.EmbeddingModel = missingGemini;

        // Pricing covers only the remaining (still-default) Anthropic ids and
        // the Gemini analyzer model — none of the three substituted ids.
        var pricing = BuildPricingCovering(
            anthropicIds: new[]
            {
                AnthropicContentGeneratorId,
                AnthropicClassifierId,
                AnthropicContradictionDetectorId,
                AnthropicSummaryUpdaterId,
                AnthropicKeyFactsExtractorId,
                AnthropicTitleGeneratorId,
            },
            geminiIds: new[] { GeminiAnalyzerId });

        // Act
        ModelPricingValidator.ValidateOrLog(ai, pricing, _loggerMock.Object);

        // Assert
        VerifyErrorLoggedFor("Anthropic", missingAnthropic1, Times.Once());
        VerifyErrorLoggedFor("Anthropic", missingAnthropic2, Times.Once());
        VerifyErrorLoggedFor("Gemini", missingGemini, Times.Once());
        VerifyErrorLogCount(Times.Exactly(3));
    }

    // ------------------------------------------------------------------
    // P2 — null/empty model ids are silently skipped (they cannot collide
    //       with any pricing dictionary key and would only generate noise).
    // ------------------------------------------------------------------

    [Test]
    public void ValidateOrLog_WhenAnthropicModelIdsAreNullOrEmpty_SkipsThemAndDoesNotLog()
    {
        // Arrange
        var ai = new AiOptions
        {
            Anthropic = new AnthropicOptions
            {
                AnalyzerModel = string.Empty,
                GeneratorModel = null!,
                ContentGeneratorModel = string.Empty,
                ClassifierModel = string.Empty,
                ContradictionDetectorModel = string.Empty,
                SummaryUpdaterModel = string.Empty,
                KeyFactsExtractorModel = string.Empty,
                TitleGeneratorModel = string.Empty,
            },
            Gemini = new GeminiOptions
            {
                AnalyzerModel = string.Empty,
                EmbeddingModel = string.Empty,
            },
        };

        // Empty pricing dictionaries; if any null/empty id slipped through it
        // would not be found and would trigger a LogError.
        var pricing = new ModelPricingOptions();

        // Act
        ModelPricingValidator.ValidateOrLog(ai, pricing, _loggerMock.Object);

        // Assert
        VerifyErrorLogCount(Times.Never());
    }

    // ------------------------------------------------------------------
    // P2 — invoked with brand-new AiOptions (defaults) and an EMPTY pricing
    //       table: must complete without throwing. The defaults populate
    //       both providers' model ids so we expect errors, but the test
    //       asserts only that no exception escapes — this guards against
    //       NullReferenceException regressions in the iteration code.
    // ------------------------------------------------------------------

    [Test]
    public void ValidateOrLog_WhenInvokedWithDefaultOptionsAndEmptyPricing_DoesNotThrow()
    {
        // Arrange
        var ai = new AiOptions();
        var pricing = new ModelPricingOptions();

        // Act
        var act = () => ModelPricingValidator.ValidateOrLog(ai, pricing, _loggerMock.Object);

        // Assert
        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static AiOptions BuildAiOptionsWithAllDefaults() => new()
    {
        Anthropic = new AnthropicOptions
        {
            AnalyzerModel = AnthropicAnalyzerId,
            GeneratorModel = AnthropicGeneratorId,
            ContentGeneratorModel = AnthropicContentGeneratorId,
            ClassifierModel = AnthropicClassifierId,
            ContradictionDetectorModel = AnthropicContradictionDetectorId,
            SummaryUpdaterModel = AnthropicSummaryUpdaterId,
            KeyFactsExtractorModel = AnthropicKeyFactsExtractorId,
            TitleGeneratorModel = AnthropicTitleGeneratorId,
        },
        Gemini = new GeminiOptions
        {
            AnalyzerModel = GeminiAnalyzerId,
            EmbeddingModel = GeminiEmbeddingId,
        },
    };

    private static ModelPricingOptions BuildPricingCovering(
        IEnumerable<string> anthropicIds,
        IEnumerable<string> geminiIds)
    {
        var price = new ModelPrice { InputPerMillion = 1.0m, OutputPerMillion = 1.0m };
        return new ModelPricingOptions
        {
            Anthropic = anthropicIds.Distinct().ToDictionary(id => id, _ => price),
            Gemini = geminiIds.Distinct().ToDictionary(id => id, _ => price),
        };
    }

    private void VerifyErrorLoggedFor(string expectedProvider, string expectedModel, Times times)
    {
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains(expectedProvider) &&
                    state.ToString()!.Contains(expectedModel)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times,
            $"missing pricing for {expectedProvider} model '{expectedModel}' must be surfaced via LogError");
    }

    private void VerifyErrorLogCount(Times times)
    {
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }
}
