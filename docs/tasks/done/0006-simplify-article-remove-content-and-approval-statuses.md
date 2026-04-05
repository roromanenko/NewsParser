# Simplify Article: Remove Content Column and Approval Statuses

## Goal

Remove the unused `Content` property and the redundant `Approved`/`Published` status values
from the Article domain model, eliminating dead code and making Event the sole authority for
editorial approval state.

## Affected Layers

- Core
- Infrastructure
- Api
- Worker
- UI

## ADR Reference

`docs/architecture/decisions/simplify-article-remove-content-and-approval-statuses.md`

---

## Tasks

---

### Phase 1 — Database migration (non-breaking, deploy before code changes)

> Goal: drop the two columns and back-fill illegal status strings so that when Phase 2
> removes the enum values the database has no rows that would cause `Enum.Parse` to throw.
> This phase can be deployed while the old code is still running.

- [x] **Add EF Core migration `<timestamp>_SimplifyArticleRemoveContentAndApproval`** in
      `Infrastructure/Persistence/Migrations/` — generated via
      `dotnet ef migrations add SimplifyArticleRemoveContentAndApproval --project Infrastructure --startup-project Worker`.
      `Up` must:
      1. Execute SQL: `UPDATE articles SET "Status" = 'AnalysisDone' WHERE "Status" IN ('Approved', 'Published');`
      2. Drop column `Content` from table `articles`
      3. Drop column `RejectedByEditorId` from table `articles`
      `Down` must re-add both columns (nullable, no data recovery needed).
      _Acceptance: migration file exists under `Infrastructure/Persistence/Migrations/`; `Up` and
      `Down` compile; running `dotnet ef database update` against a dev DB succeeds without error_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

---

### Phase 2 — Core and Infrastructure changes (breaking)

> Depends on Phase 1 migration having been applied. All steps in this phase must compile
> together before any individual step is "done" — the build may be broken between steps.

- [x] **Modify `Core/DomainModels/Article.cs`** — remove `Content` property (line 17) and
      `RejectedByEditorId` property (line 31); remove `Approved` (line 54) and `Published`
      (line 56) values from `ArticleStatus` enum; keep `Rejected`.
      Resulting enum values: `Pending`, `Analyzing`, `AnalysisDone`, `Rejected`.
      _Acceptance: file compiles; `ArticleStatus` has exactly 4 values; no `Content` or
      `RejectedByEditorId` property; no EF or infrastructure references in the file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Entity/ArticleEntity.cs`** — remove `Content`
      property (line 20) and `RejectedByEditorId` property (line 31).
      _Acceptance: file compiles; no `Content` or `RejectedByEditorId` property; solution builds_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Mappers/ArticleMapper.cs`** — remove
      `Content = entity.Content` from `ToDomain` (line 20); remove `Content = domain.Content`
      from `ToEntity` (line 49); remove `RejectedByEditorId = entity.RejectedByEditorId` from
      `ToDomain` (line 30); remove `RejectedByEditorId = domain.RejectedByEditorId` from
      `ToEntity` (line 59). `FromAnalysisResult` needs no change (it never set these fields).
      _Acceptance: all three mapper methods compile; no reference to `Content` or
      `RejectedByEditorId` remains in this file_
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Modify `Core/Interfaces/Repositories/IArticleRepository.cs`** — remove
      `UpdateRejectionAsync(Guid id, Guid editorId, string reason, CT)` (line 12);
      rename `GetPendingForApprovalAsync(int page, int pageSize, CT)` to `GetAnalysisDoneAsync`;
      rename `CountPendingForApprovalAsync(CT)` to `CountAnalysisDoneAsync`;
      add `Task RejectAsync(Guid id, string reason, CancellationToken cancellationToken = default);`
      _Acceptance: interface compiles; `UpdateRejectionAsync` does not exist; `RejectAsync`
      with two non-CT parameters is present; `GetAnalysisDoneAsync` and `CountAnalysisDoneAsync`
      are present; no old names remain_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/ArticleRepository.cs`** — remove
      `UpdateRejectionAsync` implementation (lines 61–70); rename `GetPendingForApprovalAsync`
      to `GetAnalysisDoneAsync` and update its filter to `ArticleStatus.AnalysisDone`;
      rename `CountPendingForApprovalAsync` to `CountAnalysisDoneAsync` and update its filter;
      add `RejectAsync` implementation using `ExecuteUpdateAsync` that sets
      `Status = ArticleStatus.Rejected.ToString()` and `RejectionReason = reason` in a single call.
      _Acceptance: class satisfies `IArticleRepository`; `RejectAsync` uses `ExecuteUpdateAsync`
      with two `SetProperty` calls; queries filter by `AnalysisDone`; solution builds_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Services/EventApprovalService.cs`** — in `ApproveAsync`,
      remove the `foreach` loop (lines 52–55) that calls
      `articleRepository.UpdateStatusAsync(article.Id, ArticleStatus.Approved, ...)`;
      in `RejectAsync`, remove the `foreach` loop (lines 73–75) that calls
      `articleRepository.UpdateStatusAsync(article.Id, ArticleStatus.Rejected, ...)`.
      _Acceptance: both methods compile; neither calls `articleRepository` to set article
      statuses; `eventRepository.UpdateStatusAsync` calls remain unchanged_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/HaikuKeyFactsExtractor.cs`** — in `ExtractAsync`, change
      `article.Content` (line 34) to `article.OriginalContent ?? string.Empty` in the
      `userPrompt` string.
      _Acceptance: file compiles; `article.Content` is not referenced anywhere in the file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Delete `Core/DomainModels/AI/ArticleGenerationResult.cs`** — dead model used only
      by the deleted generator.
      _Acceptance: file does not exist; solution builds without reference errors_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Delete `Core/Interfaces/AI/IArticleGenerator.cs`** — dead interface with no
      remaining implementors or consumers.
      _Acceptance: file does not exist; solution builds without reference errors_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Delete `Infrastructure/AI/ClaudeArticleGenerator.cs`** — dead implementation of
      the deleted `IArticleGenerator` interface.
      _Acceptance: file does not exist; solution builds without reference errors_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — remove
      the `IArticleGenerator` DI registration block (lines 100–105):
      ```
      services.AddScoped<IArticleGenerator>(_ => new ClaudeArticleGenerator(...));
      ```
      Also remove any `using` directive for `IArticleGenerator` or `ClaudeArticleGenerator`
      if it becomes unused.
      _Acceptance: file compiles; `IArticleGenerator` is not referenced; solution builds_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — replace the two calls to
      `UpdateRejectionAsync` at lines 118–119 (duplicate-by-vector path) with a call to
      `RejectAsync(article.Id, "duplicate_by_vector", cancellationToken)`.
      The max-retries rejection at line 140 already uses `UpdateStatusAsync` — leave it
      unchanged (it does not set a reason).
      _Acceptance: file compiles; no call to `UpdateRejectionAsync` remains; solution builds_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 3 — API, UI, and cleanup

> Depends on Phase 2 compiling cleanly.

- [x] **Modify `Api/Models/ArticleDetailDto.cs`** — remove the `Content` parameter (line 4)
      from the record constructor.
      _Acceptance: record compiles with no `Content` parameter; all existing usages of
      `ArticleDetailDto` that do not pass `Content` compile without error_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Mappers/ArticleMapper.cs`** — in `ToDetailDto`, remove `article.Content`
      from the `ArticleDetailDto` constructor call (line 27) to match the updated record shape.
      _Acceptance: mapper compiles; `article.Content` is not referenced; Swagger spec no longer
      includes `content` in the article detail schema_
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Repurpose `Core/Interfaces/Repositories/IArticleRepository.cs`** — rename
      `GetPendingForApprovalAsync(int page, int pageSize, CT)` to
      `GetAnalysisDoneAsync(int page, int pageSize, CT)` and rename
      `CountPendingForApprovalAsync(CT)` to `CountAnalysisDoneAsync(CT)`.
      (These were already scheduled for removal in Phase 2 step 4 — update that step to rename
      rather than delete.)
      _Acceptance: interface compiles with renamed methods; old names do not exist_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Repurpose `Infrastructure/Persistence/Repositories/ArticleRepository.cs`** — rename
      `GetPendingForApprovalAsync` to `GetAnalysisDoneAsync` and update the query to filter by
      `ArticleStatus.AnalysisDone` (instead of the old approval-pending status);
      rename `CountPendingForApprovalAsync` to `CountAnalysisDoneAsync` and update its filter
      similarly.
      _Acceptance: implementation compiles; queries filter `Status == ArticleStatus.AnalysisDone`;
      old method names do not exist_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Repurpose `Api/Controllers/ArticlesController.cs`** — rename the `GetPending` endpoint
      to `GetAnalysisDone`; change it to call `articleRepository.GetAnalysisDoneAsync` and
      `CountAnalysisDoneAsync`; keep the route `GET /articles` and the same pagination shape.
      Keep the `GetById` endpoint unchanged.
      _Acceptance: controller compiles; `GET /articles` returns `200` with paginated
      `AnalysisDone` articles; Swagger shows `GET /articles` and `GET /articles/{id}`_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `UI/src/features/articles/ArticleDetailPage.tsx`** — remove the "FULL CONTENT"
      conditional block (lines 181–197): the `{article.content && ( ... )}` JSX element that
      renders the content `<div>`. Leave all other sections (Summary, Key Facts, Tags, Event
      sidebar) intact.
      _Acceptance: TypeScript compiles; component renders without referencing `article.content`;
      the "FULL CONTENT" section does not appear in the rendered page_

- [x] **`UI/src/features/articles/ArticlesPage.tsx` — no changes needed.** The page continues
      to call `articlesApi.articlesGet(page, pageSize)` which maps to the repurposed
      `GET /articles` endpoint now returning `AnalysisDone` articles. Update the page title/
      description text if it says "Pending Approval" to say "Analysis Complete" or similar.
      _Acceptance: TypeScript compiles; page loads without errors; displays `AnalysisDone`
      articles_

- [x] **Regenerate API client in `UI/src/api/generated/`** — manually updated `ArticleDetailDto`
      in `UI/src/api/generated/api.ts` to remove `content` field (backend not running;
      `npm run generate-api` cannot be run).
      _Acceptance: generated files do not contain `content` on `ArticleDetailDto`_

---

### Tests

- [x] **Modify `Tests/Infrastructure.Tests/Services/EventApprovalServiceTests.cs`** — removed
      `ApproveAsync_WhenEventHasMultipleArticles_SetsAllArticlesToApproved` test; removed
      `_articleRepoMock` verify assertions from `RejectAsync` test; removed `Content = "Content."`
      from `CreateArticle` helper; updated constructor call to match new 3-parameter signature.
      _Acceptance: test file compiles; no test asserts `IArticleRepository.UpdateStatusAsync`
      is called from `EventApprovalService`_

- [x] **Modify `Tests/Worker.Tests/Workers/ArticleAnalysisWorkerKeyFactsTests.cs`** — removed
      `Content = "Article content long enough for analysis."` from `CreatePendingArticle` helper.
      _Acceptance: test file compiles; all three key-facts tests pass unchanged_

## Decisions Made

- `ArticlesPage.tsx` stays. `GET /articles` is repurposed to return `AnalysisDone` articles
  (all articles with completed analysis), keeping the page useful as a read-only article browser.
