using Api.Controllers;
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

		var items = events.Select(e => new EventListItemDto(
			e.Id,
			e.Title,
			e.Summary,
			e.Status.ToString(),
			e.FirstSeenAt,
			e.LastUpdatedAt,
			e.EventArticles.Count,
			e.Contradictions.Count(c => !c.IsResolved)
		)).ToList();

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

		return Ok(new EventDetailDto(
			evt.Id,
			evt.Title,
			evt.Summary,
			evt.Status.ToString(),
			evt.FirstSeenAt,
			evt.LastUpdatedAt,
			evt.EventArticles.Select(ea => new EventArticleDto(
				ea.ArticleId,
				ea.Article.Title,
				ea.Article.Summary,
				ea.Role.ToString(),
				ea.AddedAt
			)).ToList(),
			evt.EventUpdates.Select(eu => new EventUpdateDto(
				eu.Id,
				eu.FactSummary,
				eu.IsPublished,
				eu.CreatedAt
			)).OrderBy(u => u.CreatedAt).ToList(),
			evt.Contradictions.Select(c => new ContradictionDto(
				c.Id,
				c.Description,
				c.IsResolved,
				c.CreatedAt,
				c.ContradictionArticles.Select(ca => ca.ArticleId).ToList()
			)).ToList(),
			evt.EventArticles.Count(ea => ea.WasReclassified)
		));
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
			id, request.ArticleId, role, cancellationToken);

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