namespace Api.Models;

public record CreateUserRequest(
	string Email,
	string FirstName,
	string LastName,
	string Password,
	string Role
);