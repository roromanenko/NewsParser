---
name: dapper-conventions
description: >
  NewsParser Dapper repository conventions for Infrastructure/Persistence/Repositories/.
  Use when adding a new repository class, adding a method to an existing repository,
  writing a query with multi-table stitching, using pgvector, or writing an update/delete
  operation. Triggers on: "add repository", "new repository", "add method to repository",
  "Dapper query", "pgvector query", "ExecuteAsync", "repository pattern",
  "add GetPendingFor", "write a query", "IDbConnectionFactory", "IUnitOfWork".
---

## Purpose

This skill documents the exact patterns used in `Infrastructure/Persistence/Repositories/`.
Do not invent new patterns — match what already exists.

---

## 1. Repository class structure

### Constructor injection — primary constructor with IDbConnectionFactory and IUnitOfWork

```csharp
internal class SourceRepository(IDbConnectionFactory factory, IUnitOfWork uow) : ISourceRepository
{
    // methods use factory and uow
}
```

All repositories are `internal` (not `public`) because they depend on `internal` interfaces (`IDbConnectionFactory`, `IUnitOfWork`).

### Namespace and usings
```csharp
using Core.DomainModels;
using Core.Interfaces.Repositories;
using Dapper;
using Infrastructure.Persistence.Connection;
using Infrastructure.Persistence.Entity;
using Infrastructure.Persistence.Mappers;
using Infrastructure.Persistence.Repositories.Sql;
using Infrastructure.Persistence.UnitOfWork;

namespace Infrastructure.Persistence.Repositories;
```

Add `using Pgvector;` only when the repository uses vector parameters (e.g., `new Vector(embedding)`).

---

## 2. Connection lifecycle — per-operation

Each repository method opens and disposes its own connection via `await using`:

```csharp
public async Task<Source?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
{
    await using var conn = await factory.CreateOpenAsync(cancellationToken);
    var entity = await conn.QuerySingleOrDefaultAsync<SourceEntity>(
        new CommandDefinition(SourceSql.GetById, new { id }, cancellationToken: cancellationToken));
    return entity?.ToDomain();
}
```

### When UoW connection is available (multi-step operations)

For operations like `MergeAsync` and `AddContradictionAsync` that may run inside a caller-managed transaction:

```csharp
var conn = uow.CurrentConnection ?? await factory.CreateOpenAsync(cancellationToken);
var ownedConn = uow.CurrentConnection is null;

try
{
    await conn.ExecuteAsync(new CommandDefinition(Sql, params,
        transaction: uow.CurrentTransaction, cancellationToken: cancellationToken));
}
finally
{
    if (ownedConn)
        await conn.DisposeAsync();
}
```

---

## 3. CommandDefinition — always use it

Every Dapper call uses `CommandDefinition` with `cancellationToken`:

```csharp
new CommandDefinition(SqlConstant, parameterObject, cancellationToken: cancellationToken)
new CommandDefinition(SqlConstant, parameterObject, transaction: txn, cancellationToken: cancellationToken)
```

Never pass SQL as a plain string directly to `QueryAsync`/`ExecuteAsync`.

---

## 4. SQL constant classes

All SQL lives in `Infrastructure/Persistence/Repositories/Sql/{Aggregate}Sql.cs` as `internal static class`:

```csharp
internal static class SourceSql
{
    public const string GetById = """
        SELECT "Id", "Name", "Url", "Type", "IsActive", "LastFetchedAt"
        FROM sources WHERE "Id" = @id LIMIT 1
        """;

    public const string Insert = """
        INSERT INTO sources ("Id", "Name", "Url", "Type", "IsActive", "LastFetchedAt")
        VALUES (@Id, @Name, @Url, @Type, @IsActive, @LastFetchedAt)
        """;
}
```

Rules:
- Column names are PascalCase and **quoted** in SQL (e.g., `"Status"`, `"ProcessedAt"`).
- Table names are snake_case and unquoted (e.g., `articles`, `publish_targets`).
- No SQL literals in repository method bodies.

---

## 5. Update patterns

### Simple update — anonymous object parameters

```csharp
public async Task UpdateStatusAsync(Guid id, ArticleStatus status, CancellationToken cancellationToken = default)
{
    await using var conn = await factory.CreateOpenAsync(cancellationToken);
    await conn.ExecuteAsync(new CommandDefinition(ArticleSql.UpdateStatus,
        new { id, status = status.ToString() },
        cancellationToken: cancellationToken));
}
```

### Self-referencing increment

```csharp
public const string IncrementRetry = """
    UPDATE articles SET "RetryCount" = "RetryCount" + 1 WHERE "Id" = @id
    """;
```

### Insert returning domain object

```csharp
public async Task<Source> CreateAsync(Source source, CancellationToken cancellationToken = default)
{
    var entity = source.ToEntity();
    await using var conn = await factory.CreateOpenAsync(cancellationToken);
    await conn.ExecuteAsync(new CommandDefinition(SourceSql.Insert, new
    {
        entity.Id, entity.Name, entity.Url, entity.Type, entity.IsActive, entity.LastFetchedAt,
    }, cancellationToken: cancellationToken));
    return entity.ToDomain();
}
```

---

## 6. Enum storage and parsing

Enums are stored as **strings** in the database. Pass `.ToString()` when binding; parse with `Enum.Parse` in mappers.

```csharp
// Binding in SQL
new { status = status.ToString() }

// In mapper (ToDomain)
Status = Enum.Parse<ArticleStatus>(entity.Status),
```

---

## 7. pgvector parameter binding

Use `new Vector(embedding)` for vector parameters — the `VectorTypeHandler` handles wire format:

```csharp
await conn.ExecuteAsync(new CommandDefinition(ArticleSql.UpdateEmbedding,
    new { id, embedding = new Vector(embedding) },
    cancellationToken: cancellationToken));
```

For similarity queries:
```sql
1 - ("Embedding" <=> @vector::vector) AS similarity
```

---

## 8. Tags (text[]) — explicit DynamicParameters binding

Tags columns are `text[]` in PostgreSQL. Do NOT use `List<string>` directly — the `JsonbTypeHandler<List<string>>` handles JSONB and would intercept it. Instead, use `string[]` via `DynamicParameters`:

```csharp
private static DynamicParameters BuildTagsParameters(Guid id, List<string> tags, ...)
{
    var parameters = new DynamicParameters();
    parameters.Add("id", id);
    parameters.Add("tags", tags.ToArray()); // string[], not List<string>
    // ...
    return parameters;
}
```

Npgsql maps `string[]` → `text[]` natively without a type handler. This avoids the CLR-type collision with `JsonbTypeHandler<List<string>>` (used for KeyFacts JSONB).

---

## 9. KeyFacts and SelectedMediaFileIds — JSONB type handler

`KeyFacts` (`List<string>`) and `SelectedMediaFileIds` (`List<Guid>`) are JSONB columns. The `JsonbTypeHandler<T>` handles round-trip serialization automatically when the parameter is a `List<string>` or `List<Guid>`:

```csharp
// KeyFacts — handled automatically by JsonbTypeHandler<List<string>>
await conn.ExecuteAsync(new CommandDefinition(ArticleSql.UpdateKeyFacts,
    new { id, keyFacts },
    cancellationToken: cancellationToken));

// SelectedMediaFileIds — handled automatically by JsonbTypeHandler<List<Guid>>
parameters.Add("selectedMediaFileIds", mediaFileIds); // List<Guid>
```

---

## 10. Multi-row graph loading — separate queries + in-memory stitching

Replace EF `Include/ThenInclude` with separate queries joined in memory:

```csharp
// EventRepository.GetByIdAsync pattern:
var eventEntity = await conn.QuerySingleOrDefaultAsync<EventEntity>(...);
var articles = await conn.QueryAsync<ArticleEntity>(...);  // WHERE "EventId" = @id
var updates = await conn.QueryAsync<EventUpdateEntity>(...);
var contradictions = await conn.QueryAsync<ContradictionEntity>(...);

// Stitch
eventEntity.Articles = articles.ToList();
eventEntity.EventUpdates = updates.ToList();
eventEntity.Contradictions = contradictions.ToList();
```

For Dapper multi-map (one-to-many with splitOn):
```csharp
var articleDict = new Dictionary<Guid, ArticleEntity>();
await conn.QueryAsync<ArticleEntity, MediaFileEntity?, ArticleEntity>(
    new CommandDefinition(sql, new { id }, cancellationToken: ct),
    (article, media) =>
    {
        if (!articleDict.TryGetValue(article.Id, out var existing))
        {
            existing = article;
            existing.MediaFiles = [];
            articleDict[article.Id] = existing;
        }
        if (media is not null)
            existing.MediaFiles.Add(media);
        return existing;
    },
    splitOn: "Id");
```

---

## 11. ILIKE search — keep EscapeILikePattern

PostgreSQL `ILIKE` does NOT escape `%` and `_` via parameterization. Always call `QueryHelpers.EscapeILikePattern` before building the pattern:

```csharp
var escaped = QueryHelpers.EscapeILikePattern(search);
var pattern = $"%{escaped}%";
// SQL: WHERE "Title" ILIKE @pattern ESCAPE '\'
```

---

## 12. SKIP LOCKED — use with same connection

For `FOR UPDATE SKIP LOCKED` patterns (worker batch queries), both the lock query and the subsequent fetch must use the same connection (optionally the same transaction) so the lock is held:

```csharp
var conn = uow.CurrentConnection ?? await factory.CreateOpenAsync(cancellationToken);
var ownedConn = uow.CurrentConnection is null;

try
{
    var lockedIds = await conn.QueryAsync<Guid>(lockSql, transaction: uow.CurrentTransaction, ...);
    var rows = await conn.QueryAsync<PublicationEntity>(fetchSql, ..., transaction: uow.CurrentTransaction, ...);
    // process rows
}
finally
{
    if (ownedConn) await conn.DisposeAsync();
}
```

---

## 13. IUnitOfWork — opt-in for multi-step operations

`IUnitOfWork` is injected into every repository but is only _used_ when the caller explicitly calls `BeginAsync`. Repositories read `uow.CurrentConnection` / `uow.CurrentTransaction` and fall back to opening their own connection when they are null.

Services that need atomicity wrap calls in a transaction:

```csharp
await uow.BeginAsync(cancellationToken);
try
{
    await eventRepository.MergeAsync(sourceId, targetId, cancellationToken);
    await uow.CommitAsync(cancellationToken);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    await uow.RollbackAsync(cancellationToken);
    throw;
}
```

---

## 14. Method-name catalogue

| Pattern | Signature example | Purpose |
|---|---|---|
| `GetByIdAsync` | `Task<T?> GetByIdAsync(Guid id, CT ct = default)` | Single entity by PK, returns null if not found |
| `GetDetailAsync` | `Task<T?> GetDetailAsync(Guid id, CT ct = default)` | Single entity with all navigation data |
| `GetAllAsync` | `Task<List<T>> GetAllAsync(CT ct = default)` | Unfiltered list |
| `GetActiveAsync` | `Task<List<T>> GetActiveAsync(CT ct = default)` | Filtered by active status |
| `GetPagedAsync` | `Task<List<T>> GetPagedAsync(int page, int pageSize, CT ct = default)` | Paginated list |
| `GetPendingForXxxAsync` | `Task<List<T>> GetPendingForAnalysisAsync(int batchSize, CT ct = default)` | Worker batch queries (may use SKIP LOCKED) |
| `GetUnpublishedXxxAsync` | `Task<List<T>> GetUnpublishedUpdatesAsync(int batchSize, CT ct = default)` | Items awaiting a pipeline step |
| `CountXxxAsync` | `Task<int> CountPendingAsync(CT ct = default)` | Aggregate count |
| `UpdateXxxAsync` | `Task UpdateStatusAsync(Guid id, XxxStatus status, CT ct = default)` | Parameterized SQL update |
| `MarkXxxAsync` | `Task MarkUpdatePublishedAsync(Guid id, CT ct = default)` | Boolean flag flip |
| `IncrementXxxAsync` | `Task IncrementRetryAsync(Guid id, CT ct = default)` | Counter increment |
| `AssignXxxAsync` | `Task AssignArticleToEventAsync(Guid articleId, Guid eventId, ..., CT ct = default)` | Relationship assignment |
| `CreateAsync` | `Task<T> CreateAsync(T entity, CT ct = default)` | Insert, returns created domain object |
| `AddAsync` | `Task AddAsync(T entity, CT ct = default)` | Insert, no return value |
| `DeleteAsync` | `Task DeleteAsync(Guid id, CT ct = default)` | Delete by PK |
| `ExistsByXxxAsync` | `Task<bool> ExistsByUrlAsync(string url, CT ct = default)` | Existence check via SELECT EXISTS |
| `FindSimilarXxxAsync` | `Task<List<(T, double)>> FindSimilarEventsAsync(...)` | pgvector cosine similarity search |

---

## Checklist when adding a new repository method

- [ ] Method name follows the naming table above
- [ ] `CancellationToken cancellationToken = default` is the last parameter
- [ ] SQL constant defined in `*Sql.cs` class; no string literals in repo method body
- [ ] `CommandDefinition` used with `cancellationToken`
- [ ] Updates use parameterized SQL `ExecuteAsync`; inserts use `ExecuteAsync` or `QuerySingleAsync`
- [ ] Enums compared/set with `.ToString()`
- [ ] Graph loading: separate queries + in-memory stitching
- [ ] `DateTimeOffset.UtcNow` used directly (no injected clock)
- [ ] pgvector: `new Vector(embedding)` for parameters
- [ ] Tags bound as `string[]` via `DynamicParameters` (not `List<string>`)
- [ ] Interface in `Core/Interfaces/Repositories/` updated to match
