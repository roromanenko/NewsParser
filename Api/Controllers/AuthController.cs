using Api.Models;
using Core.Interfaces.Services;
using Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(
	IUserService userService,
	IJwtService jwtService) : ControllerBase
{
	[HttpPost("login")]
	public async Task<ActionResult<LoginResponse>> Login(
		[FromBody] LoginRequest request,
		CancellationToken cancellationToken = default)
	{
		var user = await userService.VerifyLoginAsync(request.Email, request.Password, cancellationToken);

		if (user is null)
			return Unauthorized("Invalid email or password");

		var token = jwtService.GenerateToken(user);

		return Ok(new LoginResponse(user.Id, user.Email, user.Role.ToString(), token));
	}
}