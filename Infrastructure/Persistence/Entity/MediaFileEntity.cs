namespace Infrastructure.Persistence.Entity;

public class MediaFileEntity
{
	public Guid Id { get; init; }
	public Guid ArticleId { get; set; }
	public string R2Key { get; set; } = string.Empty;
	public string OriginalUrl { get; set; } = string.Empty;
	public string ContentType { get; set; } = string.Empty;
	public long SizeBytes { get; set; }
	public string Kind { get; set; } = string.Empty;
	public DateTimeOffset CreatedAt { get; set; }
}
