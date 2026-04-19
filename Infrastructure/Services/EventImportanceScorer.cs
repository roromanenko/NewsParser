using Core.DomainModels;
using Core.Interfaces.Services;
using Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

internal class EventImportanceScorer(
    IOptions<EventImportanceOptions> options,
    ILogger<EventImportanceScorer> logger) : IEventImportanceScorer
{
    private readonly EventImportanceOptions _options = options.Value;

    public ImportanceScoreResult Calculate(ImportanceInputs inputs)
    {
        var baseScore = ComputeBaseScore(inputs);
        var effectiveScore = ApplyDecay(baseScore, inputs);
        var tier = ResolveTier(effectiveScore);
        return new ImportanceScoreResult(baseScore, effectiveScore, tier);
    }

    private double ComputeBaseScore(ImportanceInputs inputs)
    {
        var fVolume = ComputeVolumeFactor(inputs.ArticleCount);
        var fSources = ComputeSourcesFactor(inputs.DistinctSourceCount);
        var fVelocity = ComputeVelocityFactor(inputs.ArticlesLastHour);
        var fAi = MapAiLabel(inputs.AiLabel);

        return 100.0 * (
            _options.Weights.Volume * fVolume +
            _options.Weights.Sources * fSources +
            _options.Weights.Velocity * fVelocity +
            _options.Weights.Ai * fAi);
    }

    private double ComputeVolumeFactor(int articleCount)
    {
        var raw = Math.Log(1 + articleCount) / Math.Log(1 + _options.Caps.Volume);
        return Math.Clamp(raw, 0.0, 1.0);
    }

    private double ComputeSourcesFactor(int distinctSourceCount)
    {
        return Math.Min(distinctSourceCount, _options.Caps.Sources) / (double)_options.Caps.Sources;
    }

    private double ComputeVelocityFactor(int articlesLastHour)
    {
        return Math.Min(articlesLastHour, _options.Caps.Velocity) / (double)_options.Caps.Velocity;
    }

    private double MapAiLabel(string aiLabel)
    {
        return aiLabel.ToLowerInvariant() switch
        {
            "low" => 0.25,
            "medium" => 0.5,
            "high" => 0.75,
            "breaking" => 1.0,
            _ => LogAndReturnDefault(aiLabel)
        };
    }

    private double LogAndReturnDefault(string label)
    {
        logger.LogWarning(
            "Unknown intrinsic_importance label '{Label}'; defaulting to medium (0.5)", label);
        return 0.5;
    }

    private double ApplyDecay(double baseScore, ImportanceInputs inputs)
    {
        var hoursSinceLastArticle = (inputs.Now - inputs.LastArticleAt).TotalHours;
        return baseScore * Math.Pow(0.5, hoursSinceLastArticle / _options.HalfLifeHours);
    }

    private ImportanceTier ResolveTier(double effectiveScore)
    {
        if (effectiveScore >= _options.Tiers.BreakingThreshold) return ImportanceTier.Breaking;
        if (effectiveScore >= _options.Tiers.HighThreshold) return ImportanceTier.High;
        if (effectiveScore >= _options.Tiers.NormalThreshold) return ImportanceTier.Normal;
        return ImportanceTier.Low;
    }
}
