namespace Infrastructure.Persistence.Entity;

public class SourceEntity
{
	public Guid Id { get; init; }
	public string Name { get; set; } = string.Empty;
	public string Url { get; set; } = string.Empty;
	public string Type { get; set; } = string.Empty;
	public Guid ProjectId { get; set; }
	public bool IsActive { get; set; }
	public DateTimeOffset? LastFetchedAt { get; set; }
	public List<ArticleEntity> Articles { get; set; } = [];
}
