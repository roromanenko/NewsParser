using Core.DomainModels;

namespace Core.Interfaces.Services;

public interface IPublicationMediaService
{
    Task<MediaFile> UploadAsync(
        Guid publicationId,
        Guid uploadedByUserId,
        Stream content,
        string fileName,
        string contentType,
        long sizeBytes,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid publicationId,
        Guid mediaFileId,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default);
}
