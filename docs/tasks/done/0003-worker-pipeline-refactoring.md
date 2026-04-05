# Worker Pipeline Refactoring: Consolidate 7 Workers to 3

## Goal

Eliminate the RawArticle entity, consolidate 7 background workers into 3 (SourceFetcherWorker,
ArticleAnalysisWorker, PublicationWorker), and introduce SELECT FOR UPDATE locking so the pipeline
is race-condition-free, cheaper on embedding API calls, and easier to reason about.

## Affected Layers

- Core
- Infrastructure
- Worker
- Api
- UI

---

## ADR Reference

`docs/architecture/decisions/worker-pipeline-refactoring.md`

---

## Tasks

---

### Phase 1 — Data Model Migration (non-breaking)

> Goal: add new columns to `Article` so old workers keep running while new ones are built.
> After this phase the system deploys without breaking anything.

- [ ] **Modify `Core/DomainModels/Article.cs`** — add nullable fields: `string? OriginalContent`,
      `Guid? SourceId`, `string? OriginalUrl`, `DateTimeOffset? PublishedAt`,
      `string? ExternalId`, `float[]? Embedding`; remove `RawArticle` navigation property
      and its non-null initialiser; remove `ArticleStatus.Classifying` and
      `ArticleStatus.Processing` from the `ArticleStatus` enum
      _Acceptance: file compiles; no reference to `RawArticle` type remains; enum has exactly
      6 values: Pending, Analyzing, AnalysisDone, Approved, Rejected, Published_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Create `Core/DomainModels/ArticleRole.cs`** — new file containing the `ArticleRole` enum
      with values: `Initiator`, `Update`, `Contradiction`; rename the existing `EventArticleRole`
      enum in `Core/DomainModels/Article.cs` to `ArticleRole` and remove the old `EventArticleRole`
      declaration from that file; update all references to `EventArticleRole` in `Core/` to use
      `ArticleRole`
      _Acceptance: `ArticleRole.cs` exists and compiles; no `EventArticleRole` identifier remains
      anywhere in `Core/`; `Article.Role` property type is `ArticleRole?`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Core/DomainModels/Event.cs`** — add `int ArticleCount { get; set; }` property
      with default value `0`
      _Acceptance: file compiles; `ArticleCount` property is present with default 0_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Core/DomainModels/Source.cs`** — replace `List<RawArticle> RawArticles` with
      `List<Article> Articles` navigation property
      _Acceptance: file compiles; no `RawArticle` reference remains_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Persistence/Entity/ArticleEntity.cs`** — add columns:
      `string? OriginalContent`, `Guid? SourceId`, `string? OriginalUrl`,
      `DateTimeOffset? PublishedAt`, `string? ExternalId`, `Vector? Embedding`;
      remove `Guid RawArticleId`, `RawArticleEntity RawArticle` navigation property
      _Acceptance: file compiles; no `RawArticleEntity` reference in this file_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Persistence/Entity/EventEntity.cs`** — add
      `int ArticleCount { get; set; }` property with default value `0`
      _Acceptance: file compiles; `ArticleCount` property is present_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Persistence/Entity/SourceEntity.cs`** — replace
      `List<RawArticleEntity> RawArticles` with `List<ArticleEntity> Articles` navigation property
      _Acceptance: file compiles; no `RawArticleEntity` reference remains_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Persistence/Configurations/ArticleConfiguration.cs`** — remove
      `HasOne(a => a.RawArticle).WithOne(...).HasForeignKey<ArticleEntity>(a => a.RawArticleId)`;
      add column mappings for `OriginalContent`, `SourceId`, `OriginalUrl`, `PublishedAt`,
      `ExternalId`; add `HasOne(a => a.Source).WithMany(s => s.Articles).HasForeignKey(a => a.SourceId).IsRequired(false)`;
      add `Embedding` property mapped to `HasColumnType("vector(768)")` with HNSW index
      using `vector_cosine_ops`; add `HasIndex(a => new { a.SourceId, a.ExternalId })` unique index
      _Acceptance: EF model snapshot updates cleanly; no FK to `raw_articles` remains_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Persistence/Configurations/SourceConfiguration.cs`** — replace
      `HasMany(s => s.RawArticles).WithOne(r => r.Source)` with
      `HasMany(s => s.Articles).WithOne(a => a.Source).HasForeignKey(a => a.SourceId)`
      _Acceptance: configuration compiles; no `RawArticle` reference remains_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Persistence/Mappers/ArticleMapper.cs`** — update `ToDomain` and
      `ToEntity` to map `OriginalContent`, `SourceId`, `OriginalUrl`, `PublishedAt`, `ExternalId`,
      `Embedding` (using `Vector` wrapper for entity side); remove `RawArticleId` and
      `RawArticle` mappings from both methods; update `FromAnalysisResult` signature to accept
      `Article pendingArticle` instead of `RawArticle rawArticle` (preserve source fields already
      on the pending article)
      _Acceptance: no `RawArticle` or `RawArticleEntity` import remains; `ToEntity` does not set
      `RawArticleId`; `ToDomain` does not read `RawArticle` navigation_
      _Skill: .claude/skills/mappers/SKILL.md_

- [ ] **Modify `Infrastructure/Persistence/DataBase/NewsParserDbContext.cs`** — remove
      `DbSet<RawArticleEntity> RawArticles` property
      _Acceptance: file compiles; `RawArticles` property is gone_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Update all `Infrastructure/` references to `EventArticleRole`** — replace every use of
      `EventArticleRole` in `Infrastructure/Persistence/Repositories/EventRepository.cs`,
      `Infrastructure/Persistence/Mappers/`, and any other Infrastructure file with `ArticleRole`
      after the rename performed in the `ArticleRole.cs` task above
      _Acceptance: `dotnet build` exits 0; no `EventArticleRole` identifier in `Infrastructure/`_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Add EF Core migration `<timestamp>_MergeRawArticleIntoArticle`** — generated via
      `dotnet ef migrations add MergeRawArticleIntoArticle --project Infrastructure --startup-project Worker`;
      Up: adds `original_content`, `source_id`, `original_url`, `published_at`, `external_id`,
      `embedding vector(768)` columns to `articles`; adds `article_count integer not null default 0`
      to `events`; backfills from `raw_articles` via SQL
      `UPDATE articles SET ... FROM raw_articles WHERE articles.raw_article_id = raw_articles.id`;
      drops FK `raw_article_id` from `articles`; adds unique index on `(source_id, external_id)`;
      adds HNSW index on `embedding`; Down reverses each step
      _Acceptance: `dotnet ef migrations add` exits 0; `Up` and `Down` methods are present and
      symmetric; backfill SQL is in `Up`; `article_count` column appears in the `events` table_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Write tests for new `Article` columns in `Tests/Infrastructure.Tests/`** — add
      `Tests/Infrastructure.Tests/Persistence/Repositories/ArticleRepositoryTests.cs`;
      test `AddAsync` persists `OriginalContent`, `SourceId`, `OriginalUrl`, `PublishedAt`,
      `ExternalId`; test `GetByIdAsync` returns domain model with those fields populated
      _Acceptance: tests compile and pass; `RawArticle` is not referenced_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 2 — ArticleRepository Updates (non-breaking)

> Goal: update `IArticleRepository` and its implementation so workers can safely claim articles
> without race conditions. Old workers continue to work; they just call renamed methods.

- [ ] **Modify `Core/Interfaces/Repositories/IArticleRepository.cs`** — remove
      `GetPendingForAnalysisAsync` (returns `List<RawArticle>`), `GetPendingForGenerationAsync`,
      `GetPendingForClassificationAsync`, `UpdateRawArticleStatusAsync`,
      `UpdateGeneratedContentAsync`, `IncrementRawArticleRetryAsync`; add:
      `Task<List<Article>> GetPendingAsync(int batchSize, CancellationToken ct)` (returns
      `ArticleStatus.Pending`, locked with FOR UPDATE),
      `Task<List<Article>> GetPendingForClassificationAsync(int batchSize, CancellationToken ct)`
      (returns `ArticleStatus.AnalysisDone` with `EventId == null`, locked),
      `Task UpdateEmbeddingAsync(Guid id, float[] embedding, CancellationToken ct)`,
      `Task<bool> ExistsAsync(Guid sourceId, string externalId, CancellationToken ct)`,
      `Task<bool> ExistsByUrlAsync(string url, CancellationToken ct)`,
      `Task<bool> HasSimilarAsync(Guid currentId, float[] embedding, double threshold, int windowHours, CancellationToken ct)`,
      `Task<List<string>> GetRecentTitlesForDeduplicationAsync(int windowHours, CancellationToken ct)`
      _Acceptance: interface compiles; no `RawArticle` or `RawArticleStatus` in method signatures_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Persistence/Repositories/ArticleRepository.cs`** — implement all
      methods added to `IArticleRepository` in the previous task; `GetPendingAsync` and the
      new `GetPendingForClassificationAsync` must use `FromSqlRaw` or EF `Set<>().FromSql` with
      `FOR UPDATE SKIP LOCKED` suffix (PostgreSQL); implement `ExistsAsync`, `ExistsByUrlAsync`,
      `HasSimilarAsync` (pgvector cosine distance), `GetRecentTitlesForDeduplicationAsync`,
      `UpdateEmbeddingAsync`; remove methods that no longer exist on the interface
      (`UpdateRawArticleStatusAsync`, `IncrementRawArticleRetryAsync`,
      `UpdateGeneratedContentAsync`, old `GetPendingForAnalysisAsync`)
      _Acceptance: class satisfies updated `IArticleRepository`; `_context.RawArticles` is not
      referenced; `dotnet build` exits 0_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Write repository tests for new `ArticleRepository` methods** — add
      `Tests/Infrastructure.Tests/Persistence/Repositories/ArticleRepositorySelectTests.cs`;
      cover: `GetPendingAsync` returns only `Pending` articles; `ExistsAsync` true/false;
      `ExistsByUrlAsync` true/false; `GetRecentTitlesForDeduplicationAsync` window filtering;
      `UpdateEmbeddingAsync` persists value
      _Acceptance: all tests pass; no `RawArticle` entity referenced_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 3 — SourceFetcherWorker Refactoring (non-breaking)

> Goal: SourceFetcherWorker creates Article entities directly. RawArticle creation is removed.
> Deduplication moves from RawArticleRepository queries to ArticleRepository queries.

- [ ] **Modify `Core/Interfaces/Validators/IRawArticleValidator.cs`** — rename to
      `IArticleValidator`; update `Validate` parameter from `RawArticle` to `Article`
      _Acceptance: interface compiles; no `RawArticle` reference in file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Validators/RawArticleValidator.cs`** — rename class to
      `ArticleValidator`, implement `IArticleValidator`, update `Validate` to accept `Article`;
      validation logic reads `article.OriginalContent` (for content length check),
      `article.Title`, `article.OriginalUrl`, `article.PublishedAt`
      _Acceptance: class compiles; no `RawArticle` reference; same validation rules preserved_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — in
      `AddRepositories()` remove `IRawArticleRepository → RawArticleRepository` registration;
      in `AddServices()` replace `IRawArticleValidator → RawArticleValidator` with
      `IArticleValidator → ArticleValidator`
      _Acceptance: DI registrations compile; no `IRawArticleRepository` or `IRawArticleValidator`
      registered_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Worker/Workers/SourceFetcherWorker.cs`** — replace all `IRawArticleRepository`
      and `IRawArticleValidator` usage with `IArticleRepository` and `IArticleValidator`
      respectively; `ProcessSourceAsync` now creates `Article` objects (not `RawArticle`) with
      `Status = ArticleStatus.Pending`, `OriginalContent = parsed.Content`,
      `SourceId = source.Id`, `OriginalUrl`, `PublishedAt`, `ExternalId`, `Title`, `Language`;
      deduplication calls `articleRepository.ExistsAsync`, `ExistsByUrlAsync`,
      `GetRecentTitlesForDeduplicationAsync`; saves via `articleRepository.AddAsync`
      _Acceptance: worker compiles; no `IRawArticleRepository`, `IRawArticleValidator`, or
      `RawArticle` references; `dotnet build` exits 0_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Modify `Tests/Worker.Tests/Workers/SourceFetcherWorkerTests.cs`** — replace
      `Mock<IRawArticleRepository>` with `Mock<IArticleRepository>`,
      `Mock<IRawArticleValidator>` with `Mock<IArticleValidator>`; update all test
      assertions to work with `Article` objects instead of `RawArticle`; ensure existing
      deduplication scenarios still pass
      _Acceptance: all tests compile and pass; no `RawArticle` mock or assertion remains_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 4 — ArticleAnalysisWorker Consolidation (breaking)

> Goal: ArticleAnalysisWorker does enrichment + single embedding + event classification
> atomically. Embedding is generated once from `title + summary`.

- [ ] **Modify `Worker/Configuration/ArticleProcessingOptions.cs`** — remove
      `GeneratorIntervalSeconds` and `PublicationGenerationIntervalSeconds`; add
      `AnalysisIntervalSeconds` (rename from `AnalyzerIntervalSeconds` if desired — keep
      one interval for ArticleAnalysisWorker); retain `BatchSize`, `MaxRetryCount`,
      `PublicationWorkerIntervalSeconds`; merge relevant thresholds from
      `EventClassificationOptions` as optional override fields (leave
      `EventClassificationOptions` intact for now — it is still registered)
      _Acceptance: class compiles; removed properties are gone; existing fields still present_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Add `UpdateAnalysisResultAsync` to `Core/Interfaces/Repositories/IArticleRepository.cs`**
      — signature: `Task UpdateAnalysisResultAsync(Guid id, string category, List<string> tags,
      string sentiment, string language, string summary, string modelVersion, ArticleStatus status,
      CancellationToken ct)`
      _Acceptance: interface compiles; signature is present_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Persistence/Repositories/ArticleRepository.cs`** — implement
      `UpdateAnalysisResultAsync` using `ExecuteUpdateAsync`; this is needed by the refactored
      ArticleAnalysisWorker to persist enrichment results atomically
      _Acceptance: method satisfies the interface signature added in the previous task;
      uses `ExecuteUpdateAsync`; no `SaveChangesAsync` in the method body_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Verify `IEventRepository.cs` signatures match ArticleAnalysisWorker Phase C requirements**
      — confirm `FindSimilarEventsAsync(float[] embedding, double threshold, int windowHours,
      CancellationToken ct)`, `UpdateSummaryAndEmbeddingAsync(Guid id, string summary,
      float[] embedding, CancellationToken ct)`, and `AssignArticleToEventAsync(Guid articleId,
      Guid eventId, ArticleRole role, CancellationToken ct)` are present; update
      `AssignArticleToEventAsync` parameter type from `EventArticleRole` to `ArticleRole` to
      reflect the Phase 1 enum rename
      _Acceptance: `IEventRepository.cs` compiles; `AssignArticleToEventAsync` parameter type
      is `ArticleRole`; no `EventArticleRole` in file_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Modify `Infrastructure/Persistence/Repositories/EventRepository.cs`** — update
      `UpdateSummaryAndEmbeddingAsync` to also increment `ArticleCount` by 1 via
      `SetProperty(e => e.ArticleCount, e.ArticleCount + 1)` in the same `ExecuteUpdateAsync`
      call (supports the averaging formula `(old * count + new) / (count + 1)`);
      update `AssignArticleToEventAsync` parameter type from `EventArticleRole` to `ArticleRole`
      _Acceptance: `UpdateSummaryAndEmbeddingAsync` sets `ArticleCount`, `Summary`, `Embedding`,
      and `LastUpdatedAt` in a single `ExecuteUpdateAsync`; method compiles; `dotnet build`
      exits 0_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — rewrite to implement 3 phases
      per article in a single method `ProcessArticleAsync`:
      Phase A: fetch `Pending` articles via `articleRepository.GetPendingAsync`; transition to
      `Analyzing`; call `IArticleAnalyzer.AnalyzeAsync` using `article.OriginalContent`;
      store Category, Tags, Sentiment, Language, Summary, ModelVersion on Article; transition to
      `AnalysisDone`.
      Phase B: generate embedding ONCE using `$"{article.Title}. {article.Summary}"`;
      check `articleRepository.HasSimilarAsync` for near-duplicates; if duplicate, reject with
      `RejectionReason = "duplicate_by_vector"`; otherwise call
      `articleRepository.UpdateEmbeddingAsync`.
      Phase C: call `eventRepository.FindSimilarEventsAsync` with the article embedding;
      apply auto-match (>= 0.90) or grey-zone Claude classifier (0.70–0.90) or new-event
      (< 0.70) logic (port logic from `EventClassificationWorker`); when assigning to existing
      event update `Event.Embedding` via mathematical averaging formula
      `(old * count + new) / (count + 1)` and call `eventRepository.UpdateSummaryAndEmbeddingAsync`
      (which also increments `ArticleCount`);
      when creating new event initialise `Event.Embedding = article.Embedding` and
      `Event.ArticleCount = 1`;
      assign `Article.EventId` and `Article.Role` via `eventRepository.AssignArticleToEventAsync`;
      remove any dependency on `IRawArticleRepository`
      _Acceptance: worker compiles; `IGeminiEmbeddingService.GenerateEmbeddingAsync` is called
      exactly once per article; no `IRawArticleRepository` or `RawArticle` reference; event
      embedding is updated via averaging (no second API call); `dotnet build` exits 0_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Write tests for refactored `ArticleAnalysisWorker`** — add
      `Tests/Worker.Tests/Workers/ArticleAnalysisWorkerTests.cs`; cover: embedding generated
      exactly once per article; duplicate detected → article rejected with correct reason;
      high-similarity match → article assigned to existing event without new event created;
      low-similarity → new event created; grey-zone → `IEventClassifier` called;
      event embedding updated via averaging formula (no second embedding API call)
      _Acceptance: all tests pass; `IGeminiEmbeddingService` mock verifies single invocation
      per article_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 5 — Remove Old Workers (breaking)

> Goal: delete the four workers whose responsibilities are now handled by ArticleAnalysisWorker
> and PublicationWorker. Merge content generation into PublicationWorker.

- [ ] **Modify `Infrastructure/Persistence/Repositories/PublicationRepository.cs`** — rewrite
      `GetPendingForContentGenerationAsync` to use `FromSqlRaw` with `FOR UPDATE SKIP LOCKED`
      (PostgreSQL row-level locking) so concurrent PublicationWorker instances cannot process the
      same publication twice; remove the `ThenInclude(a => a.RawArticle)` include that will no
      longer exist after Phase 6
      _Acceptance: method uses `FromSqlRaw` with a `FOR UPDATE SKIP LOCKED` clause;
      no `RawArticle` navigation included; `dotnet build` exits 0_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Modify `Worker/Workers/PublicationWorker.cs`** — add Phase A before Phase B: fetch
      publications via `publicationRepository.GetPendingForContentGenerationAsync` (not just
      `GetReadyForPublishAsync`); for each pending publication call
      `IContentGenerator.GenerateForPlatformAsync`; store result via
      `publicationRepository.UpdateGeneratedContentAsync`; set status to `ContentReady`; then
      proceed with existing Phase B (publish `ContentReady` publications); the two phases can
      run in the same `ProcessAsync` cycle
      _Acceptance: worker compiles; no separate `PublicationGenerationWorker` dependency;
      `IContentGenerator` is injected; `GetPendingForContentGenerationAsync` is called;
      `GetReadyForPublishAsync` is called; both phases complete in one cycle_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Delete `Worker/Workers/ArticleGenerationWorker.cs`**
      _Acceptance: file is removed; solution builds without it_

- [ ] **Delete `Worker/Workers/EventClassificationWorker.cs`**
      _Acceptance: file is removed; solution builds without it_

- [ ] **Delete `Worker/Workers/EventUpdateWorker.cs`**
      _Acceptance: file is removed; solution builds without it_

- [ ] **Delete `Worker/Workers/PublicationGenerationWorker.cs`**
      _Acceptance: file is removed; solution builds without it_

- [ ] **Modify `Worker/Extensions/WorkerServiceExtensions.cs`** — remove registrations for
      `ArticleGenerationWorker`, `EventClassificationWorker`, `EventUpdateWorker`,
      `PublicationGenerationWorker`; remove `EventClassificationOptions` configuration binding
      (options class itself is kept for now — see Phase 6)
      _Acceptance: only 3 `AddHostedService` calls remain: `SourceFetcherWorker`,
      `ArticleAnalysisWorker`, `PublicationWorker`; `dotnet build` exits 0_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Write tests for updated `PublicationWorker`** — add
      `Tests/Worker.Tests/Workers/PublicationWorkerTests.cs`; cover: content generation called
      before publish; publication with `ContentReady` status published directly without
      re-generating content; failed content generation does not block next publication;
      reply publication uses parent external message ID
      _Acceptance: all tests pass; `IContentGenerator` mock verifies it is called for
      `Pending` publications and not called for `ContentReady` ones_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 6 — Cleanup (non-breaking)

> Goal: delete RawArticle from every layer, remove orphaned options, update API/UI.

- [ ] **Delete `Core/DomainModels/RawArticle.cs`**
      _Acceptance: file removed; `dotnet build` exits 0_

- [ ] **Delete `Core/Interfaces/Repositories/IRawArticleRepository.cs`**
      _Acceptance: file removed; no compile error_

- [ ] **Delete `Infrastructure/Persistence/Entity/RawArticleEntity.cs`**
      _Acceptance: file removed; no compile error_

- [ ] **Delete `Infrastructure/Persistence/Repositories/RawArticleRepository.cs`**
      _Acceptance: file removed; no compile error_

- [ ] **Delete `Infrastructure/Persistence/Mappers/RawArticleMapper.cs`**
      _Acceptance: file removed; no compile error_

- [ ] **Delete `Infrastructure/Persistence/Configurations/RawArticleConfiguration.cs`**
      _Acceptance: file removed; no compile error_

- [ ] **Add EF Core migration `<timestamp>_DropRawArticlesTable`** — generated via
      `dotnet ef migrations add DropRawArticlesTable --project Infrastructure --startup-project Worker`;
      `Up`: drops the `raw_articles` table; `Down`: recreates it (for rollback safety)
      _Acceptance: migration generated without error; `Up` drops `raw_articles`; `Down`
      recreates it with original columns_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Delete `Worker/Configuration/EventClassificationOptions.cs`** — options are no
      longer needed as a separate class; relevant thresholds were merged into
      `ArticleProcessingOptions` in Phase 4
      _Acceptance: file removed; no compile error; `WorkerServiceExtensions.cs` does not
      reference it_

- [ ] **Modify `Api/Models/ArticleDetailDto.cs`** — remove `RawArticleDto Source` field from
      `ArticleDetailDto`; remove `RawArticleDto` record; remove `ArticleEventDto.Role` if
      it was populated from `article.RawArticle`; add `string? OriginalUrl` and
      `DateTimeOffset? PublishedAt` sourced from the Article directly
      _Acceptance: record compiles; no `RawArticleDto` type exists in project; `ArticleDetailDto`
      reflects new Article fields_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [ ] **Modify `Api/Mappers/ArticleMapper.cs`** — remove `ToDto(this RawArticle raw)` method
      and all `RawArticle` references; update `ToDetailDto` to no longer call
      `article.RawArticle.ToDto()`; map `OriginalUrl` and `PublishedAt` directly from
      `article.OriginalUrl` and `article.PublishedAt`
      _Acceptance: no `RawArticle` type referenced; `ToDetailDto` compiles without `RawArticle`
      navigation; `dotnet build` exits 0_
      _Skill: .claude/skills/mappers/SKILL.md_

- [ ] **Modify `Api/Controllers/ArticlesController.cs`** — remove any remaining reference to
      `RawArticle` (e.g., `GetPendingForAnalysisAsync` if still present); verify `GetById`
      returns `ArticleDetailDto` with updated shape
      _Acceptance: controller compiles; `dotnet build` exits 0_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [ ] **Regenerate `UI/src/api/generated/`** — run the OpenAPI code-generation command
      (npm script or `nswag` / `openapi-typescript-codegen`) against the updated API so
      generated types no longer include `RawArticleDto`; verify TypeScript compilation
      succeeds
      _Acceptance: `npm run build` (or equivalent) exits 0; no `RawArticleDto` type in
      generated output_

- [ ] **Delete `Tests/Infrastructure.Tests/Persistence/Repositories/RawArticleRepositoryTests.cs`**
      _Acceptance: file removed; `dotnet test` exits 0_

- [ ] **Verify full solution build and tests** — run `dotnet build` from solution root and
      `dotnet test` for all test projects; confirm 0 errors, 0 warnings about deleted types
      _Acceptance: `dotnet build` exits 0; `dotnet test` exits 0; no reference to
      `RawArticle`, `IRawArticleRepository`, `RawArticleStatus`, `ArticleStatus.Classifying`,
      or `ArticleStatus.Processing` anywhere in the solution_

---

## Execution Order

```
Phase 1 (tasks 1–14):   Article model → ArticleRole enum → Event.ArticleCount → Entity → Config → Mapper → DbContext → Infrastructure rename pass → Migration → Tests
Phase 2 (tasks 15–17):  IArticleRepository → ArticleRepository → Tests
Phase 3 (tasks 18–22):  IArticleValidator → ArticleValidator → DI → SourceFetcherWorker → Tests
Phase 4 (tasks 23–29):  Options → IArticleRepository addition → ArticleRepository impl → IEventRepository verify → EventRepository update → ArticleAnalysisWorker → Tests
Phase 5 (tasks 30–37):  PublicationRepository locking → PublicationWorker → Delete 4 workers → WorkerServiceExtensions → Tests
Phase 6 (tasks 38–52):  Delete Core/Infra/Api/UI RawArticle artefacts → Final migrations → Verification
```

Each phase must be fully complete (build passing) before the next phase begins.
Phases 1 and 2 are non-breaking and can be deployed independently.
Phase 3 is non-breaking (SourceFetcherWorker writes Articles but old analysis path still works).
Phases 4–6 are breaking and should be deployed together in a single release.

---

## Open Questions

- None. All design decisions are resolved in the ADR.
