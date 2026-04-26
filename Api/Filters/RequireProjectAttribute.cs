using Api.ProjectContext;
using Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Api.Filters;

public class RequireProjectAttribute : Attribute, IAsyncActionFilter
{
	private static readonly HashSet<string> WriteMethods =
		new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

	public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
	{
		var services = context.HttpContext.RequestServices;
		var repository = services.GetRequiredService<IProjectRepository>();
		var projectContext = (ProjectContextService)services.GetRequiredService<Core.Interfaces.IProjectContext>();

		var ct = context.HttpContext.RequestAborted;

		if (!context.RouteData.Values.TryGetValue("projectId", out var projectIdRaw)
			|| !Guid.TryParse(projectIdRaw?.ToString(), out var projectId))
		{
			context.Result = new BadRequestObjectResult("Invalid or missing projectId route value");
			return;
		}

		var project = await repository.GetByIdAsync(projectId, ct);
		if (project is null)
		{
			context.Result = new NotFoundObjectResult($"Project {projectId} not found");
			return;
		}

		var isWriteMethod = WriteMethods.Contains(context.HttpContext.Request.Method);
		if (isWriteMethod && !project.IsActive)
		{
			context.Result = new ConflictObjectResult($"Project {projectId} is inactive");
			return;
		}

		projectContext.Set(project);

		await next();
	}
}
