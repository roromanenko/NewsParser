# Event Importance Scoring

## Context

Editors need a way to filter and sort Events by significance. Currently `Event` exposes only
`ArticleCount` and `LastUpdatedAt` — there is no concept of "Breaking" vs "Normal" news, and the
events list (`EventsController.GetAll` → `EventRepository.GetPagedAsync`) sorts only by
`LastUpdatedAt` (see `docs/architecture/decisions/0015-server-side-search-sort-pagination.md`).

The feature adds an importance tier, a numeric base score, and a recency-decayed effective score
computed from four components: article volume, distinct source count, short-window velocity, and
an AI-provided intrinsic importance label. Recalculation must be hooked into the existing Haiku
event summary update path and must run synchronously in that pipeline (no new worker).

Additional concrete context extracted from the codebase:

- The summary update is currently wired only into `Worker/Workers/ArticleAnalysisWorker.cs`
  (`UpdateEventEmbeddingAsync` → `IEventSummaryUpdater.UpdateSummaryAsync` →
  `IEventRepository.UpdateSummaryTitleAndEmbeddingAsync`). There is no standalone
  "event summary update service" class — the update is orchestrated inline in the worker.
- `ClaudeEventSummaryUpdater` (`Infrastructure/AI/ClaudeEventSummaryUpdater.cs`) returns a single
  `string` (the updated summary). The prompt file is
  `Infrastructure/AI/Prompts/event_summary_updater.txt`, loaded via `PromptsOptions`.
- The `Event` table lives in `Infrastructure/Persistence/Sql/0001_baseline.sql`; schema changes go
  through DbUp forward-only scripts (`Infrastructure/Persistence/Sql/000N_*.sql`, embedded resource).
- SQL lives in `Infrastructure/Persistence/Repositories/Sql/EventSql.cs` as `const string`;
  `GetPagedWithSearch` / `GetPagedWithoutSearch` take a `{0}` direction placeholder — the list
  query pattern is already structurally flexible.
- Options classes follow `Infrastructure/Configuration/*.cs` with `public const string SectionName`
  (e.g. `AiOptions`, `ValidationOptions`) and are registered via
  `services.Configure<T>(configuration.GetSection(T.SectionName))` in
  `InfrastructureServiceExtensions`.
- The AI-label threading problem is real: the scorer needs the label, but `IEventSummaryUpdater`
  currently returns only the summary string. Either its contract changes or the label is fetched
  separately.

**Affected layers:** Core (new interface + model fields + enum), Infrastructure (scorer impl +
repo changes + SQL migration + updated Haiku client + prompt), Api (DTO fields + sort/filter
query params), Worker (call the scorer after summary update).

**UI is explicitly out of scope for this iteration.**

## Options

### Option 1 — Scorer is a pure function in Core, orchestrator lives in the worker

Introduce `IEventImportanceScorer` in `Core/Interfaces/Services/` with a single pure method:

```
ImportanceScoreResult Calculate(ImportanceInputs inputs)
```

where `ImportanceInputs` carries article count, distinct source count, articles-last-hour,
AI label, last-article-at, and the weight/cap/threshold config. Implement it as
`Infrastructure/Services/EventImportanceScorer.cs` (no I/O, no DI besides `IOptions<T>`).

Extend `IEventSummaryUpdater.UpdateSummaryAsync` to return a small result object
(`EventSummaryUpdateResult`) that includes both `UpdatedSummary` and `IntrinsicImportance`.
Update `ClaudeEventSummaryUpdater` and its prompt to emit `intrinsic_importance`.

Add a new repository method `GetImportanceStatsAsync(Guid eventId, ...)` that returns
`(int ArticleCount, int DistinctSourceCount, int ArticlesLastHour, DateTimeOffset? LastArticleAt)`
in one SQL round-trip. The worker calls the scorer after `UpdateSummaryTitleAndEmbeddingAsync`
and persists the tier/base-score via a new `UpdateImportanceAsync` repo method.

**Pros:**
- Scorer is pure, trivially unit-testable (matches the testing requirement in the feature spec).
- Follows `ArticleValidator` / `IArticleValidator` precedent (business logic in a focused Core
  interface with an Infrastructure impl).
- No cross-cutting side effects: the worker owns orchestration, consistent with how
  `ArticleAnalysisWorker` already orchestrates classifier + contradiction detector + summary
  updater.
- AI label flows cleanly as a return value of the existing single AI call — no extra round-trip.

**Cons:**
- Breaks `IEventSummaryUpdater` contract (tests and one call site in `EventService.MergeAsync`
  need to be updated). Merge path does not need the label, so the merge call simply discards it.

### Option 2 — Scorer is an Infrastructure service that does its own DB reads

Put the entire `EventImportanceScorer` in Infrastructure, inject `IEventRepository` into it, and
call it from the worker as `scorer.RecalculateAsync(eventId, aiLabel, ct)`. The scorer queries
stats, computes, and persists internally.

**Pros:**
- Single call from the worker; orchestration hidden.

**Cons:**
- Violates SRP as defined in `.claude/skills/code-conventions/SKILL.md`: a "scorer" that queries
  and writes is really a mini-service. Pure math function becomes harder to unit-test (needs mock
  repo).
- The formula itself is Core business logic. Putting it behind a repo-dependent class makes it
  inaccessible for testing without mocking.
- The feature explicitly asks for unit tests on normalizers, tier mapping, and decay — these are
  cleanest against a pure function.

### Option 3 — Add a second AI call (separate importance-label endpoint)

Keep `IEventSummaryUpdater` unchanged. Introduce a new `IEventImportanceLabeler` Haiku client that
runs after the summary update with a dedicated prompt.

**Pros:**
- No contract change to the summary updater.

**Cons:**
- Doubles the AI cost and latency on every event update. The feature description explicitly asks
  to extend the existing Haiku prompt with one extra output field.
- Contradicts the spec ("Extend the Haiku event summary prompt with one output field").

## Decision

**Option 1** — pure scorer in Core/Infrastructure, AI label threaded through a richer return type
from `IEventSummaryUpdater`, recalculation orchestrated inline in `ArticleAnalysisWorker` right
after the existing summary/embedding write.

Rationale:

1. **Testability matches the feature requirement.** The feature spec calls for unit tests on
   component normalizers at boundaries, tier mapping at thresholds, and recency decay at 0 /
   half-life / 2× half-life. A pure `Calculate(ImportanceInputs)` function against
   `IOptions<EventImportanceOptions>` is the cleanest testable surface.

2. **Consistent with the existing AI pipeline.** `IEventClassifier` already returns a structured
   result (`EventClassificationResult` with multiple fields). Extending `IEventSummaryUpdater` to
   return `EventSummaryUpdateResult { UpdatedSummary, IntrinsicImportance }` mirrors that pattern
   and avoids a second AI round-trip.

3. **No separate worker.** The feature explicitly forbids one. Hooking the scorer after
   `UpdateSummaryTitleAndEmbeddingAsync` in `UpdateEventEmbeddingAsync` keeps the orchestration in
   one place and guarantees the snapshot is always fresh after a summary change.

4. **Sort uses live SQL, filter uses stored column** — exactly as the spec mandates. The stored
   `ImportanceTier` is a cheap filter; live decayed score in `ORDER BY` keeps sort honest without
   a background worker to refresh snapshots.

### Detailed Design

#### Domain model (Core)

`Core/DomainModels/Event.cs` — add three nullable fields (no data migration needed):

```csharp
public ImportanceTier? ImportanceTier { get; set; }
public double? ImportanceBaseScore { get; set; }
public DateTimeOffset? ImportanceCalculatedAt { get; set; }
```

Add enum in the same file (co-location rule per code-conventions skill):

```csharp
public enum ImportanceTier { Breaking, High, Normal, Low }
```

Also add a derived field for API exposure that is **not persisted** — distinct source count and
articles-last-hour are read from SQL at recalc time; they flow through `ImportanceInputs` and
`ImportanceScoreResult` only, never as domain properties on `Event`. Article count is already
`Event.ArticleCount`.

#### Scorer (Core interface + Infrastructure impl)

`Core/Interfaces/Services/IEventImportanceScorer.cs`:

```csharp
public interface IEventImportanceScorer
{
    ImportanceScoreResult Calculate(ImportanceInputs inputs);
}
```

`Core/DomainModels/ImportanceInputs.cs` and `Core/DomainModels/ImportanceScoreResult.cs` hold the
value types. `ImportanceInputs` carries: `ArticleCount`, `DistinctSourceCount`,
`ArticlesLastHour`, `AiLabel` (string — `"low" | "medium" | "high" | "breaking"`),
`LastArticleAt` (`DateTimeOffset`), `Now` (`DateTimeOffset`). `ImportanceScoreResult` carries
`BaseScore`, `EffectiveScore`, `Tier`.

`Infrastructure/Services/EventImportanceScorer.cs` — stateless `internal` class that takes
`IOptions<EventImportanceOptions>` (read `.Value` into a field in ctor per code-conventions).
Implements the exact formula from the spec:
- `f_volume = log(1 + count) / log(1 + VolumeCap)`, clamped `[0, 1]`
- `f_sources = min(distinct_sources, SourcesCap) / SourcesCap`
- `f_velocity = min(articles_last_hour, VelocityCap) / VelocityCap`
- `f_ai`: `low=0.25`, `medium=0.5`, `high=0.75`, `breaking=1.0` (unknown/empty → `0.5` = medium,
  with a logged warning — this must be in the scorer, not the AI client, because the AI client
  is a pure transport)
- `effective_score = base_score × 0.5 ^ (hours_since_last_article / HalfLifeHours)` (true half-life decay: factor is 0.5 at HalfLifeHours, 0.25 at 2× HalfLifeHours)
- Tier assigned from `effective_score` (not base) so a decayed event can drift down —
  spec says current snapshot persists the tier at calc time, effective decay only alters
  future recalculations and the live sort.

Register as scoped in `InfrastructureServiceExtensions.AddServices`.

#### Config (Infrastructure)

`Infrastructure/Configuration/EventImportanceOptions.cs`:

```csharp
public class EventImportanceOptions
{
    public const string SectionName = "EventImportance";
    public ImportanceWeights Weights { get; set; } = new();
    public ImportanceCaps Caps { get; set; } = new();
    public double HalfLifeHours { get; set; } = 12;
    public ImportanceTiers Tiers { get; set; } = new();
}

public class ImportanceWeights { public double Volume=0.20, Sources=0.30, Velocity=0.20, Ai=0.30; }
public class ImportanceCaps    { public int Volume=20,    Sources=5,     Velocity=5; }
public class ImportanceTiers   { public double BreakingThreshold=75, HighThreshold=50, NormalThreshold=25; }
```

(C# syntactic form — property-with-default — not the literal quoted above; treat this as a
signature sketch.)

Register via `services.Configure<EventImportanceOptions>(configuration.GetSection(...))` in
`InfrastructureServiceExtensions.AddServices` (same place as `ValidationOptions`). Defaults
published into `Api/appsettings.Development.json` and `Worker/appsettings.Development.json` under
`"EventImportance"`.

#### AI contract change — `IEventSummaryUpdater`

`Core/Interfaces/AI/IEventSummaryUpdater.cs`:

```csharp
Task<EventSummaryUpdateResult> UpdateSummaryAsync(
    Event evt, List<string> newFacts, CancellationToken cancellationToken = default);
```

New `Core/DomainModels/AI/EventSummaryUpdateResult.cs`:

```csharp
public record EventSummaryUpdateResult(string UpdatedSummary, string IntrinsicImportance);
```

`Infrastructure/AI/ClaudeEventSummaryUpdater.cs` — parser extended to read
`intrinsic_importance` alongside `updated_summary`. When the field is missing (backwards
compat / parse failure), return `"medium"` and let the scorer treat it as the neutral default.

`Infrastructure/AI/Prompts/event_summary_updater.txt` — add instruction and an extra JSON field:

```
"intrinsic_importance": "low" | "medium" | "high" | "breaking"
```

plus a short rubric ("breaking = major breaking news, high = significant national/regional
importance, medium = normal newsworthy, low = minor") so the label is stable. Prompt changes to
evaluate the event as a whole (not just the new facts). Label is used in the formula, not
persisted.

Call sites to update:
- `Worker/Workers/ArticleAnalysisWorker.UpdateEventEmbeddingAsync` — consumes
  `result.UpdatedSummary` where it currently uses the return value directly; also threads
  `result.IntrinsicImportance` into the scorer call.
- `Infrastructure/Services/EventService.MergeAsync` — uses `result.UpdatedSummary` and discards
  the label (merge does not recalc importance in this iteration; explicitly noted in code).

#### Repository changes

`Core/Interfaces/Repositories/IEventRepository.cs` — add:

```csharp
Task<EventImportanceStats> GetImportanceStatsAsync(Guid eventId, CancellationToken ct = default);
Task UpdateImportanceAsync(Guid eventId, ImportanceTier tier, double baseScore,
                           DateTimeOffset calculatedAt, CancellationToken ct = default);
```

`Core/DomainModels/EventImportanceStats.cs`:
```csharp
public record EventImportanceStats(
    int ArticleCount,
    int DistinctSourceCount,
    int ArticlesLastHour,
    DateTimeOffset? LastArticleAt);
```

Change `GetPagedAsync` signature to accept `tier` filter + `sortBy = "importance"`:

```csharp
Task<List<Event>> GetPagedAsync(int page, int pageSize, string? search,
    string sortBy, ImportanceTier? tier, CancellationToken ct = default);
```

`Infrastructure/Persistence/Repositories/EventRepository.cs` and `EventSql.cs`:

- New SQL constants for `GetImportanceStats` (single query with `COUNT(*)`,
  `COUNT(DISTINCT "SourceId")`, `COUNT(*) FILTER (WHERE "AddedToEventAt" >= NOW() - INTERVAL '1 hour')`,
  `MAX("AddedToEventAt")` grouped by the given event id).
- New SQL constants `UpdateImportance`.
- Rework `GetPagedWithSearch` / `GetPagedWithoutSearch`:
  - Add optional `"ImportanceTier" = @tier` clause.
  - Introduce a third ORDER BY variant when `sortBy == "importance"`:
    ```
    ORDER BY "ImportanceBaseScore" *
      EXP(-EXTRACT(EPOCH FROM (NOW() - GREATEST("LastUpdatedAt", "ImportanceCalculatedAt")))
          / 3600.0 / @halfLifeHours)
      DESC NULLS LAST
    ```
    Pass `halfLifeHours` from the repo via `IOptions<EventImportanceOptions>` (inject it into the
    repo constructor — matches existing pattern of repos receiving DI context).
  - Update `CountAsync` / `CountWithSearch` / `CountWithoutSearch` to accept the tier filter.
- Per `.claude/skills/dapper-conventions/SKILL.md`: keep SQL in `EventSql.cs` constants, use
  `CommandDefinition` with `cancellationToken`, `ExecuteAsync` for the update.

Entity + mapper updates:
- `Infrastructure/Persistence/Entity/EventEntity.cs` — three new nullable columns
  (`ImportanceTier` string, `ImportanceBaseScore` double?, `ImportanceCalculatedAt` DateTimeOffset?).
- `Infrastructure/Persistence/Mappers/EventMapper.cs` — map with string↔enum pattern
  (`Enum.Parse<ImportanceTier>(entity.ImportanceTier!)` when not null).
- Every `SELECT` in `EventSql.cs` that returns an `EventEntity` gets the three new columns
  appended (consistent — audit `GetById`, `GetActiveEvents`, `FindSimilarEvents`, `GetPagedWith*`,
  `GetUnpublishedUpdateEvents`).

#### SQL migration

New file `Infrastructure/Persistence/Sql/0002_add_event_importance.sql` (embedded resource —
picked up by `DbUpMigrator` automatically, no code change needed):

```sql
ALTER TABLE events ADD COLUMN "ImportanceTier"           TEXT        NULL;
ALTER TABLE events ADD COLUMN "ImportanceBaseScore"      DOUBLE PRECISION NULL;
ALTER TABLE events ADD COLUMN "ImportanceCalculatedAt"   TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS "IX_events_ImportanceTier" ON events ("ImportanceTier");
```

No back-fill — nullable, per spec.

#### Orchestration (Worker)

`Worker/Workers/ArticleAnalysisWorker.UpdateEventEmbeddingAsync`:

1. Existing: update summary + title + embedding via repo.
2. **New:** if the new-facts path ran (i.e. summary was actually updated), call:
   ```
   var stats = await eventRepo.GetImportanceStatsAsync(evt.Id, ct);
   var result = scorer.Calculate(new ImportanceInputs(stats..., summaryResult.IntrinsicImportance, DateTimeOffset.UtcNow));
   await eventRepo.UpdateImportanceAsync(evt.Id, result.Tier, result.BaseScore, DateTimeOffset.UtcNow, ct);
   ```
3. Scorer + stats call is wrapped in its own try/catch with `LogWarning` — importance failure must
   not roll back the summary update. Matches the pattern of key-facts-extraction failure handling
   in the same worker.

Inject `IEventImportanceScorer` into the existing `AnalysisContext` record and resolve it in
`ProcessAsync` alongside the other scoped services.

`EventService.MergeAsync` does **not** recalc importance in this iteration (out of scope — could
be added when the recency-refresh worker on the roadmap is built).

#### API

`Api/Models/EventDtos.cs`:

- `EventListItemDto` — add `ImportanceTier` (string?), `ImportanceBaseScore` (double?),
  `DistinctSourceCount` (int). `ArticleCount` already present.
- `EventDetailDto` — same additions.

The list endpoint must return `DistinctSourceCount` without a per-row round trip. Two options:
- **Preferred:** extend the main paged query to include `COUNT(DISTINCT a."SourceId")` via a
  LEFT JOIN subquery / correlated aggregate. Single query, consistent with how `ArticleCount`
  is already a column.
- Alternative (worse): separate query per event (N+1).

Add `DistinctSourceCount` to `EventEntity` as a non-column read-only field populated from the
paged query result. For the detail endpoint, the same subquery works in `GetById`.

`Api/Mappers/EventMapper.cs` — add the three new fields. `ImportanceTier` → `?.ToString()`.

`Api/Controllers/EventsController.GetAll`:
- Add `[FromQuery] string? tier = null` and extend `sortBy` whitelist. Update
  `Api/Controllers/SortOptions.cs`:
  ```csharp
  public static readonly HashSet<string> EventSortValues = ["newest", "oldest", "importance"];
  ```
  (Rename `BasicSortValues` to domain-specific sets if needed — check for other callers first;
  `ArticlesController` still uses the old set.)
- `tier` parsed via `Enum.TryParse<ImportanceTier>(..., ignoreCase: true, out ...)` per enum
  handling rules in `code-conventions`. Invalid → `BadRequest` with allowed values.
- Pass `tier` down to the repo.

Controller keeps the pagination guard pattern. No new endpoint.

#### Tests (matches spec)

Unit tests against `EventImportanceScorer` only — pure function, no DB:

- Volume normalizer at boundaries (0, 1, `VolumeCap`, `VolumeCap * 2` — clamp).
- Sources normalizer at 0, `SourcesCap`, `SourcesCap + 1`.
- Velocity normalizer at 0, `VelocityCap`, `VelocityCap + 1`.
- AI label mapping for `low`/`medium`/`high`/`breaking` (+ unknown → default).
- Tier mapping at each threshold boundary (74.999 → High, 75.0 → Breaking, 50.0 → High, 49.999 →
  Normal, 25.0 → Normal, 24.999 → Low).
- Decay at `hours = 0` (factor = 1), `hours = HalfLifeHours` (factor ≈ 0.5), `hours =
  2 × HalfLifeHours` (factor ≈ 0.25).

No integration tests against real DB for the scorer are required in this iteration. Repository
query tests for importance sort/filter are a "nice to have" but follow the existing test-writer
conventions.

## Implementation Notes

### Order of changes (for feature-planner)

1. **Migration first** — `0002_add_event_importance.sql` so every subsequent entity change has a
   real schema to talk to.
2. **Core domain model** — `Event` fields + `ImportanceTier` enum + `EventSummaryUpdateResult`,
   `ImportanceInputs`, `ImportanceScoreResult`, `EventImportanceStats`.
3. **Core interfaces** — `IEventImportanceScorer`, update `IEventSummaryUpdater`, extend
   `IEventRepository` with `GetImportanceStatsAsync` / `UpdateImportanceAsync` and the
   `GetPagedAsync` / `CountAsync` signature changes.
4. **Infrastructure** —
   - `EventImportanceOptions` + DI registration.
   - `EventImportanceScorer` impl (pure, tested in step 8).
   - `ClaudeEventSummaryUpdater` + prompt update to return both fields.
   - `EventEntity` + `EventMapper` + `EventSql` (new constants + amended SELECTs + index + tier
     filter + importance sort with half-life parameter).
   - `EventRepository` methods: `GetImportanceStatsAsync`, `UpdateImportanceAsync`, and updated
     `GetPagedAsync` / `CountAsync` + injected `IOptions<EventImportanceOptions>`.
5. **Worker** — extend `AnalysisContext` with `IEventImportanceScorer`, call scorer after summary
   update in `UpdateEventEmbeddingAsync` with try/catch + LogWarning.
6. **EventService.MergeAsync** — adapt to the new summary-updater return type (discard label).
7. **Api** — DTOs, `EventMapper`, `EventsController.GetAll` (`tier` param + `sortBy=importance`),
   `SortOptions.cs` whitelist extension, appsettings defaults.
8. **Tests** — scorer unit tests per the list above. Test-writer agent handles this.

### Risks / trade-offs to flag

- **Prompt churn.** Changing `event_summary_updater.txt` risks regressing the summary quality for
  Ukrainian output (normalization). Keep `updated_summary` instructions identical; only append the
  new field. Adjust the parser defensively: if `intrinsic_importance` is missing, default to
  `"medium"` and log once — do NOT fail the summary update.
- **List query performance.** Adding `COUNT(DISTINCT SourceId)` to the paged query is OK for
  current dataset size (ADR 0015 notes the same). Watch for it on dashboards; a materialized
  column is a future optimization, not this iteration.
- **Decay drift on the stored tier.** The spec acknowledges this ("Current snapshot can drift for
  dormant events") and the roadmap entry below captures it. Do NOT silently recompute tier on
  read — that would hide the drift and make debugging harder.
- **`sortBy=importance` with NULL score.** Use `DESC NULLS LAST` so un-scored events sink to the
  bottom, not the top.
- **Merge path.** `MergeAsync` currently does not recalc importance. Target event keeps its
  snapshot. If this becomes a problem, add it to the roadmap worker or extend
  `MergeAsync` later.

### Roadmap

Per the spec: add a future lightweight worker to refresh stored `ImportanceTier` when the decayed
effective score crosses a tier boundary. Suggested location: `Worker/Workers/EventImportanceRefresherWorker.cs`,
driven by `EventImportanceRefresherOptions` (interval seconds, batch size). Not in this iteration.

### Out of scope (explicit)

Manual override, category multiplier, persisting the AI label, recency-refresh worker, UI
changes. The API is fully backend-only.

### Skills for feature-planner to follow

- `.claude/skills/code-conventions/SKILL.md` — layer placement, Options pattern, enum storage,
  BaseController, worker three-level structure, domain model conventions.
- `.claude/skills/clean-code/SKILL.md` — no magic numbers (all weights/caps/thresholds in
  Options), early returns in validators, no AI-label enum invention (use string per spec).
- `.claude/skills/api-conventions/SKILL.md` — DTO naming, `PagedResult<T>`, pagination guard,
  enum-as-string in DTOs, `Enum.TryParse` validation of `tier` query param.
- `.claude/skills/ef-core-conventions/SKILL.md` — N/A (Dapper now; listed in CLAUDE.md as
  superseded).
- `.claude/skills/dapper-conventions/SKILL.md` — SQL constant placement, `CommandDefinition` +
  `cancellationToken`, `ExecuteAsync` for updates, enum-as-string binding, repo interface in
  `Core/Interfaces/Repositories/`.
- `.claude/skills/mappers/SKILL.md` — `EventMapper.cs` in both `Infrastructure/Persistence/Mappers/`
  (Entity↔Domain) and `Api/Mappers/` (Domain→DTO), enum via `Enum.Parse` / `.ToString()`.
- `.claude/skills/testing/SKILL.md` — NUnit + FluentAssertions unit tests for the scorer
  (parameterized `[TestCase]` for boundary tables).
