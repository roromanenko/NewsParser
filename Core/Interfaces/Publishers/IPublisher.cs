using Core.DomainModels;

namespace Core.Interfaces.Publishers;

public interface IPublisher
{
	Platform Platform { get; }

	Task<string> PublishAsync(
		Publication publication,
		List<ResolvedMedia> media,
		CancellationToken cancellationToken = default);

	Task<string> PublishReplyAsync(
		Publication publication,
		string replyToMessageId,
		List<ResolvedMedia> media,
		CancellationToken cancellationToken = default);
}