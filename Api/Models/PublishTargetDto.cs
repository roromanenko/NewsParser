namespace Api.Models;

public record PublishTargetDto(
	Guid Id,
	string Name,
	string Platform,
	string Identifier,
	string SystemPrompt,
	bool IsActive
);

public record CreatePublishTargetRequest(
	string Name,
	string Platform,
	string Identifier,
	string SystemPrompt
);

public record UpdatePublishTargetRequest(
	string Name,
	string Identifier,
	string SystemPrompt,
	bool IsActive
);