using Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Net;
using System.Text.Json;

namespace Api.Tests.Middleware;

[TestFixture]
public class ExceptionMiddlewareTests
{
    private Mock<ILogger<ExceptionMiddleware>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<ExceptionMiddleware>>();
    }

    // ------------------------------------------------------------------
    // P0 — KeyNotFoundException → 404 + LogInformation (not LogError)
    // ------------------------------------------------------------------

    [Test]
    public async Task InvokeAsync_WhenKeyNotFoundExceptionThrown_Returns404AndLogsAtInformation()
    {
        // Arrange
        var context = BuildWritableContext();
        var sut = BuildMiddleware(next: _ => throw new KeyNotFoundException("Article not found"));

        // Act
        await sut.InvokeAsync(context);

        // Assert — status code
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);

        // Assert — logged at Information level
        VerifyLogLevel(LogLevel.Information, Times.Once());

        // Assert — NOT logged at Error level
        VerifyLogLevel(LogLevel.Error, Times.Never());
    }

    // ------------------------------------------------------------------
    // P0 — InvalidOperationException → 409 + LogInformation (not LogError)
    // ------------------------------------------------------------------

    [Test]
    public async Task InvokeAsync_WhenInvalidOperationExceptionThrown_Returns409AndLogsAtInformation()
    {
        // Arrange
        var context = BuildWritableContext();
        var sut = BuildMiddleware(next: _ => throw new InvalidOperationException("State conflict"));

        // Act
        await sut.InvokeAsync(context);

        // Assert — status code
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);

        // Assert — logged at Information level
        VerifyLogLevel(LogLevel.Information, Times.Once());

        // Assert — NOT logged at Error level
        VerifyLogLevel(LogLevel.Error, Times.Never());
    }

    // ------------------------------------------------------------------
    // P0 — Unhandled Exception → 500 + LogError with the exception object
    // ------------------------------------------------------------------

    [Test]
    public async Task InvokeAsync_WhenUnhandledExceptionThrown_Returns500AndLogsAtErrorWithException()
    {
        // Arrange
        var context = BuildWritableContext();
        var unhandled = new Exception("Something unexpected");
        var sut = BuildMiddleware(next: _ => throw unhandled);

        // Act
        await sut.InvokeAsync(context);

        // Assert — status code
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);

        // Assert — logged at Error level with the exception instance
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                unhandled,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Assert — NOT logged at Information level
        VerifyLogLevel(LogLevel.Information, Times.Never());
    }

    // ------------------------------------------------------------------
    // P1 — Response body contains JSON with correct status and message fields
    // ------------------------------------------------------------------

    [Test]
    public async Task InvokeAsync_WhenKeyNotFoundExceptionThrown_ResponseBodyContainsStatusAndMessage()
    {
        // Arrange
        const string errorMessage = "Resource with id 99 not found";
        var context = BuildWritableContext();
        var sut = BuildMiddleware(next: _ => throw new KeyNotFoundException(errorMessage));

        // Act
        await sut.InvokeAsync(context);

        // Assert — response body is valid JSON with the expected fields
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        doc.RootElement.GetProperty("message").GetString().Should().Be(errorMessage);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private ExceptionMiddleware BuildMiddleware(RequestDelegate next) =>
        new(next, _loggerMock.Object);

    private static DefaultHttpContext BuildWritableContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/articles/99";
        // Replace the response body stream with one that supports reading back
        context.Response.Body = new MemoryStream();
        return context;
    }

    private void VerifyLogLevel(LogLevel level, Times times)
    {
        _loggerMock.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }
}
