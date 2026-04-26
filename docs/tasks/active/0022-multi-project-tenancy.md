# Multi-Project Tenancy

## Goal
Introduce `Project` as a hard tenant root so that Sources, Articles, Events,
PublishTargets, and Publications are isolated per project, enabling multiple
thematically distinct news streams to run on a single NewsParser instance.

## Affected Layers
- Core / Infrastructure / Api / Worker / UI

## ADR
`docs/architecture/decisions/0022-multi-project-tenancy.md`

---

## Tasks

### Phase 1 — Core: domain models and interfaces

- [x] **Create `Core/DomainModels/Project.cs`** — domain model with all eight properties
      from ADR §A: `Id` (`Guid`, `init`), `Name`, `Slug`, `AnalyzerPromptText`,
      `Categories` (`List<string>`), `OutputLanguage`, `OutputLanguageName`, `IsActive`
      (`bool`, default `true`), `CreatedAt` (`DateTimeOffset`, `init`).
      Co-locate `ProjectConstants` static class in the same file with one constant:
      `public static readonly Guid DefaultProjectId = new("00000000-0000-0000-0000-000000000001")`.
      _Acceptance: file compiles in `Core`; zero Infrastructure or Api references;
      `dotnet build Core` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/DomainModels/Source.cs`** — add `public Guid ProjectId { get; set; }`
      property.
      _Acceptance: `dotnet build Core` green; property is present and settable_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/DomainModels/Article.cs`** — add `public Guid ProjectId { get; set; }`
      property.
      _Acceptance: `dotnet build Core` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/DomainModels/Event.cs`** — add `public Guid ProjectId { get; set; }`
      property.
      _Acceptance: `dotnet build Core` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/DomainModels/PublishTarget.cs`** — add
      `public Guid ProjectId { get; set; }` property.
      _Acceptance: `dotnet build Core` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/DomainModels/Publication.cs`** — add
      `public Guid ProjectId { get; set; }` property.
      _Acceptance: `dotnet build Core` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/Interfaces/Repositories/IProjectRepository.cs`** — interface with
      eight method signatures exactly as specified in ADR §A:
      `GetByIdAsync`, `GetAllAsync`, `GetBySlugAsync`, `ExistsAsync`, `CreateAsync`,
      `UpdateAsync`, `UpdateActiveAsync`, `DeleteAsync`. No implementation.
      _Acceptance: interface compiles in `Core`; no Infrastructure references;
      `dotnet build Core` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/Interfaces/Services/IProjectService.cs`** — interface with methods
      wrapping the repository: `GetAllAsync`, `GetByIdAsync`, `GetBySlugAsync`,
      `CreateAsync(Project project, CancellationToken ct)`,
      `UpdateAsync(Project project, CancellationToken ct)`,
      `UpdateActiveAsync(Guid id, bool isActive, CancellationToken ct)`,
      `DeleteAsync(Guid id, CancellationToken ct)`.
      _Acceptance: `dotnet build Core` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/Interfaces/IProjectContext.cs`** — interface with three members:
      `Guid ProjectId { get; }`, `Project Current { get; }`, `bool IsSet { get; }`.
      _Acceptance: `dotnet build Core` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/Interfaces/AI/IArticleAnalyzer.cs`** — add overload
      `Task<ArticleAnalysisResult> AnalyzeAsync(Article article, string systemPrompt,
      CancellationToken cancellationToken = default)`.
      Keep the original single-parameter overload signature present so implementations
      can satisfy it (it will be removed or delegated in Phase 5).
      _Acceptance: `dotnet build Core` green; both signatures present_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/Interfaces/Repositories/IEventRepository.cs`** — change
      `FindSimilarEventsAsync` signature to add leading `Guid projectId` parameter:
      `Task<List<(Event Event, double Similarity)>> FindSimilarEventsAsync(
      Guid projectId, float[] embedding, double threshold, int windowHours, int maxTake,
      CancellationToken cancellationToken = default)`.
      Also add `Guid projectId` as the first parameter to `GetActiveEventsAsync`,
      `GetPagedAsync`, and `CountAsync` so scoped queries can be threaded through.
      _Acceptance: `dotnet build Core` green; all callers in Infrastructure and Worker
      are now compile-broken (expected — resolved in Phase 3)_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 2 — Infrastructure: migration script

- [x] **Create `Infrastructure/Persistence/Sql/0007_introduce_projects.sql`** — DbUp
      forward-only migration script. Content must include in this exact order:
      1. `CREATE TABLE IF NOT EXISTS projects` with columns matching ADR §A:
         `"Id"` UUID PK, `"Name"` TEXT NOT NULL, `"Slug"` TEXT NOT NULL,
         `"AnalyzerPromptText"` TEXT NOT NULL, `"Categories"` TEXT[] NOT NULL DEFAULT `'{}'`,
         `"OutputLanguage"` TEXT NOT NULL DEFAULT `'uk'`,
         `"OutputLanguageName"` TEXT NOT NULL DEFAULT `'Ukrainian'`,
         `"IsActive"` BOOLEAN NOT NULL DEFAULT TRUE,
         `"CreatedAt"` TIMESTAMPTZ NOT NULL.
      2. `CREATE UNIQUE INDEX "IX_projects_Slug" ON projects ("Slug")` — if not exists.
      3. `INSERT INTO projects (...) VALUES ('00000000-0000-0000-0000-000000000001',
         'Default', 'default', <analyzer.txt body verbatim inside $PROMPT$...$PROMPT$
         dollar-quoting, with `{CATEGORIES}` placeholder added before the CATEGORY list>,
         ARRAY['Politics','Economics','Technology','Sports','Culture','Science','War',
         'Society','Health','Environment'], 'uk', 'Ukrainian', TRUE, now())
         ON CONFLICT ("Id") DO NOTHING`.
      4. Three-step backfill for each of the five tables (`sources`, `articles`, `events`,
         `publish_targets`, `publications`) as specified in ADR §D:
         (a) `ALTER TABLE <t> ADD COLUMN "ProjectId" UUID NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'`;
         (b) `ALTER TABLE <t> ALTER COLUMN "ProjectId" DROP DEFAULT`;
         (c) `ALTER TABLE <t> ADD CONSTRAINT "FK_<t>_projects_ProjectId" FOREIGN KEY
             ("ProjectId") REFERENCES projects ("Id") ON DELETE RESTRICT`.
      5. `DROP INDEX IF EXISTS "IX_sources_Url"`.
      6. `CREATE UNIQUE INDEX "IX_sources_ProjectId_Url" ON sources ("ProjectId", "Url")`.
      7. `CREATE INDEX "IX_articles_ProjectId_Status_ProcessedAt" ON articles
         ("ProjectId", "Status", "ProcessedAt" DESC)`.
      8. `CREATE INDEX "IX_events_ProjectId_Status_LastUpdatedAt" ON events
         ("ProjectId", "Status", "LastUpdatedAt" DESC)`.
      9. `CREATE INDEX "IX_publications_ProjectId_Status_CreatedAt" ON publications
         ("ProjectId", "Status", "CreatedAt" DESC)`.
      _Acceptance: script applied against a local Postgres instance via DbUp without
      error; `SELECT * FROM projects` returns one Default row; every scoped table has
      `ProjectId` NOT NULL with FK to `projects`; all indexes exist; script is idempotent
      (second run skips due to `schemaversions` tracking)_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

---

### Phase 3 — Infrastructure: entities, SQL constants, repositories

- [x] **Create `Infrastructure/Persistence/Entity/ProjectEntity.cs`** — Dapper binding
      class with eight properties matching `Project` domain model: `Id` (`Guid`),
      `Name` (`string`), `Slug` (`string`), `AnalyzerPromptText` (`string`),
      `Categories` (`string[]`), `OutputLanguage` (`string`),
      `OutputLanguageName` (`string`), `IsActive` (`bool`), `CreatedAt` (`DateTimeOffset`).
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Entity/SourceEntity.cs`** — add
      `public Guid ProjectId { get; set; }` property.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Entity/ArticleEntity.cs`** — add
      `public Guid ProjectId { get; set; }` property.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Entity/EventEntity.cs`** — add
      `public Guid ProjectId { get; set; }` property.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Entity/PublishTargetEntity.cs`** — add
      `public Guid ProjectId { get; set; }` property.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Entity/PublicationEntity.cs`** — add
      `public Guid ProjectId { get; set; }` property.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Mappers/SourceMapper.cs`** — add
      `ProjectId = entity.ProjectId` in `ToDomain` and `ProjectId = domain.ProjectId`
      in `ToEntity`.
      _Acceptance: mapper round-trips `ProjectId`; `dotnet build Infrastructure` green_
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Mappers/ArticleMapper.cs`** — add
      `ProjectId` to `ToDomain`, `ToEntity`, and `FromAnalysisResult` (inheriting from
      `pendingArticle.ProjectId` in `FromAnalysisResult`).
      _Acceptance: mapper round-trips `ProjectId`; `dotnet build Infrastructure` green_
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Mappers/EventMapper.cs`** — add
      `ProjectId` to `ToDomain` and `ToEntity`.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Mappers/PublishTargetMapper.cs`** — add
      `ProjectId` to `ToDomain` and `ToEntity`.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Mappers/PublicationMapper.cs`** — add
      `ProjectId` to `ToDomain` and `ToEntity`.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Create `Infrastructure/Persistence/Repositories/Sql/ProjectSql.cs`** — internal
      static class with constants:
      `GetById` — `SELECT` all nine columns `WHERE "Id" = @id LIMIT 1`;
      `GetAll` — `SELECT` all nine columns `ORDER BY "Name"`;
      `GetBySlug` — `SELECT` all nine columns `WHERE "Slug" = @slug LIMIT 1`;
      `Exists` — `SELECT EXISTS(SELECT 1 FROM projects WHERE "Id" = @id)`;
      `Insert` — `INSERT INTO projects (all nine columns) VALUES (all nine params)`;
      `Update` — `UPDATE projects SET "Name"=@Name, "Slug"=@Slug,
      "AnalyzerPromptText"=@AnalyzerPromptText, "Categories"=@Categories,
      "OutputLanguage"=@OutputLanguage, "OutputLanguageName"=@OutputLanguageName,
      "IsActive"=@IsActive WHERE "Id"=@Id`;
      `UpdateActive` — `UPDATE projects SET "IsActive"=@isActive WHERE "Id"=@id`;
      `Delete` — `DELETE FROM projects WHERE "Id"=@id`.
      _Acceptance: `dotnet build Infrastructure` green; all constants are `internal static`_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/Sql/SourceSql.cs`** — update
      `GetActive` to add `JOIN projects p ON p."Id" = s."ProjectId" AND p."IsActive" = TRUE`
      and add alias prefix `s.` to all selected columns; alias sources table as `s`.
      Update `GetAll` and `GetById` to include `"ProjectId"` in the SELECT column list.
      Update `Insert` to include `"ProjectId"` in column list and `@ProjectId` in values.
      Update `UpdateFields` to not include `ProjectId` (FK is immutable after insert).
      Add `ExistsByProjectAndUrl`: `SELECT EXISTS(SELECT 1 FROM sources WHERE
      "ProjectId" = @projectId AND "Url" = @url)` — replaces the global `ExistsByUrl`
      for the scoped service; keep `ExistsByUrl` for backward compat during transition
      or remove it if confirmed unused after this phase.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/Sql/ArticleSql.cs`** — add
      `"ProjectId"` to every `SELECT` column list that returns full article rows
      (`GetById`, `GetAnalysisDoneWithSearch`, `GetAnalysisDoneWithoutSearch`,
      `GetPending`, `GetPendingForClassification`, `GetRecentTitlesForDeduplication`).
      Add `"ProjectId"` to `Insert` column list and `@ProjectId` to values.
      Add `AND "ProjectId" = @projectId` predicate to `GetAnalysisDoneWithSearch`,
      `GetAnalysisDoneWithoutSearch`, `CountAnalysisDoneWithSearch`,
      `CountAnalysisDoneWithoutSearch` queries (these are called from scoped controllers).
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/Sql/EventSql.cs`** — add
      `"ProjectId"` to `EventColumns` private constant and to `Insert` column list /
      values. Add `AND "ProjectId" = @projectId` predicate to `FindSimilarEvents`,
      `GetActiveEvents`, `GetPagedWithSearch`, `GetPagedWithoutSearch`,
      `CountWithSearch`, `CountWithoutSearch`. Update `GetArticlesByEventId` and
      `GetUnpublishedUpdateArticles` to include `"ProjectId"` in article SELECT lists.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/Sql/PublishTargetSql.cs`** — add
      `"ProjectId"` to every `SELECT` column list (`GetAll`, `GetActive`, `GetById`),
      to `Insert` column list and values, and to `Update` SET clause (so a future
      update that changes target configuration preserves `ProjectId` — but do NOT
      allow updating `ProjectId` itself; keep it immutable via the service layer).
      Add `GetAllByProject`: `SELECT ... FROM publish_targets WHERE "ProjectId" = @projectId
      ORDER BY "Name"` — used by the scoped controller.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/Sql/PublicationSql.cs`** — add
      `"ProjectId"` to the `PublicationColumns` private constant and to `Insert` column
      list / values. Add `AND p."ProjectId" = @projectId` predicate to `GetAll`
      and `CountAll` (these serve the paged list endpoint). Update `GetPendingForGeneration`
      and `GetPendingForPublish` to include `p."ProjectId"` in the selected columns (workers
      need it for any future project-level routing, even if they do not filter by it today).
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/ArticleRepository.cs`** — add
      `Guid projectId` as the first parameter to `GetAnalysisDoneAsync` and
      `CountAnalysisDoneAsync`; thread `projectId` into the corresponding SQL calls.
      Ensure `AddAsync` passes `article.ProjectId` via the entity.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/EventRepository.cs`** — update
      `FindSimilarEventsAsync` signature to match the new interface (leading `Guid
      projectId` parameter); thread `projectId` into the SQL. Update `GetActiveEventsAsync`,
      `GetPagedAsync`, and `CountAsync` signatures to add leading `Guid projectId` and
      thread it into SQL. Update `CreateAsync` to include `ProjectId` from the domain
      model. Fix the `CreateNewEvent` instantiation in the worker (see Phase 7).
      _Acceptance: `dotnet build Infrastructure` green; interface is satisfied_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/SourceRepository.cs`** — update
      `GetActiveAsync` to use the new JOIN-based `SourceSql.GetActive`; update `GetAllAsync`
      and `GetByIdAsync` to return `ProjectId`; update `AddAsync` to pass `source.ProjectId`
      via the entity.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/PublishTargetRepository.cs`** — add
      `Guid projectId` parameter to `GetAllAsync` (rename the method or add an overload
      consistent with `IPublishTargetRepository`); update `GetAllByProjectAsync` to use
      `PublishTargetSql.GetAllByProject`; update `AddAsync` to pass `ProjectId` via the entity.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/PublicationRepository.cs`** — update
      `GetPagedAsync` / `CountAsync` to accept leading `Guid projectId` and thread it to SQL;
      update `AddAsync` to pass `publication.ProjectId` via the entity.
      _Acceptance: `dotnet build Infrastructure` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

---

### Phase 4 — Infrastructure: Project repository, service, and mapper

- [x] **Create `Infrastructure/Persistence/Repositories/ProjectRepository.cs`** — internal
      class implementing `IProjectRepository`. Primary constructor:
      `ProjectRepository(IDbConnectionFactory factory, IUnitOfWork uow)`.
      Each read method: `await using var conn = await factory.CreateOpenAsync(ct)`,
      Dapper `QuerySingleOrDefaultAsync` / `QueryAsync` with `CommandDefinition`.
      `CreateAsync` and `UpdateAsync` use `uow` with `ExecuteAsync`.
      `DeleteAsync` wraps in a try/catch on Npgsql FK violation (error code `23503`)
      and rethrows as `InvalidOperationException("Project has children; archive instead.")`.
      _Acceptance: satisfies `IProjectRepository`; no raw SQL in the class body (all SQL
      in `ProjectSql`); `dotnet build Infrastructure` green_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Create `Infrastructure/Services/ProjectService.cs`** — internal class implementing
      `IProjectService`. Constructor signature:
      `ProjectService(IProjectRepository repository, ILogger<ProjectService> logger)`.
      Rules enforced:
      `CreateAsync`: derives `Slug` from `Name` (lowercase, spaces → hyphens) if `Slug`
      is null/empty; calls `IProjectRepository.GetBySlugAsync` to check uniqueness and
      throws `InvalidOperationException("Slug already in use")` on collision.
      Sets `Id = Guid.NewGuid()`, `CreatedAt = DateTimeOffset.UtcNow`.
      `DeleteAsync`: throws `InvalidOperationException("The Default project cannot be
      deleted")` when `id == ProjectConstants.DefaultProjectId`.
      `UpdateActiveAsync(id, false)` when `id == ProjectConstants.DefaultProjectId`:
      logs a warning via `_logger.LogWarning` but proceeds (does not throw).
      All other methods delegate directly to the repository.
      _Acceptance: business rules are enforced; constructor takes `ILogger<ProjectService>`;
      `UpdateActiveAsync(ProjectConstants.DefaultProjectId, false)` logs a warning via
      `_logger.LogWarning`; `dotnet build Infrastructure` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Create `Infrastructure/Persistence/Mappers/ProjectMapper.cs`** — static class
      with extension methods:
      `ToEntity(this Project domain) → ProjectEntity` — maps all nine fields; `Categories`
      maps `List<string>` → `string[]`.
      `ToDomain(this ProjectEntity entity) → Project` — maps all nine fields; `Categories`
      maps `string[]` → `List<string>`.
      No I/O, no logging, no DI.
      _Acceptance: round-trip `domain → entity → domain` preserves all fields; `dotnet
      build Infrastructure` green_
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Modify `Infrastructure/Services/PublicationService.cs`** — constructor-inject
      `IProjectContext projectContext`. In the `CreateAsync` method, after loading the
      article from the repository, add a guard: if `article.ProjectId` does not match
      `_projectContext.Current.ProjectId`, throw
      `InvalidOperationException("Article does not belong to the current project")`.
      This maps to 409 via `ExceptionMiddleware` per the existing exception-handling
      contract (ADR §C, §F.4).
      _Acceptance: `dotnet build Infrastructure` green; creating a publication with a
      cross-project article returns 409_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/Services/EventService.cs`** — at the start of the
      `MergeAsync` method, verify that both events share the same `ProjectId`. If they
      differ, throw `InvalidOperationException("Cannot merge events from different
      projects")`. This maps to 409 via `ExceptionMiddleware`.
      _Acceptance: `dotnet build` (full solution) green; calling merge on two events
      with different `ProjectId` values throws `InvalidOperationException`_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — in
      `AddRepositories`: register `services.AddScoped<IProjectRepository, ProjectRepository>()`.
      In `AddServices`: register `services.AddScoped<IProjectService, ProjectService>()`.
      In `AddAiServices`: remove the constructor-injected `promptsOptions.Analyzer`
      string from `GeminiArticleAnalyzer` instantiation (the analyzer will receive
      the prompt per-call after Phase 5).
      _Acceptance: `dotnet build Infrastructure` green; DI registrations for
      `IProjectRepository` and `IProjectService` present_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 5 — Api: project context, action filter, and MVC convention

- [x] **Create `Api/ProjectContext/ProjectContextService.cs`** — scoped implementation
      of `IProjectContext`. Properties `ProjectId`, `Current`, `IsSet`.
      Expose a `void Set(Project project)` method (internal or package-visible) so the
      action filter can push the resolved project.
      _Acceptance: class implements `IProjectContext`; `dotnet build Api` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Api/Filters/RequireProjectAttribute.cs`** — `IAsyncActionFilter`
      implementing ADR §F.3 behavior:
      1. Read `projectId` from `context.HttpContext.Request.RouteValues["projectId"]`.
      2. Call `IProjectRepository.ExistsAsync(projectId, ct)`.
         If false → short-circuit with `NotFoundObjectResult("Project {id} not found")`.
      3. On write methods (`POST`/`PUT`/`PATCH`/`DELETE`) only:
         call `IProjectRepository.GetByIdAsync(projectId, ct)`;
         if `IsActive == false` → short-circuit with
         `ConflictObjectResult("Project {id} is inactive")`.
      4. On non-write (GET) or after write check passes: call
         `IProjectRepository.GetByIdAsync` (using the cached result from step 3 when
         available); push the resolved `Project` into `IProjectContext.Set(project)`.
      The filter resolves `IProjectRepository` and `IProjectContext` from
      `context.HttpContext.RequestServices`.
      _Acceptance: filter logic matches ADR §F.3 decision table; `dotnet build Api` green_
      _Skill: .claude/skills/api-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Api/Extensions/ApiServiceExtensions.cs`** — in `AddApi`, replace
      `services.AddControllers()` with a call that adds an MVC convention attaching
      `RequireProjectAttribute` to any controller whose route template starts with
      `projects/{projectId:guid}`. Concretely: use `services.AddControllers(options =>
      { options.Conventions.Add(new RequireProjectConvention()); })` and create
      `Api/Filters/RequireProjectConvention.cs` (an `IApplicationModelConvention`) that
      iterates controller route templates and adds `RequireProjectAttribute` to those
      that match `projects/{projectId:guid}`.
      Register `IProjectContext` as scoped:
      `services.AddScoped<IProjectContext, ProjectContextService>()`.
      _Acceptance: convention is registered; `IProjectContext` / `ProjectContextService`
      DI registration is present in `ApiServiceExtensions`; scoped controllers
      automatically receive the filter; global controllers do not; `dotnet build Api` green_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Create `Api/Filters/RequireProjectConvention.cs`** — `IApplicationModelConvention`
      that iterates `ApplicationModel.Controllers`, and for each controller whose route
      template (from `[Route]` attribute) begins with `projects/{projectId:guid}`,
      adds a `RequireProjectAttribute` instance to `controller.Filters`.
      _Acceptance: convention compiles; `dotnet build Api` green_
      _Skill: .claude/skills/api-conventions/SKILL.md_

---

### Phase 6 — Api: reroute scoped controllers

- [x] **Modify `Api/Controllers/ArticlesController.cs`** — change `[Route("articles")]`
      to `[Route("projects/{projectId:guid}/articles")]`. Controller actions do NOT add
      `[FromRoute] Guid projectId` parameters — project context is read from
      `IProjectContext` when needed. Inject `IProjectContext` via primary constructor
      and use `_projectContext.ProjectId` as the leading argument to `GetAnalysisDoneAsync`
      and `CountAnalysisDoneAsync`.
      _Acceptance: `dotnet build Api` green; Swagger shows routes under
      `projects/{projectId}/articles`_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Controllers/EventsController.cs`** — change `[Route("events")]` to
      `[Route("projects/{projectId:guid}/events")]`. Inject `IProjectContext` and thread
      `_projectContext.ProjectId` into `GetPagedAsync`, `CountAsync`, and
      `GetActiveEventsAsync` calls.
      _Acceptance: `dotnet build Api` green; Swagger shows scoped event routes_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Controllers/SourcesController.cs`** — change `[Route("sources")]` to
      `[Route("projects/{projectId:guid}/sources")]`. Inject `IProjectContext`; pass
      `_projectContext.ProjectId` when creating a source via `ISourceService.CreateAsync`.
      _Acceptance: `dotnet build Api` green_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Controllers/PublishTargetsController.cs`** — change
      `[Route("publish-targets")]` to
      `[Route("projects/{projectId:guid}/publish-targets")]`. Inject `IProjectContext`;
      thread `_projectContext.ProjectId` into list and create calls.
      _Acceptance: `dotnet build Api` green_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Controllers/PublicationsController.cs`** — change
      `[Route("publications")]` to
      `[Route("projects/{projectId:guid}/publications")]`. Inject `IProjectContext`;
      thread `_projectContext.ProjectId` into list and create calls.
      _Acceptance: `dotnet build Api` green_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Controllers/AiOperationsController.cs`** — change
      `[Route("ai-operations")]` to
      `[Route("projects/{projectId:guid}/ai-operations")]`. No other behavior changes
      (AI log is global; the route change keeps consistency).
      _Acceptance: `dotnet build Api` green; Swagger shows scoped ai-operations route_
      _Skill: .claude/skills/api-conventions/SKILL.md_

---

### Phase 7 — Api: ProjectsController, DTOs, mapper, validators

- [x] **Create `Api/Models/ProjectDtos.cs`** — single file with four records matching
      ADR §F.2:
      `ProjectListItemDto(Guid Id, string Name, string Slug, bool IsActive)`;
      `ProjectDetailDto(Guid Id, string Name, string Slug, string AnalyzerPromptText,
      List<string> Categories, string OutputLanguage, string OutputLanguageName,
      bool IsActive, DateTimeOffset CreatedAt)`;
      `CreateProjectRequest(string Name, string? Slug, string AnalyzerPromptText,
      List<string> Categories, string OutputLanguage, string OutputLanguageName)`;
      `UpdateProjectRequest(string Name, string AnalyzerPromptText,
      List<string> Categories, string OutputLanguage, string OutputLanguageName, bool IsActive)`.
      _Acceptance: file compiles in `Api`; no Infrastructure references;
      `dotnet build Api` green_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Create `Api/Mappers/ProjectMapper.cs`** — static class with extension methods:
      `ToListItemDto(this Project p) → ProjectListItemDto`;
      `ToDetailDto(this Project p) → ProjectDetailDto`;
      `ToDomain(this CreateProjectRequest r) → Project` — maps all fields, leaves
      `Id` as default (`Guid.Empty` — service sets it); `Slug` defaults to empty string
      if null (service derives it).
      `ApplyUpdate(this UpdateProjectRequest r, Project p) → Project` — returns a
      mutated copy applying the request fields to the existing project.
      No I/O, no logging.
      _Acceptance: `dotnet build Api` green; no inline DTO construction in the controller_
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Create `Api/Validators/CreateProjectRequestValidator.cs`** — `AbstractValidator<CreateProjectRequest>`.
      Rules from ADR §F.2:
      `Name.NotEmpty()`;
      `AnalyzerPromptText.NotEmpty()`;
      `Categories.Must(c => c.Count >= 1).WithMessage("At least one category required")`;
      `OutputLanguage.Length(2, 5)`.
      _Acceptance: `dotnet build Api` green; validator is auto-discovered; invalid
      request returns 400_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Create `Api/Validators/UpdateProjectRequestValidator.cs`** — `AbstractValidator<UpdateProjectRequest>`.
      Same rules as `CreateProjectRequestValidator` applied to the update fields.
      _Acceptance: `dotnet build Api` green; validator auto-discovered_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Create `Api/Controllers/ProjectsController.cs`** — per ADR §F.2:
      `[ApiController]`, `[Route("projects")]`,
      `[Authorize(Roles = nameof(UserRole.Admin))]`, extends `BaseController`.
      Primary constructor: `IProjectService projectService`.
      Endpoints:
      `GET /projects` — returns `Ok(projects.Select(p => p.ToListItemDto()))`.
      `GET /projects/{id:guid}` — returns `Ok(project.ToDetailDto())` or `NotFound()`.
      `POST /projects` — `[FromBody] CreateProjectRequest request`; calls
      `projectService.CreateAsync(request.ToDomain(), ct)`; returns
      `CreatedAtAction(nameof(GetById), new { id }, project.ToDetailDto())`.
      `PUT /projects/{id:guid}` — `[FromBody] UpdateProjectRequest request`; fetches
      existing project; calls `request.ApplyUpdate(existing)` then
      `projectService.UpdateAsync`; returns `Ok(updated.ToDetailDto())`.
      `PATCH /projects/{id:guid}/status` — `[FromBody] bool isActive`; calls
      `projectService.UpdateActiveAsync(id, isActive, ct)`; returns `NoContent()`.
      `DELETE /projects/{id:guid}` — calls `projectService.DeleteAsync(id, ct)`;
      returns `NoContent()`.
      All actions take `CancellationToken cancellationToken = default`.
      _Acceptance: `dotnet build Api` green; Swagger shows all six endpoints under
      `projects` with Admin lock; no JWT returns 401; Editor JWT returns 403_
      _Skill: .claude/skills/api-conventions/SKILL.md_

---

### Phase 8 — Infrastructure: AI analyzer rewiring and ProjectPromptBuilder

- [x] **Modify `Infrastructure/AI/GeminiArticleAnalyzer.cs`** — remove the
      constructor-injected `string prompt` field and parameter. Implement the new
      `IArticleAnalyzer.AnalyzeAsync(Article article, string systemPrompt,
      CancellationToken ct)` overload as the primary implementation using `systemPrompt`
      in place of `_prompt`. Remove or mark the old single-argument overload as
      delegating to the new one with an empty string fallback (to be removed in cleanup
      after Phase 8 is confirmed green). Update usages of `_prompt` inside the method
      body to use the passed-in `systemPrompt` parameter.
      _Acceptance: `GeminiArticleAnalyzer` no longer holds a prompt field; `dotnet build
      Infrastructure` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Create `Infrastructure/AI/ProjectPromptBuilder.cs`** — internal static class
      with a single static method:
      `public static string Build(Project project) → string` that performs:
      `project.AnalyzerPromptText
          .Replace("{OUTPUT_LANGUAGE}", project.OutputLanguageName)
          .Replace("{CATEGORIES}", string.Join(", ", project.Categories))`.
      No I/O, no logging, no DI.
      _Acceptance: class compiles; `Build` for the Default project returns the expected
      substituted prompt string; `dotnet build Infrastructure` green_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — inject `IProjectRepository`
      into the `AnalysisContext` record. In `ProcessAsync`, declare a
      `Dictionary<Guid, Project>` project cache initialized to empty before the
      article loop. In `ProcessArticleAsync`, before calling `ctx.Analyzer.AnalyzeAsync`,
      look up `article.ProjectId` in the cache; if absent, call
      `ctx.ProjectRepository.GetByIdAsync(article.ProjectId, ct)` and cache the result.
      Call `ProjectPromptBuilder.Build(project)` to get `systemPrompt`. Pass
      `systemPrompt` as the second argument to
      `ctx.Analyzer.AnalyzeAsync(article, systemPrompt, ct)`.
      In `ClassifyIntoEventAsync`, pass `article.ProjectId` as the first argument to
      `ctx.EventRepository.FindSimilarEventsAsync`. In `CreateNewEventAsync`, set
      `ProjectId = article.ProjectId` on the new `Event` object.
      _Acceptance: `dotnet build Worker` green; analyzer worker uses per-project prompt_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 9 — Worker: source fetcher, publication workers, delete analyzer.txt

- [x] **Modify `Worker/Workers/SourceFetcherWorker.cs`** — in `ProcessSourceAsync`,
      when constructing new `Article` objects (via the parser output and before calling
      `articleRepository.AddAsync`), set `article.ProjectId = source.ProjectId`.
      No other logic changes.
      _Acceptance: new articles inherit `source.ProjectId`; `dotnet build Worker` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/Workers/PublicationGenerationWorker.cs`** — no logic change
      needed; verify that `publication.ProjectId` is returned by
      `publicationRepository.GetPendingForGenerationAsync` (the SQL was updated in
      Phase 3). If the entity `ProjectId` is not mapped through to the domain model,
      fix the mapper call. Add a log-scope entry `["ProjectId"] = publication.ProjectId`
      alongside the existing `PublicationId` scope entry for observability.
      _Acceptance: `dotnet build Worker` green; `ProjectId` present on Publication
      objects loaded by the worker_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/Workers/PublishingWorker.cs`** — same pattern as
      `PublicationGenerationWorker`: verify `publication.ProjectId` flows from DB through
      mapper to domain. Add `["ProjectId"] = publication.ProjectId` to log scope.
      _Acceptance: `dotnet build Worker` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Delete `Infrastructure/AI/Prompts/analyzer.txt`** — the file is no longer read
      at runtime; its content is now in the `Default` project row in the database.
      Remove it from the repository. Verify no remaining `using` or file-read reference
      to `analyzer.txt` exists in the codebase (search in `PromptsOptions.cs` and
      `InfrastructureServiceExtensions.cs`).
      _Acceptance: file is absent from the repository; `dotnet build` (full solution)
      green; no reference to `analyzer.txt` remains in any `.cs` file_
      _Skill: .claude/skills/clean-code/SKILL.md_

---

### Phase 10 — Tests

- [x] **Create `Tests/Infrastructure.Tests/Services/ProjectServiceTests.cs`** _Delegated to test-writer agent_ —
      NUnit + Moq + FluentAssertions unit tests for `ProjectService` business rules.

      Required cases:
      1. `CreateAsync` with no slug → slug is derived from name (lowercase, spaces → hyphens).
      2. `CreateAsync` with a slug that already exists → throws `InvalidOperationException`.
      3. `CreateAsync` with a unique slug → calls `IProjectRepository.CreateAsync`.
      4. `DeleteAsync(ProjectConstants.DefaultProjectId)` → throws `InvalidOperationException`.
      5. `DeleteAsync` with a non-default id → calls `IProjectRepository.DeleteAsync`.
      6. `UpdateActiveAsync(ProjectConstants.DefaultProjectId, false)` → calls `UpdateActiveAsync`
         on repository (does not throw); verifies warning is logged.
      7. `UpdateActiveAsync` with a non-default id → calls repository without logging a warning.

      Mock `IProjectRepository` and `ILogger<ProjectService>`.
      Follow AAA pattern and `MethodName_Scenario_ExpectedResult` naming.
      _Acceptance: all seven tests pass with `dotnet test`; no live DB_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Create `Tests/Api.Tests/Filters/RequireProjectAttributeTests.cs`** _Delegated to test-writer agent_ —
      NUnit + Moq tests for `RequireProjectAttribute` filter branches.

      Required cases:
      1. Project not found → filter short-circuits with `NotFoundObjectResult`.
      2. Project found and active, GET request → filter sets `IProjectContext.Current`
         and calls next.
      3. Project found, `IsActive = false`, GET request → filter calls next (reads are
         allowed on inactive projects).
      4. Project found, `IsActive = false`, POST request → filter short-circuits with
         `ConflictObjectResult`.
      5. Project found and active, POST request → filter sets context and calls next.

      Mock `IProjectRepository`; use a real `DefaultHttpContext` with appropriate
      `RouteValues["projectId"]` set.
      _Acceptance: all five tests pass with `dotnet test`_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Create `Tests/Infrastructure.Tests/Repositories/ProjectRepositoryTests.cs`** _Delegated to test-writer agent_ —
      NUnit tests for `ProjectRepository` Dapper round-trip using a mocked
      `IDbConnectionFactory` (or integration style if the project has a test-DB fixture).

      Required cases:
      1. `GetByIdAsync` with known id → returns correct `Project` domain object.
      2. `GetByIdAsync` with unknown id → returns `null`.
      3. `ExistsAsync` with known id → returns `true`.
      4. `ExistsAsync` with unknown id → returns `false`.
      5. `GetBySlugAsync` with known slug → returns `Project` with matching slug.
      6. `CreateAsync` → inserts and returns the project.
      7. `DeleteAsync` when FK violation → throws `InvalidOperationException`.

      _Acceptance: all seven tests pass with `dotnet test`_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Create `Tests/Infrastructure.Tests/Repositories/EventRepositoryFindSimilarTests.cs`** _Delegated to test-writer agent_ —
      NUnit tests for the updated `IEventRepository.FindSimilarEventsAsync` signature.

      Required cases:
      1. Mock returns events from two projects; call with `projectId = A` → result
         contains only project-A events.
      2. Mock with zero matching events → empty list returned.
      3. Verify `FindSimilarEventsAsync` is called with the `projectId` argument that
         was passed in (not a different one).

      _Acceptance: all three tests pass with `dotnet test`_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Create `Tests/Api.Tests/Controllers/ProjectsControllerTests.cs`** _Delegated to test-writer agent_ —
      NUnit + WebApplicationFactory (or mocked `IProjectService`) integration tests.

      Required cases:
      1. `GET /projects` with Admin JWT → 200; body is `List<ProjectListItemDto>`.
      2. `GET /projects/{id}` with known id → 200 with `ProjectDetailDto`.
      3. `GET /projects/{id}` with unknown id → 404.
      4. `POST /projects` with valid body → 201 with `ProjectDetailDto`.
      5. `POST /projects` with missing `Name` → 400.
      6. `POST /projects` with `Categories = []` → 400.
      7. `PUT /projects/{id}` with valid body → 200.
      8. `PATCH /projects/{id}/status` → 204.
      9. `DELETE /projects/{id}` → 204.
      10. `DELETE /projects/{ProjectConstants.DefaultProjectId}` → 409.
      11. All endpoints with no JWT → 401.
      12. All endpoints with Editor JWT → 403.

      _Acceptance: all twelve cases pass with `dotnet test`; no live DB_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Create `Tests/Api.Tests/Controllers/ScopedControllerIsolationTests.cs`** _Delegated to test-writer agent_ —
      WebApplicationFactory end-to-end test asserting cross-project isolation.

      Required cases:
      1. Create two projects (A and B). Create a source and article in project A.
         `GET /projects/{B.id}/articles` → 200 with empty list.
      2. Create an event in project A. `GET /projects/{B.id}/events` → 200 with empty list.
      3. `GET /projects/{nonexistent-guid}/articles` → 404 (filter fires).
      4. Source with same URL created in project A and project B → both return 201
         (unique index is per project, not global).

      Uses a real test DB (or in-memory Postgres via Testcontainers if available).
      _Acceptance: all four cases pass with `dotnet test`_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 11 — UI

- [x] **Create `UI/src/store/projectStore.ts`** — Zustand slice (with `persist` to
      `localStorage` under key `'project-storage'`). State: `selectedProjectId: string | null`.
      Actions: `setProject(id: string): void`, `clearProject(): void`.
      Export `useProjectStore` hook.
      _Acceptance: TypeScript compiles (`tsc --noEmit` in `UI/`); no `any` types;
      store persists across page reload_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `UI/src/features/projects/useProjects.ts`** — TanStack Query hook for
      `GET /projects`. Query key: `['projects']`.
      Returns `data: ProjectListItemDto[] | undefined` (use the generated type once API
      client is regenerated; use a hand-typed interim type until then:
      `{ id: string; name: string; slug: string; isActive: boolean }`).
      _Acceptance: TypeScript compiles; hook can be imported and used in a component_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `UI/src/features/projects/ProjectSwitcher.tsx`** — dropdown component
      bound to `useProjects()`. On mount, if `useProjectStore().selectedProjectId` is
      null or does not match any project in the list, set it to the first project's id.
      On option change, call `useProjectStore().setProject(id)` and call
      `queryClient.invalidateQueries({ queryKey: ['project', previousId] })`.
      _Acceptance: TypeScript compiles; component renders without errors when projects
      list is non-empty_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/layouts/DashboardLayout.tsx`** — add `<ProjectSwitcher />` to
      the top-bar header area (alongside the existing role / date display). Pass no
      props; the component reads from the store internally.
      _Acceptance: TypeScript compiles; `ProjectSwitcher` appears in the header_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/router/index.tsx`** — restructure scoped routes to nest under a
      `projects/:projectId` path segment. The root redirect becomes
      `<Navigate to={"/projects/" + (selectedProjectId ?? defaultId) + "/articles"} replace />`.
      Wrap the `/:projectId/*` subtree in a new `ProjectRoute` guard component that
      reads `useParams().projectId`, validates it against `useProjects()` data, and
      redirects to `"/projects/{firstProjectId}/articles"` if the id is unknown.
      Admin-only scoped routes (`sources`, `publish-targets`, `ai-operations`) keep their
      `AdminRoute` wrapper inside the `projects/:projectId` nesting.
      Add a new top-level `AdminRoute`-wrapped route at `projects` (no `:projectId`)
      pointing to a future `ProjectsPage` (create a placeholder component at
      `UI/src/features/projects/ProjectsPage.tsx` that renders "Projects CRUD — coming soon"
      until the full CRUD page is built).
      _Acceptance: TypeScript compiles; navigating to `/projects/{validId}/articles`
      renders the articles page; navigating to `/projects/{invalidId}/articles` redirects_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify all scoped feature hooks to prefix query keys with project id** —
      update `UI/src/features/articles/useArticles.ts`,
      `UI/src/features/articles/useArticleDetail.ts`,
      `UI/src/features/events/useEvents.ts`,
      `UI/src/features/events/useEventDetail.ts`,
      `UI/src/features/events/useEventMutations.ts`,
      `UI/src/features/publications/usePublications.ts`,
      `UI/src/features/publications/usePublicationDetail.ts`,
      `UI/src/features/publications/usePublicationMutations.ts`,
      `UI/src/features/publications/useAllPublications.ts`,
      `UI/src/features/sources/useSources.ts`,
      `UI/src/features/sources/useSourceMutations.ts`,
      `UI/src/features/publishTargets/usePublishTargets.ts`,
      `UI/src/features/publishTargets/usePublishTargetMutations.ts`,
      `UI/src/features/aiOperations/useAiRequestMetrics.ts`,
      `UI/src/features/aiOperations/useAiRequestList.ts`,
      `UI/src/features/aiOperations/useAiRequestDetail.ts`.
      Change each hook to read `selectedProjectId` from `useProjectStore()` and prefix
      the query key with `['project', selectedProjectId, ...]`. Each query function must
      pass `projectId` as the leading argument to the generated API method.
      _Acceptance: TypeScript compiles; switching projects in `ProjectSwitcher` invalidates
      all scoped queries and triggers refetch_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `UI/src/features/projects/ProjectsPage.tsx`** — admin-only page for
      project CRUD. Displays a list of projects (using `useProjects()`) with name, slug,
      and active toggle. Includes a "New Project" button that opens a `SlideOver` form
      with fields for `Name`, `Slug` (optional), `AnalyzerPromptText` (textarea),
      `Categories` (comma-separated input parsed to array), `OutputLanguage`, and
      `OutputLanguageName`. Uses React Hook Form + Zod for validation. Mutations
      delegate to a new `useProjectMutations.ts` hook (create below).
      _Acceptance: TypeScript compiles; page renders projects list; form submission calls
      the create mutation_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `UI/src/features/projects/useProjectMutations.ts`** — TanStack Query
      mutation hooks: `useCreateProject`, `useUpdateProject`, `useToggleProjectActive`,
      `useDeleteProject`. Each invalidates `['projects']` on success.
      _Acceptance: TypeScript compiles; mutations can be invoked from `ProjectsPage`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Regenerate OpenAPI client** — once backend routes are reshaped, run
      `npm run generate-api` from `UI/`. Confirm `UI/src/api/generated/` files reference
      `projectId` as a leading parameter on scoped endpoints. Update any wrapper hooks
      that rely on the old non-project-scoped method names.
      _Note: requires the API to be running with a live DB on port 5172._
      _Acceptance: `npm run generate-api` exits 0; `npm run build` in `UI/` exits 0;
      generated files contain `projectId` parameter on scoped service methods_

---

## Open Questions
- None. All design decisions are resolved in ADR
  `docs/architecture/decisions/0022-multi-project-tenancy.md`.
