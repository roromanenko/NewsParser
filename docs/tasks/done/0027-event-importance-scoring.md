# Event Importance Scoring

## Goal

Add a numeric importance score and a tier label (`Breaking` / `High` / `Normal` / `Low`) to
Events so editors can filter and sort the events list by significance, with scores computed from
article volume, distinct source count, velocity, and an AI-provided intrinsic label produced
inline during the existing Haiku event-summary update.

## Affected Layers

- Core
- Infrastructure
- Api
- Worker
- Tests

---

## Tasks

### Phase 1 — SQL Migration

- [x] **Create `Infrastructure/Persistence/Sql/0002_add_event_importance.sql`** — forward-only
      DbUp script (embedded resource) that adds three nullable columns to `events` and one index:
      ```sql
      ALTER TABLE events ADD COLUMN "ImportanceTier"         TEXT             NULL;
      ALTER TABLE events ADD COLUMN "ImportanceBaseScore"    DOUBLE PRECISION NULL;
      ALTER TABLE events ADD COLUMN "ImportanceCalculatedAt" TIMESTAMPTZ      NULL;
      CREATE INDEX IF NOT EXISTS "IX_events_ImportanceTier" ON events ("ImportanceTier");
      ```
      _Acceptance: file is marked as an embedded resource in `Infrastructure.csproj`; `DbUpMigrator`
      picks it up automatically on next startup; no `DOWN` script._
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

---

### Core

- [x] **Modify `Core/DomainModels/Event.cs`** — add three nullable properties and the
      `ImportanceTier` enum (co-located in the same file per code-conventions):
      ```csharp
      public ImportanceTier? ImportanceTier { get; set; }
      public double? ImportanceBaseScore { get; set; }
      public DateTimeOffset? ImportanceCalculatedAt { get; set; }
      ```
      ```csharp
      public enum ImportanceTier { Breaking, High, Normal, Low }
      ```
      _Acceptance: file compiles; enum is in `Core.DomainModels` namespace; no EF or
      infrastructure references._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/DomainModels/AI/EventSummaryUpdateResult.cs`** — record returned by the
      updated `IEventSummaryUpdater`:
      ```csharp
      public record EventSummaryUpdateResult(string UpdatedSummary, string IntrinsicImportance);
      ```
      _Acceptance: file compiles; no infrastructure references; sits alongside
      `Core/DomainModels/AI/ArticleAnalysisResult.cs`._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/DomainModels/ImportanceInputs.cs`** — value object carrying scorer inputs:
      `ArticleCount` (int), `DistinctSourceCount` (int), `ArticlesLastHour` (int), `AiLabel`
      (string), `LastArticleAt` (`DateTimeOffset`), `Now` (`DateTimeOffset`).
      _Acceptance: file compiles; record or class with those six members; no infrastructure
      references._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/DomainModels/ImportanceScoreResult.cs`** — value object carrying scorer
      output: `BaseScore` (double), `EffectiveScore` (double), `Tier` (`ImportanceTier`).
      _Acceptance: file compiles; record or class with those three members; no infrastructure
      references._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/DomainModels/EventImportanceStats.cs`** — value object returned by the
      new repository method:
      ```csharp
      public record EventImportanceStats(
          int ArticleCount,
          int DistinctSourceCount,
          int ArticlesLastHour,
          DateTimeOffset? LastArticleAt);
      ```
      _Acceptance: file compiles; no infrastructure references._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/Interfaces/Services/IEventImportanceScorer.cs`** — pure scorer interface:
      ```csharp
      public interface IEventImportanceScorer
      {
          ImportanceScoreResult Calculate(ImportanceInputs inputs);
      }
      ```
      _Acceptance: interface compiles; no implementation details; resides in
      `Core.Interfaces.Services` namespace._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/Interfaces/AI/IEventSummaryUpdater.cs`** — change return type from
      `Task<string>` to `Task<EventSummaryUpdateResult>`:
      ```csharp
      Task<EventSummaryUpdateResult> UpdateSummaryAsync(
          Event evt, List<string> newFacts, CancellationToken cancellationToken = default);
      ```
      _Acceptance: interface compiles; `EventSummaryUpdateResult` resolves from
      `Core.DomainModels.AI`._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/Interfaces/Repositories/IEventRepository.cs`** — add two new methods and
      update the `GetPagedAsync` / `CountAsync` signatures:
      - Add `Task<EventImportanceStats> GetImportanceStatsAsync(Guid eventId, CancellationToken ct = default);`
      - Add `Task UpdateImportanceAsync(Guid eventId, ImportanceTier tier, double baseScore, DateTimeOffset calculatedAt, CancellationToken ct = default);`
      - Change `GetPagedAsync` to `Task<List<Event>> GetPagedAsync(int page, int pageSize, string? search, string sortBy, ImportanceTier? tier, CancellationToken cancellationToken = default);`
      - Change `CountAsync` to `Task<int> CountAsync(string? search, ImportanceTier? tier, CancellationToken cancellationToken = default);`
      _Acceptance: interface compiles; no implementation; all existing methods unchanged except
      the two updated signatures._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Infrastructure

- [x] **Create `Infrastructure/Configuration/EventImportanceOptions.cs`** — options class with
      `public const string SectionName = "EventImportance"` and nested classes
      `ImportanceWeights` (Volume=0.20, Sources=0.30, Velocity=0.20, Ai=0.30),
      `ImportanceCaps` (Volume=20, Sources=5, Velocity=5), `HalfLifeHours` (double, default 12),
      and `ImportanceTiers` (BreakingThreshold=75, HighThreshold=50, NormalThreshold=25).
      _Acceptance: class compiles in `Infrastructure.Configuration` namespace; default values match
      spec; no circular dependencies._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/InfrastructureServiceExtensions.cs`** (or the equivalent DI
      registration entry point) — register `EventImportanceOptions` via
      `services.Configure<EventImportanceOptions>(configuration.GetSection(EventImportanceOptions.SectionName))`
      and register `EventImportanceScorer` as scoped:
      `services.AddScoped<IEventImportanceScorer, EventImportanceScorer>();`
      _Acceptance: `dotnet build` passes; `EventImportanceScorer` can be resolved from DI in a
      running host._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Infrastructure/Services/EventImportanceScorer.cs`** — `internal` scoped class
      implementing `IEventImportanceScorer`. Constructor takes `IOptions<EventImportanceOptions>`
      and reads `.Value` into a field. Implements `Calculate(ImportanceInputs)` with:
      - `f_volume = Clamp(log(1 + count) / log(1 + VolumeCap), 0, 1)`
      - `f_sources = min(distinct_sources, SourcesCap) / SourcesCap`
      - `f_velocity = min(articles_last_hour, VelocityCap) / VelocityCap`
      - `f_ai`: `"low"=0.25`, `"medium"=0.5`, `"high"=0.75`, `"breaking"=1.0`; unknown/empty
        defaults to `0.5` (no exception; no external logging dependency — caller logs).
      - `base_score = 100 × (w_volume×f_volume + w_sources×f_sources + w_velocity×f_velocity + w_ai×f_ai)`
      - `effective_score = base_score × exp(-hours_since_last_article / HalfLifeHours)`
      - Tier assigned from `effective_score`: `≥BreakingThreshold → Breaking`,
        `≥HighThreshold → High`, `≥NormalThreshold → Normal`, else `Low`.
      _Acceptance: class compiles; no I/O; no repository or DB dependencies; pure math; all
      weights/caps/thresholds read from options (no magic numbers)._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/Prompts/event_summary_updater.txt`** — append a new output
      field to the required JSON response shape: `"intrinsic_importance": "low" | "medium" | "high" | "breaking"`
      and add a short rubric: `breaking = major breaking news; high = significant national or
      regional importance; medium = normal newsworthy; low = minor or niche.` Leave all
      `updated_summary` instructions identical.
      _Acceptance: prompt file still instructs the model to return `updated_summary`; new field
      and rubric are present; no Ukrainian-language instructions changed._

- [x] **Modify `Infrastructure/AI/ClaudeEventSummaryUpdater.cs`** — change return type to
      `Task<EventSummaryUpdateResult>` and extend `ParseResult` to read the
      `intrinsic_importance` JSON field. If the field is absent or empty, default to `"medium"`.
      Do NOT throw on a missing field. Return
      `new EventSummaryUpdateResult(summary, intrinsicImportance)`.
      _Acceptance: class satisfies the updated `IEventSummaryUpdater` interface; unit-compilable;
      missing `intrinsic_importance` field returns `"medium"` without exception; `updated_summary`
      absence still throws (unchanged guard)._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Entity/EventEntity.cs`** — add three nullable
      columns and one non-persisted read-only helper for the paged query:
      ```csharp
      public string? ImportanceTier { get; set; }
      public double? ImportanceBaseScore { get; set; }
      public DateTimeOffset? ImportanceCalculatedAt { get; set; }
      public int DistinctSourceCount { get; set; }   // populated from paged/detail queries, not stored
      ```
      _Acceptance: class compiles; `DistinctSourceCount` has no column mapping annotation._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Mappers/EventMapper.cs`** — extend `ToDomain` to map
      the three new nullable columns using `Enum.Parse<ImportanceTier>(entity.ImportanceTier!)`
      when not null, and `ToEntity` to map back with `.ToString()`. `DistinctSourceCount` is
      read-only and is not mapped in `ToEntity`.
      _Acceptance: `ToDomain` handles `null` ImportanceTier gracefully (null → null);
      `ToEntity` produces a non-null string when tier is set._
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/Sql/EventSql.cs`** — make the following
      additions and changes:
      1. Append `"ImportanceTier"`, `"ImportanceBaseScore"`, `"ImportanceCalculatedAt"` to every
         `SELECT` that returns an `EventEntity`: `GetById`, `GetActiveEvents`,
         `FindSimilarEvents`, `GetUnpublishedUpdateEvents`, `GetPagedWithSearch`,
         `GetPagedWithoutSearch`.
      2. Rework `GetPagedWithSearch` and `GetPagedWithoutSearch` to:
         - Include `COUNT(DISTINCT a."SourceId") AS "DistinctSourceCount"` via a `LEFT JOIN`
           to the `articles` table (or correlated subquery) so the list query returns it per row.
         - Accept an optional `{1}` tier-filter placeholder clause (e.g.,
           `AND "ImportanceTier" = @tier` when tier is provided; blank otherwise).
         - Introduce a third ORDER BY template when `sortBy == "importance"`:
           ```sql
           ORDER BY "ImportanceBaseScore"
             * EXP(-EXTRACT(EPOCH FROM (NOW() - GREATEST("LastUpdatedAt", "ImportanceCalculatedAt"))) / 3600.0 / @halfLifeHours)
             DESC NULLS LAST
           ```
      3. Add `GetImportanceStats` constant — single query returning
         `ArticleCount`, `DistinctSourceCount`, `ArticlesLastHour`, `LastArticleAt`:
         ```sql
         SELECT
           COUNT(*)                                                         AS "ArticleCount",
           COUNT(DISTINCT "SourceId")                                       AS "DistinctSourceCount",
           COUNT(*) FILTER (WHERE "AddedToEventAt" >= NOW() - INTERVAL '1 hour') AS "ArticlesLastHour",
           MAX("AddedToEventAt")                                            AS "LastArticleAt"
         FROM articles
         WHERE "EventId" = @eventId
         ```
      4. Add `UpdateImportance` constant:
         ```sql
         UPDATE events
         SET "ImportanceTier"         = @tier,
             "ImportanceBaseScore"    = @baseScore,
             "ImportanceCalculatedAt" = @calculatedAt
         WHERE "Id" = @eventId
         ```
      5. Amend `CountWithSearch` and `CountWithoutSearch` to accept an optional tier-filter
         clause in the same `{0}` placeholder pattern.
      _Acceptance: file compiles; all SQL constants are `const string`; no raw SQL outside this
      file; `GetById` and all other returning-entity constants now include the three new columns._
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/EventRepository.cs`** — apply all
      interface changes:
      1. Inject `IOptions<EventImportanceOptions>` in the constructor; read `.Value.HalfLifeHours`
         for use in paged queries.
      2. Update `GetPagedAsync` to accept the new `ImportanceTier? tier` parameter; build the tier
         filter clause and importance ORDER BY using the new SQL templates; pass `@halfLifeHours`
         as a Dapper param when sorting by importance.
      3. Update `CountAsync` to accept `ImportanceTier? tier` and apply the filter clause.
      4. Implement `GetImportanceStatsAsync` using `EventSql.GetImportanceStats`, returning an
         `EventImportanceStats` record. Use `CommandDefinition` with `cancellationToken`.
      5. Implement `UpdateImportanceAsync` using `EventSql.UpdateImportance` and `ExecuteAsync`
         with `CommandDefinition`. Tier bound as `tier.ToString()` (string column).
      6. Map `DistinctSourceCount` from paged query results into `EventEntity.DistinctSourceCount`.
      _Acceptance: class satisfies the full `IEventRepository` interface; `dotnet build` passes;
      no raw SQL literals in the repository — all SQL lives in `EventSql.cs`._
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Services/EventService.cs`** — update the `MergeAsync` call to
      `summaryUpdater.UpdateSummaryAsync` to consume only `result.UpdatedSummary` (discard
      `result.IntrinsicImportance`). Add a brief comment: `// importance recalc on merge is
      deferred to the roadmap refresher worker`.
      _Acceptance: `EventService` compiles against the new `IEventSummaryUpdater` contract;
      no new logic added; merge behaviour is identical to before._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Api/appsettings.Development.json`** — add `"EventImportance"` section with the
      default values from the spec:
      ```json
      "EventImportance": {
        "Weights": { "Volume": 0.20, "Sources": 0.30, "Velocity": 0.20, "Ai": 0.30 },
        "Caps": { "Volume": 20, "Sources": 5, "Velocity": 5 },
        "HalfLifeHours": 12,
        "Tiers": { "BreakingThreshold": 75, "HighThreshold": 50, "NormalThreshold": 25 }
      }
      ```
      _Acceptance: JSON is valid; `EventImportanceOptions` binds without error at startup._

- [x] **Modify `Worker/appsettings.Development.json`** — add the identical `"EventImportance"`
      section as above.
      _Acceptance: JSON is valid; worker host binds `EventImportanceOptions` without error._

---

### Worker

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — extend `AnalysisContext` record
      with `IEventImportanceScorer Scorer`, resolve it in `ProcessAsync` alongside the other
      scoped services. In `UpdateEventEmbeddingAsync`, after the existing
      `UpdateSummaryTitleAndEmbeddingAsync` call:
      1. Capture `result.IntrinsicImportance` from the `SummaryUpdater.UpdateSummaryAsync` call
         (the return type is now `EventSummaryUpdateResult`); use `result.UpdatedSummary` where
         the bare string was used before.
      2. If the `newFacts` path ran (i.e. summary was actually updated), call:
         ```csharp
         var stats  = await ctx.EventRepository.GetImportanceStatsAsync(evt.Id, ct);
         var scored = ctx.Scorer.Calculate(new ImportanceInputs(
             stats.ArticleCount, stats.DistinctSourceCount, stats.ArticlesLastHour,
             summaryResult.IntrinsicImportance, stats.LastArticleAt ?? DateTimeOffset.UtcNow,
             DateTimeOffset.UtcNow));
         await ctx.EventRepository.UpdateImportanceAsync(
             evt.Id, scored.Tier, scored.BaseScore, DateTimeOffset.UtcNow, ct);
         ```
      3. Wrap the stats + score + persist block in its own `try/catch` with `LogWarning` — a
         scorer failure must not roll back the summary update.
      _Acceptance: worker compiles; existing summary/embedding logic is unchanged; a scorer
      failure logs a warning and does not throw; `UpdateEventEmbeddingAsync` outer catch is
      unaffected._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Api

- [x] **Modify `Api/Models/EventDtos.cs`** — add three fields to `EventListItemDto` and
      `EventDetailDto`:
      - `string? ImportanceTier` (nullable — events scored before migration have no tier)
      - `double? ImportanceBaseScore`
      - `int DistinctSourceCount`
      _Acceptance: both record types compile; existing constructor parameters are not reordered;
      Swagger reflects the three new fields._
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Mappers/EventMapper.cs`** — update `ToListItemDto` and `ToDetailDto` to map
      the three new fields: `ImportanceTier = evt.ImportanceTier?.ToString()`,
      `ImportanceBaseScore = evt.ImportanceBaseScore`, `DistinctSourceCount` from the entity's
      `DistinctSourceCount` property (populated by the paged/detail query).
      _Acceptance: mapper compiles; `ImportanceTier` is nullable string in the DTO; no inline
      mapping logic in controllers._
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Modify `Api/Controllers/SortOptions.cs`** — add an `EventSortValues` set that extends
      the existing sort values with `"importance"`:
      ```csharp
      public static readonly HashSet<string> EventSortValues = ["newest", "oldest", "importance"];
      ```
      Keep `BasicSortValues` unchanged (used by `ArticlesController`).
      _Acceptance: file compiles; `ArticlesController` still references `BasicSortValues` without
      error._
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Controllers/EventsController.cs`** — in `GetAll`:
      1. Replace `SortOptions.BasicSortValues` guard with `SortOptions.EventSortValues`.
      2. Add `[FromQuery] string? tier = null` parameter.
      3. Parse `tier` via `Enum.TryParse<ImportanceTier>(tier, ignoreCase: true, out var parsedTier)`;
         if `tier` is non-null and parse fails, return `BadRequest` listing allowed values via
         `Enum.GetNames<ImportanceTier>()`.
      4. Pass `parsedTier` (nullable) to `GetPagedAsync` and `CountAsync`.
      _Acceptance: `GET /events?tier=Breaking&sortBy=importance` returns filtered, scored-sorted
      results; invalid tier value returns 400 with the allowed-values list; Swagger shows the new
      `tier` parameter; existing `search` and `sortBy` params are unaffected._
      _Skill: .claude/skills/api-conventions/SKILL.md_

---

### Tests

- [ ] **Create `Tests/Unit/EventImportanceScorerTests.cs`** (or the project's existing unit test
      location) — NUnit parameterized tests for `EventImportanceScorer` covering:
      - Volume normalizer: `count=0`, `count=1`, `count=VolumeCap`, `count=VolumeCap*2` (clamp).
      - Sources normalizer: `distinct=0`, `distinct=SourcesCap`, `distinct=SourcesCap+1`.
      - Velocity normalizer: `velocity=0`, `velocity=VelocityCap`, `velocity=VelocityCap+1`.
      - AI label mapping: `"low"→0.25`, `"medium"→0.5`, `"high"→0.75`, `"breaking"→1.0`,
        unknown/empty → `0.5` (medium default).
      - Tier mapping at each boundary (per ADR): 74.999 → High, 75.0 → Breaking, 50.0 → High,
        49.999 → Normal, 25.0 → Normal, 24.999 → Low.
      - Decay at `hours=0` (factor=1.0), `hours=HalfLifeHours` (factor≈0.5),
        `hours=2×HalfLifeHours` (factor≈0.25).
      Use `[TestCase]` for boundary tables. Build scorer with
      `Microsoft.Extensions.Options.Options.Create(new EventImportanceOptions())` — no mocks
      required.
      _Acceptance: all tests pass (`dotnet test`); no test requires a database or DI container;
      test names clearly identify the boundary being tested._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_
      _Delegated to test-writer agent_

---

## Roadmap

The following item is out of scope for this iteration but should be tracked:

- **Future: `Worker/Workers/EventImportanceRefresherWorker.cs`** — lightweight background worker
  that periodically re-evaluates `effective_score` for active events and updates
  `ImportanceTier` in the database when the decayed score crosses a tier boundary. Driven by a
  new `EventImportanceRefresherOptions` (interval seconds, batch size). No implementation in
  this iteration.

---

## Open Questions

None — the ADR fully specifies layering, file paths, formula, AI-contract change, orchestration
placement, and out-of-scope items.
