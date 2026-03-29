namespace Infrastructure.Configuration;

public class TelegramOptions
{
	public const string SectionName = "Telegram";
	public string BotToken { get; set; } = string.Empty;
}