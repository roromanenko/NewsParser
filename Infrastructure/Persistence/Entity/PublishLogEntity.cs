namespace Infrastructure.Persistence.Entity;

public class PublishLogEntity
{
	public Guid Id { get; init; }
	public Guid PublicationId { get; init; }
	public PublicationEntity Publication { get; init; } = null!;
	public string Status { get; set; } = string.Empty;
	public string? ErrorMessage { get; set; }
	public DateTimeOffset AttemptedAt { get; init; }
	public string? ExternalMessageId { get; set; }
}