using Api.Mappers;
using Api.Models;
using Core.DomainModels;
using Core.Interfaces;
using Core.Interfaces.Repositories;
using Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Api.Controllers;

[ApiController]
[Route("projects/{projectId:guid}/articles")]
[Authorize(Roles = nameof(UserRole.Editor) + "," + nameof(UserRole.Admin))]
public class ArticlesController(
	IArticleRepository articleRepository,
	IEventRepository eventRepository,
	IProjectContext projectContext,
	IOptions<CloudflareR2Options> r2Options) : BaseController
{
	private readonly string _publicBaseUrl = r2Options.Value.PublicBaseUrl;

	[HttpGet]
	public async Task<ActionResult<PagedResult<ArticleListItemDto>>> GetAnalysisDone(
		[FromQuery] int page = 1,
		[FromQuery] int pageSize = PaginationDefaults.DefaultPageSize,
		[FromQuery] string? search = null,
		[FromQuery] string? sortBy = null,
		CancellationToken cancellationToken = default)
	{
		if (page < 1) page = 1;
		if (pageSize is < 1 or > PaginationDefaults.MaxPageSize) pageSize = PaginationDefaults.DefaultPageSize;
		if (!SortOptions.BasicSortValues.Contains(sortBy ?? "")) sortBy = "newest";

		var articles = await articleRepository.GetAnalysisDoneAsync(projectContext.ProjectId, page, pageSize, search, sortBy!, cancellationToken);
		var total = await articleRepository.CountAnalysisDoneAsync(projectContext.ProjectId, search, cancellationToken);

		var items = articles.Select(a => a.ToListItemDto()).ToList();

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

		Event? relatedEvent = null;
		if (article.EventId is not null)
			relatedEvent = await eventRepository.GetByIdAsync(article.EventId.Value, cancellationToken);

		return Ok(article.ToDetailDto(_publicBaseUrl, relatedEvent));
	}
}
