using Api.Mappers;
using Api.Models;
using Core.DomainModels;
using Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("ai-operations")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class AiOperationsController(IAiRequestLogRepository repository) : BaseController
{
    [HttpGet("metrics")]
    public async Task<ActionResult<AiOperationsMetricsDto>> GetMetrics(
        [FromQuery] AiOperationsMetricsQuery query,
        CancellationToken cancellationToken = default)
    {
        var filter = new AiRequestLogFilter(query.From, query.To, query.Provider, query.Worker, query.Model,
            Status: null, Search: null);
        var metrics = await repository.GetMetricsAsync(filter, cancellationToken);
        return Ok(metrics.ToDto());
    }

    [HttpGet("requests")]
    public async Task<ActionResult<PagedResult<AiRequestLogDto>>> GetRequests(
        [FromQuery] AiRequestsListQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var filter = new AiRequestLogFilter(
            query.From, query.To, query.Provider, query.Worker, query.Model,
            query.Status, query.Search);

        var items = await repository.GetPagedAsync(filter, page, pageSize, cancellationToken);
        var total = await repository.CountAsync(filter, cancellationToken);

        return Ok(new PagedResult<AiRequestLogDto>(
            items.Select(l => l.ToDto()).ToList(), page, pageSize, total));
    }

    [HttpGet("requests/{id:guid}")]
    public async Task<ActionResult<AiRequestLogDto>> GetRequestById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var log = await repository.GetByIdAsync(id, cancellationToken);
        if (log is null)
            return NotFound();
        return Ok(log.ToDto());
    }
}
