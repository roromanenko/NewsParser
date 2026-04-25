namespace Core.DomainModels;

public record AiRequestLogFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Provider,
    string? Worker,
    string? Model,
    string? Status,
    string? Search);
