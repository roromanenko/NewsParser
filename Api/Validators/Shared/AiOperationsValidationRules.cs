using System.Linq.Expressions;
using FluentValidation;

namespace Api.Validators.Shared;

internal static class AiOperationsValidationRules
{
    internal static void AddSharedDateAndStringLengthRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, DateTimeOffset?>> from,
        Expression<Func<T, DateTimeOffset?>> to,
        Expression<Func<T, string?>> provider,
        Expression<Func<T, string?>> worker,
        Expression<Func<T, string?>> model)
    {
        var fromFunc = from.Compile();
        var toFunc = to.Compile();

        validator.RuleFor(from)
            .LessThan(to)
            .When(x => fromFunc(x) is not null && toFunc(x) is not null)
            .WithMessage("'from' must be earlier than 'to'");

        validator.RuleFor(provider).MaximumLength(50);
        validator.RuleFor(worker).MaximumLength(100);
        validator.RuleFor(model).MaximumLength(100);
    }
}
