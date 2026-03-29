namespace Infrastructure.Persistence.Entity;

public class UserEntity
{
	public Guid Id { get; set; }
	public string FirstName { get; set; } = string.Empty;
	public string LastName { get; set; } = string.Empty;
	public string Email { get; set; } = string.Empty;
	public string PasswordHash { get; set; } = string.Empty;
	public string Role { get; set; } = string.Empty;

	public List<PublicationEntity> Publications { get; set; } = [];
}

