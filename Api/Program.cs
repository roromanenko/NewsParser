using Api.Extensions;
using Api.Middleware;
using Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services
	.AddInfrastructure(builder.Configuration)
	.AddApi(builder.Configuration);

if (!builder.Environment.IsEnvironment("Testing"))
	builder.Configuration.MigrateDatabase();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI(options =>
	{
		options.SwaggerEndpoint("/swagger/v1/swagger.json", "Media Platform API v1");
		options.RoutePrefix = "swagger";
	});
}

app.UseMiddleware<ExceptionMiddleware>();
app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();