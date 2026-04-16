# EF Core to Dapper Migration

## Goal

Replace Entity Framework Core with pure Dapper + `IDbConnectionFactory` for all data
access in `Infrastructure/`, and replace EF migrations with DbUp forward-only SQL
scripts, while leaving every `Core/Interfaces/Repositories/I*Repository.cs` signature
unchanged so that no controller, service, or worker requires edits during the rewrite.

## Affected Layers

- Infrastructure
- Api
- Worker
- Tests (Infrastructure.Tests)

---

## Invariants (hold after every task)

- `dotnet build` is green.
- All non-repository tests remain passing.
- `Core/Interfaces/` files are never modified.

---

## Tasks

### Phase 1 — Scaffolding (Dapper + DbUp side-by-side with EF)

- [ ] **Modify `Infrastructure/Infrastructure.csproj`** — add `Dapper` (latest) and
      `dbup-postgresql` (latest) package references. Do NOT remove any EF packages yet.
      _Acceptance: `dotnet build` is green; both new packages appear in the restored
      dependency graph._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Inspect `Infrastructure/Infrastructure.csproj` for a direct `Npgsql` package
      reference** — open the file and check whether `Npgsql` appears as a direct
      `<PackageReference>`. If it is present only as a transitive dependency (pulled in
      by `Npgsql.EntityFrameworkCore.PostgreSQL`, which will be removed in Phase 8),
      add an explicit `<PackageReference Include="Npgsql" Version="..." />` now, using
      the same version already resolved transitively. If a direct reference already
      exists, this task is a no-op verification.
      _Acceptance: `Infrastructure.csproj` contains a direct `<PackageReference>` for
      `Npgsql`; `dotnet build` is green. If it was already direct, mark done with a
      note confirming no change was needed._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Create `Infrastructure/Persistence/Connection/IDbConnectionFactory.cs`** —
      internal interface with two members:
      `NpgsqlConnection Create()` (unopened, caller disposes) and
      `Task<NpgsqlConnection> CreateOpenAsync(CancellationToken ct)`.
      Namespace: `Infrastructure.Persistence.Connection`.
      _Acceptance: file compiles; interface has exactly the two members above; no
      public visibility; no infrastructure references leak into `Core/`._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Create `Infrastructure/Persistence/Connection/NpgsqlConnectionFactory.cs`** —
      implements `IDbConnectionFactory`. Constructor takes `IConfiguration`. Reads
      `ConnectionStrings:NewsParserDbContext` (the existing key — no `appsettings`
      change required). Builds a single `NpgsqlDataSource` via
      `NpgsqlDataSourceBuilder` with `.UseVector()` called (from `Pgvector.Npgsql`)
      so that the `vector` type is registered on every connection from this factory.
      Stores the data source as a field. `Create()` returns `dataSource.CreateConnection()`.
      `CreateOpenAsync` returns `await dataSource.OpenConnectionAsync(ct)`.
      Registered as **singleton** in DI.
      _Acceptance: file compiles; `UseVector()` is called exactly once on the builder;
      no `NewsParserDbContext` reference in this file._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Create `Infrastructure/Persistence/Dapper/VectorTypeHandler.cs`** —
      `SqlMapper.TypeHandler<Vector>` (Pgvector namespace). `Parse` returns
      `new Vector((float[])value)`. `SetValue` sets `parameter.Value = value` and
      `parameter.NpgsqlDbType = NpgsqlDbType.Unknown` (let Npgsql plugin handle
      wire format). Class is `internal`.
      _Acceptance: file compiles; class is sealed and internal._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Create `Infrastructure/Persistence/Dapper/JsonbTypeHandler.cs`** —
      generic `SqlMapper.TypeHandler<T>` implementation used for `List<string>` (KeyFacts)
      and `List<Guid>` (SelectedMediaFileIds), using `System.Text.Json.JsonSerializer`.
      `SetValue` serializes to JSON string and sets `NpgsqlDbType.Jsonb`. `Parse`
      deserializes from the object returned by Npgsql (cast to `string`). Class is
      `internal sealed`.
      _Acceptance: file compiles; handler round-trips an empty list and a non-empty list
      without throwing._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Create `Infrastructure/Persistence/Dapper/DapperTypeHandlers.cs`** —
      `internal static class` with a single `public static void Register()` method.
      The method calls:
      - `SqlMapper.AddTypeHandler(new VectorTypeHandler())` for `Vector`.
      - `SqlMapper.AddTypeHandler(new JsonbTypeHandler<List<string>>())` for `KeyFacts`
        (JSONB columns only).
      - `SqlMapper.AddTypeHandler(new JsonbTypeHandler<List<Guid>>())` for
        `SelectedMediaFileIds`.
      Do NOT register a global handler for `List<string>` as `text[]`. Dapper's
      `SqlMapper.AddTypeHandler<T>` registers by CLR type: both `KeyFacts` and `Tags`
      are `List<string>`, so a second global handler would overwrite the first. Instead,
      `Tags` (stored as `text[]`) must be bound explicitly in `ArticleRepository` via
      `DynamicParameters` with `NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text`
      and a `string[]` value (see Phase 4). Each handler registered once; the method
      is idempotent.
      _Acceptance: file compiles; `Register()` can be called from DI setup without
      throwing; no `StringListTypeHandler` is registered globally._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Create `Infrastructure/Persistence/UnitOfWork/IUnitOfWork.cs`** —
      `internal interface IUnitOfWork` with members:
      `NpgsqlConnection? CurrentConnection { get; }`
      `NpgsqlTransaction? CurrentTransaction { get; }`
      `Task BeginAsync(CancellationToken ct = default)`
      `Task CommitAsync(CancellationToken ct = default)`
      `Task RollbackAsync(CancellationToken ct = default)`
      The interface is `internal` (only `Infrastructure` uses it).
      _Acceptance: file compiles; no public visibility; interface exposes exactly the
      five members above — two typed properties (`NpgsqlConnection?`,
      `NpgsqlTransaction?`) and three task-returning methods._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Create `Infrastructure/Persistence/Migrator/DbUpMigrator.cs`** —
      `internal static class` with `public static void Migrate(string connectionString)`.
      Uses DbUp `DeployChanges.To.PostgresqlDatabase(connectionString)
      .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrator).Assembly)
      .LogToConsole().Build().PerformUpgrade()`. Throws
      `InvalidOperationException("DB migration failed", result.Error)` if
      `!result.Successful`. No other logic.
      _Acceptance: file compiles; method signature matches exactly._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Infrastructure.csproj`** — add an `<ItemGroup>` with
      `<EmbeddedResource Include="Persistence\Sql\*.sql" />` so all `.sql` files
      under `Infrastructure/Persistence/Sql/` are embedded into the assembly.
      _Acceptance: `dotnet build` is green; the `<EmbeddedResource>` node is present
      in the project file._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** —
      add a private `AddDapper` extension method that: (1) calls
      `DapperTypeHandlers.Register()`, (2) registers `NpgsqlConnectionFactory` as
      singleton for `IDbConnectionFactory`. Chain the call from `AddInfrastructure`
      **before** `AddDatabase`. Do NOT touch the existing `AddDatabase` (EF) yet.
      _Acceptance: `dotnet build` is green; `IDbConnectionFactory` is resolvable from
      DI when the application starts._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 2 — Baseline SQL Script

- [ ] **Create `Infrastructure/Persistence/Sql/0001_baseline.sql`** — a hand-authored
      SQL file that represents the complete current schema. Must include:
      `CREATE EXTENSION IF NOT EXISTS vector;`
      `CREATE TABLE articles (...)` with all columns matching the EF configuration
      (snake_case table name, PascalCase quoted column names, enums as `TEXT`,
      `"KeyFacts" JSONB`, `"Tags" TEXT[]`, `"Embedding" vector(768)`).
      `CREATE TABLE events (...)` with `"Embedding" vector(768)`.
      All remaining tables: `sources`, `users`, `publish_targets`, `publications`,
      `publish_logs`, `event_updates`, `contradictions`, `contradiction_articles`,
      `media_files`.
      All foreign keys, unique indexes (including the partial unique index on
      `(SourceId, ExternalId)` filtered `WHERE source_id IS NOT NULL AND external_id
      IS NOT NULL`), and the HNSW index on `articles."Embedding"` with
      `vector_cosine_ops`.
      A DbUp `SchemaVersions` table is created automatically by DbUp — do not include
      it in this script.
      _Acceptance: running `DbUpMigrator.Migrate(connectionString)` against an empty
      PostgreSQL database (with pgvector installed) produces a schema that passes a
      `pg_dump --schema-only` diff against a schema produced by EF migrations. All
      tables, columns, types, and indexes are present._
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Verify schema equivalence between EF-migrated DB and DbUp-migrated DB** —
      this is the highest-risk step in the entire migration and requires explicit
      sign-off before proceeding. Steps: (1) run `DbUpMigrator.Migrate(connectionString)`
      against a fresh local PostgreSQL database that has the `vector` extension
      available; (2) run `pg_dump --schema-only --no-owner --no-privileges` against
      both the existing EF-migrated development database and the freshly DbUp-migrated
      database; (3) diff the two dump outputs and resolve every discrepancy in
      `0001_baseline.sql` until the diff is empty (ignoring `__EFMigrationsHistory`
      and `SchemaVersions` table definitions, which are migration-tooling artifacts
      and not part of the application schema).
      _Acceptance: `diff <(pg_dump EF-db --schema-only ...) <(pg_dump DbUp-db --schema-only ...)`
      produces zero application-schema differences; all tables, columns, types, constraints,
      and indexes match exactly._
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Audit `Api/Program.cs` and `Worker/Program.cs` for EF migration startup calls**
      — grep both files for `Database.Migrate()`, `Database.EnsureCreated()`,
      `context.Database`, or any other EF startup migration invocation. If found,
      remove or comment out the call (DbUp now owns schema management). If none are
      found, mark the task done as a no-op verification.
      _Acceptance: neither `Api/Program.cs` nor `Worker/Program.cs` contains a call
      to `Database.Migrate()`, `Database.EnsureCreated()`, or any similar EF
      startup migration method; `dotnet build` is green._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Api/Program.cs`** — call `DbUpMigrator.Migrate(connectionString)`
      at startup, before `builder.Build()`, where `connectionString` is obtained
      via `builder.Configuration.GetConnectionString("NewsParserDbContext")`.
      Keep the existing EF migration call (if any) for now — DbUp is additive.
      _Acceptance: `Api` starts cleanly in Development; DbUp logs "No new scripts
      need to be executed" on the second run (idempotent)._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Worker/Program.cs`** — same DbUpMigrator call as `Api/Program.cs`.
      _Acceptance: `Worker` starts cleanly in Development; DbUp is idempotent._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 3 — Rewrite Simple Repositories (no graph loads)

#### SQL constant classes

- [ ] **Create `Infrastructure/Persistence/Repositories/Sql/UserSql.cs`** — `internal
      static class UserSql` holding string constants for all SQL used by
      `UserRepository`: `GetByEmail`, `GetById`, `GetAll`, `ExistsByEmail`, `Insert`,
      `Update`, `Delete`.
      _Acceptance: file compiles; no string literals appear inside `UserRepository`
      itself after the rewrite._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Create `Infrastructure/Persistence/Repositories/Sql/SourceSql.cs`** — same
      pattern for `SourceRepository`: `GetActive`, `GetAll`, `GetById`,
      `ExistsByUrl`, `Insert`, `UpdateLastFetchedAt`, `UpdateFields`, `Delete`.
      _Acceptance: file compiles; no string literals in `SourceRepository` after
      rewrite._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Create `Infrastructure/Persistence/Repositories/Sql/PublishTargetSql.cs`** —
      SQL constants for `PublishTargetRepository`: `GetAll`, `GetActive`, `GetById`,
      `Insert`, `Update`, `Delete`.
      _Acceptance: file compiles._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Create `Infrastructure/Persistence/Repositories/Sql/MediaFileSql.cs`** —
      SQL constants for `MediaFileRepository`: `Insert`, `GetByArticleId`,
      `ExistsByArticleAndUrl`, `GetByIds`.
      _Acceptance: file compiles._
      _Skill: .claude/skills/code-conventions/SKILL.md_

#### Rewrite repositories

- [ ] **Rewrite `Infrastructure/Persistence/Repositories/UserRepository.cs`** —
      replace `NewsParserDbContext` injection with `IDbConnectionFactory` and
      `IUnitOfWork` (second parameter, for consistency with the pattern established
      in Phase 1). Each method opens its own connection via
      `await factory.CreateOpenAsync(ct)` when `uow.CurrentConnection` is null,
      executes the corresponding Dapper call (`QuerySingleOrDefaultAsync`,
      `QueryAsync`, `ExecuteAsync`, `QueryFirstOrDefaultAsync`), and disposes the
      connection. Use `CommandDefinition` with `cancellationToken`. Enum columns
      stored as strings (insert/update pass `.ToString()`; `ToDomain` parses via
      `Enum.Parse`). No EF or `DbContext` references remain.
      _Acceptance: file compiles; no `using Microsoft.EntityFrameworkCore` or
      `NewsParserDbContext` references; `dotnet build` is green._
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Verify: no EF-InMemory UserRepository test files exist** — grep
      `Tests/Infrastructure.Tests/Repositories/` for any file that instantiates
      `UserRepository` with a `DbContext` or references `EnsureCreated`. If none
      exist, mark done. Do not delete any file.
      _Acceptance: grep returns zero hits for `new UserRepository(` paired with
      a `DbContext` argument in the test directory; no file is deleted._
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Rewrite `Infrastructure/Persistence/Repositories/SourceRepository.cs`** —
      same pattern as `UserRepository`. Replace EF with Dapper + `IDbConnectionFactory`
      and `IUnitOfWork`.
      `GetActiveAsync` uses `WHERE "IsActive" = true AND "Type" = @type`.
      `DeleteAsync` uses `DELETE FROM sources WHERE "Id" = @id`.
      No EF references remain.
      _Acceptance: file compiles; `dotnet build` is green._
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Delete `Tests/Infrastructure.Tests/Repositories/` test files that instantiate
      `SourceRepository` or `ArticleRepository` via EF InMemory `DbContext`** —
      specifically: `ArticleRepositorySearchSortTests.cs`,
      `ArticleRepositoryGetByIdWithMediaTests.cs`.
      Retain all mock-based test files (they do not need changes).
      _Acceptance: deleted files no longer exist; `dotnet test` is green on the
      remaining test files._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Rewrite `Infrastructure/Persistence/Repositories/PublishTargetRepository.cs`** —
      replace EF with Dapper + `IDbConnectionFactory` and `IUnitOfWork`. `CreateAsync`
      inserts and returns the entity mapped back to domain. `UpdateAsync` issues a
      parameterized `UPDATE`. `DeleteAsync` issues `DELETE`. No EF references remain.
      _Acceptance: file compiles; `dotnet build` is green._
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Rewrite `Infrastructure/Persistence/Repositories/MediaFileRepository.cs`** —
      replace EF with Dapper + `IDbConnectionFactory` and `IUnitOfWork`. `GetByIdsAsync`
      uses `WHERE "Id" = ANY(@ids)` with `ids.ToArray()`. `ExistsByArticleAndUrlAsync`
      uses `SELECT EXISTS(...)`. No EF references remain.
      _Acceptance: file compiles; `dotnet build` is green._
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Delete `Tests/Infrastructure.Tests/Repositories/MediaFileRepositoryTests.cs`**
      and `Tests/Infrastructure.Tests/Repositories/MediaFileRepositoryGetByIdsTests.cs`
      — both use EF InMemory. Cannot be fully mocked (they test the concrete class).
      Delete entirely per user decision.
      _Acceptance: files no longer exist; `dotnet test` is green._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 4 — Rewrite ArticleRepository (medium complexity)

- [ ] **Modify `Infrastructure/Persistence/Entity/ArticleEntity.cs`** — change
      `Vector? Embedding` to `float[]? Embedding`. Remove `using Pgvector;`.
      _Acceptance: file compiles; no `Pgvector` reference._
      _Skill: .claude/skills/mappers/SKILL.md_

- [ ] **Modify `Infrastructure/Persistence/Mappers/ArticleMapper.cs`** — `ToEntity`
      sets `Embedding = domain.Embedding` directly (now `float[]?`). `ToDomain` sets
      `Embedding = entity.Embedding` directly. Remove `new Vector(...)` and
      `entity.Embedding?.ToArray()`. Remove `using Pgvector;`.
      _Acceptance: file compiles; no `Pgvector` reference._
      _Skill: .claude/skills/mappers/SKILL.md_

- [ ] **Create `Infrastructure/Persistence/Repositories/Sql/ArticleSql.cs`** —
      SQL constants for all `ArticleRepository` methods: `Insert`, `GetById`
      (with `LEFT JOIN media_files`), `GetAnalysisDone` (parameterized sort/search
      as two constants each: with-search and without-search), `CountAnalysisDone`,
      `UpdateStatus`, `Reject`, `IncrementRetry`, `GetPending` (SKIP LOCKED),
      `GetPendingForClassification` (SKIP LOCKED), `UpdateKeyFacts`,
      `UpdateAnalysisResult`, `UpdateEmbedding`, `ExistsBySourceAndExternal`,
      `ExistsByUrl`, `GetRecentTitlesForDeduplication`.
      _Acceptance: file compiles; every constant is referenced by `ArticleRepository`
      after rewrite._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Rewrite `Infrastructure/Persistence/Repositories/ArticleRepository.cs`** —
      replace EF with Dapper + `IDbConnectionFactory` and `IUnitOfWork`. Key
      translation points:
      - `GetByIdAsync`: single query with `LEFT JOIN media_files m ON m."ArticleId" =
        a."Id"`, use Dapper multi-map (`splitOn: "Id"`) to stitch `MediaFileEntity`
        rows onto `ArticleEntity`.
      - `GetAnalysisDoneAsync`: parameterized SQL with `ILIKE` (case-insensitive
        PostgreSQL operator) for search; keep the call to
        `QueryHelpers.EscapeILikePattern` on the search term before binding it as a
        parameter — user-entered `%` and `_` are wildcard characters in `ILIKE` and
        are NOT escaped by parameterization alone; removing the escape call would be
        a behavioral regression. `ORDER BY "ProcessedAt" DESC/ASC` for sort;
        `LIMIT @pageSize OFFSET @offset`.
      - `GetPendingAsync` and `GetPendingForClassificationAsync`: keep the existing
        `FOR UPDATE SKIP LOCKED` SQL verbatim; use `Dapper.QueryAsync<ArticleEntity>`.
      - `UpdateEmbeddingAsync`: pass `new Vector(embedding)` as parameter; Dapper
        `VectorTypeHandler` handles the wire format.
      - `UpdateKeyFactsAsync`: pass `keyFacts` as parameter; `JsonbTypeHandler<List<string>>`
        handles JSONB serialization.
      - `UpdateAnalysisResultAsync` and any method writing the `Tags` column: DO NOT
        rely on a global `List<string>` type handler for `text[]` (none is registered
        — see Phase 1 `DapperTypeHandlers`). Instead, use `DynamicParameters` and
        add the Tags value as `tags.ToArray()` (a `string[]`) with
        `NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text` set explicitly. This
        avoids the CLR-type collision between the `JsonbTypeHandler<List<string>>`
        (KeyFacts) and the `text[]` column (Tags).
      - `ExistsAsync` / `ExistsByUrlAsync`: use `SELECT EXISTS(SELECT 1 FROM ...)`.
      No EF references remain in this file.
      _Acceptance: file compiles; `dotnet build` is green; no
      `using Microsoft.EntityFrameworkCore` or `Pgvector.EntityFrameworkCore`;
      `QueryHelpers.EscapeILikePattern` is still called on the search term in
      `GetAnalysisDoneAsync`; Tags are bound via `DynamicParameters` with
      `string[]` and explicit `NpgsqlDbType`._
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Verify / potentially delete
      `Tests/Infrastructure.Tests/Repositories/ArticleRepositoryRejectAndQueryTests.cs`**
      — inspect the file: if it uses `Mock<IArticleRepository>` throughout (no
      concrete `ArticleRepository` instantiation), **retain** it unchanged. If it
      instantiates the concrete class against a `DbContext`, delete it.
      _Acceptance: file is retained if mock-only; deleted if EF-dependent; `dotnet
      test` is green._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Verify (retain) `Tests/Infrastructure.Tests/Repositories/ArticleRepositoryUpdateKeyFactsTests.cs`**
      — this file already uses `Mock<IArticleRepository>` only and will continue to
      compile after the rewrite. **Retain** it unchanged. This task is a verification
      step: confirm the file has no concrete `ArticleRepository` instantiation, then
      mark done without modification.
      _Acceptance: file exists, compiles, and all three tests pass._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 5 — Rewrite EventRepository (heavy: graph loads + pgvector)

- [ ] **Modify `Infrastructure/Persistence/Entity/EventEntity.cs`** — change
      `Vector? Embedding` to `float[]? Embedding`. Remove `using Pgvector;`.
      Navigation properties (`Articles`, `EventUpdates`, `Contradictions`) are kept
      as plain `List<T>` fields — they are populated by repository stitching, not EF.
      _Acceptance: file compiles; no `Pgvector` reference; `EventMapper.ToEntity`
      and `ToDomain` still compile._
      _Skill: .claude/skills/mappers/SKILL.md_

- [ ] **Modify `Infrastructure/Persistence/Mappers/EventMapper.cs`** — remove
      `new Vector(domain.Embedding)` from `ToEntity`; set `Embedding = domain.Embedding`
      directly (now `float[]?`). Remove `entity.Embedding?.ToArray()` from `ToDomain`;
      set `Embedding = entity.Embedding` directly. Remove `using Pgvector;`.
      _Acceptance: file compiles; no `Pgvector` reference._
      _Skill: .claude/skills/mappers/SKILL.md_

- [ ] **Create `Infrastructure/Persistence/Repositories/Sql/EventSql.cs`** —
      SQL constants for all `EventRepository` methods:
      `GetById` (event + articles + event_updates + contradictions + contradiction_articles),
      `GetActive` (event + articles), `FindSimilarEvents` (cosine similarity via
      `1 - ("Embedding" <=> @vector::vector)` operator), `Insert`,
      `UpdateSummaryTitleAndEmbedding`, `UpdateLastUpdatedAt`, `AssignArticleToEvent`,
      `InsertEventUpdate`, `InsertContradiction`, `InsertContradictionArticles`,
      `GetUnpublishedUpdates` (event_updates joined to events and articles),
      `MarkUpdatePublished`, `CountUpdatesFrom`, `GetLastUpdateTime`,
      `GetPaged` (events + articles + contradictions, paginated, two variants:
      with and without search), `Count` (two variants: with and without search),
      `GetDetail` (event + articles + media_files + event_updates + contradictions +
      contradiction_articles), `GetWithContext` (event + articles + event_updates),
      `ResolveContradiction`, `MergeArticles`, `MergeEventUpdates`,
      `MergeContradictions`, `ArchiveEvent`, `TouchLastUpdatedAt`,
      `UpdateArticleRole`, `UpdateEventStatus`, `MarkArticleReclassified`.
      _Acceptance: file compiles; every constant is used by `EventRepository` after
      rewrite._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Rewrite `Infrastructure/Persistence/Repositories/EventRepository.cs`** —
      replace EF with Dapper + `IDbConnectionFactory` and `IUnitOfWork` (constructor
      parameter from the start — UoW is needed for `MergeAsync` and
      `AddContradictionAsync`). Key translation points:
      - `GetByIdAsync`: three separate queries — (1) fetch event row, (2) fetch
        articles where `"EventId" = @id`, (3) fetch event_updates, contradictions,
        contradiction_articles for that event — stitch in memory via LINQ grouping
        before calling `ToDomain()`.
      - `GetDetailAsync`: same as `GetByIdAsync` but also fetch `media_files` joined
        to articles via a fourth query.
      - `GetWithContextAsync`: fetch event + articles + event_updates (no
        contradictions, no media).
      - `GetActiveEventsAsync`: fetch active events + their articles.
      - `FindSimilarEventsAsync`: single query using `1 - ("Embedding" <=> @vector::vector)`
        with `WHERE "Status" = 'Active' AND "LastUpdatedAt" >= @windowStart AND
        "Embedding" IS NOT NULL AND (1 - ("Embedding" <=> @vector::vector)) >= @threshold
        ORDER BY 1 - ("Embedding" <=> @vector::vector) DESC LIMIT @maxTake`.
        Pass the embedding as `new Vector(embedding)` — handled by `VectorTypeHandler`.
      - `GetPagedAsync`: events + articles + contradictions (no media); use
        `ILIKE` for search; pagination via `LIMIT/OFFSET`.
      - `AddContradictionAsync`: insert contradiction row, then insert
        contradiction_article rows; use `uow.CurrentConnection` and
        `uow.CurrentTransaction` when available (non-null), so that callers wrapping
        in a UoW transaction get atomicity.
      - `MergeAsync`: five `UPDATE` statements; use `uow.CurrentConnection` and
        `uow.CurrentTransaction` when available, so the caller (`EventService`) can
        wrap all five in one transaction.
      - `GetUnpublishedUpdatesAsync`: join event_updates to events and articles.
      Replace `EF.Functions.ILike` with PostgreSQL `ILIKE` operator in raw SQL.
      No EF or `Pgvector.EntityFrameworkCore` references remain.
      _Acceptance: file compiles; `dotnet build` is green; `IUnitOfWork` is a
      constructor parameter; `MergeAsync` and `AddContradictionAsync` use
      `uow.CurrentConnection`/`uow.CurrentTransaction` when non-null._
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Delete `Tests/Infrastructure.Tests/Repositories/EventRepositoryGetWithContextTests.cs`**
      — uses EF InMemory via `TestNewsParserDbContext`; cannot be fully mocked as it
      tests the concrete class. Delete entirely.
      _Acceptance: file no longer exists; `dotnet test` is green._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Delete `Tests/Infrastructure.Tests/Repositories/EventRepositoryGetDetailWithMediaTests.cs`**
      — uses EF InMemory via `TestNewsParserDbContext`. Delete entirely.
      _Acceptance: file no longer exists; `dotnet test` is green._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Delete `Tests/Infrastructure.Tests/Repositories/EventRepositorySearchSortTests.cs`**
      — uses EF InMemory. Delete entirely.
      _Acceptance: file no longer exists; `dotnet test` is green._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 6 — Rewrite PublicationRepository (heavy: graph loads + SKIP LOCKED)

- [ ] **Create `Infrastructure/Persistence/Repositories/Sql/PublicationSql.cs`** —
      SQL constants for all `PublicationRepository` methods:
      `GetPendingForGenerationLockIds` (SKIP LOCKED, returns only `"Id"` column),
      `GetByIdsWithArticleAndTargetAndEvent` (for generation batch with event articles),
      `GetPendingForPublishLockIds` (SKIP LOCKED),
      `GetByIdsWithTargetAndArticle` (for publish batch),
      `Insert`, `GetById` (with publish_target), `GetDetail` (with publish_target,
      publish_logs, event + articles + media_files), `GetByEventId` (with
      publish_target, ordered by `"CreatedAt"`), `GetAll` (with publish_target +
      event, paginated), `CountAll`, `UpdateStatus`, `UpdateGeneratedContent`,
      `UpdatePublishedAt`, `UpdateContentAndMedia`, `UpdateApproval`,
      `UpdateRejection`, `InsertPublishLog`, `GetExternalMessageId`,
      `GetOriginalEventPublication`.
      _Acceptance: file compiles._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Rewrite `Infrastructure/Persistence/Repositories/PublicationRepository.cs`** —
      replace EF with Dapper + `IDbConnectionFactory` and `IUnitOfWork` (constructor
      parameter from the start). Key translation points:
      - `GetPendingForGenerationAsync`: use `uow.CurrentConnection` and
        `uow.CurrentTransaction` when available for both queries (lock IDs with
        SKIP LOCKED, then fetch full rows). When UoW is not active (null), open one
        owned connection for both queries — the SKIP LOCKED lock is held for the
        connection lifetime. Second query fetches full rows including `Article`,
        `PublishTarget`, `Event` + `Event.Articles` by joining.
      - `GetPendingForPublishAsync`: same pattern.
      - `GetDetailAsync`: four queries — publication + publish_target, publish_logs,
        event + articles, media_files for those articles — stitched in memory.
      - `GetByEventIdAsync`: publications + publish_target joined.
      - `GetAllAsync`: publications + publish_target + event joined, paginated.
      - `UpdateContentAndMedia`: pass `selectedMediaFileIds.ToArray()` for
        `List<Guid>` column — handled by `JsonbTypeHandler<List<Guid>>`.
      - `AddPublishLogAsync`: simple insert.
      - `GetExternalMessageIdAsync`: scalar query.
      No EF references remain.
      _Acceptance: file compiles; `dotnet build` is green; `IUnitOfWork` is a
      constructor parameter; SKIP LOCKED and detail query use the same
      `NpgsqlConnection`/`NpgsqlTransaction` when UoW is active._
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Delete `Tests/Infrastructure.Tests/Repositories/PublicationRepositoryTests.cs`**
      — the `PublicationRepositoryEfTests` fixture uses EF InMemory
      (`TestPublicationDbContext`); the `PublicationRepositoryInterfaceContractTests`
      fixture uses `Mock<IPublicationRepository>` only. Split the action:
      delete only the `PublicationRepositoryEfTests` fixture class AND the
      `TestPublicationDbContext` helper class from the file. Retain the
      `PublicationRepositoryInterfaceContractTests` class (it is mock-only and needs
      no changes). If both classes are in the same file, extract the mock-only class
      into a new file `PublicationRepositoryContractTests.cs` first, then delete the
      original file.
      _Acceptance: `PublicationRepositoryInterfaceContractTests` tests still exist
      and pass; no file references `TestPublicationDbContext` or EF InMemory; `dotnet
      test` is green._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 7 — Introduce DapperUnitOfWork and wrap multi-step operations in EventService

- [ ] **Create `Infrastructure/Persistence/UnitOfWork/DapperUnitOfWork.cs`** —
      `internal sealed class DapperUnitOfWork : IUnitOfWork, IAsyncDisposable`.
      Constructor takes `IDbConnectionFactory`. `BeginAsync` opens a connection via
      `factory.CreateOpenAsync(ct)` and calls `BeginTransactionAsync`, storing both
      as `NpgsqlConnection? CurrentConnection` and `NpgsqlTransaction? CurrentTransaction`.
      `CommitAsync` calls `CommitAsync()` on the `NpgsqlTransaction` then disposes it.
      `RollbackAsync` calls `RollbackAsync()` on the `NpgsqlTransaction` then disposes it.
      `DisposeAsync` rolls back if `CurrentTransaction` is still non-null, then
      disposes `CurrentConnection`. Registered as **scoped** in DI.
      _Acceptance: file compiles; `IAsyncDisposable` is implemented; no open
      connection leak if `CommitAsync` is not called; `CurrentConnection` and
      `CurrentTransaction` are typed as `NpgsqlConnection?` and `NpgsqlTransaction?`
      respectively._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Services/EventService.cs`** — inject `IUnitOfWork`.
      Wrap the body of `MergeAsync` in:
      `await uow.BeginAsync(ct)` → call `eventRepository.MergeAsync(...)` →
      `await uow.CommitAsync(ct)`. Surround with try/catch that calls
      `await uow.RollbackAsync()` on failure. The post-merge AI enrichment
      (`UpdateSummaryTitleAndEmbeddingAsync`) runs AFTER the transaction commits
      (outside the try block), so a failed AI call does not roll back the merge.
      _Acceptance: file compiles; `MergeAsync` is atomic; AI enrichment failure
      does not roll back the structural merge._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** —
      register `DapperUnitOfWork` as scoped for `IUnitOfWork` inside `AddDapper`.
      _Acceptance: `IUnitOfWork` is resolvable from DI; `dotnet build` is green._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 8 — EF Cutover (remove DbContext, Configurations, Migrations, EF packages)

- [ ] **Delete `Infrastructure/Persistence/DataBase/NewsParserDbContext.cs`** — the
      `DbContext` class. The directory `DataBase/` becomes empty and can be removed.
      _Acceptance: file no longer exists._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Delete all 10 files in `Infrastructure/Persistence/Configurations/`** —
      `ArticleConfiguration.cs`, `EventConfiguration.cs`, `PublicationConfiguration.cs`,
      `MediaFileConfiguration.cs`, `SourceConfiguration.cs`, `UserConfiguration.cs`,
      `PublishTargetConfiguration.cs`, `ContradictionConfiguration.cs`,
      `EventUpdateConfiguration.cs`, `PublishLogConfiguration.cs`
      (confirm exact filenames via glob before deleting).
      _Acceptance: `Infrastructure/Persistence/Configurations/` directory is empty or
      removed._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Delete `Infrastructure/Persistence/Migrations/` directory** — all `.cs`
      migration files and `NewsParserDbContextModelSnapshot.cs`. Do not add a down
      migration; the baseline SQL script is the new source of truth.
      _Acceptance: directory no longer contains any `.cs` file._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Infrastructure.csproj`** — remove the following
      `<PackageReference>` entries:
      `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design`,
      `Microsoft.EntityFrameworkCore.Tools`, `Npgsql.EntityFrameworkCore.PostgreSQL`,
      `Npgsql.EntityFrameworkCore.PostgreSQL.Design`, `Pgvector.EntityFrameworkCore`.
      Keep `Pgvector` (raw ADO.NET driver still needed for `VectorTypeHandler`).
      Keep `Npgsql` (now a direct reference as established in Phase 1).
      _Acceptance: `dotnet build` is green; `dotnet list package` shows none of the
      six removed packages._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** —
      remove the `AddDatabase` private method (which called `AddDbContext`) and its
      call from `AddInfrastructure`. Remove all `using` directives for EF namespaces
      (`Microsoft.EntityFrameworkCore`, `Infrastructure.Persistence.DataBase`).
      _Acceptance: file compiles; no EF references remain; `dotnet build` is green._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Tests/Infrastructure.Tests/Infrastructure.Tests.csproj`** — remove
      `Microsoft.EntityFrameworkCore.InMemory` and `Microsoft.EntityFrameworkCore.Sqlite`
      package references.
      _Acceptance: `dotnet build` on the test project is green._
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Delete `Tests/Infrastructure.Tests/Repositories/` any remaining files that
      reference `NewsParserDbContext`, `TestNewsParserDbContext`,
      `TestPublicationDbContext`, or `Microsoft.EntityFrameworkCore`** — grep the
      entire `Tests/Infrastructure.Tests/Repositories/` directory; delete every file
      that contains any of those identifiers. Retain all mock-only files.
      _Acceptance: no file in `Tests/Infrastructure.Tests/` imports
      `Microsoft.EntityFrameworkCore`; `dotnet test` is green._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Delete `Infrastructure/Persistence/Repositories/QueryHelpers.cs`** if it is
      no longer referenced by any file after the Dapper rewrites (the
      `EscapeILikePattern` helper is still used in `ArticleRepository.GetAnalysisDoneAsync`
      — if any reference to it remains, retain the file). If unreferenced, delete it
      AND delete `Tests/Infrastructure.Tests/Repositories/QueryHelpersTests.cs` in the
      same step to avoid a compile error from an orphaned test file.
      _Acceptance: if `QueryHelpers.cs` is deleted, `QueryHelpersTests.cs` is also
      deleted and `dotnet build` is green; if `QueryHelpers.cs` is retained (still
      referenced), both files remain and `dotnet build` is green._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [ ] **Verify the full solution builds and all tests pass** — run `dotnet build` on
      the solution root and `dotnet test` on each test project. Fix any compilation
      errors introduced during cutover (stale `using` directives, missing package
      transitive references, etc.).
      _Acceptance: zero build errors; zero test failures._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 9 — Skill Update

- [x] **Rename `.claude/skills/ef-core-conventions/SKILL.md` to
      `.claude/skills/dapper-conventions/SKILL.md`** — create the new file at the new
      path; content must document the Dapper repository pattern as it now exists in the
      codebase: `IDbConnectionFactory` injection, per-operation connection lifecycle,
      `CommandDefinition` with `cancellationToken`, `IUnitOfWork` opt-in for
      multi-step operations, SQL constant classes (`*Sql.cs`), type handler usage for
      `vector`, `jsonb`, and `text[]` (including the explicit `DynamicParameters`
      binding for `Tags` as `string[]` with `NpgsqlDbType.Array | NpgsqlDbType.Text`),
      and the full method-name catalogue
      (`GetByIdAsync`, `GetPendingForXxxAsync`, `UpdateXxxAsync`, etc.). Delete the
      old `ef-core-conventions/SKILL.md` file after creating the new one.
      _Acceptance: `.claude/skills/dapper-conventions/SKILL.md` exists and documents
      the new patterns; `.claude/skills/ef-core-conventions/SKILL.md` is deleted._
      _Skill: .claude/skills/skill-creator/SKILL.md_

- [x] **Modify `CLAUDE.md`** — update the `ef-core-conventions` skill entry in
      `<available_skills>` to `dapper-conventions` with an updated description.
      Update the `## Database` section to remove the reference to "EF Core migrations"
      and replace with "DbUp forward-only SQL scripts in
      `Infrastructure/Persistence/Sql/`".
      _Acceptance: `CLAUDE.md` contains no reference to `ef-core-conventions`;
      `dapper-conventions` entry is present._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `.claude/skills/testing/SKILL.md`** — replace the "EF Core InMemory"
      repository test section with the following note: "Repository tests that
      previously used EF InMemory have been deleted. New repository tests must be
      written against `Mock<I*Repository>` at the interface level only." Remove all
      mention of Testcontainers from the updated section.
      _Acceptance: skill file contains no reference to `EF InMemory`,
      `Database.EnsureCreated`, or `Testcontainers` in the context of repository
      testing._
      _Skill: .claude/skills/skill-creator/SKILL.md_

- [x] **Modify `.claude/skills/code-conventions/SKILL.md`** — remove all references
      to `ExecuteUpdateAsync` and `SetProperty` (EF Core-specific update patterns that
      no longer exist in the codebase). Add a note in the "Update Pattern" section
      that updates are now performed via hand-written parameterized SQL executed
      through Dapper's `ExecuteAsync`.
      _Acceptance: skill file contains no reference to `ExecuteUpdateAsync` or
      `SetProperty`; the update pattern section describes Dapper `ExecuteAsync` with
      parameterized SQL._
      _Skill: .claude/skills/skill-creator/SKILL.md_

---

## Open Questions

None — all ADR open questions were resolved by the user before this tasklist was
authored:
1. `vector(768)` is kept (no halfvec change).
2. Repository tests: deleted rather than converted to Testcontainers.
3. Per-operation connections (each repo method opens its own `NpgsqlConnection`).
4. No down-migrations; DbUp forward-only scripts are the version-control mechanism.
5. `CREATE EXTENSION IF NOT EXISTS vector` in `0001_baseline.sql` is acceptable.
