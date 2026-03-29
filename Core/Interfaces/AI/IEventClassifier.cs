using Core.DomainModels;
using Core.DomainModels.AI;

namespace Core.Interfaces.AI;

public interface IEventClassifier
{
	Task<EventClassificationResult> ClassifyAsync(
		Article article,
		List<Event> candidateEvents,
		CancellationToken cancellationToken = default);
}