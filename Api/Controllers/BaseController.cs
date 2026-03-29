using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

public class BaseController : ControllerBase
{
	protected string? UserEmail => User?.Identity?.Name;

	protected Guid? UserId
	{
		get
		{
			var value = User?.Claims?.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value;
			return Guid.TryParse(value, out var id) ? id : null;
		}
	}
}