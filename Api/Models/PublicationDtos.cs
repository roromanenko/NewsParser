namespace Api.Models;

public record PublicationListItemDto(
	Guid Id,
	string Status,
	string TargetName,
	string Platform,
	DateTimeOffset CreatedAt,
	DateTimeOffset? PublishedAt,
	Guid? EventId,
	string? EventTitle
);

public record PublicationDetailDto(
	Guid Id,
	string Status,
	string TargetName,
	string Platform,
	string GeneratedContent,
	List<MediaFileDto> AvailableMedia,
	List<Guid> SelectedMediaFileIds,
	DateTimeOffset CreatedAt,
	DateTimeOffset? ApprovedAt,
	DateTimeOffset? PublishedAt,
	string? RejectionReason
);

public record CreatePublicationRequest(Guid EventId, Guid PublishTargetId);

public record UpdatePublicationContentRequest(string Content, List<Guid> SelectedMediaFileIds);

public record RejectPublicationRequest(string Reason);
