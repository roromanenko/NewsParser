# Migrate from Entity Framework Core to Dapper

## Status
Proposed

## Context

The NewsParser solution currently uses Entity Framework Core 10.0.5 with `Npgsql.EntityFrameworkCore.PostgreSQL` and `Pgvector.EntityFrameworkCore` throughout `Infrastructure/Persistence/`. The stack is deeply entangled with EF Core idioms:

**Current EF surface area**
- `Infrastructure/Persistence/DataBase/NewsParserDbContext.cs` — one `DbContext` exposing 11 `DbSet<T>` (Articles, Publications, Users, Sources, PublishLogs, PublishTargets, Events, EventUpdates, Contradictions, ContradictionArticles, MediaFiles).
- `Infrastructure/Persistence/Configurations/` — 11 `IEntityTypeConfiguration<T>` classes defining FKs, indexes (incl. HNSW on `articles.Embedding`), string conversions for enums, `jsonb` for `KeyFacts` and `SelectedMediaFileIds`, `vector(768)` for `Articles.Embedding` and `Events.Embedding`.
- `Infrastructure/Persistence/Migrations/` — 20+ EF migrations (`Initial` … `AddPublicationPipelineRedesign`). Migrations are the source of truth for schema, including `CREATE EXTENSION vector`, HNSW index, enum conversions, and data-rewrite SQL (`UPDATE … SET "Status"`).
- `Infrastructure/Persistence/Repositories/` — 7 repositories, all constructor-injected with `NewsParserDbContext`. Patterns include:
  - `Include`/`ThenInclude` graphs (`EventRepository.GetDetailAsync` loads Articles → MediaFiles, EventUpdates, Contradictions → ContradictionArticles).
  - `ExecuteUpdateAsync` with `SetProperty` for every partial update (see `ef-core-conventions` skill §2).
  - `ExecuteDeleteAsync` for deletes.
  - `FromSql` interpolated raw SQL with `FOR UPDATE SKIP LOCKED` (`ArticleRepository.GetPendingAsync`, `GetPendingForClassificationAsync`, `PublicationRepository.GetPendingForGenerationAsync`, `GetPendingForPublishAsync`).
  - pgvector queries with `Pgvector.EntityFrameworkCore.CosineDistance` (`EventRepository.FindSimilarEventsAsync`).
  - `EF.Functions.ILike` for case-insensitive search.
- `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` — `AddDbContext<NewsParserDbContext>` with `UseNpgsql(...).UseVector()`; repositories registered as `Scoped` (DbContext lifetime).
- DI: no explicit transactions anywhere in the codebase (`BeginTransaction`, `UseTransaction`, `TransactionScope` — zero hits). Implicit transactional consistency comes from a single `SaveChangesAsync` call per method. `EventRepository.MergeAsync`, `AddContradictionAsync`, and the "lock IDs then re-query with Includes" pattern in `PublicationRepository` are multi-step operations that today are **not** wrapped in an explicit transaction — a correctness hole we must preserve or fix during migration.
- Tests: `Tests/Infrastructure.Tests/Repositories/*` rely on `Microsoft.EntityFrameworkCore.InMemory` and `Database.EnsureCreated()`. The `testing` skill documents this explicitly.

**Schema conventions to preserve**
- Table names are **snake_case** (`articles`, `events`, `media_files`, `publish_targets`, `event_updates`, `contradictions`, `contradiction_articles`, `publish_logs`, `publications`, `sources`, `users`) — set via `builder.ToTable("…")`.
- Column names are **PascalCase** and quoted in raw SQL (e.g., `"Status"`, `"ProcessedAt"`, `"EventId"`) — confirmed by the existing `FromSql` calls.
- Enums stored as **strings**, not ints (`ArticleConfiguration.HasConversion<string>()`, plus `ef-core-conventions` §5).
- `KeyFacts` (`List<string>`) and `SelectedMediaFileIds` (`List<Guid>`) stored as **jsonb**.
- `Tags` (`List<string>`) stored as PostgreSQL **text[]** (Npgsql default for `List<string>` without explicit configuration).
- `Embedding` stored as pgvector `vector(768)`; HNSW index on `articles.Embedding` with `vector_cosine_ops`.

**Why the change is being considered**
Dapper offers a smaller runtime surface, explicit SQL (a plus for a team that already writes raw SQL for `SKIP LOCKED`), zero change-tracker overhead, and removes three NuGet packages (`EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Pgvector.EntityFrameworkCore`) plus the migration toolchain. It also removes EF's LINQ-to-SQL translation quirks (which already forced us into `FromSql` for `SKIP LOCKED`). The cost is: writing and owning all SQL, re-implementing schema management, re-implementing graph loading ("Includes"), and re-implementing the change-tracking equivalent for multi-row operations.

---

## Options

The ADR presents two orthogonal axes of choice: (a) **ORM strategy**, and (b) **schema-management strategy**. Sub-options are grouped accordingly.

### Axis A — Data access strategy

#### Option A1 — Pure Dapper + `IDbConnectionFactory`
Replace `NewsParserDbContext` with an `IDbConnectionFactory` that returns `NpgsqlConnection` instances. Each repository injects the factory and opens a connection per method call. Use Dapper's `QueryAsync`, `ExecuteAsync`, `QuerySingleOrDefaultAsync` with hand-written parameterized SQL.

**Pros:**
- Smallest dependency footprint.
- Matches the project's existing taste for raw SQL (already used in 4 `FromSql` call sites).
- Zero LINQ-translation surprises; SQL is what runs.
- Easier to reason about `FOR UPDATE SKIP LOCKED`, CTEs, and pgvector operators.

**Cons:**
- Must re-implement all `Include`/`ThenInclude` graph loading by hand (split queries + in-memory stitching, or JSON aggregation via `jsonb_agg`). Non-trivial for `EventRepository.GetDetailAsync` and `PublicationRepository.GetDetailAsync`.
- No compile-time schema safety; typos in column names surface at runtime.
- Must hand-write a `TypeHandler` for `Pgvector.Vector` ↔ half-float/single-float arrays, plus one for `List<string>`/`List<Guid>` ↔ `jsonb`, plus one for `List<string>` ↔ `text[]`.
- Loses free atomicity from `SaveChangesAsync`; multi-statement operations (`MergeAsync`, `AddContradictionAsync`) now require explicit transaction plumbing.

#### Option A2 — Dapper + Dapper.Contrib (or Dapper.FastCrud / RepoDb)
Same as A1, but layer a micro-helper on top for simple `Get`/`Insert`/`Update`/`Delete` by primary key to reduce boilerplate for the trivial CRUD repos (`SourceRepository`, `UserRepository`, `PublishTargetRepository`, `MediaFileRepository`).

**Pros:**
- Less boilerplate for simple CRUD.
- Conventions (`[Table]`, `[Key]`) mirror EF's attribute approach.

**Cons:**
- Adds an unmaintained/semi-maintained dependency (Dapper.Contrib is deprecated; Dapper.FastCrud and RepoDb are niche).
- Inconsistent style: simple repos use helpers, complex repos use raw Dapper. New third "way" to learn on top of raw Dapper.
- All the hard cases (graph loads, pgvector, `SKIP LOCKED`, partial updates) still require raw SQL — the helper buys little.
- Custom types (`Vector`, `jsonb` lists, `text[]`) need type handlers either way.

#### Option A3 — Hybrid: keep EF for CRUD + Dapper for hot/complex paths
Reject the user's explicit requirement ("excluding EF entirely"). Listed only for completeness.

**Pros:** smallest diff, retains migrations.
**Cons:** violates the stated task.

---

### Axis B — Schema management

#### Option B1 — DbUp (idempotent SQL scripts, forward-only)
Embed `.sql` scripts under `Infrastructure/Persistence/Migrations/Sql/` and run them via DbUp on startup. DbUp tracks applied scripts in a `SchemaVersions` table. Forward-only; no down-migrations.

**Pros:**
- Mature, simple, battle-tested.
- Full PostgreSQL syntax available — pgvector `CREATE EXTENSION`, `CREATE INDEX USING hnsw (…) WITH (m = 16, ef_construction = 64)`, partial unique indexes, raw data-rewrite SQL. All of this works identically to what today's EF migrations already emit.
- Scripts are reviewable SQL, embeddable as resources. No magic.
- Works well with Docker and CI/CD (runs once on startup or via a separate CLI).

**Cons:**
- Forward-only (no automatic rollback). In practice EF `Down()` methods are also rarely used correctly, so this is a small loss.
- Must manually write a "baseline" script that represents the current schema (snapshot of the existing database), then start versioning from there.

#### Option B2 — FluentMigrator (C# DSL, two-way migrations)
Use FluentMigrator's C# fluent DSL (`Create.Table("articles").WithColumn(…)`) as a typed replacement for EF migrations.

**Pros:**
- Typed, familiar to EF-migration users.
- Supports Up/Down.
- Good PostgreSQL provider.

**Cons:**
- Requires a custom syntax/extension to express `vector(768)`, HNSW indexes, `jsonb` columns, `text[]` columns, and partial indexes. Most of these need raw-SQL escape hatches anyway, so the "typed" benefit erodes on exactly the columns we care about.
- Adds a DSL on top of SQL — two languages to understand instead of one.
- We lose the direct-SQL reviewability that we already have on advanced pgvector/jsonb features.

#### Option B3 — Raw SQL scripts, manually applied
Ship versioned `.sql` files; apply manually or via `psql` in deployment scripts. No runtime migration runner.

**Pros:** zero dependencies.
**Cons:** no tracking of what's been applied, no idempotency guard, error-prone, does not fit a multi-project solution (Api and Worker both start independently).

---

## Decision

**Choose Option A1 (pure Dapper + `IDbConnectionFactory`) and Option B1 (DbUp).**

### Reasoning

1. **A1 over A2:** The project already favors explicit SQL (four `FromSql` call sites, `ExecuteUpdateAsync` over load-then-mutate). Adding a second helper library (A2) creates inconsistency and does not help the hard cases. A1 gives one consistent style.
2. **A1 over A3:** The user explicitly excluded EF.
3. **B1 over B2:** Every "interesting" column in the schema (`vector(768)`, HNSW index, `jsonb`, `text[]`, partial unique index on `(SourceId, ExternalId)`) requires raw-SQL escape hatches in FluentMigrator anyway. DbUp's plain-`.sql`-files approach is simpler and matches the reality of the schema better.
4. **B1 over B3:** B3 has no tracking table and cannot be safely run on Api + Worker startup.

### Concrete shape of the solution

**New `Core.DataAccess` concept (or put it in Infrastructure root)** — stay inside `Infrastructure/` since `Core/` forbids infrastructure dependencies per `code-conventions` skill:

```
Infrastructure/Persistence/
├── Connection/
│   ├── IDbConnectionFactory.cs        (internal interface in Infrastructure)
│   └── NpgsqlConnectionFactory.cs     (reads connection string from IConfiguration)
├── Dapper/
│   ├── DapperTypeHandlers.cs          (registers all handlers once on startup)
│   ├── VectorTypeHandler.cs           (Pgvector.Vector ↔ float[] via pgvector-dotnet raw driver)
│   ├── JsonbTypeHandler.cs            (System.Text.Json for List<string>, List<Guid>)
│   └── StringListTypeHandler.cs       (text[] ↔ List<string>)
├── Sql/                               (embedded resource .sql files for DbUp)
│   ├── 0001_baseline.sql              (snapshot of current schema: all CREATE TABLE/INDEX/EXTENSION)
│   ├── 0002_<next-change>.sql         (future deltas)
│   └── ...
├── Migrator/
│   └── DbUpMigrator.cs                (runs DbUp on startup)
├── Entity/                            (KEEP — pure POCOs, no EF attributes needed)
├── Mappers/                           (KEEP — ToDomain/ToEntity; remove any Pgvector.Vector references from ToEntity and map straight from float[])
├── Repositories/                      (REWRITE each class to use IDbConnectionFactory + Dapper)
└── UnitOfWork/
    ├── IUnitOfWork.cs
    └── DapperUnitOfWork.cs            (scoped-lifetime connection + transaction scope)
```

### 1. Replacing `DbContext` with `IDbConnectionFactory`

Define one interface in `Infrastructure/Persistence/Connection/`:

```csharp
internal interface IDbConnectionFactory
{
    NpgsqlConnection Create();                              // unopened, caller disposes
    Task<NpgsqlConnection> CreateOpenAsync(CancellationToken ct);
}
```

Implementation reads `ConnectionStrings:NewsParserDbContext` from `IConfiguration` (keep the existing key so `appsettings.Development.json` is untouched). Registered as **singleton** (factories are stateless).

Repositories are still `Scoped` but now depend on `IDbConnectionFactory`, not on a shared `DbContext`. Each repository method opens and disposes its own connection:

```csharp
public class SourceRepository(IDbConnectionFactory factory, IUnitOfWork uow) : ISourceRepository
{
    public async Task<Source?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = uow.Current ?? await factory.CreateOpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<SourceEntity>(
            new CommandDefinition(Sql.Source.GetById, new { id }, cancellationToken: ct));
        return row?.ToDomain();
    }
}
```

The `uow.Current` pattern lets a service wrap several repository calls in one transaction (see §4) without changing repository signatures.

**SQL-string storage:** one static class per aggregate under `Infrastructure/Persistence/Repositories/Sql/` (e.g. `SourceSql`, `ArticleSql`, `EventSql`) holding all SQL constants. This keeps SQL co-located with the repo that uses it and makes it greppable.

### 2. Schema management: DbUp

Package: `dbup-postgresql`.

**Baseline strategy:**
- Generate the current schema with `pg_dump --schema-only --no-owner --no-privileges` against a database migrated by the current EF migrations, clean it up by hand (remove EF's `__EFMigrationsHistory` references; fold in the `CREATE EXTENSION vector`), and commit as `0001_baseline.sql`.
- Drop `Infrastructure/Persistence/Migrations/` entirely after verification.
- Any future schema change = add a new file `NNNN_descriptive-name.sql`.

**Embedding and runner:**
```xml
<ItemGroup>
  <EmbeddedResource Include="Persistence\Sql\*.sql" />
</ItemGroup>
```

```csharp
public static class DbUpMigrator
{
    public static void Migrate(string connectionString)
    {
        var result = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrator).Assembly)
            .LogToConsole()
            .Build()
            .PerformUpgrade();
        if (!result.Successful) throw new InvalidOperationException("DB migration failed", result.Error);
    }
}
```

Call `DbUpMigrator.Migrate(...)` **once at startup** in both `Api/Program.cs` and `Worker/Program.cs` before `builder.Build()`. Concurrent starts are safe — DbUp acquires an advisory lock in the `SchemaVersions` table.

### 3. pgvector in Dapper

The `Pgvector` NuGet package (version **0.3.2**, already in `Infrastructure.csproj`) ships ADO.NET support for Npgsql independent of EF — we **keep this package and drop `Pgvector.EntityFrameworkCore`**.

Setup sequence required by the raw driver:
1. Enable the `vector` plugin on the `NpgsqlDataSource`:
   ```csharp
   var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
   dataSourceBuilder.UseVector();            // from Pgvector package (namespace: Pgvector.Npgsql)
   var dataSource = dataSourceBuilder.Build();
   ```
2. The factory returns connections from `dataSource.OpenConnectionAsync(...)`.
3. Register a Dapper `SqlMapper.TypeHandler<Vector>` so `Vector` can be bound as a parameter and read from `DataRow`. The type-handler wraps `new Vector(float[])` on read and `parameter.Value = vectorInstance` on write — the Npgsql plugin does the wire-format translation.

**No "half-float" vector handling is needed.** The schema uses `vector(768)` (single-precision 4-byte floats), not `halfvec`. The user's mention of "half-float vectors" is either a misunderstanding of the pgvector column type in use, or a planned schema change. **This must be clarified (see Open Questions)**; for now the ADR assumes `vector(768)` stays.

Required packages (final state):
- **Add:** `Dapper` (latest), `dbup-postgresql`, `Npgsql` (if not already transitive).
- **Keep:** `Pgvector`, `Npgsql` (direct).
- **Remove:** `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.EntityFrameworkCore.Tools`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Npgsql.EntityFrameworkCore.PostgreSQL.Design`, `Pgvector.EntityFrameworkCore`.
- **Test project:** remove `Microsoft.EntityFrameworkCore.InMemory`; tests become integration tests against a real Postgres via Testcontainers (see §6).

### 4. Transactions across multiple repositories

Today the codebase has **no explicit transactions** (grep for `BeginTransaction`, `UseTransaction`, `TransactionScope` returns zero hits). In EF, every repository call was self-contained: one `SaveChangesAsync` per public method, with no caller composing multiple calls atomically. The multi-step operations that exist:
- `EventRepository.MergeAsync` — five `ExecuteUpdateAsync` calls, **already non-atomic today**. Migration should fix this.
- `EventRepository.AddContradictionAsync` — an insert then an `AddRangeAsync`, also non-atomic today.
- `PublicationRepository.GetPendingForGenerationAsync` / `GetPendingForPublishAsync` — lock IDs with `FOR UPDATE SKIP LOCKED` in one query, then re-query with `Include`. In EF this runs outside a transaction so the lock is **released immediately** when the first query completes — the "SKIP LOCKED" behavior is effectively ornamental today. Migration should fix this too.

**Chosen approach: scoped `IUnitOfWork` with explicit `BeginAsync` in services.**

```csharp
public interface IUnitOfWork
{
    IDbConnection? Current { get; }              // null when no transaction active
    IDbTransaction? Transaction { get; }
    Task<IAsyncDisposable> BeginAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
}
```

- `DapperUnitOfWork` is registered **scoped**. It lazily opens a connection and `BeginTransactionAsync` on the first call to `BeginAsync()`. The returned `IAsyncDisposable` rolls back if disposed before `CommitAsync` is called.
- Repositories pass `uow.Transaction` into `CommandDefinition(..., transaction: uow.Transaction)` whenever it is non-null. When `uow.Current` is null, the repository opens its own short-lived connection (the common case — single-statement methods).
- Services that need atomic multi-repo work opt in:
  ```csharp
  public async Task MergeAsync(Guid sourceId, Guid targetId, CT ct = default)
  {
      await using var _ = await _uow.BeginAsync(ct);
      await _eventRepo.ReassignArticlesAsync(sourceId, targetId, ct);
      await _eventRepo.ReassignEventUpdatesAsync(sourceId, targetId, ct);
      await _eventRepo.ReassignContradictionsAsync(sourceId, targetId, ct);
      await _eventRepo.ArchiveAsync(sourceId, ct);
      await _eventRepo.TouchLastUpdatedAsync(targetId, ct);
      await _uow.CommitAsync(ct);
  }
  ```
- Controllers and workers that do a single repo call do **not** need `IUnitOfWork` — they simply let the repository open-and-dispose.

This preserves the existing service-composes-repositories architecture, adds atomicity exactly where it is needed (Merge, AddContradiction, lock-then-query-with-graph), and does not change any controller or worker outside those call sites.

### 5. Migration strategy: staged big-bang within one PR series (NOT parallel run)

**Recommendation: big-bang per-repository, behind a feature branch, merged as one cohesive PR series.**

Rationale against parallel run:
- Dual-writing or dual-reading from EF and Dapper against the **same schema** offers no real safety (same DB, same data) and massively increases complexity (DI must resolve two implementations of every repo interface; tests double).
- There is no behavioral change in the application — same SQL, same results. The risk is translation bugs (typos in SQL, wrong type mapping), which are caught by tests, not by production traffic comparison.
- Running both stacks in parallel in production would require behind-the-scenes feature flags per repo method, which is disproportionate for a refactor that must end with EF removed anyway.

**Ordered implementation plan (each step independently verifiable):**

1. **Prep** — add `Dapper`, `dbup-postgresql`; keep EF installed temporarily. Add `Infrastructure/Persistence/Connection/` + `Infrastructure/Persistence/Dapper/` with type handlers, registered in `AddInfrastructure`.
2. **Baseline script** — generate `0001_baseline.sql` by dumping the current dev schema; verify DbUp runs cleanly on a fresh database and produces an identical schema to the EF-migrated one (compare via `pg_dump` diff).
3. **Rewrite simple repos first** (smallest blast radius): `UserRepository` → `SourceRepository` → `PublishTargetRepository` → `MediaFileRepository`. Keep the same interface. Rewrite the matching tests to use Testcontainers (real Postgres with pgvector) per the `testing` skill.
4. **Rewrite medium repos**: `PublishTargetRepository` done above, then `ArticleRepository` (graph-light: only `MediaFiles` include on `GetByIdAsync`).
5. **Rewrite the two heavy repos**: `EventRepository` (deep `Include` graphs, pgvector similarity) and `PublicationRepository` (deep `Include` graphs + `SKIP LOCKED`).
6. **Introduce `IUnitOfWork`** and wrap `EventService.MergeAsync`, `EventRepository.AddContradictionAsync`, and the two `PublicationRepository.GetPendingFor…` methods so that the `SKIP LOCKED` lock is held across the detail re-query.
7. **Cut over** — remove `NewsParserDbContext`, `Infrastructure/Persistence/Configurations/`, `Infrastructure/Persistence/Migrations/`, and all EF NuGet packages in one commit. Update DI: replace `AddDbContext` with `IDbConnectionFactory` + `AddInfrastructureDapper`.
8. **Update tests** — all repository tests now use a Postgres Testcontainer; delete `Microsoft.EntityFrameworkCore.InMemory` references. The three tests that currently use `Mock<IRepository>` (because InMemory couldn't simulate `SKIP LOCKED`) can now exercise the real behavior.
9. **Run all integration tests + manual smoke of the full pipeline** (RSS fetch → analyze → classify → approve → publish).

### 6. Test impact

- `Microsoft.EntityFrameworkCore.InMemory` is removed. It could never model pgvector, `jsonb`, `SKIP LOCKED`, or `ILike` anyway; most tests of complex behaviors were mock-based.
- Adopt **Testcontainers for PostgreSQL + pgvector**: `Testcontainers.PostgreSql` NuGet package, `pgvector/pgvector:pg16` image. A shared `PostgresTestFixture` runs DbUp against a fresh container per test class (or reuses one with `TRUNCATE` between tests).
- Worker/API tests that only depend on the repository *interface* (mocked) are unaffected.
- The `testing` skill must be updated to note: "Repository tests run against a real Postgres via Testcontainers; EF InMemory is no longer used."

---

## Consequences

### Positive

- **Smaller dependency surface:** six EF-related NuGets removed from `Infrastructure/Infrastructure.csproj`.
- **Explicit SQL:** no more LINQ-translation surprises; the SQL in the repo is literally the SQL that runs. Easier to EXPLAIN ANALYZE, easier to review.
- **Correctness improvements:** `MergeAsync`, `AddContradictionAsync`, and the two `SKIP LOCKED` lock-then-query paths finally become atomic (they are not today).
- **Simpler test story for DB-heavy code:** one provider (real Postgres via Testcontainers) instead of two (InMemory for simple, manual for hard).
- **Faster startup:** no model building; DbUp only runs pending scripts.

### Negative / risks

- **Loss of compile-time schema safety.** A typo in a column name (e.g., `"ProccessedAt"`) compiles and fails at runtime. Mitigate with integration tests that touch every column at least once (all existing repository tests already do so).
- **Hand-written graph loading is verbose.** `EventRepository.GetDetailAsync` becomes a set of 3–4 queries + in-memory stitching (or one query with `jsonb_agg`). Acceptable but more code.
- **Migrations toolchain is new.** The team loses `dotnet ef migrations add` muscle memory. Replaced by writing plain `.sql` files, which most developers already know.
- **Testcontainers requires Docker on dev machines and CI agents.** Slightly higher friction than EF InMemory for local test runs (multi-second container startup).
- **The `ef-core-conventions` skill becomes obsolete and must be rewritten as `dapper-conventions`.**
- **One-shot rewrite risk:** every repository changes in the same PR series. Partial rollout is not practical. Mitigated by step 2 (baseline script verified byte-equivalent to EF output) and by keeping interfaces unchanged so controllers/workers/services need zero edits.

### Files affected

**Removed:**
- `Infrastructure/Persistence/DataBase/NewsParserDbContext.cs`
- `Infrastructure/Persistence/Configurations/*` (11 files)
- `Infrastructure/Persistence/Migrations/*` (all migration and snapshot files)

**Rewritten (interfaces unchanged):**
- `Infrastructure/Persistence/Repositories/ArticleRepository.cs`
- `Infrastructure/Persistence/Repositories/EventRepository.cs`
- `Infrastructure/Persistence/Repositories/PublicationRepository.cs`
- `Infrastructure/Persistence/Repositories/MediaFileRepository.cs`
- `Infrastructure/Persistence/Repositories/SourceRepository.cs`
- `Infrastructure/Persistence/Repositories/UserRepository.cs`
- `Infrastructure/Persistence/Repositories/PublishTargetRepository.cs`

**New:**
- `Infrastructure/Persistence/Connection/IDbConnectionFactory.cs`
- `Infrastructure/Persistence/Connection/NpgsqlConnectionFactory.cs`
- `Infrastructure/Persistence/Dapper/DapperTypeHandlers.cs` (+ `VectorTypeHandler`, `JsonbTypeHandler`, `StringListTypeHandler`)
- `Infrastructure/Persistence/Migrator/DbUpMigrator.cs`
- `Infrastructure/Persistence/Sql/0001_baseline.sql` (plus future delta files)
- `Infrastructure/Persistence/Repositories/Sql/*Sql.cs` (static SQL constants, one per aggregate)
- `Infrastructure/Persistence/UnitOfWork/IUnitOfWork.cs`, `DapperUnitOfWork.cs`

**Edited:**
- `Infrastructure/Infrastructure.csproj` — add Dapper + dbup-postgresql; remove 6 EF packages and `Pgvector.EntityFrameworkCore`.
- `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` — replace `AddDbContext` with `IDbConnectionFactory` + Dapper setup + UnitOfWork registration.
- `Api/Program.cs` and `Worker/Program.cs` — call `DbUpMigrator.Migrate(...)` at startup.
- `Infrastructure/Persistence/Mappers/*.cs` — remove `Pgvector.Vector` references from `ToEntity`; entities now expose `float[]? Embedding` directly (the `Vector` boxing happens only at the parameter layer).
- `Infrastructure/Persistence/Entity/*.cs` — change `Vector? Embedding` to `float[]? Embedding`; collection navigation properties stay (populated by repository stitching, not by EF).
- `Infrastructure/Services/EventService.cs` — inject `IUnitOfWork`, wrap `MergeAsync` body.
- `Tests/Infrastructure.Tests/**` — replace EF InMemory setup with a `PostgresTestFixture` using Testcontainers.
- `.claude/skills/ef-core-conventions/SKILL.md` — rename/rewrite to `dapper-conventions` and update `code-conventions` §"Update Pattern" to reference Dapper.

---

## Open Questions (answer before implementation)

1. **`vector(768)` vs `halfvec(768)`** — the task brief mentions "half-float vectors". The current schema uses single-precision `vector(768)` (see `EventConfiguration.cs` line 33 and `ArticleConfiguration.cs` line 59). Is the migration also intended to switch to `halfvec`, or is `vector(768)` preserved? If switching is intended, that is a separate ADR (storage, recall-vs-size trade-off, index rebuild).
2. **Are we willing to require Docker for local test runs?** Testcontainers is the standard way to test pgvector-touching code; EF InMemory cannot model it. If Docker-free local tests are a hard constraint, the fallback is Sqlite InMemory with an `IArticleRepository` fake that skips vector search — but coverage drops.
3. **Do we want per-request or per-operation connections?** The ADR assumes per-operation (open on each repo call). An alternative is per-scope (one connection held for the lifetime of the HTTP request / worker iteration). Per-operation is simpler and matches the repository-as-self-contained-unit style of the codebase; per-scope is faster under load but requires careful connection-lifetime management.
4. **Down-migrations: do we care?** DbUp is forward-only. EF migrations technically support `Down()` but the project never uses them. Confirm we can drop them permanently.
5. **Is Aiven hosted Postgres running pgvector?** The connection string in `appsettings.Development.json` points to Aiven Cloud. Confirm the `vector` extension is available there (today's EF migration `CREATE EXTENSION vector` succeeds, so presumably yes) — but a new baseline script will try to create it again on first deploy if the current DB already has it. The baseline script must use `CREATE EXTENSION IF NOT EXISTS vector`.

---

## Implementation Notes

**For `feature-planner`:**
- Break the work into nine atomic steps mirroring §5 of the Decision. Each step must leave the solution compilable and all tests green.
- The baseline script (step 2) is the highest-risk step: schedule a dedicated verification task that diffs `pg_dump` output before and after.
- Each rewritten repository keeps its existing `Core/Interfaces/Repositories/I*Repository.cs` signature unchanged — controllers, services, workers, and their tests do not change during repo rewrites.
- `IUnitOfWork` is introduced in step 6, **after** all repos are converted, so that repos can be written once with the optional-transaction pattern rather than being refactored twice.
- Tests must move to Testcontainers in lockstep with each repo rewrite; do not accumulate a backlog.

**Skills feature-planner and implementer should follow:**
- `.claude/skills/code-conventions/SKILL.md` — layering rules (no DB in `Core/`), repository naming, enum-as-string storage, Options pattern, primary-constructor preference.
- `.claude/skills/ef-core-conventions/SKILL.md` — still authoritative for the method-shape catalogue (`GetByIdAsync`, `GetPendingForXxxAsync`, `UpdateXxxAsync`, `CountXxxAsync`, `FindSimilarXxxAsync`, `ExistsByXxxAsync`, etc.) and for the behaviors each method must replicate. The SQL changes; the method contract does not. After the migration, this skill must be replaced by a `dapper-conventions` skill.
- `.claude/skills/mappers/SKILL.md` — `ToDomain`/`ToEntity` stay; remove `new Vector(...)` wrapping from mappers since entities now hold `float[]`.
- `.claude/skills/testing/SKILL.md` — update the "EF Core InMemory" section to "Testcontainers PostgreSQL with pgvector".
- `.claude/skills/api-conventions/SKILL.md` — no changes; API surface is unaffected.

**Order of changes (summary):**
1. Add Dapper + DbUp + connection factory + type handlers (side-by-side with EF).
2. Author baseline SQL; verify byte-for-byte schema equivalence.
3. Rewrite repos one at a time (simple → complex); rewrite the matching tests to Testcontainers in the same PR.
4. Introduce `IUnitOfWork`; wrap the three multi-step operations.
5. Delete `NewsParserDbContext`, configurations, and EF migrations in one cutover commit; remove EF NuGets.
6. Rewrite `ef-core-conventions` skill as `dapper-conventions`.
