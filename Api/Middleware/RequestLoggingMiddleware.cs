using System.Diagnostics;
using System.Security.Claims;

namespace Api.Middleware;

public class RequestLoggingMiddleware(
	RequestDelegate next,
	ILogger<RequestLoggingMiddleware> logger)
{
	private const string CorrelationIdHeader = "X-Correlation-Id";

	public async Task InvokeAsync(HttpContext context)
	{
		var correlationId = ReadOrGenerateCorrelationId(context);
		context.Response.Headers[CorrelationIdHeader] = correlationId;

		var scopeState = BuildScopeState(context, correlationId);

		using var scope = logger.BeginScope(scopeState);

		var sw = Stopwatch.StartNew();
		await next(context);
		sw.Stop();

		if (!context.Request.Path.StartsWithSegments("/swagger"))
		{
			logger.LogInformation(
				"HTTP {Method} {Path} responded {StatusCode} in {DurationMs}ms",
				context.Request.Method,
				context.Request.Path,
				context.Response.StatusCode,
				sw.ElapsedMilliseconds);
		}
	}

	private static string ReadOrGenerateCorrelationId(HttpContext context)
	{
		return context.Request.Headers.TryGetValue(CorrelationIdHeader, out var existing)
			? existing.ToString()
			: Guid.NewGuid().ToString();
	}

	private static Dictionary<string, object> BuildScopeState(HttpContext context, string correlationId)
	{
		var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

		var state = new Dictionary<string, object>
		{
			["CorrelationId"] = correlationId,
			["Method"] = context.Request.Method,
			["Path"] = context.Request.Path.Value ?? string.Empty
		};

		if (!string.IsNullOrEmpty(userId))
			state["UserId"] = userId;

		return state;
	}
}
