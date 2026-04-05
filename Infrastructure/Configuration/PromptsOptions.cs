namespace Infrastructure.Configuration;

public class PromptsOptions
{
	public const string SectionName = "Prompts";

	public string AnalyzerPath { get; set; } = "Prompts/analyzer.txt";
	public string GeneratorPath { get; set; } = "Prompts/generator.txt";
	public string TelegramPath { get; set; } = "Prompts/telegram.txt";
	public string EventClassifierPath { get; set; } = "Prompts/event_classifier.txt";
	public string EventSummaryUpdaterPath { get; set; } = "Prompts/event_summary_updater.txt";
	public string ContradictionDetectorPath { get; set; } = "Prompts/contradiction_detector.txt";

	public string Analyzer => ReadPrompt(AnalyzerPath);
	public string Generator => ReadPrompt(GeneratorPath);
	public string Telegram => ReadPrompt(TelegramPath);
	public string EventClassifier => ReadPrompt(EventClassifierPath);
	public string EventSummaryUpdater => ReadPrompt(EventSummaryUpdaterPath);
	public string ContradictionDetector => ReadPrompt(ContradictionDetectorPath);

	private static string ReadPrompt(string relativePath)
	{
		var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
		return File.ReadAllText(fullPath);
	}
}