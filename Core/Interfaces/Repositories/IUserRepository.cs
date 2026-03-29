using Core.DomainModels;

namespace Core.Interfaces.Repositories;

public interface IUserRepository
{
	Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
	Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<List<User?>> GetUsers(CancellationToken cancellationToken = default);
	Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
	Task<User> CreateAsync(User user, string passwordHash, CancellationToken cancellationToken = default);
	Task UpdateAsync(Guid id, string firstName, string lastName, string email, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}