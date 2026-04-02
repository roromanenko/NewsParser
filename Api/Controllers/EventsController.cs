using Api.Controllers;
using Api.Mappers;
using Api.Models;
using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("events")]
[Authorize(Roles = nameof(UserRole.Editor) + "," + nameof(UserRole.Admin))]
public class EventsController(
	IEventRepository eventRepository,
	IEventService eventService) : BaseController
{
	[HttpGet]
	public async Task<ActionResult<PagedResult<EventListItemDto>>> GetAll(
		[FromQuery] int page = 1,
		[FromQuery] int pageSize = 20,
		CancellationToken cancellationToken = default)
	{
		if (page < 1) page = 1;
		if (pageSize is < 1 or > 100) pageSize = 20;

		var events = await eventRepository.GetPagedAsync(page, pageSize, cancellationToken);
		var total = await eventRepository.CountActiveAsync(cancellationToken);

		var items = events.Select(e => e.ToListItemDto()).ToList();

		return Ok(new PagedResult<EventListItemDto>(items, page, pageSize, total));
	}

	[HttpGet("{id:guid}")]
	public async Task<ActionResult<EventDetailDto>> GetById(
		Guid id,
		CancellationToken cancellationToken = default)
	{
		var evt = await eventRepository.GetDetailAsync(id, cancellationToken);
		if (evt is null)
			return NotFound();

		return Ok(evt.ToDetailDto());
	}

	[HttpPost("{id:guid}/resolve-contradiction")]
	public async Task<IActionResult> ResolveContradiction(
		Guid id,
		[FromBody] ResolveContradictionRequest request,
		CancellationToken cancellationToken = default)
	{
		var evt = await eventRepository.GetByIdAsync(id, cancellationToken);
		if (evt is null)
			return NotFound();

		await eventRepository.ResolveContradictionAsync(
			request.ContradictionId, cancellationToken);

		return NoContent();
	}

	[HttpPost("merge")]
	[Authorize(Roles = nameof(UserRole.Admin))]
	public async Task<IActionResult> Merge(
		[FromBody] MergeEventsRequest request,
		CancellationToken cancellationToken = default)
	{
		if (request.SourceEventId == request.TargetEventId)
			return BadRequest("Source and target events must be different");

		await eventService.MergeAsync(
			request.SourceEventId,
			request.TargetEventId,
			cancellationToken);

		return NoContent();
	}

	[HttpPost("{id:guid}/reclassify")]
	[Authorize(Roles = nameof(UserRole.Admin))]
	public async Task<IActionResult> ReclassifyArticle(
		Guid id,
		[FromBody] ReclassifyArticleRequest request,
		CancellationToken cancellationToken = default)
	{
		if (!Enum.TryParse<EventArticleRole>(request.Role, ignoreCase: true, out var role))
			return BadRequest($"Invalid role: {request.Role}. Valid values: " +
				$"{string.Join(", ", Enum.GetNames<EventArticleRole>())}");

		await eventService.ReclassifyArticleAsync(
			id, request.ArticleId, request.TargetEventId, role, cancellationToken);

		return NoContent();
	}

	[HttpPatch("{id:guid}/status")]
	[Authorize(Roles = nameof(UserRole.Admin))]
	public async Task<IActionResult> UpdateStatus(
		Guid id,
		[FromBody] string status,
		CancellationToken cancellationToken = default)
	{
		var evt = await eventRepository.GetByIdAsync(id, cancellationToken);
		if (evt is null)
			return NotFound();

		if (!Enum.TryParse<EventStatus>(status, ignoreCase: true, out var eventStatus))
			return BadRequest($"Invalid status: {status}. Valid values: " +
				$"{string.Join(", ", Enum.GetNames<EventStatus>())}");

		await eventRepository.UpdateStatusAsync(id, eventStatus, cancellationToken);

		return NoContent();
	}
}