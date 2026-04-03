using Core.DomainModels;
using Core.DomainModels.AI;

namespace Core.Interfaces.AI;

public interface IArticleGenerator
{
	Task<ArticleGenerationResult> GenerateAsync(Article article, ArticleAnalysisResult analysis, CancellationToken cancellationToken = default);
}