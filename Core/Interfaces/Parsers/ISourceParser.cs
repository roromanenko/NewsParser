using Core.DomainModels;

namespace Core.Interfaces.Parsers;

public interface ISourceParser
{
	SourceType SourceType { get; }
	Task<List<RawArticle>> ParseAsync(Source source, CancellationToken cancellationToken = default);
}