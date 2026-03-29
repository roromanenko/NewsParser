namespace Worker.Configuration;

public class EventClassificationOptions
{
	public const string SectionName = "EventClassification";

	public int IntervalSeconds { get; set; } = 60;
	public int BatchSize { get; set; } = 10;
	public int MaxRetryCount { get; set; } = 3;

	// Окно поиска похожих событий
	public int SimilarityWindowHours { get; set; } = 24;

	// Пороги автоклассификации без Claude
	public double AutoSameEventThreshold { get; set; } = 0.90;
	public double AutoNewEventThreshold { get; set; } = 0.70;

	// Ограничения на апдейты
	public int MinUpdateIntervalMinutes { get; set; } = 30;
	public int MaxUpdatesPerDay { get; set; } = 10;
}