using Core.DomainModels;
using Core.DomainModels.AI;

namespace Core.Interfaces.AI;

public interface IArticleAnalyzer
{
	Task<ArticleAnalysisResult> AnalyzeAsync(RawArticle rawArticle, CancellationToken cancellationToken = default);
}