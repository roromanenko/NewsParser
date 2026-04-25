namespace Core.DomainModels.AI;

public record AiRequestLogEntry(
	string Provider,
	string Operation,
	string Model,
	AiUsage Usage,
	int LatencyMs,
	AiRequestStatus Status,
	string? ErrorMessage,
	Guid CorrelationId,
	Guid? ArticleId,
	string Worker);
