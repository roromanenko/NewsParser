using Core.DomainModels;

namespace Core.Interfaces.Validators;

public interface IArticleValidator
{
	(bool IsValid, string? Reason) Validate(Article article);
}
