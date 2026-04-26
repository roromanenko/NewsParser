namespace Core.DomainModels;

public class PublishTarget
{
	public Guid Id { get; init; }
	public string Name { get; set; } = string.Empty;
	public Platform Platform { get; set; }
	public string Identifier { get; set; } = string.Empty;
	public string SystemPrompt { get; set; } = string.Empty;
	public bool IsActive { get; set; }
	public Guid ProjectId { get; set; }
}
