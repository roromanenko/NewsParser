using Core.DomainModels;
using Infrastructure.Persistence.Entity;

namespace Infrastructure.Persistence.Mappers;

public static class ProjectMapper
{
	public static Project ToDomain(this ProjectEntity entity) => new()
	{
		Id = entity.Id,
		Name = entity.Name,
		Slug = entity.Slug,
		AnalyzerPromptText = entity.AnalyzerPromptText,
		Categories = [..entity.Categories],
		OutputLanguage = entity.OutputLanguage,
		OutputLanguageName = entity.OutputLanguageName,
		IsActive = entity.IsActive,
		CreatedAt = entity.CreatedAt,
	};

	public static ProjectEntity ToEntity(this Project domain) => new()
	{
		Id = domain.Id,
		Name = domain.Name,
		Slug = domain.Slug,
		AnalyzerPromptText = domain.AnalyzerPromptText,
		Categories = [..domain.Categories],
		OutputLanguage = domain.OutputLanguage,
		OutputLanguageName = domain.OutputLanguageName,
		IsActive = domain.IsActive,
		CreatedAt = domain.CreatedAt,
	};
}
