using Api.Models;
using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
using FluentAssertions;
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

/// <summary>
/// Tests for the sortBy query parameter on GET /events.
/// The controller normalises an invalid sortBy to "newest" before
/// forwarding it to the repository.
/// </summary>
[TestFixture]
public class EventsControllerSortTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private Mock<IEventRepository> _eventRepoMock = null!;
    private Mock<IEventService> _eventServiceMock = null!;

    private const string JwtSecretKey = "65j781ddc991c216b5897b44bdsca4eff6ab75ea18448c9e43e0baasfbds4ef5";
    private const string JwtIssuer = "https://localhost:7054";
    private const string JwtAudience = "https://localhost:7054";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _eventRepoMock = new Mock<IEventRepository>();
        _eventServiceMock = new Mock<IEventService>();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    RemoveAllImplementations(services, typeof(IEventRepository));
                    RemoveAllImplementations(services, typeof(IEventService));

                    services.AddSingleton(_eventRepoMock.Object);
                    services.AddSingleton(_eventServiceMock.Object);
                });

                builder.UseSetting("Jwt:SecretKey", JwtSecretKey);
                builder.UseSetting("Jwt:Issuer", JwtIssuer);
                builder.UseSetting("Jwt:Audience", JwtAudience);

                builder.UseSetting("ConnectionStrings:NewsParserDbContext",
                    "Host=localhost;Database=test_placeholder;Username=sa;Password=sa");

                builder.UseSetting("CloudflareR2:PublicBaseUrl", "https://cdn.test.example.com");
            });

        var editorToken = GenerateJwtToken(role: nameof(UserRole.Editor));
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", editorToken);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [SetUp]
    public void ResetMocks()
    {
        _eventRepoMock.Reset();
        _eventServiceMock.Reset();

        // Default stubs so the controller can complete without throwing
        _eventRepoMock
            .Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _eventRepoMock
            .Setup(r => r.CountAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    // ------------------------------------------------------------------
    // GET /events?sortBy=invalid — P0: falls back to newest, returns 200
    // ------------------------------------------------------------------

    [Test]
    public async Task GetAll_WhenSortByIsInvalid_Returns200WithPagedResult()
    {
        // Arrange — default stubs from ResetMocks are sufficient

        // Act
        var response = await _client.GetAsync("/events?sortBy=invalid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<EventListItemDto>>();
        body.Should().NotBeNull();
        body!.Items.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // GET /events?sortBy=invalid — P1: controller passes "newest" to repo
    //     (not the raw invalid string) after normalisation
    // ------------------------------------------------------------------

    [Test]
    public async Task GetAll_WhenSortByIsInvalid_PassesNewestToRepository()
    {
        // Arrange — default stubs from ResetMocks are sufficient

        // Act
        await _client.GetAsync("/events?sortBy=garbage");

        // Assert — the controller must have normalised "garbage" to "newest"
        _eventRepoMock.Verify(
            r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string?>(), "newest",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ------------------------------------------------------------------
    // GET /events?sortBy=oldest — P0: valid sort value, returns 200
    // ------------------------------------------------------------------

    [Test]
    public async Task GetAll_WhenSortByIsOldest_Returns200()
    {
        // Arrange — default stubs from ResetMocks are sufficient

        // Act
        var response = await _client.GetAsync("/events?sortBy=oldest");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<EventListItemDto>>();
        body.Should().NotBeNull();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string GenerateJwtToken(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "test-editor@example.com"),
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
