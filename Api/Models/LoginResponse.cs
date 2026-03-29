namespace Api.Models;

public record LoginResponse(Guid UserId, string Email, string Role, string Token);