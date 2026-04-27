using Api.Mappers;
using Api.Models;
using Core.DomainModels;
using Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("projects")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class ProjectsController(IProjectService projectService) : BaseController
{
	[HttpGet]
	public async Task<ActionResult<List<ProjectListItemDto>>> GetAll(CancellationToken cancellationToken = default)
	{
		var projects = await projectService.GetAllAsync(cancellationToken);
		return Ok(projects.Select(p => p.ToListItemDto()).ToList());
	}

	[HttpGet("{id:guid}")]
	public async Task<ActionResult<ProjectDetailDto>> GetById(Guid id, CancellationToken cancellationToken = default)
	{
		var project = await projectService.GetByIdAsync(id, cancellationToken);
		if (project is null)
			return NotFound();

		return Ok(project.ToDetailDto());
	}

	[HttpPost]
	public async Task<ActionResult<ProjectDetailDto>> Create(
		[FromBody] CreateProjectRequest request,
		CancellationToken cancellationToken = default)
	{
		var project = new Project
		{
			Name = request.Name,
			Slug = request.Slug ?? string.Empty,
			AnalyzerPromptText = request.AnalyzerPromptText,
			Categories = request.Categories,
			OutputLanguage = request.OutputLanguage,
			OutputLanguageName = request.OutputLanguageName,
		};
		var created = await projectService.CreateAsync(project, cancellationToken);
		return CreatedAtAction(nameof(GetById), new { id = created.Id }, created.ToDetailDto());
	}

	[HttpPut("{id:guid}")]
	public async Task<ActionResult<ProjectDetailDto>> Update(
		Guid id,
		[FromBody] UpdateProjectRequest request,
		CancellationToken cancellationToken = default)
	{
		var existing = await projectService.GetByIdAsync(id, cancellationToken);
		if (existing is null)
			return NotFound();

		existing.Name = request.Name;
		existing.AnalyzerPromptText = request.AnalyzerPromptText;
		existing.Categories = request.Categories;
		existing.OutputLanguage = request.OutputLanguage;
		existing.OutputLanguageName = request.OutputLanguageName;
		existing.IsActive = request.IsActive;

		await projectService.UpdateAsync(existing, cancellationToken);
		return Ok(existing.ToDetailDto());
	}

	[HttpPatch("{id:guid}/status")]
	public async Task<IActionResult> UpdateStatus(
		Guid id,
		[FromBody] bool isActive,
		CancellationToken cancellationToken = default)
	{
		await projectService.UpdateActiveAsync(id, isActive, cancellationToken);
		return NoContent();
	}

	[HttpDelete("{id:guid}")]
	public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
	{
		await projectService.DeleteAsync(id, cancellationToken);
		return NoContent();
	}
}
