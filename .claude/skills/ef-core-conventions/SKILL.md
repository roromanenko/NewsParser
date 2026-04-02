---
name: ef-core-conventions
description: >
  NewsParser EF Core repository conventions for Infrastructure/Persistence/Repositories/.
  Use when adding a new repository class, adding a method to an existing repository,
  writing a query with Include/ThenInclude, using pgvector, or writing an update/delete
  operation. Triggers on: "add repository", "new repository", "add method to repository",
  "EF Core query", "pgvector query", "ExecuteUpdateAsync", "repository pattern",
  "add GetPendingFor", "write a query".
---

## Purpose

This skill documents the exact patterns used in `Infrastructure/Persistence/Repositories/`.
Do not invent new patterns — match what already exists.

---

## 1. Repository class structure

### Constructor injection — two valid styles

**Explicit field (older repos: ArticleRepository, RawArticleRepository, SourceRepository):**
```csharp
public class ArticleRepository : IArticleRepository
{
    private readonly NewsParserDbContext _context;

    public ArticleRepository(NewsParserDbContext context)
    {
        _context = context;
    }
    // methods use _context
}
```

**Primary constructor (newer repos: UserRepository, PublishTargetRepository, PublicationRepository):**
```csharp
public class PublishTargetRepository(NewsParserDbContext db) : IPublishTargetRepository
{
    // methods use db directly
}
```

Both styles are valid. Match the style of the repository you're editing. For new repositories, prefer the primary constructor style (more concise).

### Namespace and usings
```csharp
using Core.DomainModels;
using Core.Interfaces.Repositories;
using Infrastructure.Persistence.DataBase;
using Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;
```

Add `using Pgvector;` and `using Pgvector.EntityFrameworkCore;` only when the repository uses vector queries.

---

## 2. Update patterns — when to use which

### Pattern A — `ExecuteUpdateAsync` (updates and increments)

Use for **updating or deleting existing rows** without loading the entity into memory. This is the standard pattern for all mutations except inserts.

```csharp
// Single field
await _context.Articles
    .Where(a => a.Id == id)
    .ExecuteUpdateAsync(a => a.SetProperty(x => x.Status, status.ToString()), cancellationToken);

// Multiple fields — chain SetProperty
await _context.Articles
    .Where(a => a.Id == id)
    .ExecuteUpdateAsync(a => a
        .SetProperty(x => x.Title, title)
        .SetProperty(x => x.Content, content)
        .SetProperty(x => x.Status, status.ToString()),
    cancellationToken);

// Self-referencing increment
await _context.RawArticles
    .Where(r => r.Id == id)
    .ExecuteUpdateAsync(r => r
        .SetProperty(x => x.RetryCount, x => x.RetryCount + 1),
    cancellationToken);

// Delete
await db.Users
    .Where(u => u.Id == id)
    .ExecuteDeleteAsync(cancellationToken);
```

**Why:** Generates a single `UPDATE`/`DELETE` SQL statement. Never load an entity from the DB just to set a property and call `SaveChangesAsync` — that's two round-trips.

### Pattern B — `Add + SaveChangesAsync` (inserts only)

Use when **creating a new row**. Always convert domain → entity via the mapper before inserting.

```csharp
// Returns void (no round-trip needed after insert)
public async Task AddAsync(Article article, CancellationToken cancellationToken = default)
{
    var entity = article.ToEntity();
    await _context.Articles.AddAsync(entity, cancellationToken);
    await _context.SaveChangesAsync(cancellationToken);
}

// Returns the created domain object (re-maps entity after save to capture DB-generated values)
public async Task<Source> CreateAsync(Source source, CancellationToken cancellationToken = default)
{
    var entity = source.ToEntity();
    await _context.Sources.AddAsync(entity, cancellationToken);
    await _context.SaveChangesAsync(cancellationToken);
    return entity.ToDomain();
}
```

**When to return the domain object:** return `entity.ToDomain()` when the caller needs the saved state (e.g., DB-generated IDs or defaults). Use `void` / `Task` when the caller already has what it needs.

---

## 3. Method naming conventions

| Pattern | Signature example | Purpose |
|---|---|---|
| `GetByIdAsync` | `Task<T?> GetByIdAsync(Guid id, CT ct = default)` | Single entity by PK, returns null if not found |
| `GetDetailAsync` | `Task<T?> GetDetailAsync(Guid id, CT ct = default)` | Like GetByIdAsync but with deeper includes for detail views |
| `GetAllAsync` | `Task<List<T>> GetAllAsync(CT ct = default)` | Unfiltered list |
| `GetActiveAsync` | `Task<List<T>> GetActiveAsync(CT ct = default)` | Filtered by active/enabled status |
| `GetPagedAsync` | `Task<List<T>> GetPagedAsync(int page, int pageSize, CT ct = default)` | Paginated list |
| `GetPendingForXxxAsync` | `Task<List<T>> GetPendingForAnalysisAsync(int batchSize, CT ct = default)` | Batch fetch for a specific worker pipeline stage |
| `GetUnpublishedXxxAsync` | `Task<List<T>> GetUnpublishedUpdatesAsync(int batchSize, CT ct = default)` | Batch fetch for items awaiting a step |
| `GetRecentXxxAsync` | `Task<List<string>> GetRecentTitlesAsync(Guid currentId, int windowHours, CT ct = default)` | Windowed history fetch |
| `CountXxxAsync` | `Task<int> CountPendingForApprovalAsync(CT ct = default)` | Aggregate count |
| `CountTodayXxxAsync` | `Task<int> CountTodayUpdatesAsync(Guid id, CT ct = default)` | Count within the current calendar day |
| `UpdateXxxAsync` | `Task UpdateStatusAsync(Guid id, XxxStatus status, CT ct = default)` | Targeted field update via ExecuteUpdateAsync |
| `MarkXxxAsync` | `Task MarkUpdatePublishedAsync(Guid id, CT ct = default)` | Boolean flag flip |
| `IncrementXxxAsync` | `Task IncrementRetryAsync(Guid id, CT ct = default)` | Counter increment |
| `AssignXxxAsync` | `Task AssignArticleToEventAsync(Guid articleId, Guid eventId, ..., CT ct = default)` | Relationship assignment |
| `CreateAsync` | `Task<T> CreateAsync(T entity, CT ct = default)` | Insert, returns created domain object |
| `AddAsync` | `Task AddAsync(T entity, CT ct = default)` | Insert, no return value |
| `AddRangeAsync` | `Task AddRangeAsync(..., List<T> items, CT ct = default)` | Bulk insert |
| `DeleteAsync` | `Task DeleteAsync(Guid id, CT ct = default)` | Delete via ExecuteDeleteAsync |
| `ExistsByXxxAsync` | `Task<bool> ExistsByUrlAsync(string url, CT ct = default)` | Existence check |
| `HasSimilarAsync` | `Task<bool> HasSimilarAsync(...)` | Vector-based existence check |
| `FindSimilarXxxAsync` | `Task<List<(T, double)>> FindSimilarEventsAsync(...)` | Vector search returning scored results |

**Naming rules:**
- Prefix with `Get` for reads, `Update`/`Mark`/`Increment`/`Assign` for mutations, `Add`/`Create` for inserts, `Delete` for deletes, `Count`/`Exists`/`Has` for aggregates.
- Always suffix `Async`.
- The noun in the name names the *subject*, not the table: `CountPendingForApproval` (not `CountArticlesWithPendingStatus`).

---

## 4. Include/ThenInclude patterns

### Rule: include only what the caller needs

**List queries — minimal includes** (only navigation properties displayed in list views):
```csharp
// GetPagedAsync — shows article count and contradiction count in list
var entities = await _context.Events
    .Include(e => e.Articles)
    .Include(e => e.Contradictions)
    .OrderByDescending(e => e.LastUpdatedAt)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync(cancellationToken);
```

**Detail queries — full tree** (all navigation properties needed for the detail view):
```csharp
// GetDetailAsync — full event with updates and contradiction members
var entity = await _context.Events
    .Include(e => e.Articles)
    .Include(e => e.EventUpdates)
    .Include(e => e.Contradictions)
        .ThenInclude(c => c.ContradictionArticles)
    .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
```

**Batch/worker queries — include what the processor needs**:
```csharp
// GetPendingForContentGenerationAsync — worker needs article text and target config
var entities = await db.Publications
    .Include(p => p.Article)
        .ThenInclude(a => a.RawArticle)
    .Include(p => p.PublishTarget)
    .Where(p => p.Status == PublicationStatus.Pending.ToString())
    .OrderBy(p => p.CreatedAt)
    .Take(batchSize)
    .ToListAsync(cancellationToken);
```

**Single-entity with required navigation** (same pattern as GetByIdAsync):
```csharp
var entity = await _context.Articles
    .Include(a => a.RawArticle)
    .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
```

**`Include` placement:** Always before `FirstOrDefaultAsync` or `ToListAsync`. For `Take`+`Include`, place `Where`/`OrderBy`/`Take` before `Include` is fine — EF Core translates both orderings correctly, but the project places `Include` *before* `Where` in most list queries. Follow that convention for consistency.

---

## 5. Enum filtering pattern

Enums are stored as **strings** in the database. Always call `.ToString()` on the enum value when filtering — never compare an enum directly to a column:

```csharp
// Correct
.Where(r => r.Status == RawArticleStatus.Pending.ToString())
.Where(a => a.Status == ArticleStatus.AnalysisDone.ToString())
.Where(e => e.Status == EventStatus.Active.ToString())
.Where(s => s.Type == type.ToString())    // parameter enum → string

// Also used when setting values
.ExecuteUpdateAsync(a => a.SetProperty(x => x.Status, status.ToString()))
```

This applies to both `Where` filters and `SetProperty` calls in `ExecuteUpdateAsync`.

---

## 6. pgvector query pattern

Used in `RawArticleRepository` and `EventRepository`. Requires:
```csharp
using Pgvector;
using Pgvector.EntityFrameworkCore;
```

### Similarity search returning results with scores

```csharp
public async Task<List<(Event Event, double Similarity)>> FindSimilarEventsAsync(
    float[] embedding,
    double threshold,
    int windowHours,
    CancellationToken cancellationToken = default)
{
    var vector = new Vector(embedding);
    var windowStart = DateTimeOffset.UtcNow.AddHours(-windowHours);  // ⚠ see §7

    var results = await _context.Events
        .Where(e =>
            e.Status == EventStatus.Active.ToString() &&
            e.LastUpdatedAt >= windowStart &&
            e.Embedding != null)
        .Select(e => new
        {
            Entity = e,
            Similarity = 1 - e.Embedding!.CosineDistance(vector)
        })
        .Where(x => x.Similarity >= threshold)
        .OrderByDescending(x => x.Similarity)
        .ToListAsync(cancellationToken);

    return results
        .Select(x => (x.Entity.ToDomain(), x.Similarity))
        .ToList();
}
```

Key points:
- Wrap `float[]` in `new Vector(embedding)` before the query.
- Similarity = `1 - CosineDistance` (cosine distance gives 0 for identical vectors, 2 for opposite).
- Use a `Select` projection to compute similarity in-query before the threshold `Where`.
- Always filter `e.Embedding != null` before calling `.CosineDistance()`.
- Use `!` null-forgiving operator (`e.Embedding!`) inside the Select since you've already filtered nulls.

### Boolean existence check (inline CosineDistance)

```csharp
return await _context.RawArticles
    .Where(r => r.Id != currentId
        && r.PublishedAt >= since
        && r.Embedding != null
        && r.Status != RawArticleStatus.Rejected.ToString())
    .AnyAsync(r => 1 - r.Embedding!.CosineDistance(vector) >= threshold, cancellationToken);
```

For `AnyAsync`, embed the similarity expression directly in the predicate.

---

## 7. DateTimeOffset.UtcNow — testability concern

`DateTimeOffset.UtcNow` is called **directly inside repository methods** throughout the codebase:

```csharp
var windowStart = DateTimeOffset.UtcNow.AddHours(-windowHours);   // RawArticleRepository, EventRepository
var startOfDay  = DateTimeOffset.UtcNow.Date;                      // CountTodayUpdatesAsync
.SetProperty(e => e.LastUpdatedAt, DateTimeOffset.UtcNow)          // UpdateSummaryAndEmbeddingAsync, MergeAsync
.SetProperty(a => a.AddedToEventAt, DateTimeOffset.UtcNow)         // AssignArticleToEventAsync
```

**This is a known testability issue.** Tests that call these methods cannot control the clock. Until a `TimeProvider` or `IClock` abstraction is introduced, continue the existing pattern — use `DateTimeOffset.UtcNow` directly. Do not introduce a clock abstraction in a new repository without a broader decision to refactor all existing ones.

---

## 8. CancellationToken — always last, always defaulted

Every public async method takes a `CancellationToken` as its **last parameter** with `= default`:

```csharp
Task AddAsync(Article article, CancellationToken cancellationToken = default);
Task<List<T>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
Task UpdateStatusAsync(Guid id, ArticleStatus status, CancellationToken cancellationToken = default);
```

Pass `cancellationToken` to **every** EF Core async call: `AddAsync`, `SaveChangesAsync`, `ToListAsync`, `FirstOrDefaultAsync`, `AnyAsync`, `CountAsync`, `ExecuteUpdateAsync`, `ExecuteDeleteAsync`.

The parameter name is always `cancellationToken` (not `ct` or `token`).

---

## Checklist when adding a new repository method

- [ ] Method name follows the naming table in §3
- [ ] `CancellationToken cancellationToken = default` is the last parameter
- [ ] Updates use `ExecuteUpdateAsync`; inserts use `Add + SaveChangesAsync`
- [ ] Enums compared/set with `.ToString()`
- [ ] `Include` chain matches the use case (list vs detail vs worker batch)
- [ ] `DateTimeOffset.UtcNow` used directly (no injected clock)
- [ ] pgvector: `new Vector(embedding)`, `null` guard, `1 - CosineDistance`
- [ ] Interface in `Core/Interfaces/Repositories/` updated to match
