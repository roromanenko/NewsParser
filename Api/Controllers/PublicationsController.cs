using Api.Mappers;
using Api.Models;
using Core.DomainModels;
using Core.Interfaces;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Api.Controllers;

[ApiController]
[Route("projects/{projectId:guid}/publications")]
[Authorize(Roles = nameof(UserRole.Editor) + "," + nameof(UserRole.Admin))]
public class PublicationsController(
    IPublicationService publicationService,
    IPublicationRepository publicationRepository,
    IMediaFileRepository mediaFileRepository,
    IPublicationMediaService publicationMediaService,
    IProjectContext projectContext,
    IOptions<CloudflareR2Options> r2Options) : BaseController
{
    // 25 MB allows multipart framing overhead above the enforced 20 MB file cap (PublicationMediaOptions.MaxUploadBytes)
    private const long UploadRequestSizeLimit = 25L * 1024 * 1024;

    private readonly string _publicBaseUrl = r2Options.Value.PublicBaseUrl;

    [HttpGet]
    public async Task<ActionResult<PagedResult<PublicationListItemDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var publications = await publicationRepository.GetAllAsync(projectContext.ProjectId, page, pageSize, cancellationToken);
        var total = await publicationRepository.CountAllAsync(projectContext.ProjectId, cancellationToken);

        return Ok(new PagedResult<PublicationListItemDto>(
            publications.Select(p => p.ToListItemDto()).ToList(),
            page,
            pageSize,
            total));
    }

    [HttpPost("generate")]
    public async Task<ActionResult<PublicationListItemDto>> Generate(
        [FromBody] CreatePublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (UserId is null)
            return Unauthorized();

        var publication = await publicationService.CreateForEventAsync(
            request.EventId, request.PublishTargetId, UserId.Value, projectContext.ProjectId, cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { projectId = projectContext.ProjectId, id = publication.Id },
            publication.ToListItemDto());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PublicationDetailDto>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var publication = await publicationRepository.GetDetailAsync(id, cancellationToken);
        if (publication is null)
            return NotFound();

        var availableMedia = await ExtractAvailableMediaAsync(publication, cancellationToken);

        return Ok(publication.ToDetailDto(availableMedia, _publicBaseUrl));
    }

    [HttpGet("by-event/{eventId:guid}")]
    public async Task<ActionResult<List<PublicationListItemDto>>> GetByEvent(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var publications = await publicationRepository.GetByEventIdAsync(eventId, cancellationToken);
        var items = publications.Select(p => p.ToListItemDto()).ToList();

        return Ok(items);
    }

    [HttpPut("{id:guid}/content")]
    public async Task<ActionResult<PublicationDetailDto>> UpdateContent(
        Guid id,
        [FromBody] UpdatePublicationContentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (UserId is null)
            return Unauthorized();

        await publicationService.UpdateContentAsync(
            id, request.Content, request.SelectedMediaFileIds, cancellationToken);

        var detail = await publicationRepository.GetDetailAsync(id, cancellationToken);
        if (detail is null)
            return NotFound();

        var availableMedia = await ExtractAvailableMediaAsync(detail, cancellationToken);

        return Ok(detail.ToDetailDto(availableMedia, _publicBaseUrl));
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<PublicationDetailDto>> Approve(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (UserId is null)
            return Unauthorized();

        await publicationService.ApproveAsync(id, UserId.Value, cancellationToken);

        var detail = await publicationRepository.GetDetailAsync(id, cancellationToken);
        if (detail is null)
            return NotFound();

        var availableMedia = await ExtractAvailableMediaAsync(detail, cancellationToken);

        return Ok(detail.ToDetailDto(availableMedia, _publicBaseUrl));
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<PublicationDetailDto>> Reject(
        Guid id,
        [FromBody] RejectPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (UserId is null)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("Rejection reason is required");

        await publicationService.RejectAsync(id, UserId.Value, request.Reason, cancellationToken);

        var detail = await publicationRepository.GetDetailAsync(id, cancellationToken);
        if (detail is null)
            return NotFound();

        var availableMedia = await ExtractAvailableMediaAsync(detail, cancellationToken);

        return Ok(detail.ToDetailDto(availableMedia, _publicBaseUrl));
    }

    [HttpPost("{id:guid}/send")]
    public async Task<ActionResult<PublicationDetailDto>> Send(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (UserId is null)
            return Unauthorized();

        await publicationService.SendAsync(id, UserId.Value, cancellationToken);

        var detail = await publicationRepository.GetDetailAsync(id, cancellationToken);
        if (detail is null)
            return NotFound();

        var availableMedia = await ExtractAvailableMediaAsync(detail, cancellationToken);

        return Ok(detail.ToDetailDto(availableMedia, _publicBaseUrl));
    }

    [HttpPost("{id:guid}/regenerate")]
    public async Task<ActionResult<PublicationDetailDto>> Regenerate(
        Guid id,
        [FromBody] RegeneratePublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (UserId is null)
            return Unauthorized();

        await publicationService.RegenerateAsync(id, request.Feedback, cancellationToken);

        var detail = await publicationRepository.GetDetailAsync(id, cancellationToken);
        if (detail is null)
            return NotFound();

        var availableMedia = await ExtractAvailableMediaAsync(detail, cancellationToken);
        return Ok(detail.ToDetailDto(availableMedia, _publicBaseUrl));
    }

    [HttpPost("{id:guid}/media")]
    [RequestSizeLimit(UploadRequestSizeLimit)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<MediaFileDto>> UploadMedia(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (UserId is null)
            return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest("A non-empty file is required");

        var mediaFile = await publicationMediaService.UploadAsync(
            id, UserId.Value, file.OpenReadStream(), file.FileName, file.ContentType, file.Length, cancellationToken);

        var dto = mediaFile.ToDto(_publicBaseUrl);

        return CreatedAtAction(nameof(GetById), new { projectId = projectContext.ProjectId, id }, dto);
    }

    [HttpDelete("{id:guid}/media/{mediaId:guid}")]
    public async Task<IActionResult> DeleteMedia(
        Guid id,
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        if (UserId is null)
            return Unauthorized();

        await publicationMediaService.DeleteAsync(id, mediaId, UserId.Value, cancellationToken);

        return NoContent();
    }

    private async Task<List<MediaFile>> ExtractAvailableMediaAsync(
        Publication publication,
        CancellationToken cancellationToken)
    {
        var eventMedia = publication.Event is null
            ? []
            : publication.Event.Articles.SelectMany(a => a.MediaFiles).ToList();
        var customMedia = await mediaFileRepository.GetByPublicationIdAsync(
            publication.Id, cancellationToken);
        return [.. eventMedia, .. customMedia];
    }
}
