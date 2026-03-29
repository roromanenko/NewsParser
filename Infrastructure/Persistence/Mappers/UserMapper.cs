using Core.DomainModels;
using Infrastructure.Persistence.Entity;

namespace Infrastructure.Persistence.Mappers;

public static class UserMapper
{
	public static User ToDomain(this UserEntity entity) => new()
	{
		Id = entity.Id,
		FirstName = entity.FirstName,
		LastName = entity.LastName,
		Email = entity.Email,
		PasswordHash = entity.PasswordHash,
		Role = Enum.Parse<UserRole>(entity.Role),
	};
}