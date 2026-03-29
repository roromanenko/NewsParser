using Core.DomainModels;
using Core.Interfaces.AI;
using Core.Interfaces.Parsers;
using Core.Interfaces.Publishers;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using Core.Interfaces.Validators;
using Infrastructure.AI;
using Infrastructure.Configuration;
using Infrastructure.Parsers;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Publishers;
using Infrastructure.Services;
using Infrastructure.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
	public static IServiceCollection AddInfrastructure(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		services
			.AddDatabase(configuration)
			.AddRepositories()
			.AddParsers()
			.AddAiServices(configuration)
			.AddPublishers(configuration)
			.AddServices(configuration);
		return services;
	}

	private static IServiceCollection AddDatabase(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		services.AddDbContext<NewsParserDbContext>(options =>
			options.UseNpgsql(
				configuration.GetConnectionString(nameof(NewsParserDbContext)),
				npgsql => npgsql
							.MigrationsAssembly(typeof(NewsParserDbContext).Assembly.FullName)
							.UseVector()
			));
		return services;
	}

	private static IServiceCollection AddRepositories(this IServiceCollection services)
	{
		services.AddScoped<ISourceRepository, SourceRepository>();
		services.AddScoped<IRawArticleRepository, RawArticleRepository>();
		services.AddScoped<IArticleRepository, ArticleRepository>();
		services.AddScoped<IPublicationRepository, PublicationRepository>();
		services.AddScoped<IUserRepository, UserRepository>();
		services.AddScoped<IPublishTargetRepository, PublishTargetRepository>();
		services.AddScoped<IEventRepository, EventRepository>();

		return services;
	}

	private static IServiceCollection AddParsers(this IServiceCollection services)
	{
		services.AddScoped<ISourceParser, RssParser>();
		return services;
	}

	private static IServiceCollection AddAiServices(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		services.AddHttpClient();

		services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));		

		var aiOptions = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
		var promptsOptions = configuration.GetSection(PromptsOptions.SectionName).Get<PromptsOptions>() ?? new PromptsOptions();

		services.AddScoped<IArticleAnalyzer>(provider => new GeminiArticleAnalyzer(
			aiOptions.Gemini.ApiKey,
			aiOptions.Gemini.AnalyzerModel,
			promptsOptions.Analyzer,
			provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GeminiArticleAnalyzer))
		));

		services.AddScoped<IEventSummaryUpdater>(_ => new ClaudeEventSummaryUpdater(
			aiOptions.Anthropic.ApiKey,
			aiOptions.Anthropic.SummaryUpdaterModel,
			promptsOptions.EventSummaryUpdater
		));

		/*
		services.AddScoped<IArticleAnalyzer>(_ => new ClaudeArticleAnalyzer(
			aiOptions.Anthropic.ApiKey,
			aiOptions.Anthropic.AnalyzerModel,
			promptsOptions.Analyzer
		));
		*/

		services.AddScoped<IArticleGenerator>(_ => new ClaudeArticleGenerator(
			aiOptions.Anthropic.ApiKey,
			aiOptions.Anthropic.GeneratorModel,
			promptsOptions.Generator,
			aiOptions.Anthropic.OutputLanguage
		));

		var contentGeneratorPrompts = new Dictionary<Platform, string>
		{
			{ Platform.Telegram, promptsOptions.Telegram }
		};

		services.AddScoped<IContentGenerator>(_ => new ClaudeContentGenerator(
			aiOptions.Anthropic.ApiKey,
			aiOptions.Anthropic.ContentGeneratorModel,
			contentGeneratorPrompts
		));

		services.AddScoped<IEventClassifier>(_ => new ClaudeEventClassifier(
			aiOptions.Anthropic.ApiKey,
			aiOptions.Anthropic.ClassifierModel,
			promptsOptions.EventClassifier
		));

		services.AddScoped<IGeminiEmbeddingService>(provider => new GeminiEmbeddingService(
			aiOptions.Gemini.ApiKey,
			aiOptions.Gemini.EmbeddingModel,
			provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GeminiEmbeddingService))
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
		services.AddScoped<IArticleApprovalService, ArticleApprovalService>();
		services.AddScoped<IUserService, UserService>();
		services.AddScoped<IJwtService, JwtService>();
		services.AddScoped<ISourceService, SourceService>();
		services.AddScoped<IPublishTargetService, PublishTargetService>();
		services.Configure<ValidationOptions>(configuration.GetSection(ValidationOptions.SectionName));
		services.AddScoped<IRawArticleValidator, RawArticleValidator>();
		services.AddScoped<IEventService, EventService>();

		return services;
	}
}