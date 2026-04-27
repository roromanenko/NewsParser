using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Api.Filters;

public class RequireProjectConvention : IApplicationModelConvention
{
	public void Apply(ApplicationModel application)
	{
		foreach (var controller in application.Controllers)
		{
			var hasProjectRoute = controller.Selectors.Any(s =>
				s.AttributeRouteModel?.Template?.StartsWith("projects/{projectId:guid}",
					StringComparison.OrdinalIgnoreCase) == true);

			if (hasProjectRoute)
				controller.Filters.Add(new RequireProjectAttribute());
		}
	}
}
