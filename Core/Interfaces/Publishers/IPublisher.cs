using Core.DomainModels;

namespace Core.Interfaces.Publishers;

public interface IPublisher
{
	Platform Platform { get; }

	Task<string> PublishAsync(
		Publication publication,
		CancellationToken cancellationToken = default);

	Task<string> PublishReplyAsync(
		Publication publication,
		string replyToMessageId,
		CancellationToken cancellationToken = default);
}