using Core.DomainModels;
using Core.Interfaces.Validators;
using Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Infrastructure.Validators;

public class RawArticleValidator(IOptions<ValidationOptions> options) : IRawArticleValidator
{
	private readonly ValidationOptions _options = options.Value;

	public (bool IsValid, string? Reason) Validate(RawArticle rawArticle)
	{
		if (string.IsNullOrWhiteSpace(rawArticle.Title))
			return (false, "Title is empty");

		if (rawArticle.Title.Length < _options.MinTitleLength)
			return (false, $"Title too short ({rawArticle.Title.Length} chars, min {_options.MinTitleLength})");

		if (string.IsNullOrWhiteSpace(rawArticle.Content))
			return (false, "Content is empty");

		if (rawArticle.Content.Length < _options.MinContentLength)
			return (false, $"Content too short ({rawArticle.Content.Length} chars, min {_options.MinContentLength})");

		if (string.IsNullOrWhiteSpace(rawArticle.OriginalUrl))
			return (false, "URL is empty");

		var titleLower = rawArticle.Title.ToLowerInvariant();
		var contentLower = rawArticle.Content.ToLowerInvariant();

		foreach (var keyword in _options.ExcludedKeywords)
		{
			var kw = keyword.ToLowerInvariant();
			if (titleLower.Contains(kw))
				return (false, $"Title contains excluded keyword: '{keyword}'");
			if (contentLower.Contains(kw))
				return (false, $"Content contains excluded keyword: '{keyword}'");
		}

		if (rawArticle.PublishedAt < DateTimeOffset.UtcNow.AddHours(-_options.MaxAgeHours))
			return (false, $"Article is too old: published {rawArticle.PublishedAt:yyyy-MM-dd}");

		return (true, null);
	}
}