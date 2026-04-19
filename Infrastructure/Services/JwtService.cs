using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Core.DomainModels;
using Core.Interfaces.Services;
using Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Services;

public class JwtService(
	IOptions<JwtOptions> jwtOptions,
	ILogger<JwtService> logger) : IJwtService
{
	private readonly JwtOptions _jwtOptions = jwtOptions.Value;

	public string GenerateToken(User user)
	{
		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, user.Id.ToString()),
			new(ClaimTypes.Name, user.Email),
			new(ClaimTypes.Role, user.Role.ToString()),
			new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
			new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
		};

		var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SecretKey));
		var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

		var token = new JwtSecurityToken(
			issuer: _jwtOptions.Issuer,
			audience: _jwtOptions.Audience,
			claims: claims,
			expires: DateTime.UtcNow.AddHours(_jwtOptions.ExpirationHours),
			signingCredentials: credentials
		);

		var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);
		logger.LogInformation("JWT issued for user {UserId} role {Role}", user.Id, user.Role);
		return tokenValue;
	}
}
