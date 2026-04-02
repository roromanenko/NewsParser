using Api.Models;
using Core.DomainModels;

namespace Api.Mappers;

public static class UserMapper
{
    public static UserDto ToDto(this User user) => new(
        user.Id, user.Email, user.FirstName, user.LastName, user.Role.ToString()
    );

    public static LoginResponse ToLoginResponse(this User user, string token) => new(
        user.Id, user.Email, user.Role.ToString(), token
    );
}
