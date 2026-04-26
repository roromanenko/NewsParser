# AI Operations Read API

## Goal
Expose three admin-only read endpoints (`GET /ai-operations/metrics`,
`GET /ai-operations/requests`, `GET /ai-operations/requests/{id}`) backed by
new aggregation methods on `IAiRequestLogRepository`, unblocking ADR 0020's
AI Operations Dashboard frontend.

## Affected Layers
- Core / Infrastructure / Api

## ADR
`docs/architecture/decisions/0021-ai-operations-read-api.md`

---

## Tasks

### Phase 1 — Core: new value object and result records

- [x] **Create `Core/DomainModels/AiRequestLogFilter.cs`** — positional record with seven
      nullable fields: `DateTimeOffset? From`, `DateTimeOffset? To`, `string? Provider`,
      `string? Worker`, `string? Model`, `string? Status`, `string? Search`.
      `Status` is a plain string (not `AiRequestStatus?`) so the repository compares
      directly against the column value without parsing; the validator enforces allowed
      values before the record reaches the repository.
      _Acceptance: file compiles in the `Core` project; zero references to Infrastructure,
      Dapper, or any Api namespace; `dotnet build Core` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/DomainModels/AiRequestLogMetrics.cs`** — four co-located records:
      `AiMetricsTotals` (9 fields: `decimal TotalCostUsd`, `int TotalCalls`,
      `int SuccessCalls`, `int ErrorCalls`, `double AverageLatencyMs`,
      `int TotalInputTokens`, `int TotalOutputTokens`,
      `int TotalCacheCreationInputTokens`, `int TotalCacheReadInputTokens`);
      `AiMetricsTimeBucket` (`DateTimeOffset Bucket`, `string Provider`,
      `decimal CostUsd`, `int Calls`, `int Tokens`);
      `AiMetricsBreakdownRow` (`string Key`, `int Calls`, `decimal CostUsd`, `int Tokens`);
      `AiRequestLogMetrics` (`AiMetricsTotals Totals`,
      `List<AiMetricsTimeBucket> TimeSeries`, `List<AiMetricsBreakdownRow> ByModel`,
      `List<AiMetricsBreakdownRow> ByWorker`, `List<AiMetricsBreakdownRow> ByProvider`).
      _Acceptance: all four records compile in `Core`; no Infrastructure or Api references;
      `dotnet build Core` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/Interfaces/Repositories/IAiRequestLogRepository.cs`** — add four new
      method signatures below the existing `AddAsync`:
      ```
      Task<AiRequestLogMetrics> GetMetricsAsync(AiRequestLogFilter filter, CancellationToken cancellationToken = default);
      Task<List<AiRequestLog>> GetPagedAsync(AiRequestLogFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);
      Task<int> CountAsync(AiRequestLogFilter filter, CancellationToken cancellationToken = default);
      Task<AiRequestLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
      ```
      _Acceptance: interface compiles; `AiRequestLogRepository` in Infrastructure now fails
      to build (expected — stubs are added in the next task); `dotnet build Core` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/AiRequestLogRepository.cs`** — add
      four stub implementations of the new interface methods, each throwing
      `NotImplementedException`. Existing `AddAsync` is unchanged.
      _Acceptance: `dotnet build` (full solution) is green; no `NotImplementedException`
      is exercised by any existing code path_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

---

### Phase 2 — Infrastructure: SQL constants

- [x] **Modify `Infrastructure/Persistence/Repositories/Sql/AiRequestLogSql.cs`** — add the
      following constants (existing `Insert` constant is unchanged):

      `public const string GetById` — single-row `SELECT` of all 17 columns from
      `ai_request_log WHERE "Id" = @id`.

      `public const string GetPagedWithSearch` — paged `SELECT` of all 17 columns with
      a `WHERE {0}` placeholder (filled by the WHERE clause built in `BuildWhere`) plus an
      `ILIKE` predicate on `"Operation"`, `"Model"`, and `"ErrorMessage"` using
      `@pattern ESCAPE '\'`, `ORDER BY "Timestamp" DESC`, `LIMIT @pageSize OFFSET @offset`.
      Sort direction is `DESC` (no UI knob in v1 — no `{1}` placeholder needed).

      `public const string GetPagedWithoutSearch` — same but without the `ILIKE` predicate;
      `WHERE {0}`, `ORDER BY "Timestamp" DESC`, `LIMIT @pageSize OFFSET @offset`.

      `public const string CountWithSearch` — `SELECT COUNT(*) FROM ai_request_log WHERE {0}`
      plus the same `ILIKE` predicate on the three columns.

      `public const string CountWithoutSearch` — `SELECT COUNT(*) FROM ai_request_log WHERE {0}`.

      Five private fragment constants used to compose `Metrics` (one per SELECT statement;
      see ADR D2 §"New SQL constants"):
        - `private const string MetricsKpiFragment` — KPI totals row (one row result).
        - `private const string MetricsTimeSeriesFragment` — `date_trunc('day', "Timestamp")`
          grouped by day and provider.
        - `private const string MetricsByModelFragment` — `GROUP BY "Model"`.
        - `private const string MetricsByWorkerFragment` — `GROUP BY "Worker"`.
        - `private const string MetricsByProviderFragment` — `GROUP BY "Provider"`.

      `public const string Metrics` — concatenation of the five fragments separated by
      `Environment.NewLine`, each using `{0}` for the WHERE clause placeholder.

      _Acceptance: file compiles; all constants are `internal static` (class visibility
      unchanged); no code path exercises the new SQL yet; `dotnet build Infrastructure`
      green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

---

### Phase 3 — Infrastructure: repository method implementations

- [x] **Modify `Infrastructure/Persistence/Repositories/AiRequestLogRepository.cs`** — replace
      the `GetByIdAsync` stub with the real implementation.
      Opens `await using var conn = await factory.CreateOpenAsync(cancellationToken)`.
      Calls `conn.QuerySingleOrDefaultAsync<AiRequestLogEntity>(new CommandDefinition(
      AiRequestLogSql.GetById, new { id }, cancellationToken: cancellationToken))`.
      Returns `entity?.ToDomain()`. No `IUnitOfWork` — single-statement read.
      _Acceptance: method compiles; returns `null` for an unknown id; returns the correct
      domain object for a known id; no raw SQL in the class body; `dotnet build` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/AiRequestLogRepository.cs`** — add the
      private static `BuildWhere` helper method per ADR D2.
      Signature: `private static (string Sql, DynamicParameters Params) BuildWhere(
      AiRequestLogFilter filter)`.
      Builds the WHERE clause dynamically from `From`, `To`, `Provider`, `Worker`, `Model`
      (used by both metrics and paged queries). Returns `"1=1"` when all fields are null/empty.
      Does NOT add `Status` or `Search` clauses — those are appended separately in
      `GetPagedAsync` / `CountAsync`.
      Uses `QueryHelpers.EscapeILikePattern` for any ILIKE pattern (only `Search` uses ILIKE,
      and that is added by the callers, not this helper).
      _Acceptance: method compiles; `BuildWhere(new AiRequestLogFilter(null,null,null,null,null,null,null))`
      returns `"1=1"` with empty parameters; each non-null field appends exactly one clause and
      one parameter; `dotnet build` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/AiRequestLogRepository.cs`** — replace
      the `GetPagedAsync` stub with the real implementation per ADR D2.
      Calls `BuildWhere(filter)` to get the base WHERE clause and parameters.
      If `filter.Status` is non-empty, appends `"Status" = @status` clause and adds the
      parameter.
      If `filter.Search` is non-empty, escapes via `QueryHelpers.EscapeILikePattern`, appends
      the three-column ILIKE predicate with `ESCAPE '\'`, and adds `@pattern`.
      Selects the correct SQL constant (`GetPagedWithSearch` or `GetPagedWithoutSearch`),
      formats it with `string.Format(sql, whereClause)`.
      Adds `@pageSize` and `@offset` (= `(page - 1) * pageSize`) to the parameters.
      Maps rows through the existing `AiRequestLogMapper.ToDomain`.
      No `IUnitOfWork`.
      _Acceptance: method compiles; returns a correctly-shaped `List<AiRequestLog>`; SQL
      constant is selected based on `Search` nullability; `dotnet build` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/AiRequestLogRepository.cs`** — replace
      the `CountAsync` stub with the real implementation per ADR D2.
      Same WHERE-building logic as `GetPagedAsync` (same `Status` and `Search` branches).
      Selects `CountWithSearch` or `CountWithoutSearch`, formats with the WHERE clause.
      Returns `await conn.ExecuteScalarAsync<int>(new CommandDefinition(...))`.
      No `IUnitOfWork`.
      _Acceptance: method compiles; returns `0` for an empty table; `dotnet build` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/AiRequestLogRepository.cs`** — replace
      the `GetMetricsAsync` stub with the real implementation per ADR D2.
      Defines two private structs (or records) internal to the repository file:
      `AiMetricsTotalsRow` (Dapper-binding shape for the KPI row — all 9 KPI columns
      returned by the first SELECT in `Metrics`);
      `AiMetricsTimeBucketRow` (Dapper-binding shape for `bucket`, `Provider`, `cost_usd`,
      `calls`, `tokens`).
      `AiMetricsBreakdownRow` from Core can be used directly as the Dapper binding shape for
      the three breakdown queries (its property names match the SQL aliases).
      Calls `BuildWhere(filter)` for the shared WHERE clause.
      Formats `AiRequestLogSql.Metrics` with `string.Format(AiRequestLogSql.Metrics, where)`.
      Opens one connection; calls `conn.QueryMultipleAsync(new CommandDefinition(sql, params,
      cancellationToken: cancellationToken))`.
      Consumes results in order: `ReadSingleAsync<AiMetricsTotalsRow>`, `ReadAsync<AiMetricsTimeBucketRow>`,
      `ReadAsync<AiMetricsBreakdownRow>` (x3).
      Maps `AiMetricsTotalsRow` to `AiMetricsTotals`; `AiMetricsTimeBucketRow` to
      `AiMetricsTimeBucket`; `AiMetricsBreakdownRow` is used directly.
      Returns a fully-populated `AiRequestLogMetrics`.
      No `IUnitOfWork`.
      _Acceptance: method compiles; five result sets are consumed in the correct order;
      an empty table returns zero-valued totals and empty lists; `dotnet build` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

---

### Phase 4 — Repository tests

- [ ] **Create `Tests/Infrastructure.Tests/Repositories/AiRequestLogRepositoryReadTests.cs`** _Delegated to test-writer agent_ —
      NUnit + Moq + FluentAssertions unit tests for `BuildWhere` filter combinations and the
      four new repository methods (mocked at the `IAiRequestLogRepository` level).

      Required cases:
      1. `BuildWhere` with all-null filter → SQL fragment is `"1=1"`, parameter count is 0.
      2. `BuildWhere` with `From` only → SQL contains `"Timestamp" >= @from`, one parameter.
      3. `BuildWhere` with `From` + `To` + `Provider` → three clauses joined with ` AND `.
      4. `GetPagedAsync` with a non-empty `Search` → mock verifies `GetPagedAsync` was called
         with a filter whose `Search` property matches the input.
      5. `GetPagedAsync` with `Status = "Error"` → mock verifies `Status` is `"Error"`.
      6. `CountAsync` — mock returns `42`; caller receives `42`.
      7. `GetByIdAsync` with a known `Guid` → mock returns the expected `AiRequestLog`.
      8. `GetByIdAsync` with an unknown `Guid` → mock returns `null`; caller receives `null`.
      9. `GetMetricsAsync` → mock returns an `AiRequestLogMetrics` with known values;
         caller receives the same object.

      Use `Mock<IAiRequestLogRepository>` — no live DB, no Npgsql references.
      Follow the AAA pattern and the `MethodName_Scenario_ExpectedResult` naming convention.
      _Acceptance: all nine tests pass with `dotnet test`; no live DB or HTTP calls_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 5 — Api: DTOs and mapper

- [x] **Create `Api/Models/AiOperationsDtos.cs`** — single file containing all six records
      and two query records per ADR D3:
      - `AiOperationsMetricsDto` — 13-field positional record (9 KPI scalars +
        `List<AiMetricsTimeBucketDto> TimeSeries`, `List<AiMetricsBreakdownRowDto> ByModel`,
        `List<AiMetricsBreakdownRowDto> ByWorker`, `List<AiMetricsBreakdownRowDto> ByProvider`).
      - `AiMetricsTimeBucketDto` — 5 fields (`DateTimeOffset Bucket`, `string Provider`,
        `decimal CostUsd`, `int Calls`, `int Tokens`).
      - `AiMetricsBreakdownRowDto` — 4 fields (`string Key`, `int Calls`, `decimal CostUsd`,
        `int Tokens`).
      - `AiRequestLogDto` — 17 fields mirroring `AiRequestLog`; `Status` is `string`
        (per api-conventions: enums in DTOs are always strings); `ErrorMessage` is `string?`.
      - `AiOperationsMetricsQuery` — 5 nullable fields (`DateTimeOffset? From`, `DateTimeOffset? To`,
        `string? Provider`, `string? Worker`, `string? Model`) — bound via `[FromQuery]` in the
        metrics endpoint; FluentValidation attaches to this type.
      - `AiRequestsListQuery` — 9 fields: the same 5 as above plus `string? Status`,
        `string? Search`, `int Page = 1`, `int PageSize = 20` — bound via `[FromQuery]` in the
        list endpoint.
      _Acceptance: file compiles in the `Api` project; no Infrastructure references;
      `AiRequestStatus` is not referenced (DTOs use `string`); `dotnet build Api` green_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Create `Api/Mappers/AiOperationsMapper.cs`** — static class with four static extension
      methods per ADR D3:
      - `ToDto(this AiRequestLog log) → AiRequestLogDto` — maps all 17 fields;
        `log.Status.ToString()` for the enum-to-string conversion.
      - `ToDto(this AiMetricsTimeBucket b) → AiMetricsTimeBucketDto`.
      - `ToDto(this AiMetricsBreakdownRow r) → AiMetricsBreakdownRowDto`.
      - `ToDto(this AiRequestLogMetrics m) → AiOperationsMetricsDto` — maps `m.Totals`
        fields inline; calls `.Select(t => t.ToDto()).ToList()` for each list.
      No DI, no logging, no try/catch inside the mapper — pure static extension methods.
      _Acceptance: file compiles; no inline DTO construction remains in the controller;
      `dotnet build Api` green_
      _Skill: .claude/skills/mappers/SKILL.md_

---

### Phase 6 — Api: validators

- [x] **Create `Api/Validators/AiOperationsMetricsQueryValidator.cs`** — `AbstractValidator<AiOperationsMetricsQuery>`
      per ADR D4.
      Rules:
      - `From` < `To` (cross-field rule, only when both are non-null).
      - `To` > `From` (symmetric cross-field rule).
      - `Provider`.`MaximumLength(50)`.
      - `Worker`.`MaximumLength(100)`.
      - `Model`.`MaximumLength(100)`.
      No manual DI registration needed — picked up automatically by
      `AddValidatorsFromAssemblyContaining<RegisterRequestValidator>()`.
      _Acceptance: file compiles; validator is discovered at startup (confirmed by Swagger
      returning 400 on `from > to` input); `dotnet build Api` green_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Create `Api/Validators/AiRequestsListQueryValidator.cs`** — `AbstractValidator<AiRequestsListQuery>`
      per ADR D4.
      Rules:
      - `Include(new AiOperationsMetricsQueryValidator())` to reuse date/string-length rules.
      - `Status` must be null/empty or one of `nameof(AiRequestStatus.Success)` /
        `nameof(AiRequestStatus.Error)`; use a static `string[]` for the allowed values.
      - `Search`.`MaximumLength(200)`.
      - `Page`.`GreaterThanOrEqualTo(1)`.
      - `PageSize`.`InclusiveBetween(1, 100)`.
      _Acceptance: file compiles; a `Status = "Invalid"` query returns 400 before reaching
      the controller; `dotnet build Api` green_
      _Skill: .claude/skills/api-conventions/SKILL.md_

---

### Phase 7 — Api: controller

- [x] **Create `Api/Controllers/AiOperationsController.cs`** — per ADR D3 and D5.
      Class declaration: `[ApiController]`, `[Route("ai-operations")]`,
      `[Authorize(Roles = nameof(UserRole.Admin))]`, extends `BaseController`.
      Primary constructor: `IAiRequestLogRepository repository`.

      **`GET ai-operations/metrics`** — accepts `[FromQuery] AiOperationsMetricsQuery query`.
      Builds `new AiRequestLogFilter(query.From, query.To, query.Provider, query.Worker,
      query.Model, Status: null, Search: null)`.
      Calls `repository.GetMetricsAsync` and returns `Ok(metrics.ToDto())`.

      **`GET ai-operations/requests`** — accepts `[FromQuery] AiRequestsListQuery query`.
      Pagination guard: `if (query.Page < 1) page = 1; if (query.PageSize is < 1 or > 100)
      pageSize = 20;` (use local variables `page` and `pageSize` initialised from `query`).
      Builds `new AiRequestLogFilter(...)` with all seven fields from the query.
      Calls `GetPagedAsync` and `CountAsync` in sequence on the repository.
      Returns `Ok(new PagedResult<AiRequestLogDto>(items.Select(l => l.ToDto()).ToList(),
      page, pageSize, total))`.

      **`GET ai-operations/requests/{id:guid}`** — `Guid id` path param.
      Calls `repository.GetByIdAsync(id, cancellationToken)`.
      Returns `NotFound()` when `null`; `Ok(log.ToDto())` otherwise.

      All three actions take `CancellationToken cancellationToken = default` as their last
      parameter. No inline DTO construction (mapper handles it). No service layer.
      _Acceptance: `dotnet build Api` green; Swagger UI shows all three routes under
      `ai-operations` with the Admin lock icon; a request without a JWT returns 401;
      a request with an Editor JWT returns 403_
      _Skill: .claude/skills/api-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

---

### Phase 8 — Controller tests

- [ ] **Create `Tests/Api.Tests/Controllers/AiOperationsControllerTests.cs`** _Delegated to test-writer agent_ — NUnit +
      `WebApplicationFactory` (or `Mock<IAiRequestLogRepository>`) controller-level tests
      per the testing skill conventions.

      Required cases:
      1. `GET /ai-operations/metrics` with an admin JWT and no query params → 200 with an
         `AiOperationsMetricsDto` body.
      2. `GET /ai-operations/metrics` with `from > to` → 400 (FluentValidation kicks in).
      3. `GET /ai-operations/requests` with default params → 200 with `PagedResult<AiRequestLogDto>`.
      4. `GET /ai-operations/requests` with `pageSize = 200` → guard clamps to 20, 200 OK.
      5. `GET /ai-operations/requests` with `status = "Invalid"` → 400.
      6. `GET /ai-operations/requests/{id}` with a known guid → 200 with `AiRequestLogDto`.
      7. `GET /ai-operations/requests/{id}` with an unknown guid → 404.
      8. All three endpoints with no token → 401.
      9. All three endpoints with an Editor-role JWT → 403.

      Mock `IAiRequestLogRepository` in DI.
      _Acceptance: all nine cases pass with `dotnet test`; no live DB or HTTP calls_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 9 — Smoke test and API client regeneration

- [ ] **Smoke test** — _Cannot run in this environment: requires a live PostgreSQL connection to start the API._ With the Api running in Development (`dotnet run --project Api`),
      obtain an admin JWT from `POST /auth/login`, then call:
      - `GET /ai-operations/metrics` → 200, body contains `totalCalls`, `timeSeries`, etc.
      - `GET /ai-operations/requests?page=1&pageSize=5` → 200, body contains `items`,
        `totalCount`, `totalPages`.
      - `GET /ai-operations/requests/{any-uuid}` → 404 (empty table).
      Confirm `dotnet build` is green before and after.
      _Acceptance: all three endpoints return the documented HTTP status codes; no 500 errors;
      build green_

- [ ] **Regenerate API client** — _Cannot run in this environment: `npm run generate-api` fetches from `http://localhost:5172/swagger/v1/swagger.json`, which requires the API to be running with a live DB._ From the `UI/` directory, run `npm run generate-api`.
      Confirm that `UI/src/api/generated/` contains a service or namespace referencing
      `AiOperationsMetricsDto`, `AiRequestLogDto`, and the paged list endpoint.
      _Acceptance: `npm run generate-api` exits 0; TypeScript files in `UI/src/api/generated/`
      include references to `AiOperationsMetricsDto` and `AiRequestLogDto`; `npm run build`
      (or `tsc --noEmit`) in `UI/` exits 0_

---

## Open Questions

_None. All design decisions are resolved in ADR `docs/architecture/decisions/0021-ai-operations-read-api.md`._
