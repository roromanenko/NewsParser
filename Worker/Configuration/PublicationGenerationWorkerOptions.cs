namespace Worker.Configuration;

public class PublicationGenerationWorkerOptions
{
	public const string SectionName = "PublicationGenerationWorker";
	public int IntervalSeconds { get; set; } = 60;
	public int BatchSize { get; set; } = 10;
}
