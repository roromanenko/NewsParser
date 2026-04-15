namespace Infrastructure.Configuration;

public class PromptsOptions
{
	private readonly string _targetLanguageName;

	public PromptsOptions(string targetLanguageName)
	{
		_targetLanguageName = targetLanguageName;
	}

	public string AnalyzerPath { get; set; } = "Prompts/analyzer.txt";
	public string GeneratorPath { get; set; } = "Prompts/generator.txt";
	public string TelegramPath { get; set; } = "Prompts/telegram.txt";
	public string EventClassifierPath { get; set; } = "Prompts/event_classifier.txt";
	public string EventSummaryUpdaterPath { get; set; } = "Prompts/event_summary_updater.txt";
	public string ContradictionDetectorPath { get; set; } = "Prompts/contradiction_detector.txt";
	public string HaikuKeyFactsPath { get; set; } = "Prompts/haiku_key_facts.txt";
	public string HaikuEventTitlePath { get; set; } = "Prompts/haiku_event_title.txt";

	public string Analyzer => ReadPrompt(AnalyzerPath);
	public string Generator => ReadPrompt(GeneratorPath);
	public string Telegram => ReadPrompt(TelegramPath);
	public string EventClassifier => ReadPrompt(EventClassifierPath);
	public string EventSummaryUpdater => ReadPrompt(EventSummaryUpdaterPath);
	public string ContradictionDetector => ReadPrompt(ContradictionDetectorPath);
	public string HaikuKeyFacts => ReadPrompt(HaikuKeyFactsPath);
	public string HaikuEventTitle => ReadPrompt(HaikuEventTitlePath);

	private string ReadPrompt(string relativePath)
	{
		var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
		return File.ReadAllText(fullPath).Replace("{OUTPUT_LANGUAGE}", _targetLanguageName);
	}
}
