using Core.DomainModels;
using Core.DomainModels.AI;

namespace Core.Interfaces.AI;

public interface IContradictionDetector
{
    Task<List<ContradictionInput>> DetectAsync(
        Article article,
        Event targetEvent,
        CancellationToken cancellationToken = default);
}
