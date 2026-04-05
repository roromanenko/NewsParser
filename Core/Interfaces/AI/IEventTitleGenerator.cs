namespace Core.Interfaces.AI;

public interface IEventTitleGenerator
{
	Task<string> GenerateTitleAsync(
		string eventSummary,
		List<string> articleTitles,
		CancellationToken cancellationToken = default);
}
