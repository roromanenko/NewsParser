using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class RegeneratePublicationRequestValidator : AbstractValidator<RegeneratePublicationRequest>
{
    public RegeneratePublicationRequestValidator()
    {
        RuleFor(x => x.Feedback).NotEmpty().MaximumLength(2000);
    }
}
