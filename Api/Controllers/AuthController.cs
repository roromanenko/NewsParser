using Api.Mappers;
using Api.Models;
using Core.DomainModels;
using Core.Interfaces.Services;
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

		return Ok(user.ToLoginResponse(token));
	}

	[HttpPost("register")]
	public async Task<ActionResult<LoginResponse>> Register(
		[FromBody] RegisterRequest request,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var user = await userService.CreateUserAsync(
				request.Email,
				request.FirstName,
				request.LastName,
				request.Password,
				UserRole.Editor,
				cancellationToken);

			var token = jwtService.GenerateToken(user);

			return CreatedAtAction(nameof(Login), user.ToLoginResponse(token));
		}
		catch (InvalidOperationException)
		{
			return Conflict("Email is already registered");
		}
	}
}