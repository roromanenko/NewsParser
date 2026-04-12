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

[TestFixture]
public class EventsControllerTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private Mock<IEventRepository> _eventRepoMock = null!;
    private Mock<IEventService> _eventServiceMock = null!;

    // JWT config — must match the values supplied via UseSetting in OneTimeSetUp
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
    }

    // ------------------------------------------------------------------
    // PATCH /events/{id}/status — 204 with valid Active status
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateStatus_WhenValidActiveStatus_Returns204()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _eventRepoMock
            .Setup(r => r.GetByIdAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEvent(eventId, EventStatus.Active));

        // Act
        var response = await _client.PatchAsJsonAsync($"/events/{eventId}/status", "Active");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ------------------------------------------------------------------
    // PATCH /events/{id}/status — 204 with valid Archived status
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateStatus_WhenValidArchivedStatus_Returns204()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _eventRepoMock
            .Setup(r => r.GetByIdAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEvent(eventId, EventStatus.Active));

        // Act
        var response = await _client.PatchAsJsonAsync($"/events/{eventId}/status", "Archived");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ------------------------------------------------------------------
    // PATCH /events/{id}/status — 400 for invalid status string
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateStatus_WhenInvalidStatus_Returns400()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _eventRepoMock
            .Setup(r => r.GetByIdAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEvent(eventId, EventStatus.Active));

        // Act
        var response = await _client.PatchAsJsonAsync($"/events/{eventId}/status", "Approved");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Event CreateEvent(Guid id, EventStatus status) => new()
    {
        Id = id,
        Title = "Test Event",
        Summary = "Summary",
        Status = status,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        Articles = []
    };

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
