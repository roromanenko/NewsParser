# Simplify Article: Remove Content Column and Approval Statuses

## Status

Proposed

## Context

After the worker pipeline refactoring (ADR: `worker-pipeline-refactoring.md`) and the shift to event-based publication (ADR: `event-based-publication-with-key-facts.md`), the Article domain model retains two artifacts that no longer serve a purpose:

1. **`Content` property** (`string`, default `string.Empty`). This field was originally intended to hold AI-generated article content produced by the now-deleted `ArticleGenerationWorker` via `IArticleGenerator` / `ClaudeArticleGenerator`. The worker pipeline refactoring ADR explicitly states: "Article generation step is eliminated. Article.Title and Article.Content come directly from SourceFetcherWorker (raw source content)." However, raw source content is already stored in `OriginalContent`. The `Content` field is never written to by the current pipeline -- the `ArticleAnalysisWorker` does not populate it, `RssParser` does not set it, and `UpdateAnalysisResultAsync` does not update it. The field is always `string.Empty` for new articles. Meanwhile, the event-based publication ADR established that publications are generated from Event.Summary + Article.KeyFacts + Article.Summary -- not from Article.Content. The only remaining consumer is `HaikuKeyFactsExtractor`, which reads `article.Content` in its prompt, but this is always empty for new articles (it should use `OriginalContent` instead).

2. **`Approved`, `Rejected`, and `Published` status values** on `ArticleStatus`. The event-based publication ADR shifted approval from individual articles to events. `EventApprovalService` currently sets articles to `Approved`/`Rejected` as a side effect of approving/rejecting an Event, but this is redundant bookkeeping -- the article's approval state is fully determined by its Event's status. The `Published` status is defined in the enum but never used anywhere in the codebase.

Related properties that become unnecessary when `Approved`/`Rejected` are removed:
- `RejectedByEditorId` (Guid?) -- only set during article-level rejection
- `RejectionReason` (string?) -- only set during article-level rejection

These rejection fields are now tracked at the Event level (the editor rejects an Event, not individual articles).

### Relationship to Existing ADRs

- **`worker-pipeline-refactoring.md`** established the simplified status flow: Pending -> Analyzing -> AnalysisDone -> Approved -> Published / Rejected. This ADR further simplifies it by recognizing that Approved/Rejected/Published are Event-level concerns, not Article-level.
- **`event-based-publication-with-key-facts.md`** Section 5 states: "Articles transition to Approved/Rejected as a batch when their Event is approved/rejected." This ADR proposes removing that batch transition entirely, since Article approval status is derivable from `Event.Status`.

## Options

### Option 1 -- Remove Content, Approved, Rejected, Published statuses, and rejection fields

Remove `Content` from Article entirely. Remove `Approved`, `Rejected`, `Published` from `ArticleStatus` enum. Remove `RejectedByEditorId` and `RejectionReason` from Article. Stop setting article statuses in `EventApprovalService`. The article lifecycle becomes: Pending -> Analyzing -> AnalysisDone (terminal for articles; further lifecycle is on Event).

**Pros:** Cleanest model; Article becomes purely a data entity under Event; no redundant state; no dead columns.
**Cons:** Breaking change to `EventApprovalService`; requires migration to drop columns; `ArticleStatus.Rejected` is still used for worker-level rejection (failed analysis, duplicate detection) -- must preserve that.

### Option 2 -- Remove Content and Published only; keep Approved and Rejected

Remove `Content` (unused) and `Published` (unused). Keep `Approved` and `Rejected` as they are used by `EventApprovalService` and the worker (duplicate rejection).

**Pros:** Smaller change; less risk.
**Cons:** `Approved` status on Article is still redundant (derivable from Event.Status); keeps dead weight in the model.

### Option 3 -- Keep Content but rename to something; remove only Published

Rename `Content` to clarify its purpose.

**Pros:** No data loss.
**Cons:** The field is empty for all new articles; renaming does not solve the problem; `OriginalContent` already serves the raw content purpose.

## Decision

**Option 1 with a nuance: Remove Content, Approved, and Published. Rename Rejected to Failed. Keep rejection fields but repurpose them for worker-level failures.**

Detailed rationale:

### 1. Remove `Content` property and column

The `Content` field is dead. Evidence:
- `RssParser` (line 20) sets `OriginalContent`, not `Content`
- `ArticleAnalysisWorker.ProcessArticleAsync` calls `UpdateAnalysisResultAsync` which sets category, tags, sentiment, language, summary, modelVersion, status -- but not Content
- `ArticleMapper.FromAnalysisResult` does not set Content
- `ClaudeContentGenerator` uses Event-level data for publication
- The only reader is `HaikuKeyFactsExtractor` (line 34: `CONTENT: {article.Content}`), which should use `OriginalContent` instead

**Action:** Remove `Content` from `Article`, `ArticleEntity`, both mappers, `ArticleDetailDto`. Fix `HaikuKeyFactsExtractor` to use `OriginalContent`. Drop the `Content` column via EF Core migration.

### 2. Remove `Approved` and `Published` from ArticleStatus

- `Approved` is set only by `EventApprovalService.ApproveAsync` (line 54) as a batch operation when an Event is approved. This is redundant -- an article's approval state equals its Event's status. Querying "all approved articles" can be done via `JOIN events WHERE events.status = 'Approved'`.
- `Published` is defined in the enum but has zero usages in the entire codebase.

**Action:** Remove both values from `ArticleStatus`. Remove the `foreach` loop in `EventApprovalService.ApproveAsync` that sets articles to `Approved`. Remove the `foreach` loop in `EventApprovalService.RejectAsync` that sets articles to `Rejected`.

### 3. Keep `Rejected` but consider it a worker/system status, not editorial

`Rejected` is used in two distinct contexts:
- **Worker-level:** `ArticleAnalysisWorker` sets `Rejected` when an article exceeds max retries (line 140) or is detected as a duplicate (line 118-119 via `UpdateRejectionAsync`)
- **Deduplication queries:** `ExistsByUrlAsync`, `HasSimilarAsync`, `GetRecentTitlesForDeduplicationAsync` all filter out `Rejected` articles

These are legitimate uses. An article can fail processing independently of its Event. Keeping `Rejected` is correct.

**Action:** Keep `Rejected` in `ArticleStatus`. It now exclusively means "failed processing / duplicate / exceeded retries" -- never "editor rejected."

### 4. Repurpose `RejectedByEditorId` and `RejectionReason`

With editorial rejection moved to Event level, these fields are only used by the worker for system-level rejection reasons (e.g., `"duplicate_by_vector"`). The `RejectedByEditorId` is set to `Guid.Empty` in that case (line 119).

**Action:** Remove `RejectedByEditorId` (it is meaningless for worker rejections -- always `Guid.Empty`). Keep `RejectionReason` (stores `"duplicate_by_vector"` and similar worker rejection reasons). Remove `UpdateRejectionAsync` method and inline rejection reason into a simpler update. Drop the `RejectedByEditorId` column via migration.

### 5. Simplify `ArticleRepository`

- Remove `UpdateRejectionAsync` -- replace with setting status to `Rejected` and reason in a single `ExecuteUpdateAsync`
- Remove `GetPendingForApprovalAsync` and `CountPendingForApprovalAsync` -- these list articles in `AnalysisDone` status for per-article approval, which no longer exists (editors browse Events, not Articles)

### 6. Simplify `ArticlesController`

- Remove `GetPending` endpoint (articles are no longer approved individually; the Events page serves this purpose)
- Keep `GetById` for article detail viewing (linked from Event detail page)
- Remove `Content` from `ArticleDetailDto`

### 7. Resulting ArticleStatus enum

```
public enum ArticleStatus
{
    Pending,
    Analyzing,
    AnalysisDone,
    Rejected
}
```

Article lifecycle: `Pending -> Analyzing -> AnalysisDone` (success) or `Rejected` (failure). Further lifecycle (approval, publication) is tracked on Event and Publication entities.

### 8. Migration strategy

One EF Core migration:
- Drop `Content` column from `articles` table
- Drop `RejectedByEditorId` column from `articles` table
- No enum column changes needed (enums are stored as strings; removing enum values from C# does not require a DB migration, but existing rows with `"Approved"` or `"Published"` values will fail `Enum.Parse` if loaded)

**Data migration concern:** If any existing articles have `Status = "Approved"` or `Status = "Published"` in the database, loading them will throw `Enum.Parse` exceptions. The migration must include a SQL step to update these:
```sql
UPDATE articles SET "Status" = 'AnalysisDone' WHERE "Status" IN ('Approved', 'Published');
```

This is safe because:
- `Approved` articles are already assigned to an Event (their EventId is set), so their approval state is tracked on the Event
- `Published` status was never used, but defensively updating it prevents parse failures

### 9. IArticleGenerator cleanup

`IArticleGenerator`, `ClaudeArticleGenerator`, and `ArticleGenerationResult` are dead code. They are registered in DI (`InfrastructureServiceExtensions.cs` line 100-105) but never resolved by any worker or service. They should be deleted as part of this cleanup.

## Consequences

### Positive

1. **Cleaner domain model** -- Article has no unused properties; its status enum has only meaningful values
2. **Single source of truth for approval** -- Event.Status is the sole authority for whether articles in an event are approved/rejected
3. **Reduced confusion** -- developers no longer wonder what `Content` is for vs `OriginalContent`
4. **Dead code removal** -- `IArticleGenerator`, `ClaudeArticleGenerator`, `ArticleGenerationResult` are eliminated
5. **Smaller database footprint** -- two columns dropped per article row

### Negative / Risks

1. **Existing data** -- rows with `Status = 'Approved'` must be migrated before deploying the new code; otherwise `Enum.Parse` will fail at runtime
2. **Breaking API change** -- `GET /articles` (pending for approval) endpoint is removed; UI articles page loses its data source. However, the Events page already serves the editorial workflow.
3. **Skills/docs references** -- several skills reference `ArticleStatus.Approved`, `RejectedByEditorId`, `Content` -- these must be updated after implementation
4. **Test updates** -- `EventApprovalServiceTests` currently assert that articles are set to `Approved`/`Rejected`; these assertions must be removed

### Files Affected

**Core:**
- `Core/DomainModels/Article.cs` -- remove `Content`, `RejectedByEditorId`; remove `Approved`, `Published` from `ArticleStatus`
- `Core/DomainModels/AI/ArticleGenerationResult.cs` -- DELETE
- `Core/Interfaces/AI/IArticleGenerator.cs` -- DELETE
- `Core/Interfaces/Repositories/IArticleRepository.cs` -- remove `UpdateRejectionAsync`, `GetPendingForApprovalAsync`, `CountPendingForApprovalAsync`; add `RejectAsync(Guid id, string reason, CT)`

**Infrastructure:**
- `Infrastructure/Persistence/Entity/ArticleEntity.cs` -- remove `Content`, `RejectedByEditorId`
- `Infrastructure/Persistence/Mappers/ArticleMapper.cs` -- remove `Content` and `RejectedByEditorId` from `ToDomain`, `ToEntity`, `FromAnalysisResult`
- `Infrastructure/Persistence/Repositories/ArticleRepository.cs` -- remove `UpdateRejectionAsync`, `GetPendingForApprovalAsync`, `CountPendingForApprovalAsync`; add `RejectAsync`
- `Infrastructure/Persistence/Configurations/ArticleConfiguration.cs` -- no changes needed (string conversion still applies to remaining enum values)
- `Infrastructure/AI/HaikuKeyFactsExtractor.cs` -- change `article.Content` to `article.OriginalContent` in prompt
- `Infrastructure/AI/ClaudeArticleGenerator.cs` -- DELETE
- `Infrastructure/Services/EventApprovalService.cs` -- remove the `foreach` loops that set article statuses to `Approved`/`Rejected`
- `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` -- remove `IArticleGenerator` registration (lines 100-105)
- `Infrastructure/Persistence/Migrations/` -- new migration: drop `Content` column, drop `RejectedByEditorId` column, SQL update for status values

**Api:**
- `Api/Models/ArticleDetailDto.cs` -- remove `Content` parameter
- `Api/Mappers/ArticleMapper.cs` -- remove `Content` from `ToDetailDto`
- `Api/Controllers/ArticlesController.cs` -- remove `GetPending` endpoint; keep `GetById`

**Worker:**
- `Worker/Workers/ArticleAnalysisWorker.cs` -- update `UpdateRejectionAsync` calls to use new `RejectAsync` method

**UI:**
- `UI/src/features/articles/ArticleDetailPage.tsx` -- remove "FULL CONTENT" section (lines 181-197)
- `UI/src/features/articles/ArticlesPage.tsx` -- this page may need repurposing or removal since its data source (`GET /articles` with pending articles) is removed. Consider keeping it as a simple article browser (all articles, not just pending) or removing it entirely.
- `UI/src/api/generated/` -- regenerate from updated Swagger spec

**Tests:**
- `Tests/Infrastructure.Tests/Services/EventApprovalServiceTests.cs` -- remove assertions about `ArticleStatus.Approved` and `ArticleStatus.Rejected` on articles
- `Tests/Worker.Tests/Workers/ArticleAnalysisWorkerKeyFactsTests.cs` -- update if it references `Content` or removed statuses

**Dead code to delete:**
- `Core/DomainModels/AI/ArticleGenerationResult.cs`
- `Core/Interfaces/AI/IArticleGenerator.cs`
- `Infrastructure/AI/ClaudeArticleGenerator.cs`

## Implementation Notes

### For Feature-Planner

Sequence this in three phases to maintain a working system:

**Phase 1 -- Database migration and data fix (non-breaking):**
1. Create EF Core migration: drop `Content` column, drop `RejectedByEditorId` column
2. Include SQL data migration: `UPDATE articles SET "Status" = 'AnalysisDone' WHERE "Status" IN ('Approved', 'Published')`
3. Deploy migration before code changes

**Phase 2 -- Core and Infrastructure changes (breaking):**
4. Remove `Content` and `RejectedByEditorId` from `Article` domain model
5. Remove `Approved`, `Published` from `ArticleStatus` enum
6. Update `ArticleEntity` -- remove `Content`, `RejectedByEditorId`
7. Update `Infrastructure/Persistence/Mappers/ArticleMapper.cs` -- remove fields from `ToDomain`, `ToEntity`, `FromAnalysisResult`
8. Replace `UpdateRejectionAsync` with `RejectAsync(Guid id, string reason, CT)` in `IArticleRepository` and `ArticleRepository`
9. Remove `GetPendingForApprovalAsync` and `CountPendingForApprovalAsync` from `IArticleRepository` and `ArticleRepository`
10. Update `EventApprovalService` -- remove article status update loops
11. Fix `HaikuKeyFactsExtractor` -- use `OriginalContent` instead of `Content`
12. Delete `IArticleGenerator`, `ClaudeArticleGenerator`, `ArticleGenerationResult`
13. Remove `IArticleGenerator` DI registration from `InfrastructureServiceExtensions`
14. Update `ArticleAnalysisWorker` -- replace `UpdateRejectionAsync` calls with new `RejectAsync`

**Phase 3 -- API, UI, and cleanup (breaking):**
15. Remove `Content` from `ArticleDetailDto`
16. Update `Api/Mappers/ArticleMapper.ToDetailDto` -- remove `Content`
17. Remove `GetPending` endpoint from `ArticlesController`
18. Update `ArticleDetailPage.tsx` -- remove "FULL CONTENT" section
19. Update or remove `ArticlesPage.tsx` depending on whether article browsing is still needed
20. Regenerate API client (`npm run generate-api`)
21. Update tests: `EventApprovalServiceTests`, `ArticleAnalysisWorkerKeyFactsTests`

### Skills to Follow

- `.claude/skills/code-conventions/SKILL.md` -- enum conventions, domain model conventions, layer boundaries
- `.claude/skills/ef-core-conventions/SKILL.md` -- `ExecuteUpdateAsync` for the new `RejectAsync` method, migration conventions
- `.claude/skills/mappers/SKILL.md` -- updating mapper methods when removing properties
- `.claude/skills/api-conventions/SKILL.md` -- DTO changes, endpoint removal
- `.claude/skills/clean-code/SKILL.md` -- dead code removal (IArticleGenerator and related)
