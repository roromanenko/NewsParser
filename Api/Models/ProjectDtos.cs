namespace Api.Models;

public record ProjectListItemDto(Guid Id, string Name, string Slug, bool IsActive);

public record ProjectDetailDto(
	Guid Id,
	string Name,
	string Slug,
	string AnalyzerPromptText,
	List<string> Categories,
	string OutputLanguage,
	string OutputLanguageName,
	bool IsActive,
	DateTimeOffset CreatedAt);

public record CreateProjectRequest(
	string Name,
	string? Slug,
	string AnalyzerPromptText,
	List<string> Categories,
	string OutputLanguage,
	string OutputLanguageName);

public record UpdateProjectRequest(
	string Name,
	string AnalyzerPromptText,
	List<string> Categories,
	string OutputLanguage,
	string OutputLanguageName,
	bool IsActive);
