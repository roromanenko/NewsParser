using Infrastructure.Extensions;
using Worker.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
	.AddInfrastructure(builder.Configuration)
	.AddWorkers(builder.Configuration);

var host = builder.Build();
host.Run();