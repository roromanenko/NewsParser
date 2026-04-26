namespace Infrastructure.Persistence.Entity;

public class ProjectEntity
{
	public Guid Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public string Slug { get; set; } = string.Empty;
	public string AnalyzerPromptText { get; set; } = string.Empty;
	public string[] Categories { get; set; } = [];
	public string OutputLanguage { get; set; } = string.Empty;
	public string OutputLanguageName { get; set; } = string.Empty;
	public bool IsActive { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
}
