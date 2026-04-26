using Api.Mappers;
using Api.Models;
using Core.DomainModels;
using Core.Interfaces;
using Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("projects/{projectId:guid}/sources")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class SourcesController(ISourceService sourceService, IProjectContext projectContext) : BaseController
{
	[HttpGet]
	public async Task<ActionResult<List<SourceDto>>> GetAll(CancellationToken cancellationToken = default)
	{
		var sources = await sourceService.GetAllByProjectAsync(projectContext.ProjectId, cancellationToken);
		return Ok(sources.Select(s => s.ToDto()).ToList());
	}

	[HttpGet("{id:guid}")]
	public async Task<ActionResult<SourceDto>> GetById(Guid id, CancellationToken cancellationToken = default)
	{
		var source = await sourceService.GetByIdAsync(id, cancellationToken);
		return Ok(source.ToDto());
	}

	[HttpPost]
	public async Task<ActionResult<SourceDto>> Create(
		[FromBody] CreateSourceRequest request,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(request.Name))
			return BadRequest("Name is required");

		if (string.IsNullOrWhiteSpace(request.Url))
			return BadRequest("URL is required");

		if (!Enum.TryParse<SourceType>(request.Type, ignoreCase: true, out var sourceType))
			return BadRequest($"Invalid source type. Valid values: {string.Join(", ", Enum.GetNames<SourceType>())}");

		var source = await sourceService.CreateAsync(request.Name, request.Url, sourceType, projectContext.ProjectId, cancellationToken);
		return CreatedAtAction(nameof(GetById), new { projectId = projectContext.ProjectId, id = source.Id }, source.ToDto());
	}

	[HttpPut("{id:guid}")]
	public async Task<ActionResult<SourceDto>> Update(
		Guid id,
		[FromBody] UpdateSourceRequest request,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(request.Name))
			return BadRequest("Name is required");

		if (string.IsNullOrWhiteSpace(request.Url))
			return BadRequest("URL is required");

		var source = await sourceService.UpdateAsync(id, request.Name, request.Url, request.IsActive, cancellationToken);
		return Ok(source.ToDto());
	}

	[HttpDelete("{id:guid}")]
	public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
	{
		await sourceService.DeleteAsync(id, cancellationToken);
		return NoContent();
	}
}
