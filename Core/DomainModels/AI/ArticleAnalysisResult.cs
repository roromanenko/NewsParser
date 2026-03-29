namespace Core.DomainModels.AI;

public class ArticleAnalysisResult
{
	public string Category { get; set; } = string.Empty;
	public List<string> Tags { get; set; } = [];
	public string Sentiment { get; set; } = string.Empty;
	public string Language { get; set; } = string.Empty;
	public string Summary { get; set; } = string.Empty;
}