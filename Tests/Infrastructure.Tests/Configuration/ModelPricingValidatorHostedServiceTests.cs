using FluentAssertions;
using Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Infrastructure.Tests.Configuration;

/// <summary>
/// Tests for <see cref="ModelPricingValidatorHostedService"/>.
///
/// The hosted service is a thin shim that, on <see cref="IHostedService.StartAsync"/>,
/// unwraps the two <see cref="IOptions{TOptions}"/> instances and delegates to
/// <see cref="ModelPricingValidator.ValidateOrLog"/>. <see cref="IHostedService.StopAsync"/>
/// is a no-op. We assert delegation indirectly by configuring missing pricing and
/// observing the resulting <see cref="LogLevel.Error"/> on the injected logger,
/// which proves both <c>.Value</c> properties were read and the logger was forwarded.
/// </summary>
[TestFixture]
public class ModelPricingValidatorHostedServiceTests
{
    private const string MissingAnthropicModel = "claude-not-in-pricing-table";
    private const string KnownGeminiModel = "gemini-2.0-flash";

    private Mock<IOptions<AiOptions>> _aiOptionsMock = null!;
    private Mock<IOptions<ModelPricingOptions>> _pricingOptionsMock = null!;
    private Mock<ILogger<ModelPricingValidatorHostedService>> _loggerMock = null!;
    private ModelPricingValidatorHostedService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var ai = new AiOptions
        {
            Anthropic = new AnthropicOptions
            {
                // Single non-empty model id; the rest are blanked so the test
                // produces exactly one LogError that we can attribute to delegation.
                AnalyzerModel = MissingAnthropicModel,
                GeneratorModel = string.Empty,
                ContentGeneratorModel = string.Empty,
                ClassifierModel = string.Empty,
                ContradictionDetectorModel = string.Empty,
                SummaryUpdaterModel = string.Empty,
                KeyFactsExtractorModel = string.Empty,
                TitleGeneratorModel = string.Empty,
            },
            Gemini = new GeminiOptions
            {
                AnalyzerModel = KnownGeminiModel,
                EmbeddingModel = string.Empty,
            },
        };

        var pricing = new ModelPricingOptions
        {
            // Anthropic table is empty → MissingAnthropicModel triggers an error.
            Gemini = new Dictionary<string, ModelPrice>
            {
                [KnownGeminiModel] = new() { InputPerMillion = 0.10m, OutputPerMillion = 0.40m },
            },
        };

        _aiOptionsMock = new Mock<IOptions<AiOptions>>();
        _aiOptionsMock.SetupGet(o => o.Value).Returns(ai);

        _pricingOptionsMock = new Mock<IOptions<ModelPricingOptions>>();
        _pricingOptionsMock.SetupGet(o => o.Value).Returns(pricing);

        _loggerMock = new Mock<ILogger<ModelPricingValidatorHostedService>>();
        _loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        _sut = new ModelPricingValidatorHostedService(
            _aiOptionsMock.Object,
            _pricingOptionsMock.Object,
            _loggerMock.Object);
    }

    // ------------------------------------------------------------------
    // P0 — StartAsync delegates to the validator with both .Value payloads
    //       and the injected logger. Verified indirectly: the configured
    //       AnalyzerModel has no pricing → exactly one LogError is emitted
    //       carrying "Anthropic" and the missing model id.
    // ------------------------------------------------------------------

    [Test]
    public async Task StartAsync_WhenInvoked_DelegatesToValidatorWithConfiguredOptionValuesAndLogger()
    {
        // Arrange — see [SetUp]

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("Anthropic") &&
                    state.ToString()!.Contains(MissingAnthropicModel)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "StartAsync must delegate to ModelPricingValidator with the unwrapped Options values and forward the injected logger");
    }

    // ------------------------------------------------------------------
    // P0 — StartAsync returns a synchronously-completed task. The contract
    //       is intentionally I/O-free so the host start-up is not delayed.
    // ------------------------------------------------------------------

    [Test]
    public void StartAsync_WhenInvoked_ReturnsSynchronouslyCompletedTask()
    {
        // Arrange — see [SetUp]

        // Act
        var task = _sut.StartAsync(CancellationToken.None);

        // Assert
        task.IsCompletedSuccessfully.Should().BeTrue(
            "the hosted service performs no I/O and must not introduce async start-up latency");
    }

    // ------------------------------------------------------------------
    // P0 — StopAsync is a no-op completed task; nothing to log, nothing to
    //       cancel. Calling it must not invoke the validator a second time.
    // ------------------------------------------------------------------

    [Test]
    public void StopAsync_WhenInvoked_ReturnsSynchronouslyCompletedTaskWithoutLoggingErrors()
    {
        // Arrange — see [SetUp]

        // Act
        var task = _sut.StopAsync(CancellationToken.None);

        // Assert
        task.IsCompletedSuccessfully.Should().BeTrue();
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "StopAsync must not run validation");
    }
}
