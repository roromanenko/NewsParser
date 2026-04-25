using Api.Models;
using Core.DomainModels;
using Core.Interfaces.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NUnit.Framework;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;

namespace Api.Tests.Controllers;

[TestFixture]
public class AiOperationsControllerTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _adminClient = null!;
    private HttpClient _editorClient = null!;

    private Mock<IAiRequestLogRepository> _aiLogRepoMock = null!;

    // JWT config — must match the values supplied via UseSetting in OneTimeSetUp
    private const string JwtSecretKey = "65j781ddc991c216b5897b44bdsca4eff6ab75ea18448c9e43e0baasfbds4ef5";
    private const string JwtIssuer = "https://localhost:7054";
    private const string JwtAudience = "https://localhost:7054";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _aiLogRepoMock = new Mock<IAiRequestLogRepository>();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                builder.ConfigureServices(services =>
                {
                    RemoveAllImplementations(services, typeof(IAiRequestLogRepository));
                    services.AddSingleton(_aiLogRepoMock.Object);
                });

                builder.UseSetting("Jwt:SecretKey", JwtSecretKey);
                builder.UseSetting("Jwt:Issuer", JwtIssuer);
                builder.UseSetting("Jwt:Audience", JwtAudience);

                builder.UseSetting("ConnectionStrings:NewsParserDbContext",
                    "Host=localhost;Database=test_placeholder;Username=sa;Password=sa");

                builder.UseSetting("CloudflareR2:PublicBaseUrl", "https://cdn.test.example.com");
            });

        var adminToken = GenerateJwtToken(role: nameof(UserRole.Admin));
        _adminClient = _factory.CreateClient();
        _adminClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        var editorToken = GenerateJwtToken(role: nameof(UserRole.Editor));
        _editorClient = _factory.CreateClient();
        _editorClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", editorToken);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _adminClient.Dispose();
        _editorClient.Dispose();
        _factory.Dispose();
    }

    [SetUp]
    public void ResetMocks()
    {
        _aiLogRepoMock.Reset();

        // Sensible defaults so that controller code paths don't NRE on
        // unconfigured async returns. Individual tests override as needed.
        _aiLogRepoMock
            .Setup(r => r.GetMetricsAsync(It.IsAny<AiRequestLogFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyMetrics());
        _aiLogRepoMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<AiRequestLogFilter>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _aiLogRepoMock
            .Setup(r => r.CountAsync(It.IsAny<AiRequestLogFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    // ------------------------------------------------------------------
    // GET /ai-operations/metrics — admin, no params → 200 with metrics body
    // ------------------------------------------------------------------

    [Test]
    public async Task GetMetrics_WhenAdminAndNoParams_Returns200WithMetricsBody()
    {
        // Arrange
        var metrics = new AiRequestLogMetrics(
            Totals: new AiMetricsTotals(
                TotalCostUsd: 1.25m,
                TotalCalls: 10,
                SuccessCalls: 8,
                ErrorCalls: 2,
                AverageLatencyMs: 425.5,
                TotalInputTokens: 1000,
                TotalOutputTokens: 500,
                TotalCacheCreationInputTokens: 50,
                TotalCacheReadInputTokens: 25),
            TimeSeries:
            [
                new AiMetricsTimeBucket(
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    "Anthropic", 0.5m, 5, 750)
            ],
            ByModel: [new AiMetricsBreakdownRow("claude-haiku-4-5-20251001", 6, 0.7m, 900)],
            ByWorker: [new AiMetricsBreakdownRow("ArticleAnalysisWorker", 7, 0.8m, 1100)],
            ByProvider: [new AiMetricsBreakdownRow("Anthropic", 10, 1.25m, 1500)]);

        _aiLogRepoMock
            .Setup(r => r.GetMetricsAsync(It.IsAny<AiRequestLogFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);

        // Act
        var response = await _adminClient.GetAsync("/ai-operations/metrics");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AiOperationsMetricsDto>();
        body.Should().NotBeNull();
        body!.TotalCostUsd.Should().Be(1.25m);
        body.TotalCalls.Should().Be(10);
        body.SuccessCalls.Should().Be(8);
        body.ErrorCalls.Should().Be(2);
        body.TimeSeries.Should().HaveCount(1);
        body.ByModel.Should().HaveCount(1);
        body.ByWorker.Should().HaveCount(1);
        body.ByProvider.Should().HaveCount(1);
    }

    // ------------------------------------------------------------------
    // GET /ai-operations/metrics — from > to → 400 (FluentValidation)
    // ------------------------------------------------------------------

    [Test]
    public async Task GetMetrics_WhenFromIsLaterThanTo_Returns400()
    {
        // Arrange
        const string from = "2026-02-01T00:00:00Z";
        const string to = "2026-01-01T00:00:00Z";

        // Act
        var response = await _adminClient.GetAsync(
            $"/ai-operations/metrics?from={from}&to={to}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _aiLogRepoMock.Verify(
            r => r.GetMetricsAsync(It.IsAny<AiRequestLogFilter>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // GET /ai-operations/requests — defaults → 200 with PagedResult body
    // ------------------------------------------------------------------

    [Test]
    public async Task GetRequests_WhenAdminAndDefaults_Returns200WithPagedResultBody()
    {
        // Arrange
        var logs = new List<AiRequestLog> { CreateValidLog(), CreateValidLog() };

        _aiLogRepoMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<AiRequestLogFilter>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(logs);
        _aiLogRepoMock
            .Setup(r => r.CountAsync(It.IsAny<AiRequestLogFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var response = await _adminClient.GetAsync("/ai-operations/requests");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<AiRequestLogDto>>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(2);
        body.Page.Should().Be(1);
        body.PageSize.Should().Be(20);
        body.TotalCount.Should().Be(2);
    }

    // ------------------------------------------------------------------
    // GET /ai-operations/requests — pageSize=200 is rejected by the validator
    //   (RuleFor(x => x.PageSize).InclusiveBetween(1, 100)) before the
    //   controller's clamp is reached. Documents the actual behavior.
    // ------------------------------------------------------------------

    [Test]
    public async Task GetRequests_WhenPageSizeExceeds100_Returns400FromValidator()
    {
        // Arrange / Act
        var response = await _adminClient.GetAsync("/ai-operations/requests?pageSize=200");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _aiLogRepoMock.Verify(
            r => r.GetPagedAsync(
                It.IsAny<AiRequestLogFilter>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // GET /ai-operations/requests — status=Invalid → 400
    // ------------------------------------------------------------------

    [Test]
    public async Task GetRequests_WhenStatusIsInvalid_Returns400()
    {
        // Arrange / Act
        var response = await _adminClient.GetAsync("/ai-operations/requests?status=Invalid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _aiLogRepoMock.Verify(
            r => r.GetPagedAsync(
                It.IsAny<AiRequestLogFilter>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // GET /ai-operations/requests/{id} — known id → 200 with dto
    // ------------------------------------------------------------------

    [Test]
    public async Task GetRequestById_WhenLogExists_Returns200WithDto()
    {
        // Arrange
        var id = Guid.NewGuid();
        var log = CreateValidLog(id);

        _aiLogRepoMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(log);

        // Act
        var response = await _adminClient.GetAsync($"/ai-operations/requests/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AiRequestLogDto>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(id);
        body.Provider.Should().Be("Anthropic");
        body.Worker.Should().Be("ArticleAnalysisWorker");
        body.Status.Should().Be("Success");
    }

    // ------------------------------------------------------------------
    // GET /ai-operations/requests/{id} — unknown id → 404
    // ------------------------------------------------------------------

    [Test]
    public async Task GetRequestById_WhenLogNotFound_Returns404()
    {
        // Arrange
        var unknownId = Guid.NewGuid();
        _aiLogRepoMock
            .Setup(r => r.GetByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiRequestLog?)null);

        // Act
        var response = await _adminClient.GetAsync($"/ai-operations/requests/{unknownId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // All three endpoints — no token → 401
    // ------------------------------------------------------------------

    [TestCase("/ai-operations/metrics")]
    [TestCase("/ai-operations/requests")]
    [TestCase("/ai-operations/requests/00000000-0000-0000-0000-000000000001")]
    public async Task AnyEndpoint_WhenNoToken_Returns401(string url)
    {
        // Arrange
        using var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // All three endpoints — Editor JWT (not Admin) → 403
    // ------------------------------------------------------------------

    [TestCase("/ai-operations/metrics")]
    [TestCase("/ai-operations/requests")]
    [TestCase("/ai-operations/requests/00000000-0000-0000-0000-000000000001")]
    public async Task AnyEndpoint_WhenEditorRole_Returns403(string url)
    {
        // Act
        var response = await _editorClient.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static AiRequestLog CreateValidLog(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
        Worker = "ArticleAnalysisWorker",
        Provider = "Anthropic",
        Operation = "Analyze",
        Model = "claude-haiku-4-5-20251001",
        InputTokens = 100,
        OutputTokens = 50,
        CacheCreationInputTokens = 0,
        CacheReadInputTokens = 0,
        TotalTokens = 150,
        CostUsd = 0.00015m,
        LatencyMs = 350,
        Status = AiRequestStatus.Success,
        ErrorMessage = null,
        CorrelationId = Guid.NewGuid(),
        ArticleId = Guid.NewGuid()
    };

    private static AiRequestLogMetrics EmptyMetrics() => new(
        Totals: new AiMetricsTotals(0m, 0, 0, 0, 0d, 0, 0, 0, 0),
        TimeSeries: [],
        ByModel: [],
        ByWorker: [],
        ByProvider: []);

    private static string GenerateJwtToken(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "test-admin@example.com"),
            new Claim(ClaimTypes.Role, role)
        };

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void RemoveAllImplementations(IServiceCollection services, Type serviceType)
    {
        var descriptors = services
            .Where(d => d.ServiceType == serviceType)
            .ToList();
        foreach (var descriptor in descriptors)
            services.Remove(descriptor);
    }
}
