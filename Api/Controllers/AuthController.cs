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
	IJwtService jwtService,
	ILogger<AuthController> logger) : ControllerBase
{
	[HttpPost("login")]
	public async Task<ActionResult<LoginResponse>> Login(
		[FromBody] LoginRequest request,
		CancellationToken cancellationToken = default)
	{
		var user = await userService.VerifyLoginAsync(request.Email, request.Password, cancellationToken);

		if (user is null)
		{
			logger.LogWarning("Failed login attempt for {Email}", request.Email);
			return Unauthorized("Invalid email or password");
		}

		var token = jwtService.GenerateToken(user);
		logger.LogInformation("User {Email} logged in", user.Email);

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
			logger.LogInformation("User {UserId} registered with role {Role}", user.Id, user.Role);

			return CreatedAtAction(nameof(Login), user.ToLoginResponse(token));
		}
		catch (InvalidOperationException)
		{
			return Conflict("Email is already registered");
		}
	}
}
