using Core.DomainModels;

namespace Core.Interfaces.AI;

public interface IContentGenerator
{
	Task<string> GenerateForPlatformAsync(
		Event evt,
		PublishTarget target,
		CancellationToken cancellationToken = default,
		string? updateContext = null);
}
