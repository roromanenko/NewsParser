using Core.DomainModels.AI;
using Core.Interfaces.AI;
using Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Telemetry;

internal class AiCostCalculator(
    IOptions<ModelPricingOptions> options,
    ILogger<AiCostCalculator> logger) : IAiCostCalculator
{
    private readonly ModelPricingOptions _options = options.Value;

    public decimal Calculate(AiUsage usage, string provider, string model)
    {
        var priceTable = ResolveTable(provider);
        if (priceTable is null)
        {
            logger.LogWarning("Missing pricing for {Provider} {Model}", provider, model);
            return 0m;
        }

        if (!priceTable.TryGetValue(model, out var price))
        {
            logger.LogWarning("Missing pricing for {Provider} {Model}", provider, model);
            return 0m;
        }

        return provider == "Anthropic"
            ? CalculateAnthropic(usage, price)
            : CalculateGemini(usage, price);
    }

    private Dictionary<string, ModelPrice>? ResolveTable(string provider) => provider switch
    {
        "Anthropic" => _options.Anthropic,
        "Gemini" => _options.Gemini,
        _ => null
    };

    private decimal CalculateGemini(AiUsage usage, ModelPrice price) =>
        (usage.InputTokens * price.InputPerMillion +
         usage.OutputTokens * price.OutputPerMillion) / 1_000_000m;

    private decimal CalculateAnthropic(AiUsage usage, ModelPrice price)
    {
        var cacheWriteMultiplier = _options.AnthropicCacheWriteMultiplier;
        var cacheReadMultiplier = _options.AnthropicCacheReadMultiplier;

        return (usage.InputTokens * price.InputPerMillion
             + usage.OutputTokens * price.OutputPerMillion
             + usage.CacheCreationInputTokens * price.InputPerMillion * cacheWriteMultiplier
             + usage.CacheReadInputTokens * price.InputPerMillion * cacheReadMultiplier)
            / 1_000_000m;
    }
}
