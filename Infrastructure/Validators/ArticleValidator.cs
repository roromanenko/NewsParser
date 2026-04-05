using Core.DomainModels;
using Core.Interfaces.Validators;
using Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Infrastructure.Validators;

public class ArticleValidator(IOptions<ValidationOptions> options) : IArticleValidator
{
	private readonly ValidationOptions _options = options.Value;

	public (bool IsValid, string? Reason) Validate(Article article)
	{
		if (string.IsNullOrWhiteSpace(article.Title))
			return (false, "Title is empty");

		if (article.Title.Length < _options.MinTitleLength)
			return (false, $"Title too short ({article.Title.Length} chars, min {_options.MinTitleLength})");

		if (string.IsNullOrWhiteSpace(article.OriginalContent))
			return (false, "Content is empty");

		if (article.OriginalContent.Length < _options.MinContentLength)
			return (false, $"Content too short ({article.OriginalContent.Length} chars, min {_options.MinContentLength})");

		if (string.IsNullOrWhiteSpace(article.OriginalUrl))
			return (false, "URL is empty");

		var titleLower = article.Title.ToLowerInvariant();
		var contentLower = article.OriginalContent.ToLowerInvariant();

		foreach (var keyword in _options.ExcludedKeywords)
		{
			var kw = keyword.ToLowerInvariant();
			if (titleLower.Contains(kw))
				return (false, $"Title contains excluded keyword: '{keyword}'");
			if (contentLower.Contains(kw))
				return (false, $"Content contains excluded keyword: '{keyword}'");
		}

		if (article.PublishedAt.HasValue &&
			article.PublishedAt.Value < DateTimeOffset.UtcNow.AddHours(-_options.MaxAgeHours))
			return (false, $"Article is too old: published {article.PublishedAt:yyyy-MM-dd}");

		return (true, null);
	}
}
