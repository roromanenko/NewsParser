using Core.DomainModels;

namespace Infrastructure.AI;

public static class ProjectPromptBuilder
{
	public static string Build(Project project)
		=> project.AnalyzerPromptText
			.Replace("{OUTPUT_LANGUAGE}", project.OutputLanguageName)
			.Replace("{CATEGORIES}", string.Join(", ", project.Categories));
}
