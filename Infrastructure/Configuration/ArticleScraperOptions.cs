namespace Infrastructure.Configuration;

public class ArticleScraperOptions
{
    public const string SectionName = "ArticleScraper";

    public bool Enabled { get; set; } = true;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int MaxHtmlSizeBytes { get; set; } = 2097152;
    public int MaxConcurrencyPerFeed { get; set; } = 4;
    public string UserAgent { get; set; } = "NewsParserBot/1.0";
    public int MaxTagsPerArticle { get; set; } = 20;
}
