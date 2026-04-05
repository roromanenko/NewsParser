# Event-Based Publication with Key Facts Extraction

## Goal

Add structured `KeyFacts` extraction per Article (via Haiku), shift the editor approval flow from
individual Articles to Events, and update the publication content-generation pipeline to synthesise
all Article Summaries and KeyFacts belonging to an Event rather than drawing from a single Article.

## Affected Layers

- Core
- Infrastructure
- Worker
- Api
- UI

## ADR Reference

`docs/architecture/decisions/event-based-publication-with-key-facts.md`

---

## Tasks

---

### Phase 1 — Key Facts (non-breaking addition)

> Goal: Articles gain a `KeyFacts` field, a new `IKeyFactsExtractor` interface and Haiku
> implementation are wired up, and the analysis worker extracts key facts after enrichment.
> Nothing else in the approval or publication pipeline changes. Safe to deploy independently.

- [x] **Modify `Core/DomainModels/Article.cs`** — add `public List<string> KeyFacts { get; set; } = [];`
      in the "Ai" section alongside `Tags`, `Category`, `Sentiment`, `Summary`
      _Acceptance: file compiles; `KeyFacts` property is present with default empty list;
      no EF or infrastructure references in the file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Entity/ArticleEntity.cs`** — add
      `public List<string> KeyFacts { get; set; } = [];` matching the domain property;
      stored as PostgreSQL `jsonb` array (same pattern as `Tags`)
      _Acceptance: file compiles; `KeyFacts` property present; no domain model reference_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Configurations/ArticleConfiguration.cs`** — add
      `builder.Property(a => a.KeyFacts).HasColumnType("jsonb");` to register the new column
      _Acceptance: configuration compiles; `dotnet ef migrations add` does not error on the
      new property_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Mappers/ArticleMapper.cs`** — add `KeyFacts = entity.KeyFacts`
      to `ToDomain`; add `KeyFacts = domain.KeyFacts` to `ToEntity`; `KeyFacts` is not set in
      `FromAnalysisResult` (it is populated separately after analysis)
      _Acceptance: both `ToDomain` and `ToEntity` round-trip `KeyFacts`; `FromAnalysisResult`
      does not reference `KeyFacts`_
      _Skill: .claude/skills/mappers/SKILL.md_

- [ ] **Add EF Core migration `<timestamp>_AddArticleKeyFacts`** — generated via
      `dotnet ef migrations add AddArticleKeyFacts --project Infrastructure --startup-project Worker`;
      `Up`: adds `KeyFacts jsonb not null default '[]'` column to `articles`; `Down`: drops it
      _Acceptance: migration generated without error; `Up` contains the column addition with
      default empty JSON array; `Down` drops the column_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_
      **NOTE: Needs manual execution — run `dotnet ef migrations add AddArticleKeyFacts --project Infrastructure --startup-project Worker` after implementation.**

- [x] **Create `Core/Interfaces/AI/IKeyFactsExtractor.cs`** — single method:
      `Task<List<string>> ExtractAsync(Article article, CancellationToken cancellationToken = default);`
      Input: an Article with `Title`, `Content`, and `Summary` populated post-analysis.
      Output: 3–7 short factual statements.
      _Acceptance: interface compiles; no infrastructure dependencies; lives in `Core/Interfaces/AI/`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Configuration/AiOptions.cs`** — add
      `public string KeyFactsExtractorModel { get; set; } = "claude-haiku-4-5-20251001";`
      to the `AnthropicOptions` class, following the same pattern as `ClassifierModel` and
      `SummaryUpdaterModel`
      _Acceptance: file compiles; `AnthropicOptions.KeyFactsExtractorModel` property is present_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Infrastructure/AI/HaikuKeyFactsExtractor.cs`** — implements `IKeyFactsExtractor`;
      constructor takes `string apiKey, string model`; uses `AnthropicClient` (same import pattern
      as `ClaudeEventSummaryUpdater`); sends Article title, content, and summary to the model;
      parses the response as a `List<string>` of 3–7 factual statements; returns an empty list
      rather than throwing on parse failure
      _Acceptance: class implements `IKeyFactsExtractor`; compiles with no domain-layer references
      except `Core.DomainModels.Article`; no hardcoded API keys_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Add `UpdateKeyFactsAsync` to `Core/Interfaces/Repositories/IArticleRepository.cs`** —
      signature: `Task UpdateKeyFactsAsync(Guid id, List<string> keyFacts, CancellationToken cancellationToken = default);`
      _Acceptance: interface compiles; method is present with the exact signature above_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Implement `UpdateKeyFactsAsync` in `Infrastructure/Persistence/Repositories/ArticleRepository.cs`** —
      use `ExecuteUpdateAsync` with `.SetProperty(x => x.KeyFacts, keyFacts)`; no
      `SaveChangesAsync` in the method body (same pattern as `UpdateAnalysisResultAsync`)
      _Acceptance: method satisfies the interface signature; uses `ExecuteUpdateAsync`;
      `dotnet build` exits 0_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Register `IKeyFactsExtractor` in `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** —
      in `AddAiServices`: add
      `services.AddScoped<IKeyFactsExtractor>(_ => new HaikuKeyFactsExtractor(aiOptions.Anthropic.ApiKey, aiOptions.Anthropic.KeyFactsExtractorModel));`
      following the same factory-delegate pattern used for `IEventSummaryUpdater`
      _Acceptance: DI registration compiles; `IKeyFactsExtractor` resolves from the container_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — extend the `AnalysisContext` record
      with `IKeyFactsExtractor KeyFactsExtractor`; resolve it in `ProcessAsync` via
      `scope.ServiceProvider.GetRequiredService<IKeyFactsExtractor>()`; in `ProcessArticleAsync`
      insert Phase A2 after the `UpdateAnalysisResultAsync` call and before the embedding
      generation: call `ctx.KeyFactsExtractor.ExtractAsync(article, cancellationToken)`,
      then `ctx.ArticleRepository.UpdateKeyFactsAsync(article.Id, keyFacts, cancellationToken)`,
      then set `article.KeyFacts = keyFacts`; wrap Phase A2 in its own try/catch so a key-facts
      failure logs a warning but does not abort article processing
      _Acceptance: worker compiles; `IKeyFactsExtractor` is in `AnalysisContext`; key-facts
      failure does not set article status to `Rejected`; `dotnet build` exits 0_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Add `KeyFacts` to `Api/Models/ArticleDetailDto.cs`** — add `List<string> KeyFacts`
      parameter to the `ArticleDetailDto` record; position it after `Summary`
      _Acceptance: record compiles; `KeyFacts` parameter is present with type `List<string>`_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Mappers/ArticleMapper.cs`** — add `article.KeyFacts` (or `article.KeyFacts ?? []`)
      to the `ToDetailDto` mapping; the `ToListItemDto` mapping does not include `KeyFacts`
      _Acceptance: `ToDetailDto` includes `KeyFacts`; `ToListItemDto` is unchanged;
      TypeScript compilation unaffected until API client is regenerated_
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Add `KeyFacts` to `Api/Models/EventDtos.cs`** — add `List<string> KeyFacts` parameter
      to the `EventArticleDto` record; position it after `Summary`
      _Acceptance: record compiles; `KeyFacts` parameter is present_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Mappers/EventMapper.cs`** — add `article.KeyFacts ?? []` to the
      `ToEventArticleDto` mapping
      _Acceptance: `ToEventArticleDto` includes `KeyFacts`; file compiles_
      _Skill: .claude/skills/mappers/SKILL.md_

- [ ] **Write tests for `HaikuKeyFactsExtractor` and `ArticleRepository.UpdateKeyFactsAsync`** —
      add `Tests/Infrastructure.Tests/AI/HaikuKeyFactsExtractorTests.cs`: cover happy-path
      extraction returns 3–7 strings, malformed response returns empty list rather than throwing;
      add `Tests/Infrastructure.Tests/Persistence/Repositories/ArticleKeyFactsRepositoryTests.cs`:
      cover `UpdateKeyFactsAsync` persists the list, overwrites previous value
      _Acceptance: all tests compile and pass_
      _Agent: test-writer_
      _Delegated to test-writer agent_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Write tests for the updated `ArticleAnalysisWorker` Phase A2** — add or extend
      `Tests/Worker.Tests/Workers/ArticleAnalysisWorkerTests.cs`; cover: key-facts extracted
      and persisted after analysis result; key-facts extractor failure logs warning but article
      continues to embedding phase; `UpdateKeyFactsAsync` is called with the extracted list
      _Acceptance: all tests pass; `IKeyFactsExtractor` mock is used; existing tests continue
      to pass_
      _Agent: test-writer_
      _Delegated to test-writer agent_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 2 — Event Approval (breaking change to approval flow)

> Goal: editor approves and rejects Events (not individual Articles). Article-level approval
> endpoints and service are removed. Safe to deploy after Phase 1.

- [x] **Modify `Core/DomainModels/Event.cs`** — extend `EventStatus` enum with two new values:
      `Approved` and `Rejected`, so the enum is: `Active, Approved, Rejected, Resolved, Archived`
      _Acceptance: enum compiles; new values are present; existing code that switches on
      `EventStatus` will produce compiler warnings on unhandled cases — those are addressed in
      subsequent tasks_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/Interfaces/Services/IEventApprovalService.cs`** — two methods:
      `Task<Event> ApproveAsync(Guid eventId, Guid editorId, List<Guid> publishTargetIds, CancellationToken cancellationToken = default);`
      `Task<Event> RejectAsync(Guid eventId, Guid editorId, string reason, CancellationToken cancellationToken = default);`
      _Acceptance: interface compiles; no infrastructure dependencies; lives in
      `Core/Interfaces/Services/`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Infrastructure/Services/EventApprovalService.cs`** — implements
      `IEventApprovalService`; constructor-injects `IEventRepository`, `IArticleRepository`,
      `IPublicationRepository`, `IPublishTargetRepository`;
      `ApproveAsync`: loads event via `IEventRepository.GetDetailAsync`; validates event is
      `Active`; validates each publish target exists and `IsActive`; creates one `Publication`
      per publish target with `EventId` set to the event's id and `ArticleId` set to the
      initiator article (first article with `Role == ArticleRole.Initiator`, fallback to
      `Articles.First()`); calls `IPublicationRepository.AddRangeAsync`; updates event status
      to `EventStatus.Approved` via `IEventRepository.UpdateStatusAsync`; batch-updates all
      articles in the event to `ArticleStatus.Approved` via `IArticleRepository.UpdateStatusAsync`
      for each article; returns the event;
      `RejectAsync`: loads event; updates event status to `EventStatus.Rejected`; batch-updates
      all articles to `ArticleStatus.Rejected`; returns the event
      _Acceptance: class compiles; satisfies `IEventApprovalService`; no direct
      `DbContext` reference (uses repository interfaces only)_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Register `IEventApprovalService` in `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** —
      in `AddServices`: add `services.AddScoped<IEventApprovalService, EventApprovalService>();`
      _Acceptance: registration compiles; existing registrations are unchanged_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Add `ApproveEventRequest` and `RejectEventRequest` to `Api/Models/EventDtos.cs`** — add:
      `record ApproveEventRequest(List<Guid> PublishTargetIds);`
      `record RejectEventRequest(string Reason);`
      _Acceptance: records compile; file has no broken references_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Controllers/EventsController.cs`** — inject `IEventApprovalService` via
      primary constructor; add two endpoints following the exact same pattern as the
      `ArticlesController.Approve` and `.Reject` endpoints:
      `POST /events/{id}/approve` — body `ApproveEventRequest`; validates
      `PublishTargetIds` is non-empty; calls `approvalService.ApproveAsync`; returns `200 OK`
      with the event mapped to `EventListItemDto`;
      `POST /events/{id}/reject` — body `RejectEventRequest`; validates `Reason` is non-empty;
      calls `approvalService.RejectAsync`; returns `200 OK` with the event mapped to
      `EventListItemDto`
      _Acceptance: Swagger shows both new endpoints; existing endpoints are unchanged;
      `dotnet build` exits 0_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Controllers/ArticlesController.cs`** — remove the `Approve` action
      (`POST /articles/{id}/approve`) and the `Reject` action (`POST /articles/{id}/reject`);
      remove the `IArticleApprovalService approvalService` constructor parameter and its
      injection; remove the `using Core.Interfaces.Services;` import if it is no longer needed
      _Acceptance: controller compiles without `IArticleApprovalService`; Swagger no longer
      shows `POST /articles/{id}/approve` or `POST /articles/{id}/reject`;
      `GET /articles` and `GET /articles/{id}` remain intact_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Delete `Core/Interfaces/Services/IArticleApprovalService.cs`**
      _Acceptance: file is deleted; `dotnet build` exits 0_

- [x] **Delete `Infrastructure/Services/ArticleApprovalService.cs`**
      _Acceptance: file is deleted; `dotnet build` exits 0_

- [x] **Remove `IArticleApprovalService` registration from `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** —
      delete the line `services.AddScoped<IArticleApprovalService, ArticleApprovalService>();`
      from `AddServices`
      _Acceptance: file compiles; no reference to `ArticleApprovalService` or
      `IArticleApprovalService` remains in the file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Regenerate `UI/src/api/generated/`** — run `npm run generate-api` with the backend
      running on port 5172; verify the generated client includes `eventsIdApprovePost` and
      `eventsIdRejectPost` methods on `EventsApi` and no longer includes `articlesIdApprovePost`
      or `articlesIdRejectPost`
      _Acceptance: `npm run build` exits 0; generated client has the new event approval methods;
      article approval methods are absent_
      _Skipped — requires manual execution with running backend_

- [ ] **Update `UI/src/features/events/useEventMutations.ts`** — add two new mutations using
      the auto-generated `EventsApi`:
      `approveEvent`: calls `eventsApi.eventsIdApprovePost(eventId!, { publishTargetIds })`;
      on success toasts "Event approved" and invalidates `['events']` and `['event', eventId]`;
      `rejectEvent`: calls `eventsApi.eventsIdRejectPost(eventId!, { reason })`;
      on success toasts "Event rejected" and invalidates `['events']` and `['event', eventId]`
      _Acceptance: TypeScript compiles; hook exports `approveEvent` and `rejectEvent` mutations_
      _Skipped — UI task, requires API client regeneration first_

- [ ] **Modify `UI/src/features/events/EventDetailPage.tsx`** — add an "APPROVE EVENT" button
      (visible to Editors and Admins) and a "REJECT EVENT" button (visible to Editors and Admins)
      in the header card alongside the existing "ARCHIVE EVENT" button; clicking "APPROVE EVENT"
      opens a modal or inline flow to select `PublishTargetIds` then calls
      `approveEvent.mutate({ publishTargetIds })`; clicking "REJECT EVENT" opens a confirm
      dialog with a reason text field then calls `rejectEvent.mutate({ reason })`;
      hide both buttons when event status is `Approved`, `Rejected`, or `Archived`
      _Acceptance: TypeScript compiles; buttons are present on an `Active` event; buttons are
      absent on `Approved`/`Rejected`/`Archived` events_
      _Skipped — UI task_

- [ ] **Modify `UI/src/features/events/EventsPage.tsx`** — update `statusColor` to return a
      distinct color for `'Approved'` (use `var(--caramel)`) and `'Rejected'` (use `var(--rust)`)
      so event cards reflect the new statuses visually
      _Acceptance: TypeScript compiles; status colors render correctly for all 5 enum values_
      _Skipped — UI task_

- [ ] **Write tests for `EventApprovalService`** — add
      `Tests/Infrastructure.Tests/Services/EventApprovalServiceTests.cs`; cover:
      `ApproveAsync` creates one publication per publish target with correct `EventId` and
      `ArticleId` (initiator); event status set to `Approved`; all articles set to `Approved`;
      `ApproveAsync` throws `KeyNotFoundException` when event not found;
      `ApproveAsync` throws `InvalidOperationException` when event is not `Active`;
      `RejectAsync` sets event to `Rejected` and all articles to `Rejected`
      _Acceptance: all tests compile and pass_
      _Agent: test-writer_
      _Delegated to test-writer agent_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Write API tests for new `EventsController` approve/reject endpoints** — add or extend
      `Tests/Api.Tests/Controllers/EventsControllerTests.cs`; cover:
      `POST /events/{id}/approve` returns `200` with valid body; returns `400` when
      `PublishTargetIds` is empty; returns `404` when event not found;
      `POST /events/{id}/reject` returns `200`; returns `400` when `Reason` is blank
      _Acceptance: all tests compile and pass_
      _Agent: test-writer_
      _Delegated to test-writer agent_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 3 — Event-Based Content Generation (breaking change to publication)

> Goal: `IContentGenerator` accepts an `Event` (with Articles loaded) instead of a single
> Article. `PublicationWorker` loads Event context when generating content. Produces richer,
> multi-source publications. Deploy after Phase 2.

- [x] **Modify `Core/Interfaces/AI/IContentGenerator.cs`** — change `GenerateForPlatformAsync`
      signature from `(Article article, PublishTarget target, ...)` to
      `(Event evt, PublishTarget target, CancellationToken cancellationToken = default, string? updateContext = null)`;
      remove the `using Core.DomainModels` import for `Article` if `Article` is no longer
      referenced directly (it is still reachable via `Event.Articles`)
      _Acceptance: interface compiles; parameter name is `evt`; `Article` is not a direct
      parameter_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/ClaudeContentGenerator.cs`** — update
      `GenerateForPlatformAsync` to accept `Event evt` instead of `Article article`;
      for normal publications build the user prompt from: `Event.Title`, `Event.Summary`,
      and for each `Article` in `evt.Articles`: `Article.Summary`, `Article.KeyFacts`
      (joined as bullet list), combined tags and category; for update publications
      (`updateContext != null`) use the same abbreviated prompt as today but sourced from the
      initiator article in `evt.Articles`; remove the old article-centric prompt branch
      _Acceptance: class implements updated `IContentGenerator`; `dotnet build` exits 0;
      old `Article article` parameter is gone_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/Interfaces/Repositories/IPublicationRepository.cs`** — update
      `GetPendingForContentGenerationAsync` doc/comment to indicate it now must include
      `Event` with `Articles`; no signature change required at the interface level unless
      the return type changes (it does not — returns `List<Publication>`)
      _Acceptance: file compiles; existing signature is preserved_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/PublicationRepository.cs`** — in
      `GetPendingForContentGenerationAsync`: add `.Include(p => p.Event).ThenInclude(e => e.Articles)`
      to the query after the existing `.Include(p => p.Article)` and `.Include(p => p.PublishTarget)`;
      the `FOR UPDATE SKIP LOCKED` raw-SQL query pattern must be preserved (use a subquery or
      switch to a two-step: raw SQL to get ids, then EF query with includes by ids)
      _Acceptance: `Publication.Event` and `Publication.Event.Articles` are non-null after the
      query; `dotnet build` exits 0; FOR UPDATE SKIP LOCKED is preserved_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Worker/Workers/PublicationWorker.cs`** — in `GenerateContentAsync`: change
      the `contentGenerator.GenerateForPlatformAsync` call to pass `publication.Event` instead
      of `publication.Article`; add a null-guard: if `publication.Event` is null, log a warning
      and skip the publication (do not set status to `Failed` — it will be retried); update the
      log message to reference `publication.Event.Title` instead of article title where relevant
      _Acceptance: worker compiles; `IContentGenerator.GenerateForPlatformAsync` is called with
      an `Event` argument; `dotnet build` exits 0_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Write tests for updated `ClaudeContentGenerator`** — add or extend
      `Tests/Infrastructure.Tests/AI/ClaudeContentGeneratorTests.cs`; cover: prompt is built
      from `Event.Title`, `Event.Summary`, and each article's `Summary` and `KeyFacts`;
      update-context prompt uses the initiator article's data; missing articles list returns
      a prompt that does not throw
      _Acceptance: all tests compile and pass_
      _Agent: test-writer_
      _Delegated to test-writer agent_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Write tests for updated `PublicationWorker`** — add or extend
      `Tests/Worker.Tests/Workers/PublicationWorkerTests.cs`; cover: content generation called
      with `Event` argument (not `Article`); publication with null `Event` is skipped with a
      warning log; existing publish/retry behaviour is unchanged
      _Acceptance: all tests compile and pass; `IContentGenerator` mock verifies it receives
      an `Event` instance_
      _Agent: test-writer_
      _Delegated to test-writer agent_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Verify full solution build and tests** — run `dotnet build` from solution root and
      `dotnet test` for all test projects
      _Acceptance: `dotnet build` exits 0; `dotnet test` exits 0; no reference to the old
      `IArticleApprovalService`, `ArticleApprovalService`, or single-Article
      `IContentGenerator` signature remains in the solution_

---

## Execution Order

```
Phase 1 (tasks 1–17):  Article.KeyFacts → Entity → EF config → Mapper → Migration →
                        IKeyFactsExtractor → AiOptions → HaikuKeyFactsExtractor →
                        IArticleRepository.UpdateKeyFactsAsync → ArticleRepository impl →
                        DI registration → ArticleAnalysisWorker → API DTOs → API Mappers → Tests
Phase 2 (tasks 18–34): EventStatus enum → IEventApprovalService → EventApprovalService →
                        DI register → EventDtos additions → EventsController new endpoints →
                        ArticlesController remove endpoints → Delete IArticleApprovalService →
                        Delete ArticleApprovalService → Remove DI registration →
                        Regenerate API client → UI mutations → UI EventDetailPage → UI EventsPage → Tests
Phase 3 (tasks 35–42): IContentGenerator signature → ClaudeContentGenerator → IPublicationRepository →
                        PublicationRepository includes → PublicationWorker → Tests → Full build verify
```

Each phase must be fully complete (build passing) before the next phase begins.
Phase 1 is non-breaking and can be deployed independently.
Phase 2 is a breaking change to the approval flow (article approve/reject endpoints removed).
Phase 3 is a breaking change to the publication pipeline (IContentGenerator signature changes).

---

## Open Questions

- None. All design decisions are resolved in the ADR.
