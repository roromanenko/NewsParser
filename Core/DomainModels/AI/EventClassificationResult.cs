namespace Core.DomainModels.AI;

public class EventClassificationResult
{
	public bool IsNewEvent { get; set; }
	public Guid? MatchedEventId { get; set; }
	public bool IsSignificantUpdate { get; set; }
	public List<string> NewFacts { get; set; } = [];
	public List<ContradictionInput> Contradictions { get; set; } = [];
	public string Reasoning { get; set; } = string.Empty;
}

public class ContradictionInput
{
	public List<Guid> ArticleIds { get; set; } = [];
	public string Description { get; set; } = string.Empty;
}