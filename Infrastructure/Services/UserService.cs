using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Services;

public class UserService(IUserRepository userRepository) : IUserService
{
	public async Task<User?> VerifyLoginAsync(
		string email,
		string password,
		CancellationToken cancellationToken = default)
	{
		var user = await userRepository.GetByEmailAsync(email, cancellationToken);
		if (user is null)
			return null;

		var hasher = new PasswordHasher<User>();
		var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);

		return result == PasswordVerificationResult.Success ? user : null;
	}

	public async Task<User> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		return await userRepository.GetByIdAsync(id, cancellationToken)
			?? throw new KeyNotFoundException($"User {id} not found");
	}

	public async Task<List<User?>> GetAllUsers(CancellationToken cancellationToken = default)
	{
		return await userRepository.GetUsers(cancellationToken) ?? throw new KeyNotFoundException($"Editors not found");
	}

	public async Task<User> CreateUserAsync(
		string email,
		string firstName,
		string lastName,
		string password,
		UserRole role,
		CancellationToken cancellationToken = default)
	{
		var exists = await userRepository.ExistsByEmailAsync(email, cancellationToken);
		if (exists)
			throw new InvalidOperationException($"User with email {email} already exists");

		var user = new User
		{
			Id = Guid.NewGuid(),
			Email = email,
			FirstName = firstName,
			LastName = lastName,
			Role = role,
		};

		var hasher = new PasswordHasher<User>();
		var passwordHash = hasher.HashPassword(user, password);

		return await userRepository.CreateAsync(user, passwordHash, cancellationToken);
	}

	public async Task<User> UpdateEditorAsync(
	Guid id,
	string firstName,
	string lastName,
	string email,
	CancellationToken cancellationToken = default)
	{
		var user = await userRepository.GetByIdAsync(id, cancellationToken)
			?? throw new KeyNotFoundException($"User {id} not found");

		if (user.Role != UserRole.Editor)
			throw new InvalidOperationException("Cannot modify Admin users");

		if (user.Email != email)
		{
			var exists = await userRepository.ExistsByEmailAsync(email, cancellationToken);
			if (exists)
				throw new InvalidOperationException($"Email {email} is already taken");
		}

		await userRepository.UpdateAsync(id, firstName, lastName, email, cancellationToken);

		user.FirstName = firstName;
		user.LastName = lastName;
		user.Email = email;
		return user;
	}

	public async Task DeleteEditorAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var user = await userRepository.GetByIdAsync(id, cancellationToken)
			?? throw new KeyNotFoundException($"User {id} not found");

		if (user.Role != UserRole.Editor)
			throw new InvalidOperationException("Cannot delete Admin users");

		await userRepository.DeleteAsync(id, cancellationToken);
	}
}