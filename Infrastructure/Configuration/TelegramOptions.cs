namespace Infrastructure.Configuration;

public class TelegramOptions
{
	public const string SectionName = "Telegram";
	public string BotToken { get; set; } = string.Empty;
	public int ApiId { get; set; }
	public string ApiHash { get; set; } = string.Empty;
	public string PhoneNumber { get; set; } = string.Empty;
	public string SessionFilePath { get; set; } = "telegram.session";
}