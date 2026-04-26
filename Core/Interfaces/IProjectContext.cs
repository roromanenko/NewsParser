using Core.DomainModels;

namespace Core.Interfaces;

public interface IProjectContext
{
	Guid ProjectId { get; }
	Project Current { get; }
	bool IsSet { get; }
}
