using Microsoft.Extensions.Logging;

namespace Infrastructure.Configuration;

internal static class ModelPricingValidator
{
    internal static void ValidateOrLog(AiOptions ai, ModelPricingOptions pricing, ILogger logger)
    {
        CheckProvider("Anthropic", GetAnthropicModelIds(ai.Anthropic), pricing.Anthropic, logger);
        CheckProvider("Gemini", GetGeminiModelIds(ai.Gemini), pricing.Gemini, logger);
    }

    private static void CheckProvider(
        string provider,
        IEnumerable<string> modelIds,
        Dictionary<string, ModelPrice> priceTable,
        ILogger logger)
    {
        foreach (var modelId in modelIds)
        {
            if (!priceTable.ContainsKey(modelId))
                logger.LogError(
                    "Startup pricing validation: no pricing configured for {Provider} model {Model}. Cost will be logged as 0.",
                    provider, modelId);
        }
    }

    private static IEnumerable<string> GetAnthropicModelIds(AnthropicOptions anthropic)
    {
        string[] ids =
        [
            anthropic.AnalyzerModel,
            anthropic.GeneratorModel,
            anthropic.ContentGeneratorModel,
            anthropic.ClassifierModel,
            anthropic.ContradictionDetectorModel,
            anthropic.SummaryUpdaterModel,
            anthropic.KeyFactsExtractorModel,
            anthropic.TitleGeneratorModel,
        ];
        return ids.Where(id => !string.IsNullOrEmpty(id));
    }

    private static IEnumerable<string> GetGeminiModelIds(GeminiOptions gemini)
    {
        string[] ids = [gemini.AnalyzerModel, gemini.EmbeddingModel];
        return ids.Where(id => !string.IsNullOrEmpty(id));
    }
}
