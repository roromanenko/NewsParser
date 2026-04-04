using Core.DomainModels;

namespace Core.Interfaces.Services;

public interface IEventApprovalService
{
	Task<Event> ApproveAsync(Guid eventId, Guid editorId, List<Guid> publishTargetIds, CancellationToken cancellationToken = default);
	Task<Event> RejectAsync(Guid eventId, Guid editorId, string reason, CancellationToken cancellationToken = default);
}
