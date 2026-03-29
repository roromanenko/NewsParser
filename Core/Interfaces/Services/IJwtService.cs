using Core.DomainModels;

namespace Core.Interfaces.Services;

public interface IJwtService
{
	string GenerateToken(User user);
}