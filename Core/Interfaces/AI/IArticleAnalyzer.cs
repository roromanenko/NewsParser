using Core.DomainModels;
using Core.DomainModels.AI;

namespace Core.Interfaces.AI;

public interface IArticleAnalyzer
{
	Task<ArticleAnalysisResult> AnalyzeAsync(Article article, CancellationToken cancellationToken = default);
	Task<ArticleAnalysisResult> AnalyzeAsync(Article article, string systemPrompt, CancellationToken cancellationToken = default);
}
