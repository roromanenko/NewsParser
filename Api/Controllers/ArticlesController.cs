using Api.Mappers;
using Api.Models;
using Core.DomainModels;
using Core.Interfaces.Repositories;
using Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Api.Controllers;

[ApiController]
[Route("articles")]
[Authorize(Roles = nameof(UserRole.Editor) + "," + nameof(UserRole.Admin))]
public class ArticlesController(
	IArticleRepository articleRepository,
	IEventRepository eventRepository,
	IOptions<CloudflareR2Options> r2Options) : BaseController
{
	private readonly string _publicBaseUrl = r2Options.Value.PublicBaseUrl;

	[HttpGet]
	public async Task<ActionResult<PagedResult<ArticleListItemDto>>> GetAnalysisDone(
		[FromQuery] int page = 1,
		[FromQuery] int pageSize = 20,
		CancellationToken cancellationToken = default)
	{
		if (page < 1) page = 1;
		if (pageSize is < 1 or > 100) pageSize = 20;

		var articles = await articleRepository.GetAnalysisDoneAsync(page, pageSize, cancellationToken);
		var total = await articleRepository.CountAnalysisDoneAsync(cancellationToken);

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
