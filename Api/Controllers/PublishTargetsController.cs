using Api.Models;
using Core.DomainModels;
using Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("publish-targets")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class PublishTargetsController(IPublishTargetService publishTargetService) : BaseController
{
	[HttpGet]
	public async Task<ActionResult<List<PublishTargetDto>>> GetAll(
		CancellationToken cancellationToken = default)
	{
		var targets = await publishTargetService.GetAllAsync(cancellationToken);
		return Ok(targets.Select(ToDto).ToList());
	}

	[HttpGet("active")]
	public async Task<ActionResult<List<PublishTargetDto>>> GetActive(
		CancellationToken cancellationToken = default)
	{
		var targets = await publishTargetService.GetActiveAsync(cancellationToken);
		return Ok(targets.Select(ToDto).ToList());
	}

	[HttpGet("{id:guid}")]
	public async Task<ActionResult<PublishTargetDto>> GetById(
		Guid id,
		CancellationToken cancellationToken = default)
	{
		var target = await publishTargetService.GetByIdAsync(id, cancellationToken);
		return Ok(ToDto(target));
	}

	[HttpPost]
	public async Task<ActionResult<PublishTargetDto>> Create(
		[FromBody] CreatePublishTargetRequest request,
		CancellationToken cancellationToken = default)
	{
		if (!Enum.TryParse<Platform>(request.Platform, ignoreCase: true, out var platform))
			return BadRequest($"Invalid platform: {request.Platform}");

		if (string.IsNullOrWhiteSpace(request.Name))
			return BadRequest("Name is required");

		if (string.IsNullOrWhiteSpace(request.Identifier))
			return BadRequest("Identifier is required");

		if (string.IsNullOrWhiteSpace(request.SystemPrompt))
			return BadRequest("SystemPrompt is required");

		var target = await publishTargetService.CreateAsync(
			request.Name,
			platform,
			request.Identifier,
			request.SystemPrompt,
			cancellationToken);

		return CreatedAtAction(nameof(GetById), new { id = target.Id }, ToDto(target));
	}

	[HttpPut("{id:guid}")]
	public async Task<ActionResult<PublishTargetDto>> Update(
		Guid id,
		[FromBody] UpdatePublishTargetRequest request,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(request.Name))
			return BadRequest("Name is required");

		if (string.IsNullOrWhiteSpace(request.Identifier))
			return BadRequest("Identifier is required");

		if (string.IsNullOrWhiteSpace(request.SystemPrompt))
			return BadRequest("SystemPrompt is required");

		var target = await publishTargetService.UpdateAsync(
			id,
			request.Name,
			request.Identifier,
			request.SystemPrompt,
			request.IsActive,
			cancellationToken);

		return Ok(ToDto(target));
	}

	[HttpDelete("{id:guid}")]
	public async Task<IActionResult> Delete(
		Guid id,
		CancellationToken cancellationToken = default)
	{
		await publishTargetService.DeleteAsync(id, cancellationToken);
		return NoContent();
	}

	private static PublishTargetDto ToDto(PublishTarget target) => new(
		target.Id,
		target.Name,
		target.Platform.ToString(),
		target.Identifier,
		target.SystemPrompt,
		target.IsActive
	);
}