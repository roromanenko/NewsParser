using Infrastructure.Persistence.Migrator;
using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Parsers;
using Core.Interfaces.Publishers;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Core.Interfaces.Storage;
using Core.Interfaces.Validators;
using Infrastructure.AI;
using Infrastructure.Configuration;
using Infrastructure.Parsers;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Dapper;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Persistence.UnitOfWork;
using Infrastructure.Publishers;
using Infrastructure.Services;
using Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using Infrastructure.Validators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
	public static void MigrateDatabase(this IConfiguration configuration)
	{
		var connectionString = configuration.GetConnectionString("NewsParserDbContext")
			?? throw new InvalidOperationException("Connection string 'NewsParserDbContext' is not configured.");
		DbUpMigrator.Migrate(connectionString);
	}

	public static IServiceCollection AddInfrastructure(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		services
			.AddDapper(configuration)
			.AddRepositories()
			.AddParsers(configuration)
			.AddAiServices(configuration)
			.AddPublishers(configuration)
			.AddServices(configuration)
			.AddStorage(configuration);
		return services;
	}

	private static IServiceCollection AddDapper(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		DapperTypeHandlers.Register();
		services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
		services.AddScoped<IUnitOfWork, DapperUnitOfWork>();
		return services;
	}

	private static IServiceCollection AddRepositories(this IServiceCollection services)
	{
		services.AddScoped<ISourceRepository, SourceRepository>();
		services.AddScoped<IArticleRepository, ArticleRepository>();
		services.AddScoped<IPublicationRepository, PublicationRepository>();
		services.AddScoped<IUserRepository, UserRepository>();
		services.AddScoped<IPublishTargetRepository, PublishTargetRepository>();
		services.AddScoped<IEventRepository, EventRepository>();

		return services;
	}

	private static IServiceCollection AddParsers(this IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.SectionName));
		services.AddSingleton<TelegramClientService>();
		services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<TelegramClientService>());
		services.AddSingleton<ITelegramMediaGateway>(sp => sp.GetRequiredService<TelegramClientService>());
		services.AddSingleton<ITelegramChannelReader>(sp => sp.GetRequiredService<TelegramClientService>());
		services.AddScoped<ISourceParser, RssParser>();
		services.AddScoped<ISourceParser, TelegramParser>();

		services.Configure<ArticleScraperOptions>(configuration.GetSection(ArticleScraperOptions.SectionName));
		services.AddScoped<IArticleContentScraper, HtmlArticleContentScraper>();
		services.AddHttpClient("ArticleContentScraper")
			.ConfigureHttpClient((sp, client) =>
			{
				var opts = sp.GetRequiredService<IOptions<ArticleScraperOptions>>().Value;
				client.Timeout = TimeSpan.FromSeconds(opts.RequestTimeoutSeconds);
				client.DefaultRequestHeaders.UserAgent.ParseAdd(opts.UserAgent);
			});

		return services;
	}

	private static IServiceCollection AddAiServices(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		services.AddHttpClient();

		services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

		var aiOptions = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
		var promptsOptions = new PromptsOptions(aiOptions.Normalization.TargetLanguageName);

		services.AddScoped<IArticleAnalyzer>(sp => new GeminiArticleAnalyzer(
			aiOptions.Gemini.ApiKey,
			aiOptions.Gemini.AnalyzerModel,
			promptsOptions.Analyzer,
			sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GeminiArticleAnalyzer)),
			sp.GetRequiredService<ILogger<GeminiArticleAnalyzer>>()
		));

		services.AddScoped<IEventSummaryUpdater>(sp => new ClaudeEventSummaryUpdater(
			aiOptions.Anthropic.ApiKey,
			aiOptions.Anthropic.SummaryUpdaterModel,
			promptsOptions.EventSummaryUpdater,
			sp.GetRequiredService<ILogger<ClaudeEventSummaryUpdater>>()
		));

		var contentGeneratorPrompts = new Dictionary<Platform, string>
		{
			{ Platform.Telegram, promptsOptions.Telegram }
		};

		services.AddScoped<IContentGenerator>(sp => new ClaudeContentGenerator(
			aiOptions.Anthropic.ApiKey,
			aiOptions.Anthropic.ContentGeneratorModel,
			contentGeneratorPrompts,
			sp.GetRequiredService<ILogger<ClaudeContentGenerator>>()
		));

		services.AddScoped<IKeyFactsExtractor>(sp => new HaikuKeyFactsExtractor(
			aiOptions.Anthropic.ApiKey,
			aiOptions.Anthropic.KeyFactsExtractorModel,
			promptsOptions.HaikuKeyFacts,
			sp.GetRequiredService<ILogger<HaikuKeyFactsExtractor>>()
		));

		services.AddScoped<IEventTitleGenerator>(sp => new HaikuEventTitleGenerator(
			aiOptions.Anthropic.ApiKey,
			aiOptions.Anthropic.TitleGeneratorModel,
			promptsOptions.HaikuEventTitle,
			sp.GetRequiredService<ILogger<HaikuEventTitleGenerator>>()
		));

		services.AddScoped<IEventClassifier>(sp => new ClaudeEventClassifier(
			aiOptions.Anthropic.ApiKey,
			aiOptions.Anthropic.ClassifierModel,
			promptsOptions.EventClassifier,
			sp.GetRequiredService<ILogger<ClaudeEventClassifier>>()
		));

		services.AddScoped<IContradictionDetector>(sp => new ClaudeContradictionDetector(
			aiOptions.Anthropic.ApiKey,
			aiOptions.Anthropic.ContradictionDetectorModel,
			promptsOptions.ContradictionDetector,
			sp.GetRequiredService<ILogger<ClaudeContradictionDetector>>()
		));

		services.AddScoped<IGeminiEmbeddingService>(sp => new GeminiEmbeddingService(
			aiOptions.Gemini.ApiKey,
			aiOptions.Gemini.EmbeddingModel,
			sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GeminiEmbeddingService)),
			sp.GetRequiredService<ILogger<GeminiEmbeddingService>>()
		));

		return services;
	}

	private static IServiceCollection AddPublishers(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		var telegramOptions = configuration
			.GetSection(TelegramOptions.SectionName)
			.Get<TelegramOptions>() ?? new TelegramOptions();

		services.AddHttpClient();

		services.AddScoped<IPublisher>(sp => new TelegramPublisher(
			telegramOptions.BotToken,
			sp.GetRequiredService<ILogger<TelegramPublisher>>(),
			sp.GetRequiredService<IHttpClientFactory>().CreateClient()
		));

		return services;
	}

	private static IServiceCollection AddServices(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddScoped<IPublicationService, PublicationService>();
		services.AddScoped<IUserService, UserService>();
		services.AddScoped<IJwtService, JwtService>();
		services.AddScoped<ISourceService, SourceService>();
		services.AddScoped<IPublishTargetService, PublishTargetService>();
		services.Configure<ValidationOptions>(configuration.GetSection(ValidationOptions.SectionName));
		services.AddScoped<IArticleValidator, ArticleValidator>();
		services.AddScoped<IEventService, EventService>();
		services.Configure<EventImportanceOptions>(configuration.GetSection(EventImportanceOptions.SectionName));
		services.AddScoped<IEventImportanceScorer, EventImportanceScorer>();

		return services;
	}

	private static IServiceCollection AddStorage(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		services.Configure<CloudflareR2Options>(configuration.GetSection(CloudflareR2Options.SectionName));
		services.Configure<PublicationMediaOptions>(configuration.GetSection(PublicationMediaOptions.SectionName));
		services.AddScoped<IMediaStorage>(sp =>
			new CloudflareR2Storage(sp.GetRequiredService<IOptions<CloudflareR2Options>>().Value));
		services.AddScoped<IMediaFileRepository, MediaFileRepository>();
		services.AddScoped<IPublicationMediaService, PublicationMediaService>();
		services.AddScoped<IMediaIngestionService, MediaIngestionService>();
		services.AddScoped<IMediaContentDownloader, HttpMediaContentDownloader>();
		services.AddScoped<IMediaContentDownloader, TelegramMediaContentDownloader>();
		services.AddHttpClient("MediaDownloader")
			.ConfigureHttpClient((sp, client) =>
			{
				client.Timeout = TimeSpan.FromSeconds(
					sp.GetRequiredService<IOptions<CloudflareR2Options>>().Value.DownloadTimeoutSeconds);
			});
		return services;
	}
}
