using Api.Models;
using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("articles")]
[Authorize(Roles = nameof(UserRole.Editor) + "," + nameof(UserRole.Admin))]
public class ArticlesController(
	IArticleRepository articleRepository,
	IArticleApprovalService approvalService,
	IEventRepository eventRepository) : BaseController
{
	[HttpGet]
	public async Task<ActionResult<PagedResult<ArticleListItemDto>>> GetPending(
		[FromQuery] int page = 1,
		[FromQuery] int pageSize = 20,
		CancellationToken cancellationToken = default)
	{
		if (page < 1) page = 1;
		if (pageSize is < 1 or > 100) pageSize = 20;

		var articles = await articleRepository.GetPendingForApprovalAsync(page, pageSize, cancellationToken);
		var total = await articleRepository.CountPendingForApprovalAsync(cancellationToken);

		var items = articles.Select(a => new ArticleListItemDto(
			a.Id, a.Title, a.Category, a.Tags,
			a.Sentiment.ToString(), a.Language, a.Summary, a.ProcessedAt
		)).ToList();

		return Ok(new PagedResult<ArticleListItemDto>(items, page, pageSize, total));
	}

	[HttpGet("{id:guid}")]
	public async Task<ActionResult<ArticleDetailDto>> GetById(
	Guid id,
	CancellationToken cancellationToken = default)
	{
		var article = await articleRepository.GetByIdAsync(id, cancellationToken);
		if (article is null)
			return NotFound();

		// Ищем связанное событие
		ArticleEventDto? eventDto = null;
		if (article.EventId is not null)
		{
			var evt = await eventRepository.GetByIdAsync(article.EventId.Value, cancellationToken);
			if (evt is not null)
			{
				eventDto = new ArticleEventDto(
					evt.Id,
					evt.Title,
					evt.Status.ToString(),
					article.Role?.ToString() ?? string.Empty
				);
			}
		}

		return Ok(new ArticleDetailDto(
			article.Id, article.Title, article.Content, article.Category,
			article.Tags, article.Sentiment.ToString(), article.Language,
			article.Summary, article.ProcessedAt, article.ModelVersion,
			new RawArticleDto(
				article.RawArticle.Id, article.RawArticle.Title,
				article.RawArticle.OriginalUrl, article.RawArticle.PublishedAt,
				article.RawArticle.Language
			),
			eventDto
		));
	}

	[HttpPost("{id:guid}/approve")]
	public async Task<ActionResult<ArticleListItemDto>> Approve(
	Guid id,
	[FromBody] ApproveArticleRequest request,
	CancellationToken cancellationToken = default)
	{
		if (request.PublishTargetIds is null || request.PublishTargetIds.Count == 0)
			return BadRequest("At least one publish target must be specified");

		if (UserId is null)
			return Unauthorized();

		var article = await approvalService.ApproveAsync(
			id,
			UserId.Value,
			request.PublishTargetIds,
			cancellationToken);

		return Ok(new ArticleListItemDto(
			article.Id, article.Title, article.Category, article.Tags,
			article.Sentiment.ToString(), article.Language, article.Summary, article.ProcessedAt
		));
	}

	[HttpPost("{id:guid}/reject")]
	public async Task<ActionResult<ArticleListItemDto>> Reject(
		Guid id,
		[FromBody] RejectArticleRequest request,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(request.Reason))
			return BadRequest("Rejection reason is required");

		if (UserId is null)
			return Unauthorized();

		var article = await approvalService.RejectAsync(id, UserId.Value, request.Reason, cancellationToken);

		return Ok(new ArticleListItemDto(
			article.Id, article.Title, article.Category, article.Tags,
			article.Sentiment.ToString(), article.Language, article.Summary, article.ProcessedAt
		));
	}
}