using Api.Models;
using Core.DomainModels;

namespace Api.Mappers;

public static class ProjectMapper
{
	public static ProjectListItemDto ToListItemDto(this Project project) =>
		new(project.Id, project.Name, project.Slug, project.IsActive);

	public static ProjectDetailDto ToDetailDto(this Project project) =>
		new(project.Id, project.Name, project.Slug, project.AnalyzerPromptText,
			project.Categories, project.OutputLanguage, project.OutputLanguageName,
			project.IsActive, project.CreatedAt);
}
