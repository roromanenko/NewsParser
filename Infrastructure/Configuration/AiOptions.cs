namespace Infrastructure.Configuration;

public class AiOptions
{
	public const string SectionName = "Ai";
	public GeminiOptions Gemini { get; set; } = new();
	public AnthropicOptions Anthropic { get; set; } = new();
}

public class GeminiOptions
{
	public string ApiKey { get; set; } = string.Empty;
	public string AnalyzerModel { get; set; } = "gemini-2.0-flash";
	public string EmbeddingModel { get; set; } = "gemini-embedding-001";
	public double DeduplicationThreshold { get; set; } = 0.85;
	public int DeduplicationWindowHours { get; set; } = 24;
}

public class AnthropicOptions
{
	public string ApiKey { get; set; } = string.Empty;
	public string AnalyzerModel { get; set; } = "claude-haiku-4-5-20251001";
	public string GeneratorModel { get; set; } = "claude-sonnet-4-5";
	public string ContentGeneratorModel { get; set; } = "claude-sonnet-4-5";
	public string ClassifierModel { get; set; } = "claude-haiku-4-5-20251001";
	public string SummaryUpdaterModel { get; set; } = "claude-haiku-4-5-20251001";
	public string OutputLanguage { get; set; } = "uk";
}