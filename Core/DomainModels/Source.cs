namespace Core.DomainModels;

public class Source
{
	public Guid Id { get; init; }
	public string Name { get; set; } = string.Empty;
	public string Url { get; set; } = string.Empty;
	public SourceType Type { get; set; }
	public bool IsActive { get; set; }
	public DateTimeOffset? LastFetchedAt { get; set; }
	public List<RawArticle> RawArticles { get; set; } = [];
}

public enum SourceType
{
	Rss,
	Telegram
}
