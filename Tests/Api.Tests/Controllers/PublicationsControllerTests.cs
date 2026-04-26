using Api.Models;
using Core.DomainModels;
using Core.Interfaces.Repositories;
using Core.Interfaces.Services;
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
public class PublicationsControllerTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private Mock<IPublicationService> _publicationServiceMock = null!;
    private Mock<IPublicationRepository> _publicationRepoMock = null!;
    private Mock<IMediaFileRepository> _mediaFileRepoMock = null!;
    private Mock<IPublicationMediaService> _publicationMediaServiceMock = null!;
    private Mock<IProjectRepository> _projectRepoMock = null!;

    // JWT config — must match the values supplied via UseSetting in OneTimeSetUp
    private const string JwtSecretKey = "65j781ddc991c216b5897b44bdsca4eff6ab75ea18448c9e43e0baasfbds4ef5";
    private const string JwtIssuer = "https://localhost:7054";
    private const string JwtAudience = "https://localhost:7054";
    private const string TestProjectId = "11111111-1111-1111-1111-111111111111";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _publicationServiceMock = new Mock<IPublicationService>();
        _publicationRepoMock = new Mock<IPublicationRepository>();
        _mediaFileRepoMock = new Mock<IMediaFileRepository>();
        _mediaFileRepoMock
            .Setup(r => r.GetByPublicationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _publicationMediaServiceMock = new Mock<IPublicationMediaService>();
        _projectRepoMock = new Mock<IProjectRepository>();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                builder.ConfigureServices(services =>
                {
                    RemoveAllImplementations(services, typeof(IPublicationService));
                    RemoveAllImplementations(services, typeof(IPublicationRepository));
                    RemoveAllImplementations(services, typeof(IMediaFileRepository));
                    RemoveAllImplementations(services, typeof(IPublicationMediaService));
                    RemoveAllImplementations(services, typeof(IProjectRepository));

                    services.AddSingleton(_publicationServiceMock.Object);
                    services.AddSingleton(_publicationRepoMock.Object);
                    services.AddSingleton(_mediaFileRepoMock.Object);
                    services.AddSingleton(_publicationMediaServiceMock.Object);
                    services.AddSingleton(_projectRepoMock.Object);
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
        _publicationServiceMock.Reset();
        _publicationRepoMock.Reset();
        _mediaFileRepoMock.Reset();
        _mediaFileRepoMock
            .Setup(r => r.GetByPublicationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _publicationMediaServiceMock.Reset();
        _projectRepoMock.Reset();

        _projectRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = new Guid(TestProjectId), IsActive = true, Name = "Test Project" });
    }

    // ------------------------------------------------------------------
    // POST /publications/generate — 201 with location header
    // ------------------------------------------------------------------

    [Test]
    public async Task Generate_WhenServiceSucceeds_Returns201WithPublicationDto()
    {
        // Arrange
        var publication = CreatePublication(PublicationStatus.Created);

        _publicationServiceMock
            .Setup(s => s.CreateForEventAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);

        var request = new CreatePublicationRequest(EventId: Guid.NewGuid(), PublishTargetId: Guid.NewGuid());

        // Act
        var response = await _client.PostAsJsonAsync($"projects/{TestProjectId}/publications/generate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<PublicationListItemDto>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(publication.Id);
        body.Status.Should().Be("Created");
    }

    // ------------------------------------------------------------------
    // POST /publications/generate — 401 when not authenticated
    // ------------------------------------------------------------------

    [Test]
    public async Task Generate_WhenNotAuthenticated_Returns401()
    {
        // Arrange
        using var unauthClient = _factory.CreateClient();
        var request = new CreatePublicationRequest(EventId: Guid.NewGuid(), PublishTargetId: Guid.NewGuid());

        // Act
        var response = await unauthClient.PostAsJsonAsync($"projects/{TestProjectId}/publications/generate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // GET /publications/{id} — 200 with detail dto
    // ------------------------------------------------------------------

    [Test]
    public async Task GetById_WhenPublicationExists_Returns200WithDetailDto()
    {
        // Arrange
        var publication = CreatePublicationWithEvent(PublicationStatus.ContentReady);

        _publicationRepoMock
            .Setup(r => r.GetDetailAsync(publication.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);

        // Act
        var response = await _client.GetAsync($"projects/{TestProjectId}/publications/{publication.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicationDetailDto>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(publication.Id);
        body.Status.Should().Be("ContentReady");
    }

    // ------------------------------------------------------------------
    // GET /publications/{id} — 404 when not found
    // ------------------------------------------------------------------

    [Test]
    public async Task GetById_WhenPublicationDoesNotExist_Returns404()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        _publicationRepoMock
            .Setup(r => r.GetDetailAsync(nonExistentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Publication?)null);

        // Act
        var response = await _client.GetAsync($"projects/{TestProjectId}/publications/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // GET /publications/by-event/{eventId} — 200 with list
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByEvent_WhenPublicationsExist_Returns200WithList()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var publications = new List<Publication>
        {
            CreatePublication(PublicationStatus.Created),
            CreatePublication(PublicationStatus.Approved)
        };

        _publicationRepoMock
            .Setup(r => r.GetByEventIdAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publications);

        // Act
        var response = await _client.GetAsync($"projects/{TestProjectId}/publications/by-event/{eventId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<PublicationListItemDto>>();
        body.Should().NotBeNull();
        body!.Should().HaveCount(2);
    }

    // ------------------------------------------------------------------
    // GET /publications/by-event/{eventId} — 200 with empty list when none exist
    // ------------------------------------------------------------------

    [Test]
    public async Task GetByEvent_WhenNoPublicationsForEvent_Returns200WithEmptyList()
    {
        // Arrange
        var eventId = Guid.NewGuid();

        _publicationRepoMock
            .Setup(r => r.GetByEventIdAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync($"projects/{TestProjectId}/publications/by-event/{eventId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<PublicationListItemDto>>();
        body.Should().NotBeNull();
        body!.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // PUT /publications/{id}/content — 200 with updated detail dto
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateContent_WhenServiceSucceeds_Returns200WithDetailDto()
    {
        // Arrange
        var publication = CreatePublicationWithEvent(PublicationStatus.ContentReady);

        _publicationServiceMock
            .Setup(s => s.UpdateContentAsync(
                publication.Id, It.IsAny<string>(), It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);
        _publicationRepoMock
            .Setup(r => r.GetDetailAsync(publication.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);

        var request = new UpdatePublicationContentRequest(
            Content: "Updated text",
            SelectedMediaFileIds: []);

        // Act
        var response = await _client.PutAsJsonAsync($"projects/{TestProjectId}/publications/{publication.Id}/content", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicationDetailDto>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(publication.Id);
    }

    // ------------------------------------------------------------------
    // PUT /publications/{id}/content — 401 when not authenticated
    // ------------------------------------------------------------------

    [Test]
    public async Task UpdateContent_WhenNotAuthenticated_Returns401()
    {
        // Arrange
        using var unauthClient = _factory.CreateClient();
        var request = new UpdatePublicationContentRequest(Content: "text", SelectedMediaFileIds: []);

        // Act
        var response = await unauthClient.PutAsJsonAsync($"projects/{TestProjectId}/publications/{Guid.NewGuid()}/content", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/approve — 200 with detail dto
    // ------------------------------------------------------------------

    [Test]
    public async Task Approve_WhenServiceSucceeds_Returns200WithDetailDto()
    {
        // Arrange
        var publication = CreatePublicationWithEvent(PublicationStatus.Approved);

        _publicationServiceMock
            .Setup(s => s.ApproveAsync(publication.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);
        _publicationRepoMock
            .Setup(r => r.GetDetailAsync(publication.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);

        // Act
        var response = await _client.PostAsync($"projects/{TestProjectId}/publications/{publication.Id}/approve", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicationDetailDto>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Approved");
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/approve — 401 when not authenticated
    // ------------------------------------------------------------------

    [Test]
    public async Task Approve_WhenNotAuthenticated_Returns401()
    {
        // Arrange
        using var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.PostAsync($"projects/{TestProjectId}/publications/{Guid.NewGuid()}/approve", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/reject — 200 with detail dto
    // ------------------------------------------------------------------

    [Test]
    public async Task Reject_WhenReasonProvidedAndServiceSucceeds_Returns200WithDetailDto()
    {
        // Arrange
        var publication = CreatePublicationWithEvent(PublicationStatus.Rejected);

        _publicationServiceMock
            .Setup(s => s.RejectAsync(publication.Id, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);
        _publicationRepoMock
            .Setup(r => r.GetDetailAsync(publication.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);

        var request = new RejectPublicationRequest(Reason: "Tone is not appropriate.");

        // Act
        var response = await _client.PostAsJsonAsync($"projects/{TestProjectId}/publications/{publication.Id}/reject", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicationDetailDto>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Rejected");
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/reject — 400 when reason is empty
    // ------------------------------------------------------------------

    [Test]
    public async Task Reject_WhenReasonIsEmpty_Returns400()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var request = new RejectPublicationRequest(Reason: "");

        // Act
        var response = await _client.PostAsJsonAsync($"projects/{TestProjectId}/publications/{publicationId}/reject", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/reject — 400 when reason is whitespace
    // ------------------------------------------------------------------

    [Test]
    public async Task Reject_WhenReasonIsWhitespace_Returns400()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var request = new RejectPublicationRequest(Reason: "   ");

        // Act
        var response = await _client.PostAsJsonAsync($"projects/{TestProjectId}/publications/{publicationId}/reject", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/reject — 401 when not authenticated
    // ------------------------------------------------------------------

    [Test]
    public async Task Reject_WhenNotAuthenticated_Returns401()
    {
        // Arrange
        using var unauthClient = _factory.CreateClient();
        var request = new RejectPublicationRequest(Reason: "Some reason.");

        // Act
        var response = await unauthClient.PostAsJsonAsync($"projects/{TestProjectId}/publications/{Guid.NewGuid()}/reject", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/send — 200 with detail dto
    // ------------------------------------------------------------------

    [Test]
    public async Task Send_WhenServiceSucceeds_Returns200WithDetailDto()
    {
        // Arrange
        var publication = CreatePublicationWithEvent(PublicationStatus.Approved);

        _publicationServiceMock
            .Setup(s => s.SendAsync(publication.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);
        _publicationRepoMock
            .Setup(r => r.GetDetailAsync(publication.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);

        // Act
        var response = await _client.PostAsync($"projects/{TestProjectId}/publications/{publication.Id}/send", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicationDetailDto>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Approved");
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/send — 401 when not authenticated
    // ------------------------------------------------------------------

    [Test]
    public async Task Send_WhenNotAuthenticated_Returns401()
    {
        // Arrange
        using var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.PostAsync($"projects/{TestProjectId}/publications/{Guid.NewGuid()}/send", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/regenerate — 200 with detail dto on success
    // ------------------------------------------------------------------

    [Test]
    public async Task Regenerate_WhenServiceSucceeds_Returns200WithDetailDto()
    {
        // Arrange
        const string feedback = "Make it shorter.";
        var publication = CreatePublicationWithEvent(PublicationStatus.Created);
        publication.EditorFeedback = feedback;

        _publicationServiceMock
            .Setup(s => s.RegenerateAsync(publication.Id, feedback, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);
        _publicationRepoMock
            .Setup(r => r.GetDetailAsync(publication.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(publication);

        var request = new RegeneratePublicationRequest(Feedback: feedback);

        // Act
        var response = await _client.PostAsJsonAsync($"projects/{TestProjectId}/publications/{publication.Id}/regenerate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicationDetailDto>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(publication.Id);
        body.Status.Should().Be("Created");
        body.EditorFeedback.Should().Be(feedback);
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/regenerate — 401 when not authenticated
    // ------------------------------------------------------------------

    [Test]
    public async Task Regenerate_WhenNotAuthenticated_Returns401()
    {
        // Arrange
        using var unauthClient = _factory.CreateClient();
        var request = new RegeneratePublicationRequest(Feedback: "Some feedback.");

        // Act
        var response = await unauthClient.PostAsJsonAsync($"projects/{TestProjectId}/publications/{Guid.NewGuid()}/regenerate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/regenerate — 404 when GetDetailAsync returns null
    // after the service call succeeds
    // ------------------------------------------------------------------

    [Test]
    public async Task Regenerate_WhenDetailLookupReturnsNull_Returns404()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        const string feedback = "Emphasize the second source.";

        _publicationServiceMock
            .Setup(s => s.RegenerateAsync(publicationId, feedback, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePublication(PublicationStatus.Created));
        _publicationRepoMock
            .Setup(r => r.GetDetailAsync(publicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Publication?)null);

        var request = new RegeneratePublicationRequest(Feedback: feedback);

        // Act
        var response = await _client.PostAsJsonAsync($"projects/{TestProjectId}/publications/{publicationId}/regenerate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/regenerate — 400 when feedback is empty
    // (FluentValidation fires before controller)
    // ------------------------------------------------------------------

    [Test]
    public async Task Regenerate_WhenFeedbackIsEmpty_Returns400()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var request = new RegeneratePublicationRequest(Feedback: "");

        // Act
        var response = await _client.PostAsJsonAsync($"projects/{TestProjectId}/publications/{publicationId}/regenerate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _publicationServiceMock.Verify(
            s => s.RegenerateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/regenerate — 400 when feedback exceeds 2000 chars
    // ------------------------------------------------------------------

    [Test]
    public async Task Regenerate_WhenFeedbackExceedsMaxLength_Returns400()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        var oversized = new string('x', 2001);
        var request = new RegeneratePublicationRequest(Feedback: oversized);

        // Act
        var response = await _client.PostAsJsonAsync($"projects/{TestProjectId}/publications/{publicationId}/regenerate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _publicationServiceMock.Verify(
            s => s.RegenerateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------------------------------------------------------------
    // POST /publications/{id}/regenerate — 409 when service throws
    // InvalidOperationException (wrong status)
    // ------------------------------------------------------------------

    [Test]
    public async Task Regenerate_WhenServiceThrowsInvalidOperationException_Returns409()
    {
        // Arrange
        var publicationId = Guid.NewGuid();
        const string feedback = "Try again please.";

        _publicationServiceMock
            .Setup(s => s.RegenerateAsync(publicationId, feedback, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                $"Publication {publicationId} cannot be regenerated: status is Approved"));

        var request = new RegeneratePublicationRequest(Feedback: feedback);

        // Act
        var response = await _client.PostAsJsonAsync($"projects/{TestProjectId}/publications/{publicationId}/regenerate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Publication CreatePublication(PublicationStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Article = new Article { Id = Guid.NewGuid(), Title = "Test Article" },
        PublishTarget = new PublishTarget
        {
            Id = Guid.NewGuid(),
            Name = "Test Channel",
            Platform = Platform.Telegram,
            IsActive = true
        },
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        SelectedMediaFileIds = []
    };

    private static Publication CreatePublicationWithEvent(PublicationStatus status)
    {
        var article = new Article
        {
            Id = Guid.NewGuid(),
            Title = "Test Article",
            MediaFiles = []
        };

        var publication = CreatePublication(status);

        // Attach a minimal event so ExtractAvailableMedia can iterate Articles
        publication.Event = new Event
        {
            Id = Guid.NewGuid(),
            Title = "Test Event",
            Summary = "Summary",
            Status = EventStatus.Active,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Articles = [article]
        };
        publication.EventId = publication.Event.Id;

        return publication;
    }

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
