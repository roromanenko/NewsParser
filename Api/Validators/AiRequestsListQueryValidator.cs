using Api.Models;
using Api.Validators.Shared;
using Core.DomainModels;
using FluentValidation;

namespace Api.Validators;

public class AiRequestsListQueryValidator : AbstractValidator<AiRequestsListQuery>
{
    private static readonly string[] AllowedStatuses =
        [nameof(AiRequestStatus.Success), nameof(AiRequestStatus.Error)];

    public AiRequestsListQueryValidator()
    {
        this.AddSharedDateAndStringLengthRules(
            from: x => x.From,
            to: x => x.To,
            provider: x => x.Provider,
            worker: x => x.Worker,
            model: x => x.Model);

        RuleFor(x => x.Status)
            .Must(s => string.IsNullOrEmpty(s) || AllowedStatuses.Contains(s))
            .WithMessage($"Invalid status. Allowed values: {string.Join(", ", AllowedStatuses)}");

        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
