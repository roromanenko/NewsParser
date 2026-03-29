namespace Core.Options;

public class JwtOptions
{
	public const string SectionName = "Jwt";

	public string SecretKey { get; set; } = string.Empty;
	public string Issuer { get; set; } = string.Empty;
	public string Audience { get; set; } = string.Empty;
	public int ExpirationHours { get; set; } = 24;
}