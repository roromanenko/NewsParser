namespace Infrastructure.Configuration;

public class ModelPricingOptions
{
    public const string SectionName = "ModelPricing";

    public Dictionary<string, ModelPrice> Anthropic { get; set; } = new();
    public Dictionary<string, ModelPrice> Gemini { get; set; } = new();

    public decimal AnthropicCacheWriteMultiplier { get; set; } = 1.25m;
    public decimal AnthropicCacheReadMultiplier { get; set; } = 0.1m;
}

public class ModelPrice
{
    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }
}
