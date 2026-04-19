using Core.DomainModels;
using Core.DomainModels.AI;

namespace Core.Interfaces.AI;

public interface IEventSummaryUpdater
{
	Task<EventSummaryUpdateResult> UpdateSummaryAsync(
		Event evt,
		List<string> newFacts,
		CancellationToken cancellationToken = default);
}