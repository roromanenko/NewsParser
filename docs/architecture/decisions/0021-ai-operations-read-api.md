# 0021 — AI Operations Read API (Admin)

## Status
Proposed

## Context

ADR `ai-request-logging-and-cost-tracking.md` (commit `210aff4`) added the writer half of
AI request logging: every Anthropic / Gemini call now persists a row to `ai_request_log`.
That ADR explicitly closed itself with: *"No UI / API read endpoints for the log. That's
a later task"* and *"No aggregation queries; when added they belong in a new method on
`IAiRequestLogRepository`, covered by a future ADR."*

ADR 0020 (AI Operations Dashboard) is the frontend half of that later task. It is blocked
on a backend that exposes the read endpoints — verified against the codebase:

- `Core/Interfaces/Repositories/IAiRequestLogRepository.cs` exposes only `AddAsync`.
- `Api/Controllers/` contains no `AiOperationsController` or equivalent.

This ADR designs that backend half. It is the **direct backend prerequisite** ADR 0020 §D5
calls out as a hard blocker.

### What already exists (verified in repo on `feature/ai-request-logging`)

- **Domain model** — `Core/DomainModels/AiRequestLog.cs`. Class with all 17 fields plus
  `AiRequestStatus` enum (`Success | Error`). `Status` and `Provider` are stored as
  strings in the DB (per enum convention) — `Status` parsed via `Enum.Parse<AiRequestStatus>`,
  `Provider` is a free-form string set by the AI clients (`"Anthropic"` | `"Gemini"`).
- **Entity** — `Infrastructure/Persistence/Entity/AiRequestLogEntity.cs`. Mirrors the
  domain class with `Status` as `string`. No JSONB, no `text[]`, no pgvector — pure scalars.
- **Mapper** — `Infrastructure/Persistence/Mappers/AiRequestLogMapper.cs`. `ToDomain` /
  `ToEntity` pair. We will reuse `ToDomain` from the new read methods.
- **Schema** — `Infrastructure/Persistence/Sql/0005_add_ai_request_log.sql`. Indexes exist
  on `Timestamp`, `Provider`, `Worker`, `Model`, `ArticleId`, `CorrelationId`. The same
  index columns are exactly the filter axes ADR 0020 specifies — no new migration needed.
- **Repository** — `Infrastructure/Persistence/Repositories/AiRequestLogRepository.cs`,
  `internal class AiRequestLogRepository(IDbConnectionFactory factory, IUnitOfWork uow)`,
  uses `await using var conn = await factory.CreateOpenAsync(cancellationToken)` per call,
  registered as `Scoped` in `InfrastructureServiceExtensions:72`.
- **SQL constants** — `Infrastructure/Persistence/Repositories/Sql/AiRequestLogSql.cs`.
  Currently only an `Insert` constant.

### Patterns this ADR must match

- **Admin-only controller pattern** (`Api/Controllers/UsersController.cs:12`):
  `[Authorize(Roles = nameof(UserRole.Admin))]`. Inherits `BaseController`, primary
  constructor DI. No policy-based authorization is used in the project today.
- **Pagination** (`Api/Models/PagedResult.cs`): a single project-wide
  `record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount)` with
  computed `TotalPages` / `HasNextPage` / `HasPreviousPage`. Used by
  `PublicationsController.GetAll`. We must reuse it; do not invent a new shape.
- **Pagination guard** (`api-conventions` SKILL §"Pagination Guard Pattern"): clamp
  `page < 1 → 1`, `pageSize < 1 || > 100 → 20` in the controller before calling the repo.
- **Paginated repo with optional search** (`ArticleRepository.GetAnalysisDoneAsync`):
  separate `GetXxxAsync(page, pageSize, ...)` and `CountXxxAsync(...)` methods, each
  branching on whether `search` is populated, with two SQL constants per branch. Sort
  direction is interpolated via `string.Format` because `ORDER BY` cannot be parameterised.
- **ILIKE escape** (`Infrastructure/Persistence/Repositories/QueryHelpers.cs`):
  `QueryHelpers.EscapeILikePattern(input)` + `ESCAPE '\'` clause. Mandatory for any
  user-supplied search string.
- **Read-only repository methods do NOT use `IUnitOfWork`** (`dapper-conventions` SKILL §13):
  *"`IUnitOfWork` is injected into every repository but is only used when the caller
  explicitly calls `BeginAsync`."* Single-statement reads open a connection via
  `factory.CreateOpenAsync` and dispose it. Verified against `GetAnalysisDoneAsync`,
  `GetByIdAsync` patterns in `ArticleRepository`. Our three new read methods follow this
  rule — no UoW.
- **Mappers `Domain → DTO`** (`mappers` SKILL): static class `XxxMapper` in `Api/Mappers/`,
  extension methods `ToDto` / `ToListItemDto` / `ToDetailDto`. Enums to string via
  `.ToString()`.
- **DTOs are records co-located in one file per aggregate** (`Api/Models/EventDtos.cs`,
  `Api/Models/PublicationDtos.cs`). Multi-DTO file uses one `Dtos` filename suffix.
- **FluentValidation** (`api-conventions` SKILL): one `XxxValidator : AbstractValidator<XxxRequest>`
  per request type, registered automatically via
  `AddValidatorsFromAssemblyContaining<RegisterRequestValidator>()` in
  `Api/Extensions/ApiServiceExtensions.cs:20`. No manual wiring needed for new validators.

### What `Provider` and `Status` look like in the data

- `Status` — `string NOT NULL DEFAULT 'Success'`. Two values today (`"Success"`, `"Error"`),
  parsed in mapper via `Enum.Parse<AiRequestStatus>`. The filter accepts a string and the
  validator constrains it to the known enum values.
- `Provider` — `string NOT NULL DEFAULT ''`. Set by the AI clients to `"Anthropic"` or
  `"Gemini"` (per the writer ADR D3). There is no `Provider` enum on the domain model;
  it is a free-form string. The filter therefore takes a free-form string too — no
  enum coercion in the validator, just length / non-empty checks.

---

## Options

### Decision 1 — SQL strategy for `GetMetricsAsync`

`AiOperationsMetricsDto` aggregates the same filtered row set into **five** result shapes:

1. Top-level KPIs (`totalCostUsd`, `totalCalls`, `successCalls`, `errorCalls`,
   `averageLatencyMs`, four token totals).
2. `timeSeries[]` — `(date_trunc('day', "Timestamp") AS bucket, "Provider", SUM(cost),
   SUM(calls), SUM(tokens)) GROUP BY bucket, "Provider"`.
3. `byModel[]` — `("Model" AS key, COUNT, SUM(cost), SUM(tokens)) GROUP BY "Model"`.
4. `byWorker[]` — same shape, grouped by `"Worker"`.
5. `byProvider[]` — same shape, grouped by `"Provider"`.

These are five aggregations over the same WHERE clause. Two ways to execute them.

#### Option 1a — Single round-trip via `Dapper.QueryMultipleAsync`

One `CommandDefinition` carrying five `SELECT ... FROM ai_request_log WHERE ...; SELECT
date_trunc(...), ...; ...` semicolon-separated statements, executed via
`conn.QueryMultipleAsync`. Each `await reader.ReadAsync<TRow>()` consumes the next result
set and we compose the DTO in C#.

**Pros:**
- One network round-trip — significant on a remote DB. The data-access reviewer has already
  flagged this exact pattern for `EventRepository.GetByIdAsync` (`docs/reviews/data-access-findings.md:108`,
  `docs/reviews/ACTION_PLAN.md:250`) as the recommended consolidation. Adopting it here
  prevents repeating that mistake.
- Filter parameters bind once for all five queries (Dapper reuses the parameter object).
- Postgres can plan the five aggregations against the same index scan / cached pages.

**Cons:**
- `QueryMultipleAsync` is **not used anywhere in the codebase today** — this introduces
  it. Counter-balance: it is sanctioned by the existing data-access review and is plain
  Dapper, not a new abstraction.
- The composed SQL constant is longer (five SELECTs in one string). Mitigation:
  individual SQL fragments live as separate `private const string` parts in
  `AiRequestLogSql.cs` and a single `Metrics` constant concatenates them with
  `Environment.NewLine`, keeping each fragment readable.

#### Option 1b — Five sequential single-statement queries

Open one connection, run five `QueryAsync` / `ExecuteScalarAsync` calls in sequence,
build the DTO in C#.

**Pros:**
- Mirrors the per-method SQL pattern used everywhere else in repositories — no new
  Dapper API surface.

**Cons:**
- Five round-trips on a remote DB. The aggregation queries themselves are fast (the
  `Timestamp` index covers the WHERE clause) so query cost is small; **all** the latency
  is the network. This is precisely the regression flagged for `EventRepository.GetByIdAsync`.
- The five SQL constants must each duplicate the same WHERE clause, increasing the chance
  of drift when a filter is added in the future.

#### Decision 1 — Option 1a (`QueryMultipleAsync`).

The metrics endpoint is the dashboard's main payload — every full page load and every
filter change re-runs it. One round-trip versus five is a real, measurable difference on
a managed Postgres (Aiven) where RTT is double-digit milliseconds. The pattern is
explicitly endorsed by the data-access review for the comparable `EventRepository`
case. The shared WHERE clause is also a maintainability win: a new filter knob (e.g.
`status` on metrics, if added later) is added in one place.

The implementation builds the WHERE clause and parameter dictionary **once** from the
shared `AiRequestLogFilter` value object (Decision 2) and concatenates it into each of
the five SELECTs in the SQL constant (using a `{0}` placeholder per fragment, filled in
by `string.Format` — same technique already used in `ArticleSql.GetAnalysisDoneWithSearch`
for `ORDER BY` direction).

### Decision 2 — Filter shape

Three endpoints accept overlapping filters:

- `GetMetricsAsync` — `from`, `to`, `provider`, `worker`, `model`.
- `GetPagedAsync` — same five **plus** `status`, `search`. (`status` and `search` are
  list-only filters; the metrics aggregation does not narrow by them.)
- `GetByIdAsync` — none, just the id.

#### Option 2a — One shared `AiRequestLogFilter` value object in `Core/`

A single `record AiRequestLogFilter(DateTimeOffset? From, DateTimeOffset? To, string?
Provider, string? Worker, string? Model, string? Status, string? Search)` lives in
`Core/DomainModels/`. Both repository methods accept it. Optional fields the metrics
query ignores (`Status`, `Search`) are simply not used in its SQL builder.

**Pros:**
- Single point of maintenance — adding a filter knob updates one record.
- The Api-layer query DTOs (`AiOperationsMetricsQuery`, `AiRequestLogListQuery`) map
  cleanly onto subsets of this filter when calling the repository.
- Mirrors the spirit of the codebase's other "one value object passed across layers"
  precedents (`AiUsage`, `AiRequestLogEntry` in the writer ADR).

**Cons:**
- The filter on `Core` exposes filter knobs that one of the two methods ignores. Slight
  mismatch between method contract and parameter shape.

#### Option 2b — Two separate filter records, one per repo method

Two records in `Core/DomainModels/`: `AiRequestLogMetricsFilter` (5 fields) and
`AiRequestLogListFilter` (7 fields).

**Pros:**
- Each method's contract is precise — every field is used.

**Cons:**
- Two records that overlap on five of seven fields. Most filters need to be added in
  two places. Worse churn for a future change.
- The Api-layer query DTO would need two distinct mappers to two distinct filter records.

#### Decision 2 — Option 2a (one shared `AiRequestLogFilter`).

The five overlapping fields dominate the surface; the two list-only fields are clearly
a superset. We accept the small "method ignores some fields" mismatch in exchange for
single-point-of-maintenance. The repository SQL builder explicitly documents which
fields each query consumes via inline comments.

### Decision 3 — DTO file layout

Per the project's existing convention (`Api/Models/EventDtos.cs`,
`Api/Models/PublicationDtos.cs`), DTOs for one aggregate live in a single
`{Aggregate}Dtos.cs` file. We create `Api/Models/AiOperationsDtos.cs` containing all
records: `AiOperationsMetricsDto`, `AiRequestLogDto`, the four breakdown sub-records,
the time-bucket sub-record, and any query-DTOs we choose to bind from the request.

We do **not** create an `Api/Dtos/AiOperations/` subdirectory — no other admin area in
the project has a per-feature DTO folder.

---

## Decision

A new admin-only `AiOperationsController`, three new aggregation methods on
`IAiRequestLogRepository`, a single shared `AiRequestLogFilter` value object in `Core/`,
one DTO file, one mapper file, two FluentValidation validators. SQL strategy for metrics
is one-round-trip via `Dapper.QueryMultipleAsync`. No schema change.

### D1. Core layer (`Core/`)

**New value object** — `Core/DomainModels/AiRequestLogFilter.cs`:
```csharp
public record AiRequestLogFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Provider,
    string? Worker,
    string? Model,
    string? Status,
    string? Search);
```

`From` / `To` are `DateTimeOffset?` per the project's timestamp convention. `Status` is a
string here (not `AiRequestStatus?`) so the controller can pass through the raw string;
the repository compares against the column directly without parsing. The validator
ensures only `"Success"` / `"Error"` are accepted before reaching the repository.

**New aggregation result types** — same file or a small `Core/DomainModels/AiMetrics.cs`,
recorded so the repository return shape doesn't leak Api-layer DTOs into Core:

```csharp
public record AiMetricsTotals(
    decimal TotalCostUsd, int TotalCalls, int SuccessCalls, int ErrorCalls,
    double AverageLatencyMs,
    int TotalInputTokens, int TotalOutputTokens,
    int TotalCacheCreationInputTokens, int TotalCacheReadInputTokens);

public record AiMetricsTimeBucket(
    DateTimeOffset Bucket, string Provider, decimal CostUsd, int Calls, int Tokens);

public record AiMetricsBreakdownRow(
    string Key, int Calls, decimal CostUsd, int Tokens);

public record AiRequestLogMetrics(
    AiMetricsTotals Totals,
    List<AiMetricsTimeBucket> TimeSeries,
    List<AiMetricsBreakdownRow> ByModel,
    List<AiMetricsBreakdownRow> ByWorker,
    List<AiMetricsBreakdownRow> ByProvider);
```

These are the domain-side return shapes. The Api mapper then projects them into
`AiOperationsMetricsDto`. (Alternative considered: return a tuple from the repository.
Rejected — five-element nested tuples are unreadable, and the pattern of returning a
named result type has precedent in `Core/DomainModels/AI/AiUsage.cs` and
`Core/DomainModels/Article.cs` related types.)

**Updated interface** — `Core/Interfaces/Repositories/IAiRequestLogRepository.cs`
gains three methods (existing `AddAsync` stays):
```csharp
Task<AiRequestLogMetrics> GetMetricsAsync(AiRequestLogFilter filter, CancellationToken cancellationToken = default);
Task<List<AiRequestLog>> GetPagedAsync(AiRequestLogFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);
Task<int> CountAsync(AiRequestLogFilter filter, CancellationToken cancellationToken = default);
Task<AiRequestLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
```

`CountAsync` is added because the controller needs `TotalCount` for the `PagedResult<T>`.
This matches the per-aggregate pattern in `IArticleRepository` (`GetAnalysisDoneAsync` +
`CountAnalysisDoneAsync`).

### D2. Infrastructure layer

**New SQL constants** in `Infrastructure/Persistence/Repositories/Sql/AiRequestLogSql.cs`:

- `private const string WhereClauseTemplate` — the shared WHERE clause built dynamically
  in C# (described below).
- `public const string Metrics` — the five-statement SQL passed to `QueryMultipleAsync`.
  Constructed at call time via `string.Format` because the WHERE clause is dynamic.
  Statements appear in this order so the reader code is deterministic:
  1. KPI totals (one row).
  2. `timeSeries` rows.
  3. `byModel` rows.
  4. `byWorker` rows.
  5. `byProvider` rows.
- `public const string GetPagedWithSearch` / `GetPagedWithoutSearch` — paired SQL strings
  matching the established `ArticleSql.GetAnalysisDoneWith/WithoutSearch` pattern.
- `public const string CountWithSearch` / `CountWithoutSearch` — same pattern.
- `public const string GetById` — single-row select.

The dynamic WHERE clause is built in a small private helper inside the repository:
```csharp
private static (string Sql, DynamicParameters Params) BuildWhere(AiRequestLogFilter filter)
{
    var clauses = new List<string>();
    var p = new DynamicParameters();
    if (filter.From is not null)     { clauses.Add(@"""Timestamp"" >= @from");      p.Add("from", filter.From); }
    if (filter.To is not null)       { clauses.Add(@"""Timestamp"" <  @to");        p.Add("to", filter.To); }
    if (!string.IsNullOrWhiteSpace(filter.Provider)) { clauses.Add(@"""Provider"" = @provider"); p.Add("provider", filter.Provider); }
    if (!string.IsNullOrWhiteSpace(filter.Worker))   { clauses.Add(@"""Worker"" = @worker");     p.Add("worker", filter.Worker); }
    if (!string.IsNullOrWhiteSpace(filter.Model))    { clauses.Add(@"""Model"" = @model");       p.Add("model", filter.Model); }
    // status / search are only consumed by GetPaged / Count — caller passes them
    // through the same builder; metrics SQL ignores them by design.
    return (clauses.Count == 0 ? "1=1" : string.Join(" AND ", clauses), p);
}
```

For `GetPagedAsync` / `CountAsync` only, the builder also adds:
```csharp
if (!string.IsNullOrWhiteSpace(filter.Status))   { clauses.Add(@"""Status"" = @status");     p.Add("status", filter.Status); }
if (!string.IsNullOrWhiteSpace(filter.Search))
{
    var pattern = $"%{QueryHelpers.EscapeILikePattern(filter.Search)}%";
    clauses.Add(@"(""Operation"" ILIKE @pattern ESCAPE '\' OR ""Model"" ILIKE @pattern ESCAPE '\' OR ""ErrorMessage"" ILIKE @pattern ESCAPE '\')");
    p.Add("pattern", pattern);
}
```

**Note on `Search`:** the dashboard's search box (per ADR 0020 §D4) is global. Three
columns are searched: `Operation`, `Model`, and `ErrorMessage`. `CorrelationId` /
`ArticleId` are not searched by string — operators paste them as filters via dedicated
fields if needed (deferred until requested per ADR 0020's "Out of scope"). The three
chosen columns are all `TEXT` and either already indexed (`Model`) or low-cardinality
enough that a sequential scan within a date range is fine.

**Time-bucket SQL** (server-side, day granularity per ADR 0020 §D5):
```sql
SELECT date_trunc('day', "Timestamp") AS bucket,
       "Provider",
       COALESCE(SUM("CostUsd"), 0)     AS cost_usd,
       COUNT(*)                         AS calls,
       COALESCE(SUM("TotalTokens"), 0) AS tokens
FROM ai_request_log
WHERE {0}
GROUP BY bucket, "Provider"
ORDER BY bucket ASC, "Provider" ASC;
```

`date_trunc('day', ...)` is acceptable for the v1 default 7-day window. Larger ranges
(90+ days) may want week / month bucketing — flagged as future work below; the v1 client
defaults to 7 days so this does not bite immediately.

**`GetMetricsAsync` repository method:**
```csharp
public async Task<AiRequestLogMetrics> GetMetricsAsync(
    AiRequestLogFilter filter, CancellationToken cancellationToken = default)
{
    var (where, parameters) = BuildWhere(filter);
    var sql = string.Format(AiRequestLogSql.Metrics, where);

    await using var conn = await factory.CreateOpenAsync(cancellationToken);
    using var grid = await conn.QueryMultipleAsync(
        new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

    var totals    = await grid.ReadSingleAsync<AiMetricsTotalsRow>();
    var timeSeries = (await grid.ReadAsync<AiMetricsTimeBucketRow>()).ToList();
    var byModel    = (await grid.ReadAsync<AiMetricsBreakdownRow>()).ToList();
    var byWorker   = (await grid.ReadAsync<AiMetricsBreakdownRow>()).ToList();
    var byProvider = (await grid.ReadAsync<AiMetricsBreakdownRow>()).ToList();

    return new AiRequestLogMetrics(
        totals.ToDomain(),
        timeSeries.Select(t => t.ToDomain()).ToList(),
        byModel,
        byWorker,
        byProvider);
}
```

(Internal `*Row` types are private structs that match the SQL column shapes; they are
mapped to the public Core records inside the same file. This avoids leaking
Dapper-binding shapes into Core. Keeping them as `internal` rows in the Infrastructure
mapper is consistent with how `XxxEntity` already works.)

**Connection lifecycle:** `await using var conn = await factory.CreateOpenAsync(ct)` per
the dapper-conventions skill §2. **No `IUnitOfWork`** — the three new methods are
single-statement reads (the metrics call is one `QueryMultipleAsync` over one connection,
which is one statement from a transactional standpoint). This matches the pattern in
`ArticleRepository.GetAnalysisDoneAsync` and is endorsed by the dapper-conventions skill
§13: "`IUnitOfWork` is injected into every repository but is only used when the caller
explicitly calls `BeginAsync`."

**`GetPagedAsync` / `CountAsync` / `GetByIdAsync`** follow the existing
`ArticleRepository` patterns one-for-one — ILIKE search via `EscapeILikePattern`, sort
direction interpolated (default `ORDER BY "Timestamp" DESC`, no UI knob in v1), `LIMIT`
/ `OFFSET` parameterised. `ToDomain()` from the existing mapper handles row → domain
conversion.

### D3. Api layer

**New DTOs** — `Api/Models/AiOperationsDtos.cs` (one file, multiple records, matching
`EventDtos.cs` / `PublicationDtos.cs` convention):

```csharp
public record AiOperationsMetricsDto(
    decimal TotalCostUsd,
    int TotalCalls,
    int SuccessCalls,
    int ErrorCalls,
    double AverageLatencyMs,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalCacheCreationInputTokens,
    int TotalCacheReadInputTokens,
    List<AiMetricsTimeBucketDto> TimeSeries,
    List<AiMetricsBreakdownRowDto> ByModel,
    List<AiMetricsBreakdownRowDto> ByWorker,
    List<AiMetricsBreakdownRowDto> ByProvider);

public record AiMetricsTimeBucketDto(
    DateTimeOffset Bucket,
    string Provider,
    decimal CostUsd,
    int Calls,
    int Tokens);

public record AiMetricsBreakdownRowDto(
    string Key,
    int Calls,
    decimal CostUsd,
    int Tokens);

public record AiRequestLogDto(
    Guid Id,
    DateTimeOffset Timestamp,
    string Worker,
    string Provider,
    string Operation,
    string Model,
    int InputTokens,
    int OutputTokens,
    int CacheCreationInputTokens,
    int CacheReadInputTokens,
    int TotalTokens,
    decimal CostUsd,
    int LatencyMs,
    string Status,         // serialized via .ToString() — DTO convention
    string? ErrorMessage,
    Guid CorrelationId,
    Guid? ArticleId);
```

The DTO mirrors the domain `AiRequestLog` exactly — 17 fields. `Status` is a string in
the DTO per the api-conventions skill (*"Enum fields in DTOs are always strings"*).
`ErrorMessage` is included in full because (a) the controller is admin-only — full
disclosure is fine — and (b) the writer already truncates to 500 chars on insert.

**New mapper** — `Api/Mappers/AiOperationsMapper.cs` (matches the
`Api/Mappers/{Aggregate}Mapper.cs` naming used by `ArticleMapper`, `EventMapper`, etc.):

```csharp
public static class AiOperationsMapper
{
    public static AiRequestLogDto ToDto(this AiRequestLog log) => new(
        log.Id, log.Timestamp, log.Worker, log.Provider, log.Operation, log.Model,
        log.InputTokens, log.OutputTokens,
        log.CacheCreationInputTokens, log.CacheReadInputTokens,
        log.TotalTokens, log.CostUsd, log.LatencyMs,
        log.Status.ToString(), log.ErrorMessage,
        log.CorrelationId, log.ArticleId);

    public static AiMetricsTimeBucketDto ToDto(this AiMetricsTimeBucket b) =>
        new(b.Bucket, b.Provider, b.CostUsd, b.Calls, b.Tokens);

    public static AiMetricsBreakdownRowDto ToDto(this AiMetricsBreakdownRow r) =>
        new(r.Key, r.Calls, r.CostUsd, r.Tokens);

    public static AiOperationsMetricsDto ToDto(this AiRequestLogMetrics m) => new(
        m.Totals.TotalCostUsd, m.Totals.TotalCalls,
        m.Totals.SuccessCalls, m.Totals.ErrorCalls,
        m.Totals.AverageLatencyMs,
        m.Totals.TotalInputTokens, m.Totals.TotalOutputTokens,
        m.Totals.TotalCacheCreationInputTokens, m.Totals.TotalCacheReadInputTokens,
        m.TimeSeries.Select(t => t.ToDto()).ToList(),
        m.ByModel.Select(r => r.ToDto()).ToList(),
        m.ByWorker.Select(r => r.ToDto()).ToList(),
        m.ByProvider.Select(r => r.ToDto()).ToList());
}
```

Pure static extension methods, no logging, no DI — per `mappers` skill.

**New controller** — `Api/Controllers/AiOperationsController.cs`:

```csharp
[ApiController]
[Route("ai-operations")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class AiOperationsController(
    IAiRequestLogRepository repository) : BaseController
{
    [HttpGet("metrics")]
    public async Task<ActionResult<AiOperationsMetricsDto>> GetMetrics(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? provider,
        [FromQuery] string? worker,
        [FromQuery] string? model,
        CancellationToken cancellationToken = default)
    {
        var filter = new AiRequestLogFilter(from, to, provider, worker, model, Status: null, Search: null);
        var metrics = await repository.GetMetricsAsync(filter, cancellationToken);
        return Ok(metrics.ToDto());
    }

    [HttpGet("requests")]
    public async Task<ActionResult<PagedResult<AiRequestLogDto>>> GetRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? provider = null,
        [FromQuery] string? worker = null,
        [FromQuery] string? model = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        var filter = new AiRequestLogFilter(from, to, provider, worker, model, status, search);
        var items = await repository.GetPagedAsync(filter, page, pageSize, cancellationToken);
        var total = await repository.CountAsync(filter, cancellationToken);

        return Ok(new PagedResult<AiRequestLogDto>(
            items.Select(l => l.ToDto()).ToList(), page, pageSize, total));
    }

    [HttpGet("requests/{id:guid}")]
    public async Task<ActionResult<AiRequestLogDto>> GetRequestById(
        Guid id, CancellationToken cancellationToken = default)
    {
        var log = await repository.GetByIdAsync(id, cancellationToken);
        if (log is null) return NotFound();
        return Ok(log.ToDto());
    }
}
```

- `[Authorize(Roles = nameof(UserRole.Admin))]` — same line as `UsersController.cs:12`.
- Route `ai-operations` (lowercase, kebab-case, no `/api/` prefix) — matches
  api-conventions skill §"Route Naming".
- `BaseController` is inherited even though the controller does not currently use
  `UserId` — every authenticated controller in the codebase extends it; staying
  consistent costs nothing and lets a future change reach `UserId` without surgery.
- The controller does **not** depend on a service layer. There is no business rule
  beyond filter shaping; no status transitions; no domain invariants. Per code-conventions
  skill §"Layer Boundaries", services exist where business logic exists. Read endpoints
  that just call a repository method legitimately go controller → repository (precedent:
  `PublicationsController.GetAll` calls `publicationRepository.GetAllAsync` directly).

### D4. Validators

**Two new validators** in `Api/Validators/`:

`AiOperationsMetricsQueryValidator` validates the metrics query parameters. Because the
parameters are bound via `[FromQuery]` individually (not as a request DTO), we wrap them
in a small `record AiOperationsMetricsQuery(DateTimeOffset? From, DateTimeOffset? To,
string? Provider, string? Worker, string? Model)` and bind it as `[FromQuery]
AiOperationsMetricsQuery query` so FluentValidation auto-validation engages.

Same approach for `AiRequestsListQuery` (the seven-field paged-list filter, plus `Page`
and `PageSize`).

```csharp
public class AiOperationsMetricsQueryValidator : AbstractValidator<AiOperationsMetricsQuery>
{
    public AiOperationsMetricsQueryValidator()
    {
        RuleFor(x => x.From).LessThan(x => x.To)
            .When(x => x.From is not null && x.To is not null)
            .WithMessage("'from' must be earlier than 'to'");

        RuleFor(x => x.To).GreaterThan(x => x.From)
            .When(x => x.From is not null && x.To is not null);

        RuleFor(x => x.Provider).MaximumLength(50);
        RuleFor(x => x.Worker).MaximumLength(100);
        RuleFor(x => x.Model).MaximumLength(100);
    }
}

public class AiRequestsListQueryValidator : AbstractValidator<AiRequestsListQuery>
{
    private static readonly string[] AllowedStatuses =
        { nameof(AiRequestStatus.Success), nameof(AiRequestStatus.Error) };

    public AiRequestsListQueryValidator()
    {
        Include(new AiOperationsMetricsQueryValidator());   // reuse the date / string-length rules
        RuleFor(x => x.Status)
            .Must(s => string.IsNullOrEmpty(s) || AllowedStatuses.Contains(s))
            .WithMessage($"Invalid status. Allowed values: {string.Join(", ", AllowedStatuses)}");
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
```

Status is validated to one of the two enum names; the `MaximumLength` caps stop a caller
from posting megabyte-long filter strings (`Provider`, `Worker`, `Model` are short by
the writer side already). `Search` is capped at 200 chars (well above any realistic
operator query). The validator is registered automatically by
`AddValidatorsFromAssemblyContaining<RegisterRequestValidator>()` in
`Api/Extensions/ApiServiceExtensions.cs:20` — no DI changes needed.

If using bound query records turns out to be friction with the `[FromQuery]` /
controller signature, the fallback is to keep individual `[FromQuery]` parameters and
do inline validation in the controller (`return BadRequest(...)` in the `from > to`
case) — same as the inline checks in `UsersController.CreateUser`. The chosen approach
is the FluentValidation route because (a) the api-conventions skill mandates it for
*"complex request-level validation"*, and (b) `from < to` is the canonical example of a
cross-field rule that does not belong inline. The `AiRequestsListQuery` record also
becomes the type the OpenAPI generator emits, which simplifies the eventual
`UI/src/api/generated/` regeneration in ADR 0020.

### D5. Authorization rationale

Same `[Authorize(Roles = nameof(UserRole.Admin))]` pattern as `UsersController` (verified
at `Api/Controllers/UsersController.cs:12`). The cost / token / error data is operational
detail editors do not need; admin is the appropriate scope. This satisfies ADR 0020 §D1
which calls for the route to be wrapped in `<AdminRoute>` on the frontend.

### D6. Unaffected files / explicit non-changes

- **No schema migration.** All filter columns are already indexed
  (`0005_add_ai_request_log.sql`). The aggregate queries scan `Timestamp` (always in the
  WHERE clause once a `from` / `to` is supplied) and group by an indexed column — well
  within Postgres's comfort zone for the dataset's expected scale.
- **No change to `AddAsync`, `AiRequestLogger`, `AiCostCalculator`, `AiCallContext`,
  any AI client, any worker, or any Options class.** This ADR is read-only.
- **No change to `IUnitOfWork`, `IDbConnectionFactory`, `BaseController`, or
  `ExceptionMiddleware`.** The new code uses the existing infrastructure exactly as it
  is.

---

## Consequences

**Positive:**
- Unblocks ADR 0020. Once this lands, `npm run generate-api` from `UI/` produces the
  three typed endpoints / DTOs ADR 0020's hooks need.
- The metrics endpoint is one network round-trip — cheaper than the equivalent
  five-statement alternative on a remote DB. Sets the precedent that
  `Dapper.QueryMultipleAsync` is acceptable for this kind of consolidated read,
  matching the data-access reviewer's existing recommendation for
  `EventRepository.GetByIdAsync`.
- Read-only change to the existing `ai_request_log` schema — zero risk to the writer
  pipeline.
- `AiRequestLogFilter` value object centralises filter knobs; future additions
  (e.g. filter by `CorrelationId`) require updating one record and one WHERE-builder.

**Negative / risks:**
- `date_trunc('day', "Timestamp")` is fine for the default 7-day window but a 90-day
  window with multiple providers can produce 180+ time-series rows. Frontend chart
  performance is acceptable at that scale (Recharts handles 1000+ points fine), but a
  90-day request returns more bytes than is strictly useful. **Future work** flagged:
  `bucketBy=day|week|month` query parameter on the metrics endpoint. Out of scope here
  per ADR 0020's "v1 defaults to 7 days" rationale.
- `ai_request_log` grows unboundedly; aggregation latency grows with data volume.
  Mitigation belongs to a retention / partitioning ADR — explicitly out of scope here
  and in ADR 0020.
- A single multi-statement SQL command bound by `string.Format` is less obvious to read
  than five separate constants. Mitigation: each fragment is a named `private const`
  inside `AiRequestLogSql.cs`, and the assembled `Metrics` SQL is composed by a small
  helper alongside them. The reader of the repository sees one well-named SQL constant;
  the reader of `AiRequestLogSql.cs` sees five small, named fragments.
- Introducing `Dapper.QueryMultipleAsync` is a one-time pattern adoption. Its surface
  area is small and the data-access reviewer already endorses it.

**Files affected:**

- **New files:**
  - `Core/DomainModels/AiRequestLogFilter.cs`
  - `Core/DomainModels/AiRequestLogMetrics.cs` (contains `AiMetricsTotals`,
    `AiMetricsTimeBucket`, `AiMetricsBreakdownRow`, `AiRequestLogMetrics` records)
  - `Api/Models/AiOperationsDtos.cs` (contains `AiOperationsMetricsDto`,
    `AiMetricsTimeBucketDto`, `AiMetricsBreakdownRowDto`, `AiRequestLogDto`,
    `AiOperationsMetricsQuery`, `AiRequestsListQuery`)
  - `Api/Mappers/AiOperationsMapper.cs`
  - `Api/Controllers/AiOperationsController.cs`
  - `Api/Validators/AiOperationsMetricsQueryValidator.cs`
  - `Api/Validators/AiRequestsListQueryValidator.cs`

- **Modified files:**
  - `Core/Interfaces/Repositories/IAiRequestLogRepository.cs` — add `GetMetricsAsync`,
    `GetPagedAsync`, `CountAsync`, `GetByIdAsync`.
  - `Infrastructure/Persistence/Repositories/AiRequestLogRepository.cs` — implement the
    four new methods; add the private `BuildWhere` helper.
  - `Infrastructure/Persistence/Repositories/Sql/AiRequestLogSql.cs` — add the metrics /
    list / count / by-id constants.

- **Untouched:**
  - `Infrastructure/Persistence/Sql/0005_add_ai_request_log.sql` — no schema change.
  - `Infrastructure/Persistence/Entity/AiRequestLogEntity.cs` — entity already has
    every column the new methods read.
  - `Infrastructure/Persistence/Mappers/AiRequestLogMapper.cs` — `ToDomain` already
    handles the row → domain conversion the list/by-id methods need.
  - `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` —
    `IAiRequestLogRepository` is already registered as `Scoped` (line 72); no change.
  - `Api/Extensions/ApiServiceExtensions.cs` — FluentValidation auto-discovers the new
    validators via `AddValidatorsFromAssemblyContaining` (line 20); no change.
  - All AI clients, all workers, all Options classes — read-only feature.

---

## Implementation Notes

### Order of work (each step leaves the build green)

1. **Core layer first.** Add `AiRequestLogFilter.cs` and `AiRequestLogMetrics.cs`
   (with the four records). Update `IAiRequestLogRepository` with the four new method
   signatures. Project still builds; the existing repository class breaks because it
   doesn't implement the new methods — implementer fills in stubs returning
   `NotImplementedException` and moves on.
2. **SQL constants.** Add the five SQL constants to `AiRequestLogSql.cs` (metrics
   fragments, list with/without search, count with/without search, by-id). No code path
   exercises them yet.
3. **Repository methods.** Implement `BuildWhere`, `GetByIdAsync`, `GetPagedAsync`,
   `CountAsync`, `GetMetricsAsync` in this order — each is independently testable.
4. **Repository tests.** Mirror existing patterns in
   `Tests/Infrastructure.Tests/Repositories/` — mock-based contract tests for the new
   interface methods, plus parameterised tests for `BuildWhere` (filter combinations →
   expected SQL fragments and parameter sets). The metrics method's row-mapping logic
   warrants an integration test against a live test DB if one exists; otherwise contract
   tests are sufficient (the writer ADR's tests took the same approach).
5. **DTOs and mapper.** Add `AiOperationsDtos.cs` and `AiOperationsMapper.cs`.
6. **Validators.** Add the two validators in `Api/Validators/`. They are picked up
   automatically.
7. **Controller.** Add `AiOperationsController.cs`. Run the API; smoke-test the three
   endpoints with an admin token (no data needed — the metrics endpoint returns zeros
   over an empty table).
8. **Hand-off to ADR 0020.** Run `npm run generate-api` from `UI/`; confirm
   `UI/src/api/generated/` now contains `AiOperationsApi` (or whatever the OpenAPI
   generator emits), with `AiOperationsMetricsDto`, `AiRequestLogDto`, and
   `PagedResult` for `AiRequestLog`.

### Skills `feature-planner` must consult

- `.claude/skills/code-conventions/SKILL.md` — layer placement (Core has the filter and
  domain result records; Infrastructure has the repo + SQL; Api has DTOs / mappers /
  controller / validators), primary-constructor DI, no `IDbConnectionFactory` in the
  controller, `BaseController` for authenticated controllers, exception-handling
  contract (rely on `ExceptionMiddleware`; controller returns `NotFound()` directly for
  the by-id endpoint, never throws).
- `.claude/skills/api-conventions/SKILL.md` — `[ApiController]` + `[Route("ai-operations")]`
  with **no** `/api/` prefix; `[Authorize(Roles = nameof(UserRole.Admin))]`;
  `CancellationToken cancellationToken = default` last; pagination guard pattern; DTOs
  are records; enums in DTOs are strings; FluentValidation for cross-field validation;
  no inline mapping in controllers.
- `.claude/skills/dapper-conventions/SKILL.md` — `internal class` repository, primary
  constructor with `IDbConnectionFactory` and `IUnitOfWork` injected (only `factory`
  used in the new methods — UoW is NOT used because these are single-statement reads),
  `await using var conn = await factory.CreateOpenAsync(ct)`, every Dapper call wrapped
  in `CommandDefinition` with `cancellationToken`, SQL constants in `AiRequestLogSql.cs`,
  PascalCase quoted column names, snake_case table name, `QueryHelpers.EscapeILikePattern`
  for the search parameter, `DateTimeOffset` round-trip is native (no special handler).
- `.claude/skills/mappers/SKILL.md` — `Api/Mappers/AiOperationsMapper.cs` is a static
  class of static extension methods, `ToDto(this Xxx domain)` shape, enums to string via
  `.ToString()`, no logging / try-catch / DI inside the mapper, sub-record mappers
  co-located in the same file as the parent aggregate mapper.
- `.claude/skills/clean-code/SKILL.md` — no magic numbers (page-size limit `100` is
  already the project-wide convention used in the api-conventions skill — fine to
  inline; column names in SQL are *strings by definition* and don't count as magic),
  guard clauses at the top of controller actions (`if (page < 1) page = 1;` first), no
  inline DTO construction in the controller (mapper handles it), short methods (each
  controller action stays under 15 lines).
- `.claude/skills/testing/SKILL.md` — AAA, NUnit + Moq + FluentAssertions; parameterised
  `[TestCase]` for the `BuildWhere` filter combinations; mock `IAiRequestLogRepository`
  in any controller-level test (use `WebApplicationFactory` per the writer ADR's
  precedent for the contract tests).

### Out of scope (do not expand)

- No write endpoints, no PATCH / DELETE / mutation on AI request logs — read-only API.
- No retention / archival policy for `ai_request_log`.
- No CSV / Excel export endpoint.
- No cross-cycle aggregation drill-down (e.g. "all requests with this `CorrelationId`")
  beyond the `search` parameter — promote to a future ADR if operators ask.
- No `bucketBy=day|week|month` parameter on `GetMetricsAsync` — flagged as future work;
  v1 ships with `date_trunc('day', ...)` server-side, sufficient for ADR 0020's default
  7-day window.
- No alerting or threshold-breach notifications.
- No frontend changes — those are ADR 0020.
