using Api.Models;
using Api.Validators.Shared;
using FluentValidation;

namespace Api.Validators;

public class AiOperationsMetricsQueryValidator : AbstractValidator<AiOperationsMetricsQuery>
{
    public AiOperationsMetricsQueryValidator()
    {
        this.AddSharedDateAndStringLengthRules(
            from: x => x.From,
            to: x => x.To,
            provider: x => x.Provider,
            worker: x => x.Worker,
            model: x => x.Model);
    }
}
