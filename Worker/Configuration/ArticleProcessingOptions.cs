namespace Worker.Configuration;

public class ArticleProcessingOptions
{
	public const string SectionName = "ArticleProcessing";
	public int AnalysisIntervalSeconds { get; set; } = 60;
	public int PublicationWorkerIntervalSeconds { get; set; } = 30;
	public int BatchSize { get; set; } = 10;
	public int MaxRetryCount { get; set; } = 5;

	// Event classification thresholds (merged from EventClassificationOptions)
	public int SimilarityWindowHours { get; set; } = 24;
	public double AutoSameEventThreshold { get; set; } = 0.90;
	public double AutoNewEventThreshold { get; set; } = 0.70;
	public int MinUpdateIntervalMinutes { get; set; } = 30;
	public int MaxUpdatesPerDay { get; set; } = 10;
	public double DeduplicationThreshold { get; set; } = 0.95;
	public int DeduplicationWindowHours { get; set; } = 72;
}
