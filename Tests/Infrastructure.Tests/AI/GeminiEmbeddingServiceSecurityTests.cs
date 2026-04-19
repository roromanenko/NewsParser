using FluentAssertions;
using Infrastructure.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using System.Net;
using System.Text.Json;

namespace Infrastructure.Tests.AI;

/// <summary>
/// Security tests for GeminiEmbeddingService verifying that the API key is never
/// leaked to log output.
///
/// The service builds a URL containing ?key={apiKey} for the actual HTTP call,
/// but must only log the sanitized URL (without the key query parameter).
/// </summary>
[TestFixture]
public class GeminiEmbeddingServiceSecurityTests
{
    private const string ApiKey = "super-secret-api-key-12345";
    private const string Model = "text-embedding-004";

    private Mock<ILogger<GeminiEmbeddingService>> _loggerMock = null!;
    private Mock<HttpMessageHandler> _httpHandlerMock = null!;
    private HttpClient _httpClient = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<GeminiEmbeddingService>>();
        _httpHandlerMock = new Mock<HttpMessageHandler>();

        _loggerMock
            .Setup(l => l.IsEnabled(It.IsAny<LogLevel>()))
            .Returns(true);

        _httpClient = new HttpClient(_httpHandlerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient.Dispose();
    }

    // ------------------------------------------------------------------
    // P0 — No log message contains the raw ?key= query string
    // ------------------------------------------------------------------

    [Test]
    public async Task GenerateEmbeddingAsync_WhenCalled_DoesNotLogUrlWithKeyQueryParameter()
    {
        // Arrange
        SetupSuccessfulHttpResponse();

        var sut = new GeminiEmbeddingService(ApiKey, Model, _httpClient, _loggerMock.Object);

        // Act
        await sut.GenerateEmbeddingAsync("some text to embed");

        // Assert — no logged message must contain the ?key= fragment
        _loggerMock.Verify(
            l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("?key=")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "the URL with the API key query parameter must never appear in log output");
    }

    // ------------------------------------------------------------------
    // P0 — No log message contains the raw API key value
    // ------------------------------------------------------------------

    [Test]
    public async Task GenerateEmbeddingAsync_WhenCalled_DoesNotLogApiKeyValue()
    {
        // Arrange
        SetupSuccessfulHttpResponse();

        var sut = new GeminiEmbeddingService(ApiKey, Model, _httpClient, _loggerMock.Object);

        // Act
        await sut.GenerateEmbeddingAsync("some text to embed");

        // Assert — no logged message must contain the raw API key string
        _loggerMock.Verify(
            l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(ApiKey)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "the raw API key must never appear in log output");
    }

    // ------------------------------------------------------------------
    // P1 — Log output contains the sanitized URL (without the key) to
    //       confirm that the correct log variables are still emitted
    // ------------------------------------------------------------------

    [Test]
    public async Task GenerateEmbeddingAsync_WhenCalled_LogsModelNameWithoutLeakingKey()
    {
        // Arrange
        SetupSuccessfulHttpResponse();

        var sut = new GeminiEmbeddingService(ApiKey, Model, _httpClient, _loggerMock.Object);

        // Act
        await sut.GenerateEmbeddingAsync("some text to embed");

        // Assert — at least one debug-level log was emitted that mentions the model
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(Model)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            "the model name must appear in log output so operators can see which model was called");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void SetupSuccessfulHttpResponse()
    {
        var embeddingValues = new float[] { 0.1f, 0.2f, 0.3f };
        var responsePayload = JsonSerializer.Serialize(new
        {
            embedding = new
            {
                values = embeddingValues
            }
        });

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responsePayload, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
