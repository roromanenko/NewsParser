using Core.DomainModels;
using Core.Interfaces.Repositories;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class UserRepository(NewsParserDbContext db) : IUserRepository
{
	public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		var entity = await db.Users
			.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

		return entity?.ToDomain();
	}

	public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var entity = await db.Users
			.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

		return entity?.ToDomain();
	}

	public async Task<List<User?>> GetUsers(CancellationToken cancellationToken = default)
	{
		var editors = await db.Users
			.ToListAsync(cancellationToken);

		return [.. editors.Select(u => u.ToDomain())];
	}

	public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		return await db.Users
			.AnyAsync(u => u.Email == email, cancellationToken);
	}

	public async Task<User> CreateAsync(User user, string passwordHash, CancellationToken cancellationToken = default)
	{
		var entity = new UserEntity
		{
			Id = user.Id,
			FirstName = user.FirstName,
			LastName = user.LastName,
			Email = user.Email,
			PasswordHash = passwordHash,
			Role = user.Role.ToString(),
		};

		await db.Users.AddAsync(entity, cancellationToken);
		await db.SaveChangesAsync(cancellationToken);

		return entity.ToDomain();
	}

	public async Task UpdateAsync(Guid id, string firstName, string lastName, string email, CancellationToken cancellationToken = default)
	{
		await db.Users
			.Where(u => u.Id == id)
			.ExecuteUpdateAsync(u => u
				.SetProperty(x => x.FirstName, firstName)
				.SetProperty(x => x.LastName, lastName)
				.SetProperty(x => x.Email, email),
			cancellationToken);
	}

	public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		await db.Users
			.Where(u => u.Id == id)
			.ExecuteDeleteAsync(cancellationToken);
	}
}