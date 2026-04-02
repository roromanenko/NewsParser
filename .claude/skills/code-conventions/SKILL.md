---
name: code-conventions
description: NewsParser project-specific conventions for structure and placement. Use when adding a new class to any layer, asking where something belongs, or checking naming patterns. Triggers on: "where does X go", "what layer", "naming convention", "how do workers work", "how do services work", "how should I structure", "is this the right pattern", "how do repositories work", "Options pattern", "how do mappers work".
---

# Code Conventions — NewsParser

These rules are extracted from the actual codebase, not from generic theory. Every rule has a concrete reference.

---

## Layer Boundaries

### What lives where

| Layer | Allowed | Forbidden |
|---|---|---|
| `Core/` | Domain models, interfaces, enums | EF Core, HttpClient, any infrastructure |
| `Infrastructure/` | EF Core repos, AI clients, parsers, publishers, services | Direct HTTP handling, controller concerns |
| `Api/` | Controllers, middleware, DTOs, Api/Mappers | DbContext, EF Core, business logic |
| `Worker/` | BackgroundService subclasses, Worker/Configuration | DbContext direct access, controller concerns |

**Concrete violations to refuse:**
- DbContext injected into a controller → move to repository
- Business rule in a controller → move to service
- Inline `new SomeDto(...)` construction in a controller → move to `Api/Mappers/`
- EF Core using statement in `Core/` → not allowed

---

## Constructor Injection Style

### Services and controllers: primary constructor syntax

```csharp
// CORRECT — SourceService.cs
public class SourceService(ISourceRepository sourceRepository) : ISourceService

// CORRECT — ArticlesController.cs
public class ArticlesController(
    IArticleRepository articleRepository,
    IArticleApprovalService approvalService,
    IEventRepository eventRepository) : BaseController
```

### Workers: traditional constructor + private fields

Workers use old-style constructors because they must extract `options.Value` immediately and store `ILogger<T>`. Primary constructors can't do this cleanly.

```csharp
// CORRECT — ArticleAnalysisWorker.cs
public class ArticleAnalysisWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ArticleAnalysisWorker> logger,
    IOptions<ArticleProcessingOptions> options,
    IOptions<AiOptions> aiOptions)
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<ArticleAnalysisWorker> _logger = logger;
    private readonly ArticleProcessingOptions _options = options.Value;
    private readonly AiOptions _aiOptions = aiOptions.Value;
```

Workers only inject **singletons**: `IServiceScopeFactory`, `ILogger<T>`, `IOptions<T>`. Scoped services (repositories, AI clients) are resolved inside `ProcessAsync` via `_scopeFactory.CreateScope()`.

---

## Worker Architecture

Every worker follows the same three-level structure:

```
ExecuteAsync   — outer loop: while(!cancellationToken) → ProcessAsync + Task.Delay
ProcessAsync   — one cycle: create scope, resolve services, fetch batch
ProcessXxxAsync — per-item: try/catch, status transitions, retry logic
```

**Example from SourceFetcherWorker.cs:**
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await ProcessAsync(stoppingToken);
        await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
    }
}

private async Task ProcessAsync(CancellationToken cancellationToken)
{
    using var scope = _scopeFactory.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<IArticleRepository>();
    // ... resolve other scoped services
}
```

The delay interval always comes from an Options class — never a hardcoded literal.

---

## Repository Naming Patterns

| Pattern | Example | Use |
|---|---|---|
| `GetPendingForXxxAsync(int batchSize, ...)` | `GetPendingForAnalysisAsync` | Worker batch queries |
| `GetPendingForApprovalAsync(int page, int pageSize, ...)` | same | Paginated editor queries |
| `GetByIdAsync` | same | Single entity, minimal includes |
| `GetDetailAsync` | `GetDetailAsync` | Single entity, full includes for detail view |
| `UpdateXxxAsync` | `UpdateStatusAsync`, `UpdateRejectionAsync` | Targeted partial updates |
| `CountXxxAsync` | `CountPendingForApprovalAsync` | Separate count for pagination |
| `IncrementXxxAsync` | `IncrementRetryAsync` | Atomic counters |
| `MarkXxxAsync` | `MarkUpdatePublishedAsync`, `MarkAsReclassifiedAsync` | Boolean flag flips |
| `FindSimilarXxxAsync` | `FindSimilarEventsAsync` | Vector similarity search |
| `ExistsAsync` / `ExistsByXxxAsync` | `ExistsByUrlAsync` | Boolean existence checks |

---

## Update Pattern: Always ExecuteUpdateAsync

**Never** load an entity, modify properties, and call `SaveChanges` for updates. Use `ExecuteUpdateAsync` with `SetProperty`:

```csharp
// CORRECT — ArticleRepository.cs
await _context.Articles
    .Where(a => a.Id == id)
    .ExecuteUpdateAsync(a => a
        .SetProperty(x => x.Status, status.ToString())
        .SetProperty(x => x.RejectedByEditorId, editorId)
        .SetProperty(x => x.RejectionReason, reason),
    cancellationToken);

// Self-referencing increment (CORRECT)
.SetProperty(x => x.RetryCount, x => x.RetryCount + 1)
```

Exception: `CreateAsync` and `AddAsync` use `Add` + `SaveChangesAsync` — that's correct.

---

## Enum Storage and Parsing

Enums are stored in the database as **strings** (`.ToString()`), not integers.

```csharp
// ToEntity — store as string
Status = domain.Status.ToString(),
Role = domain.Role?.ToString(),

// ToDomain — parse from string
Status = Enum.Parse<ArticleStatus>(entity.Status),
Role = entity.Role != null ? Enum.Parse<EventArticleRole>(entity.Role) : null,

// Controller validation — TryParse with ignoreCase
if (!Enum.TryParse<EventStatus>(status, ignoreCase: true, out var eventStatus))
    return BadRequest($"Invalid status: {status}. Valid values: " +
        $"{string.Join(", ", Enum.GetNames<EventStatus>())}");
```

---

## Mapper Conventions

### Infrastructure/Persistence/Mappers — Entity ↔ Domain

- `static` class, `XxxMapper` name
- `ToDomain(this XxxEntity entity)` — entity → domain
- `ToEntity(this Xxx domain)` — domain → entity
- Factory methods for cross-domain creation: `FromAnalysisResult(rawArticle, result, modelVersion)`
- No I/O, no side effects — pure functions

```csharp
// EventMapper.cs — expression body for simple mappings
public static Event ToDomain(this EventEntity entity) => new()
{
    Id = entity.Id,
    Embedding = entity.Embedding?.ToArray(),
    Articles = entity.Articles?.Select(a => a.ToDomain()).ToList() ?? [],
    ...
};
```

### Api/Mappers — Domain → DTO

- `static` class, `XxxMapper` name
- `ToListItemDto(this Xxx obj)` — lightweight list representation
- `ToDetailDto(this Xxx obj, ...)` — full detail with related objects
- `ToDto(this Xxx obj)` — generic DTO conversion
- Conditional related objects handled inside the mapper (e.g., `ArticleEventDto` only when event is non-null)

```csharp
// Api/Mappers/ArticleMapper.cs
public static ArticleListItemDto ToListItemDto(this Article article) => new(
    article.Id, article.Title, article.Category, article.Tags,
    article.Sentiment.ToString(), article.Language, article.Summary, article.ProcessedAt
);
```

---

## Exception Handling Contract

Services throw typed BCL exceptions. `ExceptionMiddleware` maps them to HTTP status codes.

| Exception | HTTP Status | When to throw |
|---|---|---|
| `KeyNotFoundException` | 404 Not Found | Entity doesn't exist |
| `InvalidOperationException` | 409 Conflict | Business rule violation |
| `UnauthorizedAccessException` | 403 Forbidden | Permission denied |
| `ArgumentException` | 400 Bad Request | Invalid argument |

**Service example:**
```csharp
// SourceService.cs
var source = await sourceRepository.GetByIdAsync(id, cancellationToken)
    ?? throw new KeyNotFoundException($"Source {id} not found");

if (article.Status != ArticleStatus.Pending)
    throw new InvalidOperationException(
        $"Article {articleId} cannot be approved: status is {article.Status}");
```

**Controllers handle only input validation inline.** They do not catch service exceptions — those go to the middleware.

```csharp
// ArticlesController.cs — input validation only
if (request.PublishTargetIds is null || request.PublishTargetIds.Count == 0)
    return BadRequest("At least one publish target must be specified");

// Then just call the service — don't try/catch
var article = await approvalService.ApproveAsync(id, UserId.Value, ...);
```

---

## Configuration: Options Pattern

Every tunable value lives in an Options class, never hardcoded.

```csharp
// CORRECT — RssFetcherOptions.cs
public class RssFetcherOptions
{
    public const string SectionName = "RssFetcher";
    public int IntervalSeconds { get; set; } = 600; // default, overridable in appsettings
}

// CORRECT — ValidationOptions.cs
public int TitleSimilarityThreshold { get; set; } = 85;
public int TitleDeduplicationWindowHours { get; set; } = 24;
```

**Rules:**
- Always include `public const string SectionName = "..."` — used in DI registration
- Always provide sensible defaults
- Infrastructure config goes in `Infrastructure/Configuration/`
- Worker-specific config goes in `Worker/Configuration/`
- Inject `IOptions<T>` and extract `.Value` in the constructor, store as a plain field (not `IOptions<T>`)

---

## Interface Organization in Core/Interfaces/

Interfaces are grouped by role in subdirectories:

```
Core/Interfaces/
├── Repositories/    IArticleRepository, IEventRepository, ...
├── Services/        IArticleApprovalService, ISourceService, ...
├── AI/              IArticleAnalyzer, IEventClassifier, IGeminiEmbeddingService, ...
├── Parsers/         ISourceParser
├── Publishers/      IPublisher
└── Validators/      IRawArticleValidator
```

Each interface is focused on one role. `IArticleApprovalService` only has two methods; it does not mix approval with CRUD. `IRawArticleValidator` returns `(bool IsValid, string? Reason)` — a value tuple, not an exception.

---

## Domain Model Conventions

```csharp
// Immutable identity — init-only
public Guid Id { get; init; }
public DateTimeOffset FirstSeenAt { get; init; }

// Mutable state — settable
public ArticleStatus Status { get; set; }
public string Title { get; set; } = string.Empty;

// Collections always initialized empty
public List<Article> Articles { get; set; } = [];
public List<string> Tags { get; set; } = [];

// Navigation properties that must be loaded — null-forgiving
public RawArticle RawArticle { get; init; } = null!;

// Optional FK + optional nav
public Guid? EventId { get; set; }
public EventArticleRole? Role { get; set; }

// Timestamps use DateTimeOffset, never DateTime
public DateTimeOffset ProcessedAt { get; set; }
```

Enums co-located with the domain model file that owns them (e.g., `ArticleStatus`, `Sentiment`, `EventArticleRole` are all in `Article.cs`).

---

## Single Responsibility in Practice

| Class | Does exactly one thing |
|---|---|
| `SourceFetcherWorker` | Fetch + deduplicate raw articles from sources |
| `ArticleAnalysisWorker` | Run AI analysis on pending raw articles |
| `ArticleApprovalService` | Approve/reject articles (business rules + status transitions) |
| `SourceService` | CRUD for sources (validates uniqueness, delegates persistence) |
| `ArticleRepository` | EF Core queries/updates for articles and raw articles |
| `Api/Mappers/ArticleMapper` | Domain → DTO conversion, no other logic |

**Red flags that violate SRP:**
- A worker that also sends notifications or publishes content (separate workers exist for that)
- A controller that contains `if/else` business logic beyond input validation
- A repository method that does more than one logical data operation (except `MergeAsync` which is an explicit multi-step atomic operation)

---

## DRY Boundaries

**DRY is enforced:**
- Mapping logic — always in a static mapper class, never inline in controllers or workers
- Validation thresholds — always in Options classes, never magic numbers in code
- Status transition logic — always in services, never duplicated across workers

**DRY is intentionally relaxed:**
- Tests — duplication in test setup is acceptable for readability
- Repeated `?? throw new KeyNotFoundException(...)` calls — explicit and clear per-service, not abstracted

---

## BaseController

All controllers inherit `BaseController` for claim extraction:

```csharp
// BaseController.cs
protected Guid? UserId   // from ClaimTypes.NameIdentifier
protected string? UserEmail  // from Identity.Name
```

Always check `if (UserId is null) return Unauthorized();` before passing `UserId.Value` to a service.
