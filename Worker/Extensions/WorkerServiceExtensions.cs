using Microsoft.Extensions.DependencyInjection;
using Worker.Configuration;
using Worker.Workers;

namespace Worker.Extensions;

public static class WorkerServiceExtensions
{
	public static IServiceCollection AddWorkers(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		services.Configure<RssFetcherOptions>(
			configuration.GetSection(RssFetcherOptions.SectionName));
		services.Configure<ArticleProcessingOptions>(
			configuration.GetSection(ArticleProcessingOptions.SectionName));
		services.Configure<EventClassificationOptions>(
			configuration.GetSection(EventClassificationOptions.SectionName));

		services.AddHostedService<SourceFetcherWorker>();
		services.AddHostedService<ArticleAnalysisWorker>();
		services.AddHostedService<ArticleGenerationWorker>();
		services.AddHostedService<PublicationGenerationWorker>();
		services.AddHostedService<PublicationWorker>();
		services.AddHostedService<EventClassificationWorker>();
		services.AddHostedService<EventUpdateWorker>();

		return services;
	}
}