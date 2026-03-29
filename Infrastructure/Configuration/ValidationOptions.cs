namespace Infrastructure.Configuration;

public class ValidationOptions
{
	public const string SectionName = "Validation";
	public int MinContentLength { get; set; } = 100;
	public int MinTitleLength { get; set; } = 10;
	public List<string> ExcludedKeywords { get; set; } = [];
	public int MaxAgeHours { get; set; } = 72;
	public int TitleSimilarityThreshold { get; set; } = 85;
	public int TitleDeduplicationWindowHours { get; set; } = 24;
}