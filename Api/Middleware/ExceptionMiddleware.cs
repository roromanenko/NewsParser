using System.Net;
using System.Text.Json;

namespace Api.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
	public async Task InvokeAsync(HttpContext context)
	{
		try
		{
			await next(context);
		}
		catch (Exception ex)
		{
			await HandleExceptionAsync(context, ex);
		}
	}

	private async Task HandleExceptionAsync(HttpContext context, Exception exception)
	{
		var (statusCode, message) = MapException(exception);

		if (statusCode == HttpStatusCode.InternalServerError)
		{
			logger.LogError(exception, "Unhandled exception for {Method} {Path}",
				context.Request.Method, context.Request.Path);
		}
		else
		{
			logger.LogInformation("Request {Method} {Path} mapped to {StatusCode}",
				context.Request.Method, context.Request.Path, (int)statusCode);
		}

		context.Response.ContentType = "application/json";
		context.Response.StatusCode = (int)statusCode;

		var response = new
		{
			status = (int)statusCode,
			message,
			path = context.Request.Path.Value
		};

		await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		}));
	}

	private static (HttpStatusCode StatusCode, string Message) MapException(Exception exception) =>
		exception switch
		{
			KeyNotFoundException => (HttpStatusCode.NotFound, exception.Message),
			InvalidOperationException => (HttpStatusCode.Conflict, exception.Message),
			UnauthorizedAccessException => (HttpStatusCode.Forbidden, exception.Message),
			ArgumentException => (HttpStatusCode.BadRequest, exception.Message),
			_ => (HttpStatusCode.InternalServerError, "An unexpected error occurred")
		};
}
