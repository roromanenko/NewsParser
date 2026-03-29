using CodeHollow.FeedReader;
using Core.DomainModels;
using Core.Interfaces.Parsers;

namespace Infrastructure.Parsers;

public class RssParser : ISourceParser
{
	public SourceType SourceType => SourceType.Rss;

	public async Task<List<RawArticle>> ParseAsync(Source source, CancellationToken cancellationToken = default)
	{
		var feed = await FeedReader.ReadAsync(source.Url, cancellationToken);

		return feed.Items.Select(item => new RawArticle
		{
			Id = Guid.NewGuid(),
			SourceId = source.Id,
			Source = source,
			Title = item.Title ?? string.Empty,
			Content = item.Content ?? item.Description ?? string.Empty,
			OriginalUrl = item.Link ?? string.Empty,
			ExternalId = item.Id ?? item.Link,
			PublishedAt = item.PublishingDate ?? DateTimeOffset.UtcNow,
			Language = string.Empty,
			Status = RawArticleStatus.Pending
		}).ToList();
	}
}