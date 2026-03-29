namespace Worker.Configuration;

public class ArticleProcessingOptions
{
	public const string SectionName = "ArticleProcessing";
	public int AnalyzerIntervalSeconds { get; set; } = 60;
	public int GeneratorIntervalSeconds { get; set; } = 60;
	public int PublicationGenerationIntervalSeconds { get; set; } = 60;
	public int PublicationWorkerIntervalSeconds { get; set; } = 30;
	public int BatchSize { get; set; } = 10;
	public int MaxRetryCount { get; set; } = 5;
}