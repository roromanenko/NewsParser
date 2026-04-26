using Core.DomainModels;
using Core.Interfaces;

namespace Api.ProjectContext;

public class ProjectContextService : IProjectContext
{
	private Project? _current;

	public Guid ProjectId => _current?.Id ?? Guid.Empty;

	public Project Current => _current ?? throw new InvalidOperationException("Project context has not been set");

	public bool IsSet => _current is not null;

	public void Set(Project project)
	{
		_current = project;
	}
}
