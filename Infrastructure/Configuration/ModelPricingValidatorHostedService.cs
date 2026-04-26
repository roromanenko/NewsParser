using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Configuration;

internal sealed class ModelPricingValidatorHostedService(
    IOptions<AiOptions> aiOptions,
    IOptions<ModelPricingOptions> pricingOptions,
    ILogger<ModelPricingValidatorHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ModelPricingValidator.ValidateOrLog(aiOptions.Value, pricingOptions.Value, logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
