using Api.Models;
using Core.DomainModels;
using Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("users")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class UsersController(IUserService userService) : BaseController
{
	[HttpGet("all")]
	public async Task<ActionResult<List<UserDto>>> GetAllUsers(CancellationToken cancellationToken = default)
	{
		var users = await userService.GetAllUsers(cancellationToken);
		return Ok(users.Select(user => new UserDto(
			user!.Id, user.Email, user.FirstName, user.LastName, user.Role.ToString()
		)).ToList());
	}

	[HttpPost("users")]
	public async Task<ActionResult<UserDto>> CreateUser(
		[FromBody] CreateUserRequest request,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(request.Email))
			return BadRequest("Email is required");

		if (string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("Password is required");

		if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
			return BadRequest($"Invalid role. Valid values: {string.Join(", ", Enum.GetNames<UserRole>())}");

		var user = await userService.CreateUserAsync(
			request.Email, request.FirstName, request.LastName,
			request.Password, role, cancellationToken);

		return Ok(new UserDto(user.Id, user.Email, user.FirstName, user.LastName, user.Role.ToString()));
	}

	[HttpPut("editors/{id:guid}")]
	public async Task<ActionResult<UserDto>> UpdateEditor(
		Guid id,
		[FromBody] UpdateEditorRequest request,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(request.Email))
			return BadRequest("Email is required");

		var user = await userService.UpdateEditorAsync(
			id, request.FirstName, request.LastName, request.Email, cancellationToken);

		return Ok(new UserDto(user.Id, user.Email, user.FirstName, user.LastName, user.Role.ToString()));
	}

	[HttpDelete("editors/{id:guid}")]
	public async Task<IActionResult> DeleteEditor(Guid id, CancellationToken cancellationToken = default)
	{
		await userService.DeleteEditorAsync(id, cancellationToken);
		return NoContent();
	}
}