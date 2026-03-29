using System;
using System.Collections.Generic;
using System.Text;

namespace Core.DomainModels;

public class PublishLog
{
	public Guid Id { get; init; }
	public Guid PublicationId { get; init; }
	public Publication Publication { get; init; } = null!;
	public PublishLogStatus Status { get; set; }
	public string? ErrorMessage { get; set; }
	public DateTimeOffset AttemptedAt { get; init; }
	public string? ExternalMessageId { get; set; }
}

public enum PublishLogStatus
{
	Success,
	Failed
}