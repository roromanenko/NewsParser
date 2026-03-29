using Core.DomainModels;

namespace Core.Interfaces.Validators;

public interface IRawArticleValidator
{
	(bool IsValid, string? Reason) Validate(RawArticle rawArticle);
}