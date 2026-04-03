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

		services.AddHostedService<SourceFetcherWorker>();
		services.AddHostedService<ArticleAnalysisWorker>();
		services.AddHostedService<PublicationWorker>();

		return services;
	}
}
