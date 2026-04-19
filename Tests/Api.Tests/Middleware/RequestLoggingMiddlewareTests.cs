using Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Security.Claims;

namespace Api.Tests.Middleware;

[TestFixture]
public class RequestLoggingMiddlewareTests
{
    private Mock<ILogger<RequestLoggingMiddleware>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<RequestLoggingMiddleware>>();
    }

    // ------------------------------------------------------------------
    // P0 — When no X-Correlation-Id header is present, a new GUID is
    //       generated and written to the response header
    // ------------------------------------------------------------------

    [Test]
    public async Task InvokeAsync_WhenNoCorrelationIdInRequest_GeneratesAndReturnsCorrelationIdHeader()
    {
        // Arrange
        var context = BuildContext("/api/articles", method: "GET");
        var sut = BuildMiddleware(next: _ => Task.CompletedTask);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        context.Response.Headers.TryGetValue("X-Correlation-Id", out var headerValue).Should().BeTrue();
        Guid.TryParse(headerValue.ToString(), out _).Should().BeTrue(
            "a generated correlation id must be a valid GUID");
    }

    // ------------------------------------------------------------------
    // P0 — When X-Correlation-Id is already present in the request,
    //       it is echoed back unchanged in the response header
    // ------------------------------------------------------------------

    [Test]
    public async Task InvokeAsync_WhenCorrelationIdPresentInRequest_PassesThroughToResponseUnchanged()
    {
        // Arrange
        const string existingCorrelationId = "test-correlation-abc-123";
        var context = BuildContext("/api/articles", method: "GET");
        context.Request.Headers["X-Correlation-Id"] = existingCorrelationId;

        var sut = BuildMiddleware(next: _ => Task.CompletedTask);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        context.Response.Headers["X-Correlation-Id"].ToString().Should().Be(existingCorrelationId);
    }

    // ------------------------------------------------------------------
    // P0 — A non-swagger request emits one Information log with
    //       Method, Path, StatusCode, and DurationMs placeholders filled in
    // ------------------------------------------------------------------

    [Test]
    public async Task InvokeAsync_WhenNonSwaggerPath_EmitsOneInformationLogWithRequestDetails()
    {
        // Arrange
        var context = BuildContext("/api/articles", method: "GET", statusCode: 200);
        var sut = BuildMiddleware(next: _ => Task.CompletedTask);

        // Act
        await sut.InvokeAsync(context);

        // Assert — exactly one Information log is emitted
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("GET") &&
                    state.ToString()!.Contains("/api/articles") &&
                    state.ToString()!.Contains("200")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // P0 — A /swagger path must NOT emit the Information log line
    //       (the scope still runs, but the log call is skipped)
    // ------------------------------------------------------------------

    [Test]
    public async Task InvokeAsync_WhenSwaggerPath_DoesNotEmitInformationLog()
    {
        // Arrange
        var context = BuildContext("/swagger/index.html", method: "GET");
        var sut = BuildMiddleware(next: _ => Task.CompletedTask);

        // Act
        await sut.InvokeAsync(context);

        // Assert — no Information log should be emitted for swagger paths
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // P1 — When the user is authenticated, UserId is included in the
    //       scope state dictionary passed to BeginScope
    // ------------------------------------------------------------------

    [Test]
    public async Task InvokeAsync_WhenUserIsAuthenticated_IncludesUserIdInScope()
    {
        // Arrange
        const string userId = "user-42";
        var context = BuildContext("/api/articles", method: "GET");
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId) },
                authenticationType: "TestAuth"));

        Dictionary<string, object>? capturedScope = null;
        _loggerMock
            .Setup(l => l.BeginScope(It.IsAny<Dictionary<string, object>>()))
            .Callback<object>(state => capturedScope = state as Dictionary<string, object>)
            .Returns(Mock.Of<IDisposable>());

        var sut = BuildMiddleware(next: _ => Task.CompletedTask);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        capturedScope.Should().NotBeNull();
        capturedScope!.Should().ContainKey("UserId");
        capturedScope!["UserId"].Should().Be(userId);
    }

    // ------------------------------------------------------------------
    // P1 — When the user is anonymous (no NameIdentifier claim),
    //       UserId key must be absent from the scope state
    // ------------------------------------------------------------------

    [Test]
    public async Task InvokeAsync_WhenUserIsAnonymous_DoesNotIncludeUserIdInScope()
    {
        // Arrange
        var context = BuildContext("/api/articles", method: "GET");
        // ClaimsPrincipal with no claims = anonymous

        Dictionary<string, object>? capturedScope = null;
        _loggerMock
            .Setup(l => l.BeginScope(It.IsAny<Dictionary<string, object>>()))
            .Callback<object>(state => capturedScope = state as Dictionary<string, object>)
            .Returns(Mock.Of<IDisposable>());

        var sut = BuildMiddleware(next: _ => Task.CompletedTask);

        // Act
        await sut.InvokeAsync(context);

        // Assert
        capturedScope.Should().NotBeNull();
        capturedScope!.Should().NotContainKey("UserId");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private RequestLoggingMiddleware BuildMiddleware(RequestDelegate next) =>
        new(next, _loggerMock.Object);

    private static DefaultHttpContext BuildContext(
        string path,
        string method = "GET",
        int statusCode = 200)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.StatusCode = statusCode;
        return context;
    }
}
