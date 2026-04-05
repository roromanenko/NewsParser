using Core.DomainModels;

namespace Core.Interfaces.AI;

public interface IKeyFactsExtractor
{
	Task<List<string>> ExtractAsync(Article article, CancellationToken cancellationToken = default);
}
