namespace Core.DomainModels;

public class MediaFile
{
	public Guid Id { get; init; }
	public Guid ArticleId { get; init; }
	public string R2Key { get; init; } = string.Empty;
	public string OriginalUrl { get; init; } = string.Empty;
	public string ContentType { get; init; } = string.Empty;
	public long SizeBytes { get; init; }
	public MediaKind Kind { get; init; }
	public DateTimeOffset CreatedAt { get; init; }
}

public enum MediaKind
{
	Image,
	Video
}
