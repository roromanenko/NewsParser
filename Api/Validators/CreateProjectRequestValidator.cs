using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
	public CreateProjectRequestValidator()
	{
		RuleFor(x => x.Name).NotEmpty();
		RuleFor(x => x.AnalyzerPromptText).NotEmpty();
		RuleFor(x => x.Categories).Must(c => c.Count >= 1).WithMessage("At least one category required");
		RuleFor(x => x.OutputLanguage).Length(2, 5);
	}
}
