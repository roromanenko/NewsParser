using Core.DomainModels;

namespace Core.Interfaces.Services;

public interface IUserService
{
	Task<User?> VerifyLoginAsync(string email, string password, CancellationToken cancellationToken = default);
	Task<User> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<List<User?>> GetAllUsers(CancellationToken cancellationToken = default);
	Task<User> CreateUserAsync(string email, string firstName, string lastName, string password, UserRole role, CancellationToken cancellationToken = default);
	Task<User> UpdateEditorAsync(Guid id, string firstName, string lastName, string email, CancellationToken cancellationToken = default);
	Task DeleteEditorAsync(Guid id, CancellationToken cancellationToken = default);
}