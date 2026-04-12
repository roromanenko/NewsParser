using Api.Models;
using Core.DomainModels;

namespace Api.Mappers;

public static class PublicationMapper
{
	public static PublicationListItemDto ToListItemDto(this Publication pub) => new(
		pub.Id,
		pub.Status.ToString(),
		pub.PublishTarget.Name,
		pub.PublishTarget.Platform.ToString(),
		pub.CreatedAt,
		pub.PublishedAt,
		pub.EventId,
		pub.Event?.Title
	);

	public static PublicationDetailDto ToDetailDto(this Publication pub, List<MediaFile> availableMedia, string publicBaseUrl) => new(
		pub.Id,
		pub.Status.ToString(),
		pub.PublishTarget.Name,
		pub.PublishTarget.Platform.ToString(),
		pub.GeneratedContent,
		availableMedia.Select(m => m.ToDto(publicBaseUrl)).ToList(),
		pub.SelectedMediaFileIds,
		pub.CreatedAt,
		pub.ApprovedAt,
		pub.PublishedAt,
		pub.RejectionReason
	);
}
