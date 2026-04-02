---
name: test-writer
description: Use this skill when writing, reviewing, or generating tests for .NET projects using NUnit, Moq, and FluentAssertions. Triggers include: creating unit tests for domain logic, repository tests with EF Core InMemory, API endpoint tests with WebApplicationFactory, service tests with mocked dependencies, worker/background-service tests, parameterized tests with TestCase, or any request to follow project testing conventions (AAA pattern, naming convention, anti-patterns checklist).
---

## Stack
- **Framework**: NUnit 4.x
- **Mocking**: Moq 4.x + `Moq.Contrib.HttpClient` for HttpClient
- **Assertions**: FluentAssertions
- **EF Core**: `Microsoft.EntityFrameworkCore.InMemory` for repositories (except pgvector — Testcontainers there)
- **API**: `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory) for endpoint tests

---

## Project Test Structure
```
tests/
├── NewsParser.Core.Tests/           # Domain logic — pure unit tests, 0 dependencies
├── NewsParser.Infrastructure.Tests/ # Repositories — EF InMemory or Sqlite InMemory
└── NewsParser.Api.Tests/            # Endpoints — WebApplicationFactory
```

Each test project references only the layer it tests.
`Core.Tests` has no knowledge of EF, `Infrastructure.Tests` has no knowledge of the API.

---

## Project Setup (if test projects don't exist yet)

Create projects:
```bash
dotnet new nunit -n NewsParser.Core.Tests -o tests/NewsParser.Core.Tests
dotnet new nunit -n NewsParser.Infrastructure.Tests -o tests/NewsParser.Infrastructure.Tests
dotnet new nunit -n NewsParser.Api.Tests -o tests/NewsParser.Api.Tests
dotnet sln NewsParser.slnx add tests/**/*.csproj
```

Base packages for each test project (`*.Tests.csproj`):
```xml
<PackageReference Include="NUnit" Version="4.*" />
<PackageReference Include="NUnit3TestAdapter" Version="4.*" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
<PackageReference Include="Moq" Version="4.*" />
<PackageReference Include="FluentAssertions" Version="6.*" />
```

Additional for `Infrastructure.Tests`:
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.*" />
```

Additional for `Api.Tests`:
```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.*" />
```

**Key project types:**
- DB Context: `AppDbContext` (Infrastructure/Persistence)
- Repositories: `ArticleRepository`, `EventRepository`, `RawArticleRepository`, `SourceRepository`, `UserRepository`, `PublicationRepository`, `PublishTargetRepository`
- Services: `ArticleApprovalService`, `EventService`, `UserService`, `SourceService`, `JwtService`, `TelegramClientService`
- Workers: `SourceFetcherWorker`, `PublicationWorker`

---

## Naming Convention
```
MethodName_StateUnderTest_ExpectedBehavior
```

Examples:
```csharp
Analyze_WhenArticleHasNoContent_ThrowsValidationException
GetByIdAsync_WhenArticleExists_ReturnsCorrectEntity
GetByIdAsync_WhenArticleNotFound_ReturnsNull
Publish_WhenTelegramApiFails_RetriesThreeTimes
```

For test classes: `{ClassName}Tests`
For fixtures with shared state: `{ClassName}Fixture`

---

## AAA Template (canonical form)
```csharp
[Test]
public async Task MethodName_StateUnderTest_ExpectedBehavior()
{
    // Arrange
    var dependency = new Mock<IDependency>();
    dependency
        .Setup(d => d.GetAsync(It.IsAny<int>()))
        .ReturnsAsync(new SomeEntity { Id = 1, Name = "Test" });

    var sut = new SystemUnderTest(dependency.Object);

    // Act
    var result = await sut.MethodAsync(1);

    // Assert
    result.Should().NotBeNull();
    result.Name.Should().Be("Test");
    dependency.Verify(d => d.GetAsync(1), Times.Once);
}
```

Rules:
- Each test verifies **one behavior**
- Sections are separated by a blank line and a `// Arrange / Act / Assert` comment
- `sut` — always the name of the object under test
- `Verify` only when a side effect is important to the contract

---

## Core Layer Tests (Domain Logic)

The domain is pure logic with no dependencies. Tests are as simple as possible.
```csharp
[TestFixture]
public class ArticleTests
{
    [Test]
    public void IsPublishable_WhenApprovedAndHasContent_ReturnsTrue()
    {
        // Arrange
        var article = new Article
        {
            Status = ArticleStatus.Approved,
            Content = "Some content",
            Title = "Some title"
        };

        // Act
        var result = article.IsPublishable();

        // Assert
        result.Should().BeTrue();
    }
}
```

For value objects and domain exceptions:
```csharp
[Test]
public void Constructor_WhenUrlIsInvalid_ThrowsDomainException()
{
    // Arrange & Act
    var act = () => new ArticleUrl("not-a-url");

    // Assert
    act.Should().Throw<DomainException>()
        .WithMessage("*invalid*");
}
```

---

## Repository / EF Core Tests

Use **EF InMemory** for speed. For tests involving pgvector (`EventRepository`) — Testcontainers with a real PostgreSQL instance.

### Base Fixture
```csharp
[TestFixture]
public abstract class RepositoryTestBase
{
    protected AppDbContext DbContext { get; private set; } = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // unique DB per test
            .Options;

        DbContext = new AppDbContext(options);
        DbContext.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown() => DbContext.Dispose();
}
```

### Repository Tests
```csharp
[TestFixture]
public class ArticleRepositoryTests : RepositoryTestBase
{
    private ArticleRepository _sut = null!;

    [SetUp]
    public new void SetUp()
    {
        base.SetUp();
        _sut = new ArticleRepository(DbContext);
    }

    [Test]
    public async Task GetByIdAsync_WhenArticleExists_ReturnsCorrectEntity()
    {
        // Arrange
        var article = new Article { Title = "Test", Url = "https://example.com" };
        await DbContext.Articles.AddAsync(article);
        await DbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByIdAsync(article.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Test");
    }

    [Test]
    public async Task GetByIdAsync_WhenArticleNotFound_ReturnsNull()
    {
        var result = await _sut.GetByIdAsync(999);
        result.Should().BeNull();
    }
}
```

---

## API Endpoint Tests (WebApplicationFactory)
```csharp
[TestFixture]
public class ArticlesControllerTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor != null) services.Remove(descriptor);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseInMemoryDatabase("ApiTests"));

                    var mockAiService = new Mock<IAiAnalysisService>();
                    mockAiService
                        .Setup(s => s.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new AnalysisResult { Sentiment = "Positive" });
                    services.AddSingleton(mockAiService.Object);
                });
            });

        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task GetArticle_WhenExists_Returns200WithBody()
    {
        // Arrange — seed via scope
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var article = new Article { Title = "API Test", Url = "https://test.com" };
        db.Articles.Add(article);
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/articles/{article.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ArticleDto>();
        body!.Title.Should().Be("API Test");
    }

    [Test]
    public async Task GetArticle_WhenNotFound_Returns404()
    {
        var response = await _client.GetAsync("/api/articles/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

---

## Service Tests (with Moq)
```csharp
[TestFixture]
public class ArticleApprovalServiceTests
{
    private Mock<IArticleRepository> _articleRepoMock = null!;
    private ArticleApprovalService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _articleRepoMock = new Mock<IArticleRepository>();
        _sut = new ArticleApprovalService(_articleRepoMock.Object);
    }

    [Test]
    public async Task ApproveAsync_WhenArticleFound_SetsStatusApprovedAndSaves()
    {
        // Arrange
        var article = CreateValidArticle(id: 1);
        _articleRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(article);

        // Act
        await _sut.ApproveAsync(1, CancellationToken.None);

        // Assert
        article.Status.Should().Be(ArticleStatus.Approved);
        _articleRepoMock.Verify(r => r.UpdateAsync(article), Times.Once);
    }

    [Test]
    public async Task ApproveAsync_WhenArticleNotFound_ThrowsNotFoundException()
    {
        // Arrange
        _articleRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((Article?)null);

        // Act
        var act = async () => await _sut.ApproveAsync(999, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>().WithMessage("*999*");
        _articleRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Article>()), Times.Never);
    }

    private static Article CreateValidArticle(int id = 1) =>
        new() { Id = id, Title = "Test Article", Url = "https://example.com", Content = "Content" };
}
```

---

## Worker / BackgroundService Tests

Workers inherit `BackgroundService`. Test `ExecuteAsync` in isolation, mocking all dependencies.
```csharp
[TestFixture]
public class RssFetcherWorkerTests
{
    private Mock<IArticleRepository> _articleRepoMock = null!;
    private Mock<IRssFetcher> _fetcherMock = null!;
    private RssFetcherWorker _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _articleRepoMock = new Mock<IArticleRepository>();
        _fetcherMock = new Mock<IRssFetcher>();
        _sut = new RssFetcherWorker(_articleRepoMock.Object, _fetcherMock.Object);
    }

    [Test]
    public async Task ExecuteAsync_WhenFeedReturnsArticles_SavesAllToRepository()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var rawArticles = new List<RawArticle>
        {
            new() { Title = "Article 1", Url = "https://news.com/1" },
            new() { Title = "Article 2", Url = "https://news.com/2" }
        };

        _fetcherMock
            .Setup(f => f.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawArticles)
            .Callback(() => cts.Cancel()); // stop the loop after the first iteration

        // Act
        await _sut.StartAsync(cts.Token);

        // Assert
        _articleRepoMock.Verify(r => r.AddRangeAsync(rawArticles, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WhenFetcherThrows_LogsErrorAndContinues()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        int callCount = 0;

        _fetcherMock
            .Setup(f => f.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (++callCount == 1) throw new HttpRequestException("timeout");
                cts.Cancel();
                return new List<RawArticle>();
            });

        // Act — must not throw
        var act = async () => await _sut.StartAsync(cts.Token);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
```

---

## Parameterized Tests (TestCase)
```csharp
[TestCase("", false, Description = "Empty title is invalid")]
[TestCase("  ", false, Description = "Whitespace title is invalid")]
[TestCase("A", false, Description = "Too short title is invalid")]
[TestCase("Valid title here", true, Description = "Normal title is valid")]
public void Validate_TitleVariants_ReturnsExpectedResult(string title, bool expectedIsValid)
{
    // Arrange
    var article = new Article { Title = title, Url = "https://valid.com", Content = "Content" };

    // Act
    var result = _sut.Validate(article);

    // Assert
    result.IsValid.Should().Be(expectedIsValid);
}
```

---

## Determinism
```csharp
// BAD: depends on current time
var article = new Article { CreatedAt = DateTime.UtcNow };
article.IsRecent().Should().BeTrue(); // will fail one week from now

// GOOD: mock time via TimeProvider (.NET 8+)
var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
var sut = new ArticleService(fakeTime);

// BAD: collection order is not guaranteed
results.First().Title.Should().Be("A");

// GOOD: explicit sorting or BeEquivalentTo
results.Should().BeEquivalentTo(expected);
results.OrderBy(r => r.Title).First().Title.Should().Be("A");
```

---

## Anti-patterns (never do these)
```csharp
// BAD: test checks multiple behaviors → split into separate tests
[Test]
public async Task Test_Everything() { /* many asserts */ }

// BAD: magic numbers → use named variables
_repo.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(article);

// BAD: unused mocks
_loggerMock = new Mock<ILogger<ArticleService>>(); // if not verified — don't create it

// BAD: assertions without FluentAssertions
Assert.IsTrue(result != null); // → result.Should().NotBeNull()

// BAD: Verify on read operations
_repo.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Once); // only for write/external calls

// BAD: duplicating business logic in the expected value
var expected = article.Views * 0.4 + article.Likes * 0.6; // → use a concrete number: .Should().Be(70.0)
```

---

## Pre-submission Checklist

- [ ] Test name reads as a specification: `Method_When_Then`
- [ ] Test verifies one behavior
- [ ] Arrange/Act/Assert sections are separated and clear
- [ ] `sut` is the name of the object under test
- [ ] All mocks are created in `[SetUp]`, not inside the test body
- [ ] Only mocks that are actually used in tests are created
- [ ] No `Thread.Sleep` or `DateTime.UtcNow` — mock time via `TimeProvider`
- [ ] Boundary values are covered via `[TestCase]`
- [ ] Test does not depend on execution order
- [ ] InMemory DB uses `Guid.NewGuid()` name — isolated from other tests
- [ ] `Verify` is used only for write operations and external calls
- [ ] Business logic is not duplicated — expected value is concrete, not computed