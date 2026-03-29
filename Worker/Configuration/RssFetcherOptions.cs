namespace Worker.Configuration;

public class RssFetcherOptions
{
	public const string SectionName = "RssFetcher";
	public int IntervalSeconds { get; set; } = 600; // 10 minutes by default
}