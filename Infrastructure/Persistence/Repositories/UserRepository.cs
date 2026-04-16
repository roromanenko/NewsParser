using Core.DomainModels;
using Core.Interfaces.Repositories;
using Dapper;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Infrastructure.Persistence.Repositories.Sql;
using Infrastructure.Persistence.UnitOfWork;

namespace Infrastructure.Persistence.Repositories;

internal class UserRepository(IDbConnectionFactory factory, IUnitOfWork uow) : IUserRepository
{
    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entity = await conn.QuerySingleOrDefaultAsync<UserEntity>(
            new CommandDefinition(UserSql.GetByEmail, new { email }, cancellationToken: cancellationToken));
        return entity?.ToDomain();
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entity = await conn.QuerySingleOrDefaultAsync<UserEntity>(
            new CommandDefinition(UserSql.GetById, new { id }, cancellationToken: cancellationToken));
        return entity?.ToDomain();
    }

    public async Task<List<User?>> GetUsers(CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        var entities = await conn.QueryAsync<UserEntity>(
            new CommandDefinition(UserSql.GetAll, cancellationToken: cancellationToken));
        return entities.Select(e => (User?)e.ToDomain()).ToList();
    }

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(UserSql.ExistsByEmail, new { email }, cancellationToken: cancellationToken));
    }

    public async Task<User> CreateAsync(User user, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(UserSql.Insert, new
        {
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            PasswordHash = passwordHash,
            Role = user.Role.ToString(),
        }, cancellationToken: cancellationToken));
        user.PasswordHash = passwordHash;
        return user;
    }

    public async Task UpdateAsync(Guid id, string firstName, string lastName, string email, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(UserSql.Update,
            new { id, firstName, lastName, email },
            cancellationToken: cancellationToken));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await factory.CreateOpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(UserSql.Delete,
            new { id },
            cancellationToken: cancellationToken));
    }
}
