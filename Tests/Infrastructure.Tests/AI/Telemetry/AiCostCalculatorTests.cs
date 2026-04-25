using Core.DomainModels.AI;
using FluentAssertions;
using Infrastructure.AI.Telemetry;
using Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.AI.Telemetry;

/// <summary>
/// Tests for <see cref="AiCostCalculator"/>.
///
/// The calculator uses decimal arithmetic to compute USD cost from token usage
/// and model pricing. Gemini uses straight input+output; Anthropic additionally
/// accounts for prompt-cache creation and cache-read tokens using the
/// configured multipliers (1.25 for write, 0.1 for read — ADR D2 formula).
///
/// When the model or provider is unknown the calculator must return 0m without
/// throwing and emit a single <c>LogWarning</c> so operators can notice missing
/// pricing entries.
/// </summary>
[TestFixture]
public class AiCostCalculatorTests
{
    // Fixed pricing chosen so that per-million math produces clean numbers:
    //   Anthropic input = 1.00 USD/M, output = 5.00 USD/M
    //   Gemini    input = 0.10 USD/M, output = 0.40 USD/M
    // Cache write multiplier = 1.25; cache read multiplier = 0.1 (ADR defaults).
    private const string AnthropicProvider = "Anthropic";
    private const string GeminiProvider = "Gemini";
    private const string AnthropicModel = "claude-haiku-4-5-20251001";
    private const string GeminiModel = "gemini-2.0-flash";

    private Mock<IOptions<ModelPricingOptions>> _optionsMock = null!;
    private Mock<ILogger<AiCostCalculator>> _loggerMock = null!;
    private AiCostCalculator _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var pricing = new ModelPricingOptions
        {
            AnthropicCacheWriteMultiplier = 1.25m,
            AnthropicCacheReadMultiplier = 0.1m,
            Anthropic = new Dictionary<string, ModelPrice>
            {
                [AnthropicModel] = new() { InputPerMillion = 1.00m, OutputPerMillion = 5.00m }
            },
            Gemini = new Dictionary<string, ModelPrice>
            {
                [GeminiModel] = new() { InputPerMillion = 0.10m, OutputPerMillion = 0.40m }
            }
        };

        _optionsMock = new Mock<IOptions<ModelPricingOptions>>();
        _optionsMock.SetupGet(o => o.Value).Returns(pricing);

        _loggerMock = new Mock<ILogger<AiCostCalculator>>();
        _loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        _sut = new AiCostCalculator(_optionsMock.Object, _loggerMock.Object);
    }

    // ------------------------------------------------------------------
    // P0 — formula variants (parameterized).
    //
    // Gemini:    cost = (input * inPrice + output * outPrice) / 1_000_000
    // Anthropic: cost = (input * inPrice
    //                  + output * outPrice
    //                  + cacheCreation * inPrice * writeMult
    //                  + cacheRead     * inPrice * readMult) / 1_000_000
    //
    // Expected values are concrete literals (never recomputed in the test).
    // ------------------------------------------------------------------

    // Gemini straight: 1_000_000 * 0.10 + 500_000 * 0.40 = 100_000 + 200_000
    // divided by 1_000_000 = 0.30
    [TestCase(GeminiProvider,    GeminiModel,    1_000_000, 500_000, 0, 0, "0.30",
        TestName = "Calculate_WhenGeminiStraightCost_ReturnsInputTimesInPriceAndOutputTimesOutPrice")]

    // Anthropic no-cache: 1_000_000 * 1.00 + 500_000 * 5.00 = 1_000_000 + 2_500_000
    // divided by 1_000_000 = 3.50
    [TestCase(AnthropicProvider, AnthropicModel, 1_000_000, 500_000, 0, 0, "3.50",
        TestName = "Calculate_WhenAnthropicNoCache_ReturnsStraightInputPlusOutputCost")]

    // Anthropic cache-write only: 400_000 * 1.00 * 1.25 = 500_000 → / 1_000_000 = 0.50
    [TestCase(AnthropicProvider, AnthropicModel, 0, 0, 400_000, 0, "0.50",
        TestName = "Calculate_WhenAnthropicCacheWriteOnly_AppliesWriteMultiplier")]

    // Anthropic cache-read only: 1_000_000 * 1.00 * 0.1 = 100_000 → / 1_000_000 = 0.10
    [TestCase(AnthropicProvider, AnthropicModel, 0, 0, 0, 1_000_000, "0.10",
        TestName = "Calculate_WhenAnthropicCacheReadOnly_AppliesReadMultiplier")]

    // Anthropic all four fields:
    //   input  500_000 * 1.00                = 500_000
    //   output 200_000 * 5.00                = 1_000_000
    //   cacheW 400_000 * 1.00 * 1.25         = 500_000
    //   cacheR 100_000 * 1.00 * 0.1          = 10_000
    // total = 2_010_000 → / 1_000_000 = 2.01
    [TestCase(AnthropicProvider, AnthropicModel, 500_000, 200_000, 400_000, 100_000, "2.01",
        TestName = "Calculate_WhenAnthropicAllFourFields_SumsInputOutputCacheWriteAndCacheRead")]
    public void Calculate_FormulaVariants_ReturnsExpectedDecimalCost(
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        int cacheCreationTokens,
        int cacheReadTokens,
        string expectedCost)
    {
        // Arrange
        var usage = new AiUsage(inputTokens, outputTokens, cacheCreationTokens, cacheReadTokens);
        var expected = decimal.Parse(expectedCost, System.Globalization.CultureInfo.InvariantCulture);

        // Act
        var result = _sut.Calculate(usage, provider, model);

        // Assert
        result.Should().Be(expected);
    }

    // ------------------------------------------------------------------
    // P1 — unknown model returns 0m and emits LogWarning with
    //       {Provider} {Model} arguments
    // ------------------------------------------------------------------

    [Test]
    public void Calculate_WhenProviderKnownButModelMissing_ReturnsZeroAndLogsWarning()
    {
        // Arrange
        const string unknownModel = "claude-hypothetical-9";
        var usage = new AiUsage(1000, 500, 0, 0);

        // Act
        var result = _sut.Calculate(usage, AnthropicProvider, unknownModel);

        // Assert
        result.Should().Be(0m);
        VerifyMissingPricingWarning(AnthropicProvider, unknownModel);
    }

    // ------------------------------------------------------------------
    // P1 — unknown provider returns 0m and emits LogWarning
    // ------------------------------------------------------------------

    [Test]
    public void Calculate_WhenProviderNotAnthropicOrGemini_ReturnsZeroAndLogsWarning()
    {
        // Arrange
        const string unknownProvider = "OpenAI";
        var usage = new AiUsage(1000, 500, 0, 0);

        // Act
        var result = _sut.Calculate(usage, unknownProvider, "gpt-4");

        // Assert
        result.Should().Be(0m);
        VerifyMissingPricingWarning(unknownProvider, "gpt-4");
    }

    // ------------------------------------------------------------------
    // P2 — all-zero usage returns 0m (and does not log a warning, since
    //       pricing IS known — the zero result is a legitimate computation)
    // ------------------------------------------------------------------

    [Test]
    public void Calculate_WhenAllUsageFieldsAreZero_ReturnsZero()
    {
        // Arrange
        var usage = new AiUsage(0, 0, 0, 0);

        // Act
        var result = _sut.Calculate(usage, AnthropicProvider, AnthropicModel);

        // Assert
        result.Should().Be(0m);
    }

    // ------------------------------------------------------------------
    // P2 — one-token input is not rounded to zero and returns the exact
    //       decimal value. This demonstrates decimal precision — IEEE 754
    //       double would give 1.0000000000000001e-7 for the same math,
    //       which cannot be stored exactly in NUMERIC(18,8). Asserting
    //       strict equality against the exact decimal literal 0.0000001m
    //       verifies that decimal arithmetic (not double) is in use.
    // ------------------------------------------------------------------

    [Test]
    public void Calculate_WhenSingleInputTokenForGemini_ReturnsExactSubCentDecimalValue()
    {
        // Arrange
        var usage = new AiUsage(InputTokens: 1, OutputTokens: 0, CacheCreationInputTokens: 0, CacheReadInputTokens: 0);
        // 1 token × 0.10 USD / 1_000_000 = 0.0000001 (exact in decimal arithmetic)
        const decimal expected = 0.0000001m;

        // Act
        var result = _sut.Calculate(usage, GeminiProvider, GeminiModel);

        // Assert
        result.Should().BeGreaterThan(0m, "a single token must not underflow to zero — that would lose cost data");
        result.Should().Be(expected,
            "only decimal arithmetic can represent 0.0000001 exactly; a double would produce 1.0000000000000001e-7");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void VerifyMissingPricingWarning(string expectedProvider, string expectedModel)
    {
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains(expectedProvider) &&
                    state.ToString()!.Contains(expectedModel)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "missing pricing must be surfaced via a single LogWarning carrying the provider and model name");
    }
}
