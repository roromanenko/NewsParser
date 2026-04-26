# 0022 — Multi-Project Tenancy

## Status
Proposed

## Context

Today every operational entity in NewsParser — `Source`, `Article`, `Event`,
`PublishTarget`, `Publication` — lives in a single global scope. There
is no way to operate two thematically distinct news streams (e.g. one Telegram channel
about technology in Polish, another about war in Ukrainian) on the same instance
without their data and AI processing colliding.

Three concrete pain points motivate this ADR:

1. **Hardcoded analyzer category list.** `Infrastructure/AI/Prompts/analyzer.txt:7-8`
   pins the allowed `Article.Category` values to ten general-purpose Western news
   buckets (Politics, Economics, Technology, Sports, Culture, Science, War, Society,
   Health, Environment). A future thematic project (e.g. "AI engineering news") needs a
   completely different category list — editing the file requires a redeploy and breaks
   every other consumer of the prompt.
2. **Cross-topic contamination of the pgvector kNN.**
   `EventRepository.FindSimilarEventsAsync` (`Infrastructure/Persistence/Repositories/Sql/EventSql.cs:66`)
   does a global cosine-distance search over `events."Embedding"`. With multiple themes
   in the same table, a tech article can be matched to a sports event purely because
   their embeddings are nearby in the universal vector space.
3. **No per-project AI cost breakdown (acknowledged, deferred).** `ai_request_log`
   rows carry `Worker`, `CorrelationId`, and `ArticleId` but no `ProjectId`. A
   per-project cost view is operationally useful but is **out of scope for this ADR** —
   `ai_request_log` stays a global log; no `ProjectId` column is added to it.

The product owner has already chosen the **tenant model** (Project as a hard tenant,
Source belongs to one Project, duplication of fetch + analysis accepted) and ruled out
the alternatives (Source M:N Project; two-stage universal-then-project analysis). This
ADR does not re-litigate those choices — it specifies the schema, API, worker, and
migration consequences of implementing them.

### Scope of this ADR

In:

- The `Project` entity, its fields, and its repository.
- New FK `ProjectId` on every operational table, with backfill of existing data to a
  single `Default` project.
- A DbUp migration script `0007_introduce_projects.sql`.
- API surface change: scoped controllers move under `projects/{projectId:guid}/...`;
  a new `ProjectsController` for global project CRUD.
- Worker behavior: how `ProjectId` flows from `Source` → `Article` → `Event` →
  `Publication`, and how `ArticleAnalysisWorker` loads the project's prompt and
  category list at analysis time.
- High-level UI changes (project switcher, route segment, query-cache invalidation,
  OpenAPI regeneration).

Explicitly out:

- Per-project authorization. Every authenticated user sees every project
  (decision recorded by the product owner). No `UserProject` join table.
- Sharing one `Source` across two projects (ruled out: dup the row).
- Per-project model selection or per-project versions of `event_classifier.txt`,
  `event_summary_updater.txt`, `contradiction_detector.txt`,
  `haiku_event_title.txt`, `haiku_key_facts.txt`, `telegram.txt`, or `generator.txt`.
  Only the **analyzer prompt** and **category list** become per-project in this ADR.
- A "Default project" concept beyond the bootstrap row. After backfill, Default is a
  normal project — no code special-cases it.

### Reconciliation with existing conventions

Two existing conventions are touched and need an explicit position:

- `api-conventions` skill §"Route Naming" mandates lowercase-plural routes **without
  the `/api/` prefix**. The product owner described scoped routes as
  `/api/projects/{projectId:guid}/...`. The intent (the "projects" segment + `projectId`
  parameter) is what matters, not the literal `/api/` segment. **This ADR keeps the
  established no-`/api/`-prefix convention** — scoped routes are
  `projects/{projectId:guid}/articles`, etc. The reverse-proxy / OpenAPI client always
  saw e.g. `https://host/articles`; under this ADR they see
  `https://host/projects/{id}/articles`. No `/api/` prefix is added.
- ADR `normalize-internal-ai-fields-to-configured-language` defined `{OUTPUT_LANGUAGE}`
  as a substitution token applied by `PromptsOptions.ReadPrompt`. That contract is
  preserved: the per-project analyzer prompt continues to be subject to the same
  substitution. A new placeholder `{CATEGORIES}` is added on top of it. Substitution
  remains centralised in one place — see §G.

---

## Decision

Introduce a `Project` aggregate as the root tenancy boundary. Every operational entity
gains a non-nullable `ProjectId` FK to `projects("Id")`. The data is backfilled to a
single bootstrap **Default** project with a hardcoded GUID. The analyzer prompt and the
category whitelist live as columns on the project row; the existing
`Infrastructure/AI/Prompts/analyzer.txt` is migrated into the Default project's
`AnalyzerPromptText` column and is no longer read by the analyzer at runtime.

API endpoints split into two groups:

- **Global** (no project scope): `projects` (CRUD), `auth/*`, `users/*`, `health` —
  unchanged route shape.
- **Scoped** (per-project): every other resource controller is rerooted to
  `projects/{projectId:guid}/...`. A scoped DI service `IProjectContext` exposes the
  current `ProjectId` to downstream services and repositories without each method
  signature having to thread it.

Workers continue to iterate globally over their work queues; they read `ProjectId`
from the entity they are processing (a `Source` row, an `Article` row, a `Publication`
row), look up the project's analyzer prompt + categories when that's needed (only the
analyzer worker), and write `ProjectId` onto every row they create.

The pgvector kNN query in `EventRepository.FindSimilarEventsAsync` adds a
`AND "ProjectId" = @projectId` predicate; the existing HNSW index on
`events."Embedding"` continues to do the heavy lifting (Postgres applies the predicate
as a filter on top of the candidates returned by the vector index).

---

### A. The `Project` entity

**Domain model** — `Core/DomainModels/Project.cs`:

```csharp
public class Project
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string AnalyzerPromptText { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = [];
    public string OutputLanguage { get; set; } = "uk";
    public string OutputLanguageName { get; set; } = "Ukrainian";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; }
}
```

| Field | Type | Nullability | Justification |
|---|---|---|---|
| `Id` | `Guid` | NOT NULL | Standard PK. `init` per domain-model conventions. |
| `Name` | `string` | NOT NULL | Display name shown in the UI switcher. |
| `Slug` | `string` | NOT NULL | URL-safe short identifier (e.g. `default`, `tech-news`). Useful for log lines and human-readable references; the API still keys off `Id` for stability. UNIQUE in DB. Required to be non-empty; auto-derived from `Name` if not supplied at create time. |
| `AnalyzerPromptText` | `string` (TEXT) | NOT NULL | The full system prompt the analyzer worker uses for articles in this project. Replaces `Infrastructure/AI/Prompts/analyzer.txt` for this project. May contain `{OUTPUT_LANGUAGE}` and the new `{CATEGORIES}` placeholders — see §G. |
| `Categories` | `string[]` (PostgreSQL `text[]`) | NOT NULL DEFAULT `'{}'` | The allowed `Article.Category` values for articles in this project. Stored as `text[]` rather than a separate `project_categories` table because (a) categories have no other attributes — no description, ordering, color, etc.; (b) Postgres `text[]` is already used in this codebase (`articles."Tags"`); (c) `text[]` reads/writes as one value per row, no extra join. A separate table would only earn its keep if categories grew attributes — open as a future-work item but not justified today. |
| `OutputLanguage` | `string` (ISO 639-1) | NOT NULL DEFAULT `'uk'` | Per-project override of `AiOptions.Normalization.TargetLanguage`. The `normalize-internal-ai-fields-to-configured-language` ADR placed `TargetLanguage` in global config; in a multi-project world the Polish-tech project and the Ukrainian-war project obviously need different output languages. The global config value becomes the **default** for new projects, copied into `Project.OutputLanguage` at create time. The per-project value is what `PromptsOptions` substitutes for `{OUTPUT_LANGUAGE}` once the analyzer prompt is loaded for a given project. See §G for substitution timing. |
| `OutputLanguageName` | `string` | NOT NULL DEFAULT `'Ukrainian'` | Human-readable name used by the substitution (LLMs respond better to "Ukrainian" than "uk" — same rationale as the prior ADR). |
| `IsActive` | `bool` | NOT NULL DEFAULT `TRUE` | Soft enable/disable. When `FALSE`: the project is still visible (no destructive deletion), all scoped endpoints return `409 Conflict` for write operations and read endpoints continue to work, and workers skip sources whose project is inactive. The existing `Source.IsActive` and `PublishTarget.IsActive` flags remain — `Project.IsActive = false` short-circuits them, but turning a project back on does not flip its sources. |
| `CreatedAt` | `DateTimeOffset` | NOT NULL | Per project convention, all timestamps are `DateTimeOffset`. `init` because creation time is immutable. |

**Excluded fields** (considered, rejected):

- `Description` — no UI requirement; can be added later without migration risk.
- `OwnerUserId` / `CreatedByUserId` — out of scope per the "all users see all projects"
  decision. No ownership concept exists.
- `DefaultPublishTargetId` — `PublishTarget` is per-project anyway; no ergonomic gain.

**Repository** — `Core/Interfaces/Repositories/IProjectRepository.cs`:

```csharp
Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
Task<List<Project>> GetAllAsync(CancellationToken cancellationToken = default);
Task<Project?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default);
Task UpdateAsync(Project project, CancellationToken cancellationToken = default);
Task UpdateActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
```

`ExistsAsync` exists separately from `GetByIdAsync` because the project-validation
filter (§F) calls it on every scoped request and would otherwise pull a row containing
the full `AnalyzerPromptText` for nothing. Implementation: `SELECT 1 FROM projects
WHERE "Id" = @id LIMIT 1`.

---

### B. Changes to existing entities

Tables that gain `ProjectId UUID NOT NULL`:

| Table | New FK | CASCADE on project DELETE | Justification |
|---|---|---|---|
| `sources` | `"ProjectId" UUID NOT NULL` | RESTRICT | A project may not be deleted while sources still belong to it — the operator must explicitly migrate or delete sources first. Prevents accidental nuking of months of fetch history. |
| `articles` | `"ProjectId" UUID NOT NULL` | RESTRICT | Same rationale. Articles also chain into events, publications, media files. |
| `events` | `"ProjectId" UUID NOT NULL` | RESTRICT | Events accumulate cross-article context — they are operationally expensive to recreate. Block deletion. |
| `publish_targets` | `"ProjectId" UUID NOT NULL` | RESTRICT | A live Telegram channel target attached to a project must not be silently dropped. |
| `publications` | `"ProjectId" UUID NOT NULL` | RESTRICT | Already FK-constrained to articles, publish_targets, events — RESTRICT is consistent. |

**Why RESTRICT and not CASCADE.** A real-world project deletion is a multi-step
operation (revoke Telegram credentials, archive sources, decide what to do with
events). Cascading from a single `DELETE FROM projects WHERE "Id" = ...` would erase
historical data and break the AI cost ledger. Soft-delete via `IsActive = false` is
the recommended day-to-day path; hard `DELETE` requires the operator to clean up
children first and is a deliberate admin action.

**Tables that intentionally do NOT get `ProjectId`.** Reachable transitively, no
benefit to denormalising:

- `media_files` — reachable via `ArticleId` (its required FK) or `PublicationId`. No
  query pattern needs a direct `ProjectId` filter; ON DELETE CASCADE on `ArticleId`
  already handles cleanup.
- `event_updates`, `contradictions`, `contradiction_articles`, `publish_logs` — all
  reachable via parent `EventId` / `ArticleId` / `PublicationId`. No direct query by
  `ProjectId`.
- `ai_request_log` — intentionally global. This table is a cost-attribution ledger,
  not a scoped resource. Adding `ProjectId` here is out of scope for this ADR; it stays
  as a global log queryable by `Worker`, `CorrelationId`, and `ArticleId`.
- `users` — global resource. The product owner explicitly ruled out per-project
  user scoping.

**Unique-index implications.**

- `IX_sources_Url` (UNIQUE) currently enforces global URL uniqueness. After this
  change the same RSS URL may legitimately exist in two projects — drop the global
  UNIQUE and replace with `UNIQUE ("ProjectId", "Url")`. Without this change, the
  product decision "duplicate Source per project" is unenforceable.
- `IX_articles_SourceId_ExternalId` (partial UNIQUE) stays as-is — it scopes by
  `SourceId`, which is already per-project, so no functional change is needed.

---

### C. `PublishTarget` per-project design

`PublishTarget` becomes per-project: it gains `ProjectId` (FK to `projects`, `RESTRICT`).
This is the load-bearing decision behind the whole tenancy model — without it, two
projects share Telegram channel routing and the boundary is meaningless.

**`PublishTarget.SystemPrompt` migration.** The existing
`PublishTarget.SystemPrompt` column **stays exactly where it is** and is **not**
moved to `Project`. Rationale:

- The system prompt encodes how to write a Telegram post for a specific channel
  (style, tone, format, link rules). It is target-specific, not project-specific. A
  project may have a "main channel" and a "breaking-news channel" with very different
  voices but the same source material.
- This matches the v1 boundary set by ADR `normalize-internal-ai-fields-to-configured-language`
  §"Scope boundary on Telegram publications": publication content language and style
  is a per-channel concern.
- During migration, all existing rows already have a `SystemPrompt` populated; the
  backfill simply attaches them to Default. Nothing else is rewritten.

`Publication.Article.Source.Project` and `Publication.PublishTarget.Project` must
agree at write time — this invariant is enforced in `PublicationService` (the existing
service that creates publications). The validator filter (§F) plus the `ProjectId`
column on `publications` make divergence detectable in a single SQL query.

---

### D. The DbUp migration script

File: `Infrastructure/Persistence/Sql/0007_introduce_projects.sql`.

DbUp scripts are forward-only and run inside an implicit transaction per
`Infrastructure/Persistence/DbUpMigrator.cs`. The script is idempotent at the
**bootstrap insert** level (uses `ON CONFLICT DO NOTHING` so re-running does not
double-insert Default) but the schema operations are gated by `IF NOT EXISTS`
for the create-table parts and use plain `ALTER TABLE` for column additions —
DbUp tracks already-applied scripts in `schemaversions`, so no per-statement guard
is necessary.

**Bootstrap GUID for the Default project.** Hardcoded:
`00000000-0000-0000-0000-000000000001`. Justification:

- Idempotent: re-running the script (manually, on a non-DbUp environment, in a test)
  always produces the same row.
- Tests can `WHERE "ProjectId" = '00000000-0000-0000-0000-000000000001'` without
  having to query for the Default first.
- Documented as `ProjectConstants.DefaultProjectId` in `Core/DomainModels/Project.cs`
  so any code that refers to "the default" has one named constant.

**Backfill strategy** (per table — same shape repeated):

```sql
-- 1. Add the column with a DEFAULT pointing at Default.Id so existing rows get a value.
ALTER TABLE sources
    ADD COLUMN "ProjectId" UUID NOT NULL
    DEFAULT '00000000-0000-0000-0000-000000000001';

-- 2. Drop the DEFAULT — new inserts must specify ProjectId explicitly going forward.
ALTER TABLE sources ALTER COLUMN "ProjectId" DROP DEFAULT;

-- 3. Add the FK after the column is populated.
ALTER TABLE sources
    ADD CONSTRAINT "FK_sources_projects_ProjectId"
    FOREIGN KEY ("ProjectId") REFERENCES projects ("Id") ON DELETE RESTRICT;
```

The same three-step pattern applies to `articles`, `events`, `publications`,
`publish_targets`. Order matters: `projects` must be created and the Default row
inserted before any `ALTER TABLE … ADD COLUMN … DEFAULT '…'` runs.
`ai_request_log` is **not** altered — no `ProjectId` column is added to it.

**Indexes** — added in the same script. The critical query paths:

| Query path | Index |
|---|---|
| `articles` paged-by-status, scoped to project (`ArticleRepository.GetAnalysisDoneAsync`-style) | `CREATE INDEX "IX_articles_ProjectId_Status_ProcessedAt" ON articles ("ProjectId", "Status", "ProcessedAt" DESC);` |
| Pending-batch fetch by worker (`GetPendingAsync` will gain a project filter when called per-project; see §G note) | covered by the index above (leftmost prefix). |
| `events` paged-by-status / list view in scope | `CREATE INDEX "IX_events_ProjectId_Status_LastUpdatedAt" ON events ("ProjectId", "Status", "LastUpdatedAt" DESC);` |
| `events` kNN with project predicate | **No new vector index.** The existing `events."Embedding" hnsw vector_cosine_ops` index is preserved unchanged; the project predicate is applied as a post-filter on the candidate set HNSW returns. See §E. |
| `publications` list per project | `CREATE INDEX "IX_publications_ProjectId_Status_CreatedAt" ON publications ("ProjectId", "Status", "CreatedAt" DESC);` (drops `IX_publications_Status` if appropriate, or keeps both — keeping both for now, the cost is marginal). |
| `sources` per-project URL uniqueness | `CREATE UNIQUE INDEX "IX_sources_ProjectId_Url" ON sources ("ProjectId", "Url"); DROP INDEX "IX_sources_Url";` |
| `projects` slug uniqueness | `CREATE UNIQUE INDEX "IX_projects_Slug" ON projects ("Slug");` |

The bootstrap insert seeds the Default project. The `AnalyzerPromptText` column is
populated by reading the contents of `Infrastructure/AI/Prompts/analyzer.txt` **at
script-authoring time** and embedding it as a `$$ … $$` dollar-quoted string literal
inside the SQL file. The implementer must paste the analyzer.txt body verbatim into
the script; the categories array is also a literal:

```sql
INSERT INTO projects ("Id", "Name", "Slug", "AnalyzerPromptText",
                      "Categories", "OutputLanguage", "OutputLanguageName",
                      "IsActive", "CreatedAt")
VALUES (
    '00000000-0000-0000-0000-000000000001',
    'Default',
    'default',
    $PROMPT$<paste analyzer.txt verbatim, including {OUTPUT_LANGUAGE} and the new
    {CATEGORIES} placeholder — see §G for placeholder edits>$PROMPT$,
    ARRAY['Politics','Economics','Technology','Sports','Culture','Science',
          'War','Society','Health','Environment'],
    'uk',
    'Ukrainian',
    TRUE,
    now()
)
ON CONFLICT ("Id") DO NOTHING;
```

Embedding the prompt verbatim into the SQL file (rather than reading it at runtime
and inserting from C#) keeps DbUp self-sufficient — the migration produces a
correct `Default` row even if the .txt file is later deleted from the repository.

---

### E. Pgvector similarity for Event classification

Current SQL (`EventSql.FindSimilarEvents`):

```sql
SELECT {EventColumns}, 1 - ("Embedding" <=> @vector::vector) AS similarity
FROM events
WHERE "Status" = 'Active'
  AND "LastUpdatedAt" >= @windowStart
  AND "Embedding" IS NOT NULL
  AND (1 - ("Embedding" <=> @vector::vector)) >= @threshold
ORDER BY similarity DESC
LIMIT @maxTake
```

New SQL — adds **one** WHERE predicate:

```sql
SELECT {EventColumns}, 1 - ("Embedding" <=> @vector::vector) AS similarity
FROM events
WHERE "ProjectId" = @projectId
  AND "Status" = 'Active'
  AND "LastUpdatedAt" >= @windowStart
  AND "Embedding" IS NOT NULL
  AND (1 - ("Embedding" <=> @vector::vector)) >= @threshold
ORDER BY similarity DESC
LIMIT @maxTake
```

The `IEventRepository.FindSimilarEventsAsync` signature gains `Guid projectId` as the
first parameter; `ArticleAnalysisWorker.ClassifyIntoEventAsync` passes
`article.ProjectId` (which is set on the article when the source is fetched).

**Index decision.** Do **not** add a project-partial vector index. Rationale:

- HNSW (and ivfflat) indexes in pgvector do not support partial / multi-column
  composite indexes that include the vector. A partial index `WHERE "ProjectId" = …`
  would require one index per project — operationally untenable.
- The existing `IX_articles_Embedding` and `events."Embedding" hnsw …` (the codebase
  already uses HNSW per `0001_baseline.sql:114`) returns a small number of nearest
  neighbours (`@maxTake` is bounded — e.g. 10) per probe. The post-filter on
  `"ProjectId" = @projectId` then reduces that further. With only a handful of
  projects in the foreseeable future, this is fine.
- The `windowStart` predicate (typically a few hours) and `Status = 'Active'` already
  prune out most rows even today. Adding `ProjectId` to the post-filter is one more
  scalar comparison per candidate row.
- If, in the future, the active-event count per project rises to the point where the
  HNSW probe returns mostly other-project candidates, the right answer is **per-project
  partitioning** of the `events` table (Postgres declarative partitioning by
  `ProjectId`), not a partial index. Flagged as future work.

The new composite B-tree index `IX_events_ProjectId_Status_LastUpdatedAt` is for the
**non-vector** event list / status queries (`GetActiveEventsAsync`, `GetPagedAsync`),
not the kNN. Two access paths, two index strategies — they don't conflict.

---

### F. API changes

#### F.1 Route layout

**Global controllers** (no project context — keep their current shape):

| Existing route | Stays as |
|---|---|
| `auth/*` | `auth/*` |
| `users/*` | `users/*` |
| `health` | `health` |
| (new) `projects` (CRUD) | `projects` |

**Scoped controllers** — every other resource controller is rerouted:

| Today | After |
|---|---|
| `articles/*` | `projects/{projectId:guid}/articles/*` |
| `events/*` | `projects/{projectId:guid}/events/*` |
| `sources/*` | `projects/{projectId:guid}/sources/*` |
| `publications/*` | `projects/{projectId:guid}/publications/*` |
| `publish-targets/*` | `projects/{projectId:guid}/publish-targets/*` |
| `ai-operations/*` | `projects/{projectId:guid}/ai-operations/*` |

Implementation: each controller's `[Route("...")]` attribute changes to
`[Route("projects/{projectId:guid}/articles")]`, etc. The `{projectId}` segment is a
**route parameter** — it is captured by ASP.NET Core but the controller actions do
not declare it as a method parameter. The validation filter (§F.3) reads it from
`HttpContext.Request.RouteValues` and pushes it into the `IProjectContext` scoped
service.

**The `/api/` prefix.** The product owner's task description used `/api/projects/...`,
but the established `api-conventions` skill explicitly forbids the `/api/` prefix on
all controllers. The literal `/api/` is part of how the product owner thinks about
URLs (and how a typical reverse proxy would route), not a literal route requirement.
**This ADR keeps the no-`/api/`-prefix convention** — scoped routes are
`projects/{projectId:guid}/articles`, not `api/projects/{projectId:guid}/articles`.
Adding `/api/` to one controller would require adding it to every controller in the
project (auth, users, health) — a much wider change than this ADR sets out to do, and
not justified by any technical benefit. If a reverse proxy needs an `/api/` prefix
that is configured at the proxy, not in the controllers.

#### F.2 ProjectsController

New file `Api/Controllers/ProjectsController.cs`:

```csharp
[ApiController]
[Route("projects")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class ProjectsController(IProjectService projectService) : BaseController
{
    [HttpGet]                                                       // list, no scope
    [HttpGet("{id:guid}")]                                          // detail
    [HttpPost]                                                      // create
    [HttpPut("{id:guid}")]                                          // full update
    [HttpPatch("{id:guid}/status")]                                 // toggle IsActive
    [HttpDelete("{id:guid}")]                                       // hard delete (RESTRICT)
}
```

`IProjectService` (Core) wraps the repository and enforces the `Slug` derivation,
uniqueness check, and the rule that you cannot delete the Default project (lookup by
the hardcoded constant). Throws `KeyNotFoundException` / `InvalidOperationException`
per the existing exception-handling contract.

`[Authorize(Roles = nameof(UserRole.Admin))]` — only admins can create/edit/delete
projects. Editors interact with projects through the scoped controllers but cannot
mutate the project itself. This matches `SourcesController` and `PublishTargetsController`
which are already Admin-only.

DTOs (`Api/Models/ProjectDtos.cs`):

```csharp
public record ProjectListItemDto(Guid Id, string Name, string Slug, bool IsActive);
public record ProjectDetailDto(
    Guid Id, string Name, string Slug, string AnalyzerPromptText,
    List<string> Categories, string OutputLanguage, string OutputLanguageName,
    bool IsActive, DateTimeOffset CreatedAt);
public record CreateProjectRequest(
    string Name, string? Slug, string AnalyzerPromptText,
    List<string> Categories, string OutputLanguage, string OutputLanguageName);
public record UpdateProjectRequest(
    string Name, string AnalyzerPromptText,
    List<string> Categories, string OutputLanguage, string OutputLanguageName, bool IsActive);
```

FluentValidation validators in `Api/Validators/`:
`CreateProjectRequestValidator`, `UpdateProjectRequestValidator` enforce non-empty
Name, non-empty AnalyzerPromptText, `Categories.Count >= 1`, `OutputLanguage` length
(2–5 chars, e.g. `uk`, `en`, `pt-br`).

#### F.3 Project validation: action filter + IProjectContext

Three options for validating `{projectId}`:

| Option | Pros | Cons |
|---|---|---|
| Custom **route constraint** (e.g. `[Route("…/{projectId:projectExists}")]`) | Fails at routing — never reaches the controller. | Constraints must be sync; project lookup is async. Forces sync-over-async, contradicts the codebase's all-async style. |
| **Middleware** with conditional path matching | One place, runs before MVC. | Has to inspect the request path with string matching; ugly and brittle as routes change. |
| **Action filter** (`IAsyncActionFilter` or `ActionFilterAttribute`) registered on scoped controllers via a marker attribute / convention | Async-friendly, runs after model binding so `projectId` is already a `Guid`, integrates with MVC's response pipeline. | One extra DI registration. |

**Decision: action filter**, named `RequireProjectAttribute` and globally added by an
MVC convention to any controller whose route template starts with
`projects/{projectId:guid}`. Behavior:

1. Read `projectId` from `HttpContext.Request.RouteValues["projectId"]`.
2. Call `IProjectRepository.ExistsAsync(projectId, ct)` (cheap — `SELECT 1`).
   - If not found: short-circuit with `NotFound("Project {id} not found")`.
3. Call `IProjectRepository.GetByIdAsync` (cached for the request scope) only when an
   inactive-project check is required for write operations:
   - On `POST` / `PUT` / `PATCH` / `DELETE`: if `IsActive == false` →
     `Conflict("Project {id} is inactive")`.
   - On `GET`: read endpoints are allowed regardless of `IsActive`.
4. Push the resolved `Project` into `IProjectContext.Current` (scoped, see below) so
   downstream code can read it without re-querying.

A small per-request **scoped DI** service:

```csharp
public interface IProjectContext
{
    Guid ProjectId { get; }
    Project Current { get; }
    bool IsSet { get; }
}
```

`IProjectContext` is registered as `Scoped`. The action filter populates it; services
and repositories called from the controller read `_projectContext.ProjectId` instead
of having to thread it through every method signature. This pattern keeps existing
controller signatures small (no `[FromRoute] Guid projectId` clutter on every action)
and lets repositories transparently scope queries.

**HTTP-status decision tree** (asked in the task):

| Situation | Status | Reason |
|---|---|---|
| `{projectId}` does not parse as `Guid` | `400 Bad Request` | Routing-level failure (`{id:guid}` constraint). Built-in. |
| Project not found | `404 Not Found` | Tenant resource missing. |
| Project found but `IsActive = false`, write operation | `409 Conflict` | Per the existing exception-handling contract (`InvalidOperationException` → 409). The semantic is "the project is in a state that does not permit this write". |
| Project found but `IsActive = false`, read operation | `200 OK` | Read access never blocked — operators need to inspect inactive projects to decide whether to reactivate or delete. |
| Caller authenticated but not admin trying to write a `Project` (CRUD) | `403 Forbidden` | Standard `[Authorize(Roles = ...)]`. |

Authorization (the per-project `User` ACL question) is **out of scope** per the
recorded product decision; the action filter does not check user-vs-project
permissions, only the project's existence and active state.

#### F.4 Cross-project URL collisions

If a caller passes a `{projectId}` in the URL but the body references an entity from
a **different** project (e.g. `POST projects/A/publications` with
`articleId = <article belonging to project B>`), the service layer rejects with
`InvalidOperationException` (→ 409). Each scoped service must validate that the body
entities' `ProjectId` matches `IProjectContext.Current.ProjectId`. This is a one-line
guard per service — listed under "Implementation Notes" sequencing.

---

### G. Worker changes

The product owner specified that workers continue to iterate globally. This is the
correct call: it preserves a single batched fetch / analysis cycle and avoids the
N-cycles-per-project anti-pattern. The change is **what each row knows about itself**,
not how the workers schedule.

#### G.1 SourceFetcherWorker

`SourceFetcherWorker.ProcessAsync` reads `ISourceRepository.GetActiveAsync(sourceType)`
and iterates the global active-sources list. Each `Source` row now carries
`ProjectId`. When the worker writes new `Article` rows via `articleRepository.AddRange`
(or however articles are inserted today), the article inherits `source.ProjectId`.

**Change required:** `ArticleEntity` / `ArticleMapper.ToEntity` and the relevant
insert SQL gain a `ProjectId` column; the worker passes `source.ProjectId` to the
mapper. No change to scheduling, no change to `Source` lookup logic.

`Source.IsActive == true AND Project.IsActive == false` should skip the source.
Implementation: add `JOIN projects p ON p."Id" = s."ProjectId" AND p."IsActive" = TRUE`
to `SourceSql.GetActive`.

#### G.2 ArticleAnalysisWorker

This is the worker most affected. Today it loads a global analyzer prompt at startup
via DI (`PromptsOptions.Analyzer`, substituted with `{OUTPUT_LANGUAGE}` once). Going
forward:

1. The worker fetches a batch of `Pending` articles (unchanged, still global).
2. For each article, it loads the article's `Project` row by
   `article.ProjectId` via a new `IProjectRepository.GetByIdAsync` call. Cache the
   project for the duration of the batch — typically one or two distinct projects
   appear in any batch, so a small `Dictionary<Guid, Project>` inside `ProcessAsync`
   is sufficient.
3. The worker builds the analyzer prompt **per project**:
   ```
   prompt = project.AnalyzerPromptText
       .Replace("{OUTPUT_LANGUAGE}", project.OutputLanguageName)
       .Replace("{CATEGORIES}", string.Join(", ", project.Categories));
   ```
   The substitution lives in a new helper, `ProjectPromptBuilder`, in
   `Infrastructure/AI/`. Same shape as `PromptsOptions.ReadPrompt`, but accepting a
   `Project` instead of a static config value.
4. The worker passes the substituted prompt into the analyzer. Two ways to wire this:
   - **Chosen:** add an overload on `IArticleAnalyzer.AnalyzeAsync(Article article,
     string systemPrompt, CancellationToken ct)`. The existing constructor-injected
     `_prompt` field on `GeminiArticleAnalyzer` / `ClaudeArticleAnalyzer` becomes a
     fallback / removed. The DI registration in
     `Infrastructure/Extensions/InfrastructureServiceExtensions.cs:115-122` drops
     `promptsOptions.Analyzer` from the constructor; the analyzer is now a
     stateless executor that takes the prompt per call.
   - Rejected: keep the constructor-injected prompt and resolve the analyzer fresh
     per project via DI. That implies one DI registration per project, which is
     incompatible with the dynamic project list.

**Placeholder substitution timing.** Per ADR
`normalize-internal-ai-fields-to-configured-language`, `{OUTPUT_LANGUAGE}` is the only
placeholder, applied once when `PromptsOptions.ReadPrompt` reads the file. With this
ADR:

- For the analyzer prompt only, substitution happens **per project, per analysis
  call** in `ProjectPromptBuilder`. Both `{OUTPUT_LANGUAGE}` and `{CATEGORIES}` are
  substituted at the same time using `Project.OutputLanguageName` and
  `Project.Categories`. The configured global `AiOptions.Normalization.TargetLanguageName`
  is no longer used for the analyzer (it stays as the default value used by
  `ProjectsService.CreateAsync` when `OutputLanguageName` is omitted from the
  request).
- For all other prompts (`event_classifier.txt`, `event_summary_updater.txt`,
  `contradiction_detector.txt`, `haiku_*.txt`, `telegram.txt`, `generator.txt`),
  the existing global `PromptsOptions.ReadPrompt` mechanism with the global
  `AiOptions.Normalization.TargetLanguageName` value is unchanged. Per the recorded
  product decision, only the analyzer becomes per-project on this iteration.

The `Project.OutputLanguage` field is therefore consumed **only by the analyzer
worker** today. The other workers continue to use the global value. This is a
deliberate v1 boundary — extending per-project language to the other prompts is a
future ADR.

**Article repository.** `IArticleRepository.GetPendingAsync(int batchSize, ...)`
keeps its global signature but the SELECT now also returns `ProjectId` so the worker
can route per-article to the correct project's prompt without an extra query per
article. No new "by project" overload is needed for this worker.

#### G.3 PublicationGenerationWorker

Reads `Publication` rows. Each `Publication.ProjectId` is set at create time (the
controller is project-scoped, the publication inherits from the route). Worker
behavior unchanged beyond adding `ProjectId` to the entity / SQL.

The Telegram content generator (`ClaudeContentGenerator`) is **not** project-scoped
in this iteration — the prompt remains the global `telegram.txt` plus the
`PublishTarget.SystemPrompt`. The only change downstream is that the publication row
itself carries `ProjectId` for read-side filtering and cost attribution.

#### G.4 PublishingWorker

Reads `Publication` rows (`Status = ContentReady`) and pushes them to the
`PublishTarget`. No logic change — both rows carry `ProjectId`, but the worker only
cares about the publish-target identifier and the content. Filter scoping in the read
queries is what gains the `ProjectId` predicate.

---

### H. UI changes (high-level)

Detailed wireframes are out of scope. The high-level surface:

- **Project switcher in the shell layout.** Top-bar dropdown bound to a TanStack
  Query for `GET /projects`. Selection is persisted in `localStorage` as
  `selectedProjectId` and exposed via a Zustand slice (`useProjectStore`).
- **React Router segment.** The router config gains a `:projectId` segment between
  the root and every existing scoped route. The shell layout reads `useParams()`,
  validates against the projects list, and redirects to
  `/projects/<defaultId>/articles` if `:projectId` is missing or unknown.
- **Redirect when no project is selected.** On root visit (`/`), redirect to
  `/projects/<persisted-or-Default-id>/articles`.
- **TanStack Query cache invalidation.** Switching project must invalidate every
  scoped query. Implement via a cache key prefix:
  `['project', projectId, 'articles', filters]` — `queryClient.invalidateQueries({
  queryKey: ['project', oldProjectId] })` runs on switch. Alternatively, the project
  id flows into every hook factory and React Query naturally rekeys; either pattern
  works, the implementer picks one and applies it consistently.
- **OpenAPI client regeneration.** Once the Api routes are reshaped, run
  `npm run generate-api` from `UI/`. Every generated method gains a `projectId`
  parameter as the leading positional arg. A wrapper layer in `UI/src/api/`
  (non-generated) injects `useProjectStore.getState().selectedProjectId` so feature
  hooks can call `articlesApi.list({ page, pageSize })` instead of repeating
  `projectId` everywhere.
- **`AdminRoute` guard for `/projects/*` admin routes** (project CRUD page) reuses
  the existing role-guard pattern.

---

### I. Edge cases and risks

1. **Project deletion vs publish-target attachments.** Hard `DELETE /projects/{id}`
   fails with the FK RESTRICT until every publish target is reassigned or deleted.
   The `ProjectsController.Delete` endpoint catches the underlying Postgres FK error
   in `ProjectService` and rethrows as
   `InvalidOperationException("Project has N publish targets / sources / events;
   archive instead.")` → `409 Conflict`. UI surfaces "Archive (set inactive)" as the
   primary action and "Delete" only when the project has no children.

2. **Changing a `Source.ProjectId` after the fact.** **Forbidden in v1.** The PUT
   endpoint for sources does not include `ProjectId` in the request DTO; the
   `SourcesController.Update` request shape stays as-is. Rationale: re-attaching a
   source to a different project would orphan its existing `Article` rows
   (semantically: those articles still belong to the old project). Supporting this
   correctly requires either (a) bulk-rewriting `Article.ProjectId` for every
   existing article from that source — destructive and surprising; or (b) fork: a
   new source row in the new project, leaving the old one. Option (b) is what an
   operator can already do manually. Save complexity for when the use case is real.

3. **Duplicate sources across projects.** When `POST projects/{a}/sources` with
   `Url = X` succeeds and the same `Url` already exists in another project, **no
   warning is raised.** This is the deliberate product decision (duplication is
   accepted). UI may render a soft hint in a follow-up iteration; not in scope here.

4. **The `Default` project as a special row.** Two safeguards:
   - `ProjectService.DeleteAsync(ProjectConstants.DefaultProjectId)` throws
     `InvalidOperationException("The Default project cannot be deleted")`.
   - `ProjectService.UpdateActiveAsync(ProjectConstants.DefaultProjectId, false)`
     is allowed but logs a warning — operators may want to retire it once they have
     a real project, but bricking the only project at startup is an awkward state.

5. **Testing.**
   - **Fixtures with multiple projects.** Integration tests create at least two
     projects and assert that scoped queries return only the target project's rows.
   - **Isolation in integration tests.** Existing tests that use the global Default
     project continue to work (Default still exists). New tests that exercise tenant
     boundaries spin up their own project rows; `IProjectContext` is mockable
     because it's an interface with a small surface.
   - **The action filter** is unit-tested in isolation by mocking
     `IProjectRepository` and asserting the right `IActionResult` short-circuit on
     each branch.

6. **Dapper mapping of `Project.Categories` (text[]).** Postgres `text[]` is
   handled out-of-the-box by Npgsql / Dapper for `string[]` parameters. The codebase
   already uses this for `articles."Tags"`. No type handler addition needed.

---

### J. Phasing recommendation

**Single PR.** The change is invasive at the schema level (every operational table
gains `ProjectId NOT NULL`), and intermediate states are worse than either end:

- A schema with `ProjectId` columns but the API still ignoring it would silently let
  operators write rows attributed to the wrong project.
- An API rerouted to `projects/{projectId}/...` without a backfill in the database
  would 500 on every read.

The minimum viable scope for the first release is:

1. The migration script (creates `projects`, inserts Default, adds FKs to all five
   scoped tables — `sources`, `articles`, `events`, `publish_targets`, `publications` —
   and adds the indexes from §D).
2. `IProjectContext` + the action filter + the `ProjectsController`.
3. Repository interfaces and SQL constants updated to take and apply `projectId` on
   every read and write of the six scoped tables.
4. Worker changes (analyzer worker reads project; other workers carry `ProjectId`
   through; `AiCallContext` gains `ProjectId`).
5. UI changes (project switcher + route segment + cache invalidation + OpenAPI
   regeneration).

What is **deferred** (explicit non-goals for this ADR's PR):

- Per-project `event_classifier.txt`, `event_summary_updater.txt`,
  `contradiction_detector.txt`, `haiku_*.txt`, `telegram.txt`, `generator.txt`.
- Per-project model selection (`Project.AnalyzerModel` etc.).
- A `byProject[]` breakdown on the `ai-operations/metrics` endpoint (even though the
  data is now there).
- Source-URL duplicate warning across projects.
- Per-project user ACL / `UserProject` table.
- Soft-delete for projects beyond `IsActive` (no `DeletedAt` column).

---

## Consequences

**Positive:**

- Hard tenancy boundary for every operational query and write. The
  `ProjectId = @projectId` predicate becomes a load-bearing part of every list /
  paged query, eliminating cross-topic leakage in the kNN classifier and editor
  views.
- Analyzer prompt and category list are now data, edited via the UI without a
  redeploy. Every project chooses its own categorical taxonomy.
- The migration is forward-only and idempotent; existing data is preserved
  unchanged, attached to the bootstrap Default project.
- The `IProjectContext` scoped service keeps controller signatures clean — no
  `Guid projectId` parameter clutter on every action method.

**Negative / risks:**

- Every existing scoped controller route changes. Anyone bookmarking a URL or
  hitting the API directly with the old shape (`/articles/...`) gets a 404 after
  this ships. Frontend OpenAPI regeneration must run as part of the same release.
- The new action filter runs `ExistsAsync` on every scoped request. Cheap but not
  free — typical added latency is sub-millisecond; the WHERE-by-PK is index-bound.
- The duplication of sources across projects (when intentionally created) means
  duplicate fetches and duplicate AI analysis spend. This is the recorded product
  trade-off but should be visible on the AI Operations dashboard.
- The HNSW vector index on `events."Embedding"` does not project-partition. Worst
  case (one project dominates the active-event population) the kNN probe returns
  mostly other-project candidates that the WHERE-filter then discards. Mitigation
  belongs to a future partitioning ADR.
- Embedding the analyzer prompt verbatim into the migration SQL is a small
  duplication of the file; the file remains in the repo for now (still used by the
  bootstrap), but on-disk and in-DB copies can drift. **Resolution:** delete
  `Infrastructure/AI/Prompts/analyzer.txt` from the repository in the same PR
  immediately after the migration runs successfully — the file no longer has a
  caller after the analyzer worker switches to `Project.AnalyzerPromptText`.

**Files affected (high-level — implementer's planner produces the per-step list):**

- **New:**
  - `Core/DomainModels/Project.cs` (entity + `ProjectConstants.DefaultProjectId`)
  - `Core/Interfaces/Repositories/IProjectRepository.cs`
  - `Core/Interfaces/Services/IProjectService.cs`
  - `Core/Interfaces/IProjectContext.cs`
  - `Infrastructure/Persistence/Sql/0007_introduce_projects.sql`
  - `Infrastructure/Persistence/Entity/ProjectEntity.cs`
  - `Infrastructure/Persistence/Mappers/ProjectMapper.cs`
  - `Infrastructure/Persistence/Repositories/ProjectRepository.cs`
  - `Infrastructure/Persistence/Repositories/Sql/ProjectSql.cs`
  - `Infrastructure/AI/ProjectPromptBuilder.cs`
  - `Infrastructure/Services/ProjectService.cs`
  - `Api/Controllers/ProjectsController.cs`
  - `Api/Models/ProjectDtos.cs`
  - `Api/Mappers/ProjectMapper.cs` (Domain → DTO)
  - `Api/Validators/CreateProjectRequestValidator.cs`,
    `Api/Validators/UpdateProjectRequestValidator.cs`
  - `Api/Filters/RequireProjectAttribute.cs` (action filter)
  - `Api/ProjectContext/ProjectContextService.cs` (scoped impl of `IProjectContext`)

- **Modified — Core:**
  - `Source.cs`, `Article.cs`, `Event.cs`, `PublishTarget.cs`, `Publication.cs` —
    add `Guid ProjectId { get; set; }`.
    `AiRequestLog.cs` is **not modified** — `ai_request_log` stays global.
  - `IEventRepository.FindSimilarEventsAsync` — leading `Guid projectId` parameter.
  - `IArticleAnalyzer.AnalyzeAsync` — accept `string systemPrompt` overload; drop
    constructor-injected `_prompt` from the implementations.

- **Modified — Infrastructure:**
  - All entity classes mirror the new `ProjectId` column.
  - All mappers (`ToEntity` / `ToDomain`) round-trip the column.
  - All `*Sql.cs` constants for the six scoped tables — `INSERT` lists gain
    `"ProjectId"`, `SELECT` projections gain `"ProjectId"`, list/count queries gain
    `WHERE "ProjectId" = @projectId`.
  - `EventSql.FindSimilarEvents` — gains `AND "ProjectId" = @projectId`.
  - `EventSql.GetActiveEvents`, `GetPagedAsync` — same predicate, plus uses the new
    composite index.
  - `SourceSql.GetActive` — `JOIN projects p ON p."Id" = s."ProjectId" AND
    p."IsActive" = TRUE`.
  - `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` —
    register `IProjectRepository` (Scoped), `IProjectService` (Scoped),
    `IProjectContext` (Scoped); rework the `IArticleAnalyzer` registration to drop
    the constructor-injected analyzer prompt.

- **Modified — Api:**
  - All scoped controllers (`ArticlesController`, `EventsController`,
    `SourcesController`, `PublishTargetsController`, `PublicationsController`,
    `AiOperationsController`) — `[Route("...")]` reroute, MVC convention adds the
    `RequireProjectAttribute`.
  - The OpenAPI / Swagger doc regeneration is automatic — no per-controller change
    required.
  - `Program.cs` — register the MVC convention that attaches `RequireProjectAttribute`
    to controllers whose route template begins with `projects/{projectId:guid}`.

- **Modified — Worker:**
  - `SourceFetcherWorker` — read project-active sources only; pass `source.ProjectId`
    when creating articles.
  - `ArticleAnalysisWorker` — load `Project` per article (cached per batch); call
    `ProjectPromptBuilder`; pass the substituted prompt into the analyzer.
  - `PublicationGenerationWorker`, `PublishingWorker` — pass `ProjectId` through to
    new rows (no logic change).

- **Modified — UI** (separate area; planner's responsibility — listed at high level
  in §H):
  - Shell layout, router config, project switcher, store, generated API client.

- **Deleted:**
  - `Infrastructure/AI/Prompts/analyzer.txt` — once Default project's
    `AnalyzerPromptText` is verified populated and the analyzer worker no longer
    reads the file. The implementer removes it in the same PR.

- **Untouched:**
  - All other `.txt` prompts (`event_classifier.txt`, `event_summary_updater.txt`,
    `contradiction_detector.txt`, `haiku_key_facts.txt`, `haiku_event_title.txt`,
    `telegram.txt`, `generator.txt`).
  - `users` table, `UserRole` enum, all auth.
  - `IUnitOfWork`, `IDbConnectionFactory`, `ExceptionMiddleware`, `BaseController`,
    `PromptsOptions` (its substitution mechanism is reused by the new
    `ProjectPromptBuilder`, but the class itself is unchanged).

---

## Implementation Notes

### Order of work (each step leaves the build / DB green)

1. **Core layer first.** Add `Project.cs`, `IProjectRepository`, `IProjectService`,
   `IProjectContext`, `ProjectConstants.DefaultProjectId`. Add `Guid ProjectId` to
   each existing domain model in the six affected aggregates. Project still builds.
2. **Migration script.** Author `0007_introduce_projects.sql`. Run against a local
   Postgres copy, verify Default exists, every backfill column is populated, every
   FK and index exists. The test suite is still red because the SQL constants
   still reference the old column shapes.
3. **Repository SQL constants and methods.** Update every `*Sql.cs` for the six
   tables. Update repository `*.cs` files to thread `ProjectId` through inserts and
   `projectId` through reads. `FindSimilarEventsAsync` gains the `projectId`
   parameter. Build green again.
4. **ProjectRepository, ProjectService, ProjectMapper.** Implement and register.
5. **Action filter and IProjectContext.** Implement, wire via MVC convention in
   `Program.cs`.
6. **Reroute scoped controllers.** Update `[Route]` attributes; nothing else
   changes in the controllers themselves (they consume `IProjectContext` via DI
   when they need the id, e.g. when creating new entities).
7. **ProjectsController + DTOs + validators.** Implement.
8. **AI analyzer rewiring.** `IArticleAnalyzer.AnalyzeAsync(article, systemPrompt,
   ct)` overload; `ProjectPromptBuilder`; `ArticleAnalysisWorker` rewires.
9. **Workers carry `ProjectId` through.** `SourceFetcherWorker`,
   `PublicationGenerationWorker`, `PublishingWorker`. `analyzer.txt` deleted.
   `AiCallContext` / `AiRequestLogger` are **not changed** — `ai_request_log` stays
   global.
10. **Tests** (delegated to test-writer per the standard cycle): `ProjectService`
    business rules, `RequireProjectAttribute` filter branches, `ProjectRepository`
    Dapper round-trip, `EventRepository.FindSimilarEventsAsync` with project
    predicate, end-to-end `WebApplicationFactory` test on a scoped controller
    asserting cross-project isolation.
11. **UI changes** (separate area, see §H): switcher, route segment, OpenAPI
    regeneration, query-cache invalidation.

### Skills `feature-planner` must consult

- `.claude/skills/code-conventions/SKILL.md` — domain model conventions
  (`init` for identity, `set` for mutable, `[]` for collections,
  `DateTimeOffset` for timestamps, enums co-located with the owning model). Layer
  boundaries (no Dapper in `Core/`, no `IDbConnectionFactory` in controllers, no
  inline DTO construction). Worker structure (three-level
  `ExecuteAsync` → `ProcessAsync` → `ProcessXxxAsync`, scoped services resolved
  inside `ProcessAsync`). Options pattern stays the same — the per-project
  `OutputLanguage` does NOT become an `Options` entry; it's a column on `Project`.
- `.claude/skills/api-conventions/SKILL.md` — `[Route("…")]` template format
  (no `/api/` prefix), `[Authorize(Roles = nameof(...))]` syntax, `BaseController`
  inheritance for authenticated controllers, FluentValidation per-request
  validator naming, pagination guard, DTO records in `Api/Models/`. Specifically
  for the rerouted controllers: the route template now contains
  `projects/{projectId:guid}/...` but the action methods do not declare
  `Guid projectId` as a parameter — the action filter reads it from
  `RouteValues` and the controller obtains it via `IProjectContext` if needed.
- `.claude/skills/dapper-conventions/SKILL.md` — every new SQL constant lives in
  `Infrastructure/Persistence/Repositories/Sql/{Aggregate}Sql.cs`, primary
  constructor on the new `ProjectRepository(IDbConnectionFactory factory,
  IUnitOfWork uow)`, `await using var conn = await factory.CreateOpenAsync(ct)`
  per call, every `Dapper` call wrapped in `CommandDefinition`. The migration
  script naming follows the `NNNN_…` pattern (next free number is `0007` after
  `0006_backfill_gemini_flash_cost.sql`). PostgreSQL `text[]` round-trips as
  `string[]` natively — no type handler change needed (mirrors `articles."Tags"`).
- `.claude/skills/mappers/SKILL.md` — two mapper files for `Project`: one in
  `Infrastructure/Persistence/Mappers/ProjectMapper.cs` for Entity ↔ Domain,
  another in `Api/Mappers/ProjectMapper.cs` for Domain → DTO. Static class with
  static extension methods, `ToEntity` / `ToDomain` / `ToDto` /
  `ToListItemDto` / `ToDetailDto`. No I/O, no logging in mappers.
- `.claude/skills/clean-code/SKILL.md` — the `ProjectConstants.DefaultProjectId`
  constant exists precisely so no test or seed code repeats the literal GUID;
  the bootstrap value lives in one place. Project-id-routing logic must not be
  duplicated across controllers — that's why the action filter exists. Avoid
  string-concatenation building of the analyzer prompt across multiple
  classes — confine it to `ProjectPromptBuilder`.
- `.claude/skills/testing/SKILL.md` — AAA, NUnit + Moq + FluentAssertions; the
  project-fixture test pattern that creates two projects and asserts isolation
  is the right shape for the integration tests.

### Out of scope (do not let scope creep in)

- Per-project authorization. (Recorded product decision.)
- Per-project event/contradiction/key-facts/title/telegram prompts. (Only the
  analyzer prompt becomes per-project in this iteration.)
- Per-project model selection (`AnalyzerModel`, `ClassifierModel`, etc.) on
  `Project`. Future ADR if needed.
- Cross-project source URL warning.
- A `byProject[]` breakdown on the AI Operations metrics endpoint. (Data is
  there; UI surface is a separate ADR.)
- Soft-delete with `DeletedAt`. `IsActive` is sufficient for v1.
- Migrating cross-channel publications between projects, or cross-project event
  merging. Existing `MergeAsync` only merges within a single project — the
  service should fail-fast if the two events have different `ProjectId`.
