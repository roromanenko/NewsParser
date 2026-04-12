namespace Worker.Configuration;

public class PublishingWorkerOptions
{
	public const string SectionName = "PublishingWorker";
	public int IntervalSeconds { get; set; } = 30;
	public int BatchSize { get; set; } = 10;
}
