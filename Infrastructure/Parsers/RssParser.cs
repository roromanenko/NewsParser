using CodeHollow.FeedReader;
using Core.DomainModels;
using Core.Interfaces.Parsers;

namespace Infrastructure.Parsers;

public class RssParser : ISourceParser
{
	public SourceType SourceType => SourceType.Rss;

	public async Task<List<Article>> ParseAsync(Source source, CancellationToken cancellationToken = default)
	{
		var feed = await FeedReader.ReadAsync(source.Url, cancellationToken);

		return feed.Items.Select(item => new Article
		{
			Id = Guid.NewGuid(),
			SourceId = source.Id,
			Title = item.Title ?? string.Empty,
			OriginalContent = item.Content ?? item.Description ?? string.Empty,
			OriginalUrl = item.Link ?? string.Empty,
			ExternalId = item.Id ?? item.Link,
			PublishedAt = item.PublishingDate ?? DateTimeOffset.UtcNow,
			Language = string.Empty,
			Status = ArticleStatus.Pending,
			ProcessedAt = DateTimeOffset.UtcNow,
		}).ToList();
	}
}
