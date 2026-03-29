using Core.DomainModels;

namespace Core.Interfaces.AI;

public interface IEventSummaryUpdater
{
	Task<string> UpdateSummaryAsync(
		Event evt,
		List<string> newFacts,
		CancellationToken cancellationToken = default);
}