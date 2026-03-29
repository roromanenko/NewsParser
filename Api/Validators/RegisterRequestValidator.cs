using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
	public RegisterRequestValidator()
	{
		RuleFor(x => x.Email)
			.NotEmpty().WithMessage("Email is required")
			.EmailAddress().WithMessage("Invalid email format");

		RuleFor(x => x.FirstName)
			.NotEmpty().WithMessage("First name is required");

		RuleFor(x => x.LastName)
			.NotEmpty().WithMessage("Last name is required");

		RuleFor(x => x.Password)
			.NotEmpty().WithMessage("Password is required")
			.MinimumLength(8).WithMessage("Password must be at least 8 characters")
			.Matches(@"\d").WithMessage("Password must contain at least one digit")
			.Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter");
	}
}
