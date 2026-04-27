namespace Infrastructure.Persistence.Entity;

public class PublishTargetEntity
{
	public Guid Id { get; init; }
	public string Name { get; set; } = string.Empty;
	public string Platform { get; set; } = string.Empty;
	public string Identifier { get; set; } = string.Empty;
	public string SystemPrompt { get; set; } = string.Empty;
	public bool IsActive { get; set; }
	public Guid ProjectId { get; set; }
	public List<PublicationEntity> Publications { get; set; } = [];
}
