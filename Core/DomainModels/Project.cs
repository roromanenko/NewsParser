namespace Core.DomainModels;

public class Project
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string AnalyzerPromptText { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = [];
    public string OutputLanguage { get; set; } = "uk";
    public string OutputLanguageName { get; set; } = "Ukrainian";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; }
}

public static class ProjectConstants
{
    public static readonly Guid DefaultProjectId = new("00000000-0000-0000-0000-000000000001");
}
