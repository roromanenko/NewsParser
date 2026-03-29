namespace Api.Models;

public record RegisterRequest(
	string Email,
	string FirstName,
	string LastName,
	string Password
);
