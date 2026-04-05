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
    private Mock<IEventApprovalService> _approvalServiceMock = null!;
    private Mock<IEventService> _eventServiceMock = null!;

    // JWT config — must match the values supplied via UseSetting in OneTimeSetUp
    private const string JwtSecretKey = "65j781ddc991c216b5897b44bdsca4eff6ab75ea18448c9e43e0baasfbds4ef5";
    private const string JwtIssuer = "https://localhost:7054";
    private const string JwtAudience = "https://localhost:7054";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _eventRepoMock = new Mock<IEventRepository>();
        _approvalServiceMock = new Mock<IEventApprovalService>();
        _eventServiceMock = new Mock<IEventService>();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace all infrastructure registrations that touch real DB/AI/Telegram
                    RemoveAllImplementations(services, typeof(IEventRepository));
                    RemoveAllImplementations(services, typeof(IEventApprovalService));
                    RemoveAllImplementations(services, typeof(IEventService));

                    services.AddSingleton(_eventRepoMock.Object);
                    services.AddSingleton(_approvalServiceMock.Object);
                    services.AddSingleton(_eventServiceMock.Object);
                });

                // appsettings.Development.json ships with an empty SecretKey.
                // Override JWT settings here so the server validates tokens with the
                // same secret key, issuer, and audience that GenerateJwtToken() uses.
                // Without this, SymmetricSecurityKey throws ArgumentException (key
                // length is zero) on every request, which ExceptionMiddleware catches
                // and maps to 400 — masking the real controller response.
                builder.UseSetting("Jwt:SecretKey", JwtSecretKey);
                builder.UseSetting("Jwt:Issuer", JwtIssuer);
                builder.UseSetting("Jwt:Audience", JwtAudience);

                builder.UseSetting("ConnectionStrings:NewsParserDbContext",
                    "Host=localhost;Database=test_placeholder;Username=sa;Password=sa");
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
        _approvalServiceMock.Reset();
        _eventServiceMock.Reset();
    }

    // ------------------------------------------------------------------
    // POST /events/{id}/approve — 200 with valid body
    // ------------------------------------------------------------------

    [Test]
    public async Task Approve_WhenValidRequest_Returns200WithEventListItemDto()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var approvedEvent = CreateApprovedEvent(eventId);

        _approvalServiceMock
            .Setup(s => s.ApproveAsync(
                eventId,
                It.IsAny<Guid>(),
                It.IsAny<List<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(approvedEvent);

        var request = new ApproveEventRequest([Guid.NewGuid()]);

        // Act
        var response = await _client.PostAsJsonAsync($"/events/{eventId}/approve", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EventListItemDto>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(eventId);
        body.Status.Should().Be(EventStatus.Approved.ToString());
    }

    // ------------------------------------------------------------------
    // POST /events/{id}/approve — 400 when PublishTargetIds is empty
    // ------------------------------------------------------------------

    [Test]
    public async Task Approve_WhenPublishTargetIdsIsEmpty_Returns400()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var request = new ApproveEventRequest([]);

        // Act
        var response = await _client.PostAsJsonAsync($"/events/{eventId}/approve", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _approvalServiceMock.Verify(
            s => s.ApproveAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<List<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // POST /events/{id}/approve — 404 when event not found
    // ------------------------------------------------------------------

    [Test]
    public async Task Approve_WhenEventNotFound_Returns404()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        _approvalServiceMock
            .Setup(s => s.ApproveAsync(
                eventId,
                It.IsAny<Guid>(),
                It.IsAny<List<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException($"Event {eventId} not found"));

        var request = new ApproveEventRequest([Guid.NewGuid()]);

        // Act
        var response = await _client.PostAsJsonAsync($"/events/{eventId}/approve", request);

        // Assert — ExceptionMiddleware maps KeyNotFoundException → 404
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // POST /events/{id}/reject — 200 with valid body
    // ------------------------------------------------------------------

    [Test]
    public async Task Reject_WhenValidRequest_Returns200WithEventListItemDto()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var rejectedEvent = CreateRejectedEvent(eventId);

        _approvalServiceMock
            .Setup(s => s.RejectAsync(
                eventId,
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rejectedEvent);

        var request = new RejectEventRequest("Reason: outdated information");

        // Act
        var response = await _client.PostAsJsonAsync($"/events/{eventId}/reject", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EventListItemDto>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(eventId);
        body.Status.Should().Be(EventStatus.Rejected.ToString());
    }

    // ------------------------------------------------------------------
    // POST /events/{id}/reject — 400 when Reason is blank
    // ------------------------------------------------------------------

    [TestCase("")]
    [TestCase("   ")]
    public async Task Reject_WhenReasonIsBlank_Returns400(string blankReason)
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var request = new RejectEventRequest(blankReason);

        // Act
        var response = await _client.PostAsJsonAsync($"/events/{eventId}/reject", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _approvalServiceMock.Verify(
            s => s.RejectAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Event CreateApprovedEvent(Guid id) => new()
    {
        Id = id,
        Title = "Approved Event",
        Summary = "Summary",
        Status = EventStatus.Approved,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastUpdatedAt = DateTimeOffset.UtcNow,
        Articles = []
    };

    private static Event CreateRejectedEvent(Guid id) => new()
    {
        Id = id,
        Title = "Rejected Event",
        Summary = "Summary",
        Status = EventStatus.Rejected,
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
