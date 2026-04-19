using Core.DomainModels;
using FluentAssertions;
using Infrastructure.Configuration;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Infrastructure.Tests.Services;

[TestFixture]
public class EventImportanceScorerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------
    // Calculate — Volume normalizer at boundaries
    // f_volume = log(1 + count) / log(1 + VolumeCap), clamped to [0, 1]
    // Isolated via Weights: Volume=1.0, others=0 → base = 100 * f_volume
    // ------------------------------------------------------------------

    [TestCase(0, 0.0, Description = "count=0 → f_volume=0 → base=0")]
    [TestCase(1, 22.76702487, Description = "count=1 → f_volume=log(2)/log(21)")]
    [TestCase(20, 100.0, Description = "count=VolumeCap → f_volume=1")]
    [TestCase(40, 100.0, Description = "count=VolumeCap*2 → clamped to 1")]
    public void Calculate_VolumeNormalizerAtBoundaries_ReturnsExpectedBaseScore(
        int articleCount, double expectedBase)
    {
        // Arrange
        var options = OptionsWithIsolatedWeight(volume: 1.0);
        var sut = new EventImportanceScorer(options, NullLogger<EventImportanceScorer>.Instance);

        var inputs = new ImportanceInputs(
            ArticleCount: articleCount,
            DistinctSourceCount: 0,
            ArticlesLastHour: 0,
            AiLabel: "medium",
            LastArticleAt: FixedNow,
            Now: FixedNow);

        // Act
        var result = sut.Calculate(inputs);

        // Assert
        result.BaseScore.Should().BeApproximately(expectedBase, 0.001);
    }

    // ------------------------------------------------------------------
    // Calculate — Sources normalizer at boundaries
    // f_sources = min(distinct, SourcesCap) / SourcesCap
    // Isolated via Weights: Sources=1.0, others=0 → base = 100 * f_sources
    // ------------------------------------------------------------------

    [TestCase(0, 0.0, Description = "distinct=0 → f_sources=0")]
    [TestCase(5, 100.0, Description = "distinct=SourcesCap → f_sources=1")]
    [TestCase(6, 100.0, Description = "distinct=SourcesCap+1 → clamped to 1")]
    public void Calculate_SourcesNormalizerAtBoundaries_ReturnsExpectedBaseScore(
        int distinctSources, double expectedBase)
    {
        // Arrange
        var options = OptionsWithIsolatedWeight(sources: 1.0);
        var sut = new EventImportanceScorer(options, NullLogger<EventImportanceScorer>.Instance);

        var inputs = new ImportanceInputs(
            ArticleCount: 0,
            DistinctSourceCount: distinctSources,
            ArticlesLastHour: 0,
            AiLabel: "medium",
            LastArticleAt: FixedNow,
            Now: FixedNow);

        // Act
        var result = sut.Calculate(inputs);

        // Assert
        result.BaseScore.Should().BeApproximately(expectedBase, 0.001);
    }

    // ------------------------------------------------------------------
    // Calculate — Velocity normalizer at boundaries
    // f_velocity = min(articles_last_hour, VelocityCap) / VelocityCap
    // Isolated via Weights: Velocity=1.0, others=0 → base = 100 * f_velocity
    // ------------------------------------------------------------------

    [TestCase(0, 0.0, Description = "velocity=0 → f_velocity=0")]
    [TestCase(5, 100.0, Description = "velocity=VelocityCap → f_velocity=1")]
    [TestCase(6, 100.0, Description = "velocity=VelocityCap+1 → clamped to 1")]
    public void Calculate_VelocityNormalizerAtBoundaries_ReturnsExpectedBaseScore(
        int articlesLastHour, double expectedBase)
    {
        // Arrange
        var options = OptionsWithIsolatedWeight(velocity: 1.0);
        var sut = new EventImportanceScorer(options, NullLogger<EventImportanceScorer>.Instance);

        var inputs = new ImportanceInputs(
            ArticleCount: 0,
            DistinctSourceCount: 0,
            ArticlesLastHour: articlesLastHour,
            AiLabel: "medium",
            LastArticleAt: FixedNow,
            Now: FixedNow);

        // Act
        var result = sut.Calculate(inputs);

        // Assert
        result.BaseScore.Should().BeApproximately(expectedBase, 0.001);
    }

    // ------------------------------------------------------------------
    // Calculate — AI label mapping (case-insensitive)
    // Isolated via Weights: Ai=1.0, others=0 → base = 100 * f_ai
    // ------------------------------------------------------------------

    [TestCase("low", 25.0)]
    [TestCase("medium", 50.0)]
    [TestCase("high", 75.0)]
    [TestCase("breaking", 100.0)]
    [TestCase("LOW", 25.0, Description = "upper-case low → case-insensitive match")]
    [TestCase("Medium", 50.0, Description = "mixed-case medium → case-insensitive match")]
    [TestCase("HIGH", 75.0, Description = "upper-case high → case-insensitive match")]
    [TestCase("Breaking", 100.0, Description = "mixed-case breaking → case-insensitive match")]
    public void Calculate_AiLabelMapping_ReturnsExpectedBaseScore(
        string aiLabel, double expectedBase)
    {
        // Arrange
        var options = OptionsWithIsolatedWeight(ai: 1.0);
        var sut = new EventImportanceScorer(options, NullLogger<EventImportanceScorer>.Instance);

        var inputs = new ImportanceInputs(
            ArticleCount: 0,
            DistinctSourceCount: 0,
            ArticlesLastHour: 0,
            AiLabel: aiLabel,
            LastArticleAt: FixedNow,
            Now: FixedNow);

        // Act
        var result = sut.Calculate(inputs);

        // Assert
        result.BaseScore.Should().BeApproximately(expectedBase, 0.001);
    }

    [TestCase("unknown", Description = "unknown label → default medium (0.5)")]
    [TestCase("", Description = "empty label → default medium (0.5)")]
    [TestCase("critical", Description = "another unknown label → default medium (0.5)")]
    public void Calculate_AiLabelUnknownOrEmpty_DefaultsToMedium(string aiLabel)
    {
        // Arrange
        var options = OptionsWithIsolatedWeight(ai: 1.0);
        var sut = new EventImportanceScorer(options, NullLogger<EventImportanceScorer>.Instance);

        var inputs = new ImportanceInputs(
            ArticleCount: 0,
            DistinctSourceCount: 0,
            ArticlesLastHour: 0,
            AiLabel: aiLabel,
            LastArticleAt: FixedNow,
            Now: FixedNow);

        // Act
        var result = sut.Calculate(inputs);

        // Assert — unknown/empty must map to 0.5 → base = 50.0
        result.BaseScore.Should().BeApproximately(50.0, 0.001);
    }

    // ------------------------------------------------------------------
    // Calculate — Tier mapping at threshold boundaries
    // Effective score is produced from a known base (100, via Ai=1.0 + "breaking")
    // and decayed to hit the exact effective value via custom LastArticleAt.
    // Tier is assigned from effective_score:
    //   >= 75 → Breaking, >= 50 → High, >= 25 → Normal, else → Low
    // ------------------------------------------------------------------

    [TestCase(75.0, ImportanceTier.Breaking, Description = "effective=75.0 → Breaking (boundary)")]
    [TestCase(74.999, ImportanceTier.High, Description = "effective=74.999 → High (just below breaking)")]
    [TestCase(50.0, ImportanceTier.High, Description = "effective=50.0 → High (boundary)")]
    [TestCase(49.999, ImportanceTier.Normal, Description = "effective=49.999 → Normal (just below high)")]
    [TestCase(25.0, ImportanceTier.Normal, Description = "effective=25.0 → Normal (boundary)")]
    [TestCase(24.999, ImportanceTier.Low, Description = "effective=24.999 → Low (just below normal)")]
    public void Calculate_TierMappingAtBoundaries_ReturnsExpectedTier(
        double targetEffectiveScore, ImportanceTier expectedTier)
    {
        // Arrange
        // Isolate Ai weight so base score is predictable: ai="breaking" → base=100.
        // Then pick hours such that base * 0.5^(hours / HalfLifeHours) == targetEffectiveScore.
        const double baseScore = 100.0;
        const double halfLifeHours = 12.0;
        var options = OptionsWithIsolatedWeight(ai: 1.0, halfLifeHours: halfLifeHours);
        var sut = new EventImportanceScorer(options, NullLogger<EventImportanceScorer>.Instance);

        var decayFactor = targetEffectiveScore / baseScore;
        var hoursSinceLastArticle = -halfLifeHours * Math.Log2(decayFactor);

        var inputs = new ImportanceInputs(
            ArticleCount: 0,
            DistinctSourceCount: 0,
            ArticlesLastHour: 0,
            AiLabel: "breaking",
            LastArticleAt: FixedNow.AddHours(-hoursSinceLastArticle),
            Now: FixedNow);

        // Act
        var result = sut.Calculate(inputs);

        // Assert — first sanity-check the effective score, then assert the tier
        result.EffectiveScore.Should().BeApproximately(targetEffectiveScore, 0.0001);
        result.Tier.Should().Be(expectedTier);
    }

    // ------------------------------------------------------------------
    // Calculate — Decay factor at 0 / HalfLife / 2×HalfLife
    // effective_score / base_score ≈ 1.0 / 0.5 / 0.25
    // ------------------------------------------------------------------

    [TestCase(0.0, 1.0, Description = "hours=0 → decay factor 1.0")]
    [TestCase(12.0, 0.5, Description = "hours=HalfLifeHours → decay factor 0.5")]
    [TestCase(24.0, 0.25, Description = "hours=2×HalfLifeHours → decay factor 0.25")]
    public void Calculate_DecayFactorAtHalfLifeMultiples_ReturnsExpectedRatio(
        double hoursSinceLastArticle, double expectedRatio)
    {
        // Arrange — defaults (HalfLifeHours = 12). Produce a nonzero base score.
        var options = Options.Create(new EventImportanceOptions());
        var sut = new EventImportanceScorer(options, NullLogger<EventImportanceScorer>.Instance);

        var inputs = new ImportanceInputs(
            ArticleCount: 20,
            DistinctSourceCount: 5,
            ArticlesLastHour: 5,
            AiLabel: "breaking",
            LastArticleAt: FixedNow.AddHours(-hoursSinceLastArticle),
            Now: FixedNow);

        // Act
        var result = sut.Calculate(inputs);

        // Assert
        result.BaseScore.Should().BeGreaterThan(0, "precondition: base score must be nonzero to validate the ratio");
        (result.EffectiveScore / result.BaseScore).Should().BeApproximately(expectedRatio, 0.001);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static IOptions<EventImportanceOptions> OptionsWithIsolatedWeight(
        double volume = 0.0,
        double sources = 0.0,
        double velocity = 0.0,
        double ai = 0.0,
        double halfLifeHours = 12.0)
    {
        return Options.Create(new EventImportanceOptions
        {
            Weights = new ImportanceWeights
            {
                Volume = volume,
                Sources = sources,
                Velocity = velocity,
                Ai = ai
            },
            HalfLifeHours = halfLifeHours
            // Caps and Tiers use defaults (Volume=20, Sources=5, Velocity=5; 75/50/25)
        });
    }
}
