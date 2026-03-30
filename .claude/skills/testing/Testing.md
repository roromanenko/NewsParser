# Testing Skill — NewsParser (.NET 10, NUnit, Moq)

## Stack
- **Framework**: NUnit 3.x
- **Mocking**: Moq 4.x + `Moq.Contrib.HttpClient` для HttpClient
- **Assertions**: FluentAssertions
- **EF Core**: `Microsoft.EntityFrameworkCore.InMemory` для репозиториев
- **API**: `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory) для endpoint тестов

---

## Project Test Structure

```
tests/
├── NewsParser.Core.Tests/          # Domain логика — чистые unit тесты, 0 зависимостей
├── NewsParser.Infrastructure.Tests/ # Репозитории — EF InMemory или Sqlite InMemory
└── NewsParser.Api.Tests/           # Endpoints — WebApplicationFactory
```

Каждый тестовый проект ссылается только на тот слой, который тестирует.
`Core.Tests` не знает об EF, `Infrastructure.Tests` не знает об API.

---

## Naming Convention

```
MethodName_StateUnderTest_ExpectedBehavior
```

Примеры:
```csharp
Analyze_WhenArticleHasNoContent_ThrowsValidationException
GetByIdAsync_WhenArticleExists_ReturnsCorrectEntity
GetByIdAsync_WhenArticleNotFound_ReturnsNull
Publish_WhenTelegramApiFails_RetriesThreeTimes
```

Для тест-классов: `{ClassName}Tests`
Для фикстур с общим состоянием: `{ClassName}Fixture`

---

## AAA Template (каноническая форма)

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

Правила:
- Каждый тест проверяет **одно поведение**
- Секции разделяются пустой строкой и комментарием `// Arrange / Act / Assert`
- `sut` (System Under Test) — всегда имя тестируемого объекта
- `Verify` только если side effect важен для контракта

---

## Core Layer Tests (Domain Logic)

Domain — чистая логика без зависимостей. Тесты максимально простые.

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

    [Test]
    public void IsPublishable_WhenNotApproved_ReturnsFalse()
    {
        // Arrange
        var article = new Article
        {
            Status = ArticleStatus.Pending,
            Content = "Some content",
            Title = "Some title"
        };

        // Act
        var result = article.IsPublishable();

        // Assert
        result.Should().BeFalse();
    }
}
```

Для value objects и domain exceptions:

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

Используем **EF InMemory** для скорости. Для тестов с pgvector или raw SQL — Sqlite InMemory или реальная тестовая БД через `TestContainers`.

### Base Fixture (общий паттерн)

```csharp
[TestFixture]
public abstract class RepositoryTestBase
{
    protected AppDbContext DbContext { get; private set; } = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // уникальная БД на каждый тест
            .Options;

        DbContext = new AppDbContext(options);
        DbContext.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        DbContext.Dispose();
    }
}
```

### Repository Tests

```csharp
[TestFixture]
public class ArticleRepositoryTests : RepositoryTestBase
{
    private ArticleRepository _repository = null!;

    [SetUp]
    public new void SetUp()
    {
        base.SetUp();
        _repository = new ArticleRepository(DbContext);
    }

    [Test]
    public async Task GetByIdAsync_WhenArticleExists_ReturnsCorrectEntity()
    {
        // Arrange
        var article = new Article { Title = "Test", Url = "https://example.com" };
        await DbContext.Articles.AddAsync(article);
        await DbContext.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(article.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Test");
    }

    [Test]
    public async Task GetByIdAsync_WhenArticleNotFound_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public async Task AddAsync_PersistsArticleToDatabase()
    {
        // Arrange
        var article = new Article { Title = "New", Url = "https://new.com" };

        // Act
        await _repository.AddAsync(article);
        await DbContext.SaveChangesAsync();

        // Assert
        var persisted = await DbContext.Articles.FindAsync(article.Id);
        persisted.Should().NotBeNull();
        persisted!.Title.Should().Be("New");
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
                    // Заменяем реальный DbContext на InMemory
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor != null) services.Remove(descriptor);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseInMemoryDatabase("ApiTests"));

                    // Мокаем внешние зависимости
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
        // Arrange — seed через scope
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
        body.Should().NotBeNull();
        body!.Title.Should().Be("API Test");
    }

    [Test]
    public async Task GetArticle_WhenNotFound_Returns404()
    {
        // Act
        var response = await _client.GetAsync("/api/articles/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

---

## Service Tests (с Moq)

Для сервисов которые оркестрируют репозитории и внешние зависимости:

```csharp
[TestFixture]
public class ArticleAnalysisServiceTests
{
    private Mock<IArticleRepository> _articleRepoMock = null!;
    private Mock<IAiClient> _aiClientMock = null!;
    private ArticleAnalysisService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _articleRepoMock = new Mock<IArticleRepository>();
        _aiClientMock = new Mock<IAiClient>();
        _sut = new ArticleAnalysisService(_articleRepoMock.Object, _aiClientMock.Object);
    }

    [Test]
    public async Task AnalyzeAsync_WhenArticleFound_CallsAiAndSavesResult()
    {
        // Arrange
        var article = new Article { Id = 1, Content = "Breaking news..." };
        _articleRepoMock
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(article);

        _aiClientMock
            .Setup(c => c.AnalyzeAsync(article.Content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiAnalysisResult { Category = "Politics", Sentiment = "Neutral" });

        // Act
        await _sut.AnalyzeAsync(1, CancellationToken.None);

        // Assert
        article.Category.Should().Be("Politics");
        _articleRepoMock.Verify(r => r.UpdateAsync(article), Times.Once);
    }

    [Test]
    public async Task AnalyzeAsync_WhenArticleNotFound_ThrowsNotFoundException()
    {
        // Arrange
        _articleRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Article?)null);

        // Act
        var act = async () => await _sut.AnalyzeAsync(999, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*999*");
    }

    [Test]
    public async Task AnalyzeAsync_WhenAiClientThrows_DoesNotSaveAndRethrows()
    {
        // Arrange
        var article = new Article { Id = 1, Content = "Content" };
        _articleRepoMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(article);
        _aiClientMock
            .Setup(c => c.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiClientException("Rate limit exceeded"));

        // Act
        var act = async () => await _sut.AnalyzeAsync(1, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<AiClientException>();
        _articleRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Article>()), Times.Never);
    }
}
```

---

## Параметризованные тесты (TestCase / TestCaseSource)

Для граничных значений и множества входных данных:

```csharp
[TestFixture]
public class ArticleValidatorTests
{
    private ArticleValidator _sut = null!;

    [SetUp]
    public void SetUp() => _sut = new ArticleValidator();

    [TestCase("", false, Description = "Empty title is invalid")]
    [TestCase("  ", false, Description = "Whitespace title is invalid")]
    [TestCase("A", false, Description = "Too short title is invalid")]
    [TestCase("Valid title here", true, Description = "Normal title is valid")]
    [TestCase("A very long title that exceeds the maximum allowed length for articles", false)]
    public void Validate_TitleVariants_ReturnsExpectedResult(string title, bool expectedIsValid)
    {
        // Arrange
        var article = new Article { Title = title, Url = "https://valid.com", Content = "Content" };

        // Act
        var result = _sut.Validate(article);

        // Assert
        result.IsValid.Should().Be(expectedIsValid);
    }
}
```

---

## Test Data Helpers (приватные фабрики внутри тест-класса)

Если одни и те же объекты создаются в нескольких тестах — выноси инициализацию в приватный метод.
Не дублируй одинаковые блоки `new Article { ... }` по всему классу.

```csharp
[TestFixture]
public class ArticleAnalysisServiceTests
{
    // ...

    // Приватная фабрика — минимальный валидный объект для большинства тестов
    private static Article CreateValidArticle(int id = 1, string content = "Default content") =>
        new Article
        {
            Id = id,
            Content = content,
            Title = "Test Article",
            Url = "https://example.com"
        };

    [Test]
    public async Task AnalyzeAsync_WhenArticleFound_CallsAiAndSavesResult()
    {
        // Arrange
        var article = CreateValidArticle(id: 1, content: "Breaking news...");
        // ...
    }

    [Test]
    public async Task AnalyzeAsync_WhenArticleNotFound_ThrowsNotFoundException()
    {
        // Arrange — фабрика не нужна, тестируем отсутствие
        _articleRepoMock.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Article?)null);
        // ...
    }
}
```

Если билдеры/фабрики уже есть в проекте (`ArticleBuilder`, `TestDataFactory`) — используй их.

---

## Детерминизм

Тесты не должны зависеть от внешних факторов. Типичные нарушения и как их исправить:

```csharp
// ПЛОХО: зависит от текущего времени
var article = new Article { CreatedAt = DateTime.UtcNow };
article.IsRecent().Should().BeTrue(); // упадёт ровно через неделю

// ХОРОШО: мокай время через TimeProvider (.NET 8+)
var fakeTime = TimeProvider.System; // или кастомный FakeTimeProvider
var sut = new ArticleService(fakeTime);

// ПЛОХО: порядок коллекции не гарантирован
var results = await _repository.GetAllAsync();
results.First().Title.Should().Be("A"); // порядок не гарантирован

// ХОРОШО: явная сортировка или BeEquivalentTo
results.Should().BeEquivalentTo(expected); // порядок не важен
results.OrderBy(r => r.Title).First().Title.Should().Be("A"); // когда порядок важен
```

---

## Anti-patterns (никогда не делать)

```csharp
// ПЛОХО: тест проверяет несколько поведений
[Test]
public async Task Test_Everything()
{
    var article = await _service.CreateAsync(...);
    article.Should().NotBeNull();           // поведение 1
    article.Status.Should().Be(Pending);    // поведение 2
    _repo.Verify(r => r.AddAsync(...));     // поведение 3
    // → разбить на три отдельных теста
}

// ПЛОХО: магические числа без объяснения
_repo.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(article);
// → используй именованные переменные: const int existingArticleId = 42;

// ПЛОХО: тесты зависят друг от друга через статическое состояние
// → каждый [SetUp] создаёт свежие моки и sut

// ПЛОХО: Assert без FluentAssertions
Assert.IsTrue(result != null); // → result.Should().NotBeNull()
Assert.AreEqual(expected, actual); // → actual.Should().Be(expected)

// ПЛОХО: Verify для read-операций и чистых функций
_repo.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Once);
// → Verify только для write-операций и внешних вызовов (AI, HTTP, Telegram)

// ПЛОХО: лишние моки которые нигде не используются
[SetUp]
public void SetUp()
{
    _repoMock = new Mock<IArticleRepository>();
    _loggerMock = new Mock<ILogger<ArticleService>>(); // не используется ни в одном тесте
    _cacheMock = new Mock<ICacheService>();             // не используется ни в одном тесте
    _sut = new ArticleService(_repoMock.Object);
}
// → создавай только то что реально нужно

// ПЛОХО: дублирование бизнес-логики в тестах
[Test]
public void CalculateScore_ReturnsCorrectValue()
{
    var article = new Article { Views = 100, Likes = 50 };
    // воспроизводим алгоритм из прод-кода — так не надо
    var expected = article.Views * 0.4 + article.Likes * 0.6;
    _sut.CalculateScore(article).Should().Be(expected);
    // → используй конкретное известное значение: .Should().Be(70.0)
}

// ПЛОХО: DateTime.UtcNow в тестах
var article = new Article { PublishedAt = DateTime.UtcNow.AddDays(-8) };
sut.IsRecent(article).Should().BeFalse(); // хрупко
// → мокай TimeProvider или передавай явную дату-константу
```

---

## Checklist перед отправкой теста

- [ ] Имя теста читается как спецификация: `Method_When_Then`
- [ ] Тест проверяет одно поведение
- [ ] Секции Arrange/Act/Assert разделены и понятны
- [ ] `sut` — имя тестируемого объекта
- [ ] Все моки создаются в `[SetUp]`, не в теле теста
- [ ] Созданы только те моки, которые реально используются в тестах
- [ ] Нет `Thread.Sleep` или `DateTime.UtcNow` — мокай время через `TimeProvider`
- [ ] Граничные значения покрыты через `[TestCase]`
- [ ] Тест не зависит от порядка выполнения
- [ ] InMemory БД с `Guid.NewGuid()` именем — изолирован от других тестов
- [ ] `Verify` используется только для write-операций и внешних вызовов
- [ ] Бизнес-логика не продублирована — expected значение конкретное, не вычисленное
- [ ] Не более 5–8 тестов на метод (или есть явное обоснование почему больше)