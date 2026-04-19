namespace Core.DomainModels;

public record ImportanceScoreResult(double BaseScore, double EffectiveScore, ImportanceTier Tier);
