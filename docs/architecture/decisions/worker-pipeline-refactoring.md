# Worker Pipeline Refactoring: Consolidate 7 Workers to 3

## Status

Proposed

---

## Context

The NewsParser worker pipeline currently consists of 7 independent background workers:

1. **SourceFetcherWorker** — fetches RSS/Telegram sources, creates RawArticles
2. **ArticleAnalysisWorker** — analyzes RawArticles with Gemini, generates embeddings, creates Articles
3. **ArticleGenerationWorker** — generates article content (title, content) with Claude for Articles with EventId
4. **EventClassificationWorker** — classifies Articles into Events using vector search + Claude
5. **PublicationGenerationWorker** — generates platform-specific content for Publications
6. **PublicationWorker** — publishes to Telegram/other platforms
7. **EventUpdateWorker** — creates Publication replies for EventUpdates

### Identified Problems

**Data Model Issues:**
- RawArticle entity is unnecessary — content is duplicated and managed inconsistently with Article
- Both RawArticle and Article store `Content`, `Title`, `Language`, `Category` — redundant storage and differing semantics
- Article must have a non-null RawArticle navigation property (FK), making it impossible to work with Article as a standalone entity
- Two entity lifecycles (RawArticleStatus: Pending→Analyzing→Rejected/Completed; ArticleStatus: Pending→Analyzing→AnalysisDone→Classifying→Processing→Approved→Rejected→Published) with inconsistent transitions

**Worker Architecture Issues:**
- **7 workers with significant overlap** — many steps are responsible for status transitions, retries, and exception handling separately
- **Race conditions** — no distributed locking, no `SELECT FOR UPDATE`, multiple workers can claim and process the same entity simultaneously
- **Duplicate embedding generation** — the pipeline calls `IGeminiEmbeddingService.GenerateEmbeddingAsync` three times for the same article content:
  1. ArticleAnalysisWorker: generates for raw article summary for deduplication
  2. EventClassificationWorker: generates for article title+summary for event matching
  3. EventUpdateWorker or PublicationGenerationWorker: may regenerate for publication context
- **No transaction isolation** on critical operations — batch queries are not locked, entities can be modified mid-processing
- **Unclear responsibility boundaries** — ArticleGenerationWorker generates title/content for articles with EventId, but this could be part of ArticleAnalysisWorker or deferred to PublicationWorker
- **Status transitions are fragile** — if a worker crashes, entities may get stuck in intermediate states (e.g., ArticleStatus.Analyzing) with no clear recovery path

**Operational Issues:**
- Difficult to debug the pipeline — 7 different worker logs to correlate
- Difficult to reason about ordering — dependency chain is implicit across worker code
- Difficult to test — each worker has complex internal control flow with multiple async operations

---

## Proposed New Architecture

### Three Workers: Clear Responsibility Domains

#### 1. SourceFetcherWorker → Article Creation

**Responsibility:** Convert source material into Article entities.

**Input:** Configured Sources (RSS feeds, Telegram channels)

**Process:**
- Fetch articles from all active sources
- Minimal deduplication: exact match on `SourceId + ExternalId` and URL
- Create Article entity in `Pending` status immediately (do not wait for AI processing)
- Store raw article content in Article.Content column (not in a separate RawArticle entity)
- Record source metadata: SourceId, OriginalUrl, PublishedAt, Language

**Output:** Article entities in `Pending` status ready for analysis

**Race condition mitigation:**
- Use `SELECT FOR UPDATE` in batch query to lock articles claimed for processing
- Constraint: `UNIQUE (SourceId, ExternalId)` prevents duplicate creation

---

#### 2. ArticleAnalysisWorker → Enrichment and Event Classification

**Responsibility:** Enrich articles with AI analysis and classify into Events.

**Input:** Articles in `Pending` status

**Process:**

**Phase A — Article Enrichment:**
- Run AI analysis (Gemini): extract category, tags, sentiment, language, summary
- Store results in Article: Category, Tags, Sentiment, Summary, ModelVersion
- Update Article status to `AnalysisDone`

**Phase B — Single Embedding Generation:**
- Generate embedding ONCE using article title + summary (not content)
- Store embedding in Article.Embedding column
- Use this single embedding for both deduplication and event classification

**Phase C — Event Classification:**
- Vector similarity search: find existing Events with similar embeddings
- Three outcomes:
  1. **Similarity > threshold_high (0.90):** automatically assign to that Event as "Initiator" or "Update"
  2. **Similarity in range [threshold_low, threshold_high] (0.70–0.90):** use Claude Haiku to decide
  3. **Similarity < threshold_low (0.70):** create new Event

- When assigning to existing Event:
  - Detect contradictions by comparing sentiment/tags with existing articles
  - Update Event.Summary using Claude (lightweight, structured output)
  - Update Event.Embedding via mathematical averaging of contributor vectors (no API call)
  - Set Article.Role (Initiator/Update/Contradiction) and Article.EventId

- When creating new Event:
  - Create Event with Article as Initiator
  - Set Event.Embedding = Article.Embedding
  - Set Event.Summary = Article.Summary (will be refined on updates)

**Output:** Articles in `AnalysisDone` status with EventId assigned, Event entities with updated embeddings

**Race condition mitigation:**
- Lock row with `SELECT FOR UPDATE` before phase A
- Atomic transaction: lock → analyze → classify → assign → update event embedding
- If lock times out, retry with exponential backoff

---

#### 3. PublicationWorker → Content Generation and Publishing

**Responsibility:** Generate platform-specific content and publish.

**Input:** Publication entities in `Pending` status (created by editor approval of an Event)

**Process:**

**Phase A — Content Generation:**
- Fetch Publication with related Article, Event, PublishTarget
- Generate platform-specific content using IContentGenerator:
  - Input: Article summary, Event summary, Event articles context, PublishTarget.SystemPrompt
  - Output: formatted content string (Telegram: hashtags + threading context, etc.)
- Store in Publication.GeneratedContent
- Update Publication status to `ContentReady`

**Phase B — Publishing:**
- Send content to target platform via IPublisher
- Capture external message ID (for Telegram threading)
- Record PublishLog entry
- Update Publication status to `Published` and set PublishedAt timestamp

**Event Update Handling (embedded in ArticleAnalysisWorker):**
- When an Article is added to an existing Event as an "Update" or "Contradiction":
  - The ArticleAnalysisWorker automatically updates Event.Summary
  - Editor (or automated rules) creates Publication entities for event updates
  - PublicationWorker publishes these as Telegram replies (linked via PublishLog parent ID)
  - No separate EventUpdateWorker needed

**Output:** Published events in Telegram and other platforms, PublishLog entries for threading

**Race condition mitigation:**
- Lock Publication row before content generation
- Single transaction: lock → generate → store → publish → log
- If any step fails, rollback and retry with backoff

---

## Article Lifecycle: New Status Flow

### Single Article Entity Lifecycle

```
Pending
  ↓ (SourceFetcherWorker creates)
  └─→ Created with Article.Content, SourceId, OriginalUrl, PublishedAt
      Articles in this state are waiting for AI processing

AnalysisDone
  ↓ (ArticleAnalysisWorker runs phase A)
  └─→ AI enrichment complete; has Category, Tags, Sentiment, Summary, Embedding
      Ready for event classification

AnalysisDone + EventId (assigned in ArticleAnalysisWorker phase C)
  ↓ (Editor approves Event)
  └─→ Event moves to publication queue; Article is part of approved Event

Approved
  ↓ (Editor explicitly approves Article/Event)
  └─→ Ready for publication

Published
  ↓ (PublicationWorker publishes)
  └─→ Article has been published to one or more platforms

Rejected
  ↓ (Editor rejects Article/Event)
  └─→ Article is marked as rejected; not published
      RejectionReason and RejectedByEditorId recorded
```

### Status Transition Rules

| Current Status | Transition | Trigger | Next Status | Conditions |
|---|---|---|---|---|
| Pending | analysis | SourceFetcherWorker creates | (no transition yet; article is created in Pending) | N/A |
| Pending | analyze | ArticleAnalysisWorker phase A starts | Analyzing | Claimed by worker |
| Analyzing | done | ArticleAnalysisWorker phase A completes | AnalysisDone | AI results stored, embedding generated |
| AnalysisDone | classify | ArticleAnalysisWorker phase C | (no status change; EventId is assigned) | EventId now set |
| AnalysisDone + EventId | reject | Editor rejects Event | Rejected | RejectionReason recorded |
| AnalysisDone + EventId | approve | Editor approves Event | Approved | Event moves to publication |
| Approved | publish | PublicationWorker publishes | Published | PublishLog created, external ID stored |
| Pending / AnalysisDone | reject | Editor rejects | Rejected | Manual rejection, RejectionReason recorded |

### Removed Status Values

- `Classifying` — no longer needed; event classification happens atomically in ArticleAnalysisWorker
- `Processing` — no longer needed; replaced by event-level publication workflow
- `RawArticleStatus` enum — entire RawArticle entity is eliminated

---

## Event Lifecycle: Updated Status and Embedding Strategy

### Event Status Flow

```
Active
  ↓ (created by ArticleAnalysisWorker when first article classifies to it)
  └─→ Event is collecting articles, summary is being updated

Active → Resolved
  ↓ (Editor or automated rule)
  └─→ No new updates expected; articles may still be published

Resolved → Archived
  ↓ (Automated after retention period)
  └─→ Old event moved to archive
```

### Event Embedding Strategy

**Generation:**
- Event embedding is initialized with the first Article.Embedding

**Updates (when articles are added):**
- Do NOT regenerate via API call
- Use mathematical averaging: `new_embedding = (old_embedding + article_embedding) / 2`
- This is fast, deterministic, and avoids redundant API calls
- Quality trade-off: slight drift over many additions, but acceptable for similarity search

**Deduplication:**
- When searching for similar events, use current Event.Embedding
- Vector similarity threshold (0.90 / 0.70) calibrated on this approach

---

## Key Design Decisions: Addressing Original Questions

### 1. Merging RawArticle into Article

**Decision:** Eliminate RawArticle entity entirely. Merge all content into Article.

**Rationale:**
- RawArticle and Article both store `Content`, `Title`, `Language`, creating confusion and duplication
- Article already has a required FK to RawArticle (line 6 of Article.cs), making them inseparable
- SourceFetcherWorker creates RawArticles; ArticleAnalysisWorker immediately wraps them in Articles — no independent lifecycle
- Single entity simplifies data model and eliminates race conditions between two parallel lifecycles

**Migration Strategy:**
1. Add `Article.Content` column (nullable, default NULL)
2. Add `Article.SourceId` FK column
3. Add `Article.OriginalUrl` column
4. Add `Article.Embedding` column (nullable)
5. Backfill existing Articles by copying data from RawArticle
6. Remove RawArticle references from Article navigation
7. Update ArticleRepository: no `GetPendingForAnalysisAsync` parameter (now just load Articles.Pending)
8. Remove RawArticleRepository from codebase
9. Drop RawArticle table in final migration

---

### 2. Article Status Flow: Single Entity Lifecycle

**Decision:** Consolidate `ArticleStatus` to reflect a single entity with clear state transitions. Remove intermediate states that don't represent meaningful work.

**Rationale:**
- Current statuses: Pending → Analyzing → AnalysisDone → Classifying → Processing → Approved → Rejected → Published (8 states)
- New statuses: Pending → Analyzing → AnalysisDone → Approved → Published / Rejected (5 states)
- Eliminates `Classifying` (event classification is synchronous within ArticleAnalysisWorker, not a separate worker state)
- Eliminates `Processing` (replaced by event-level publication workflow, not article-level)
- Easier to reason about: each status represents a clear phase of the pipeline

---

### 3. Event Status Flow

**Decision:** Keep Event status simple (Active → Resolved → Archived) and separate from Article approval workflow.

**Rationale:**
- Article approval is editorial (editor decides if this article should be published)
- Event status is operational (is this story still active or resolved?)
- These are orthogonal concerns; keep them independent
- Editor approves an Event (all its articles are approved together), Articles move to publication queue

---

### 4. Race Condition Prevention Strategy

**Decision:** Use database-level `SELECT FOR UPDATE` locking in batch queries.

**Rationale:**
- Simplest approach without external distributed lock service
- PostgreSQL supports it natively
- Fits the existing repository pattern (all queries are in `Infrastructure/Persistence/Repositories/`)
- Each worker uses a single transaction for its batch cycle (lock → process → update → release)

**Implementation Pattern:**

```csharp
// In repository batch query methods
var articles = await _context.Articles
    .Where(a => a.Status == ArticleStatus.Pending.ToString())
    .OrderBy(a => a.CreatedAt)
    .Take(batchSize)
    .ForUpdate()  // PostgreSQL extension: locks rows for this transaction
    .ToListAsync(cancellationToken);
```

**Trade-offs:**
- **Pro:** no external service, transactional consistency, works with PostgreSQL
- **Con:** scales to ~100 articles per batch (tuned by batchSize option), worker cycles block briefly during lock
- **Acceptable:** NewsParser is single-deployment, not distributed cluster

---

### 5. Embedding Strategy: Generate Once, Reuse Twice

**Decision:** Generate article embedding ONCE (in ArticleAnalysisWorker phase B) for both deduplication and event classification.

**Rationale:**
- Current pipeline calls `IGeminiEmbeddingService.GenerateEmbeddingAsync` 3+ times per article
- Embedding input: `title + summary` (semantic meaning is preserved)
- Used for:
  1. Deduplication: raw article uniqueness check
  2. Event classification: vector similarity search for related articles
- Same embedding serves both purposes; no need to regenerate

**Deduplication Logic:**
- Before enrichment: do simple exact-match dedup (URL, SourceId + ExternalId)
- After embedding generation: use vector similarity to detect near-duplicates in a time window
- If duplicate found: mark article as Rejected with reason "duplicate_by_vector"

---

### 6. Event Embedding Updates: Mathematical Averaging

**Decision:** Update Event.Embedding via mathematical averaging when articles are added. Do NOT call embedding API.

**Rationale:**
- Avoids redundant API calls (cost and latency)
- Deterministic: averaging produces consistent results
- Acceptable quality: slight drift over many additions (10+ articles), but vector search thresholds were calibrated with this approach
- Fast and simple to implement

**Formula:**
```
new_embedding = (old_embedding * old_article_count + new_article_embedding) / (old_article_count + 1)
```

---

### 7. Publication Workflow: Single Worker, Event-Level Lifecycle

**Decision:** Article generation (title + content) is deferred from Article analysis. It happens in PublicationWorker when a Publication is created.

**Current state:**
- ArticleGenerationWorker generates title/content for Articles with EventId (undefined timing)
- PublicationGenerationWorker generates platform-specific content for Publications
- Result: Title + Content live in Article; platform-specific content lives in Publication.GeneratedContent

**New state:**
- Article generation step is eliminated
- Article.Title and Article.Content come directly from SourceFetcherWorker (raw source content)
- When editor approves Event → Publication entities are created (one per publish target)
- PublicationWorker generates platform-specific content on-demand via IContentGenerator
  - Input: Article.Content, Event summary, event context, PublishTarget.SystemPrompt
  - Output: formatted post (Telegram with hashtags, etc.)
  - Stored in Publication.GeneratedContent

**Rationale:**
- Simpler pipeline: no intermediate Article.Title/Content generation step
- Content generation is tied to actual publication intent (editor approved Event)
- One content generation per publish target (platform-specific) rather than one per article
- IContentGenerator already exists and can generate from article content + event context

**Trade-off:**
- Article model loses auto-generated Title/Content fields
- Article.Title and Article.Content are source material (possibly raw/unpolished)
- Quality assurance: Title/Content are part of the publication workflow, reviewed by editor as part of Event approval

---

### 8. EventUpdateWorker: Merged into ArticleAnalysisWorker

**Decision:** Remove EventUpdateWorker. Event updates are handled synchronously in ArticleAnalysisWorker phase C.

**Current flow:**
- EventClassificationWorker classifies article into existing event
- EventUpdateWorker detects this and creates Publication entities for event updates
- PublicationWorker publishes these updates as Telegram replies

**New flow:**
- ArticleAnalysisWorker phase C classifies article into event
- If article is assigned as "Update" or "Contradiction" role:
  - ArticleAnalysisWorker updates Event.Summary immediately
  - Editor (or automated rules) creates Publication entities for the update
  - PublicationWorker publishes as replies (using PublishLog parent ID for threading)
- No separate EventUpdateWorker needed

**Rationale:**
- Simpler architecture: fewer workers
- Event updates are discovered eagerly (as articles arrive) rather than as a separate polling step
- Publication creation can be manual (editor creates per-event publications) or automated (publish new event updates automatically)

---

## Consequences

### Positive

1. **Simplified data model** — single Article entity, no RawArticle duplication
2. **Reduced worker count** — 7 → 3 workers, each with clear responsibility
3. **Fewer race conditions** — explicit locking via SELECT FOR UPDATE, atomic transactions per batch
4. **Single embedding per article** — cost and latency reduction (3x fewer embedding API calls)
5. **Easier to reason about** — 3 sequential stages: fetch → analyze → publish
6. **Clearer status transitions** — 5 article statuses instead of 8, no ambiguous intermediate states
7. **Simpler debugging** — 3 worker logs instead of 7
8. **Faster time-to-publication** — article analysis and event classification are atomic (no round-trip through separate worker)

### Negative / Risks

1. **Migration complexity** — merging RawArticle into Article requires data migration and FK adjustments
2. **Embedding quality drift** — averaging Event embeddings may lose precision on 20+ article events (acceptable per requirement; thresholds calibrated with this approach)
3. **Content generation deferral** — Article.Title/Content are now raw source material; polished content is only generated at publication time (acceptable; editor approval is the quality gate)
4. **Loss of article-level publication tracking** — Article.Status no longer has `Processing` state; instead, publication is tracked at Event + Publication level (acceptable; Publication entity already tracks platform-level status)
5. **SELECT FOR UPDATE scalability** — locks are held during batch processing; very large batches (1000+ articles) may cause brief contention (acceptable for single-deployment; mitigated by configurable batchSize)

### Files Affected

**Core changes:**
- `Core/DomainModels/Article.cs` — add Content, SourceId, OriginalUrl, Embedding; remove RawArticle FK; update enum
- `Core/DomainModels/RawArticle.cs` — DELETE
- `Core/DomainModels/Event.cs` — no changes (already has Embedding, Summary)
- `Core/Interfaces/Repositories/IArticleRepository.cs` — rename GetPendingForAnalysisAsync, add methods for SELECT FOR UPDATE
- `Core/Interfaces/Repositories/IRawArticleRepository.cs` — DELETE

**Infrastructure/Persistence:**
- `Infrastructure/Persistence/DataBase/NewsParserDbContext.cs` — remove RawArticle DbSet, update Article configuration
- `Infrastructure/Persistence/Repositories/ArticleRepository.cs` — add Content/SourceId/Embedding columns, implement locking, merge analysis logic
- `Infrastructure/Persistence/Repositories/RawArticleRepository.cs` — DELETE
- `Infrastructure/Persistence/Mappers/ArticleMapper.cs` — update to handle new Article fields
- `Infrastructure/Persistence/Mappers/RawArticleMapper.cs` — DELETE
- `Infrastructure/Persistence/Migrations/` — add migration for merging RawArticle into Article

**Worker changes:**
- `Worker/Workers/SourceFetcherWorker.cs` — create Article directly (not RawArticle), include Content and source metadata
- `Worker/Workers/ArticleAnalysisWorker.cs` — consolidate phases (enrichment → embedding → classification), implement locking, update Event embedding
- `Worker/Workers/ArticleGenerationWorker.cs` — DELETE
- `Worker/Workers/EventClassificationWorker.cs` — DELETE
- `Worker/Workers/EventUpdateWorker.cs` — DELETE
- `Worker/Workers/PublicationGenerationWorker.cs` — DELETE
- `Worker/Workers/PublicationWorker.cs` — add content generation phase (currently IContentGenerator is external)
- `Worker/Configuration/ArticleProcessingOptions.cs` — merge options from removed workers, simplify intervals

**API/UI:**
- `Api/Controllers/ArticlesController.cs` — update endpoints to work with new Article lifecycle (no raw articles endpoint)
- `Api/Mappers/ArticleMapper.cs` — update DTO construction
- `UI/src/api/generated/` — regenerate from new API (articles no longer reference raw articles)

**Tests:**
- All worker tests need refactoring to match new pipeline (3 workers instead of 7)
- Add concurrency tests for SELECT FOR UPDATE locking
- Update mappers and repository tests

---

## Implementation Notes

### For Feature-Planner

This refactoring is **not atomic**. It must be broken into carefully sequenced tasks to maintain a working system during migration:

**Phase 1: Data Model Migration (non-breaking)**
1. Add Article.Content, SourceId, OriginalUrl, Embedding columns (nullable, with defaults)
2. Migrate data from RawArticle to Article (one-time backfill script)
3. Write tests for new Article columns
4. Deploy; old workers still work

**Phase 2: ArticleRepository Updates (non-breaking)**
5. Implement SELECT FOR UPDATE in batch queries
6. Rename `GetPendingForAnalysisAsync` → `GetPendingAsync` (update all worker usages)
7. Add methods to update Content, SourceId, OriginalUrl atomically
8. Write repository tests for new methods

**Phase 3: SourceFetcherWorker Refactoring (non-breaking)**
9. Update SourceFetcherWorker to create Articles directly (include Content, SourceId, OriginalUrl)
10. Keep RawArticle creation as fallback (temporary; for backward compatibility during transition)
11. Deploy; validate article creation works

**Phase 4: ArticleAnalysisWorker Consolidation (breaking)**
12. Consolidate ArticleAnalysisWorker to do enrichment + embedding + event classification atomically
13. Implement Event embedding averaging (no API calls)
14. Update Article.Embedding column in batch queries
15. Remove old ArticleAnalysisWorker behavior
16. Deploy with new worker enabled

**Phase 5: Remove Old Workers (breaking)**
17. Remove ArticleGenerationWorker (defer title/content to publication time)
18. Remove EventClassificationWorker (merged into ArticleAnalysisWorker)
19. Remove EventUpdateWorker (event updates handled in ArticleAnalysisWorker phase C)
20. Remove PublicationGenerationWorker (merge into PublicationWorker)
21. Deploy; validate publication workflow still works

**Phase 6: Cleanup (non-breaking)**
22. Remove RawArticle entity from Core and Infrastructure
23. Remove RawArticleRepository
24. Drop RawArticle table migration
25. Update API and UI to not reference RawArticles
26. Full integration tests

**Key Checkpoints:**
- After Phase 1: articles are created with new fields; old worker pipeline still works
- After Phase 2: repository methods are thread-safe; old workers can use new batch queries
- After Phase 3: SourceFetcherWorker creates Articles; switch workers to new model in subsequent phases
- After Phase 4: ArticleAnalysisWorker is consolidated; vector search works with new embedding strategy
- After Phase 5: old workers are removed; publication workflow is simplified
- After Phase 6: RawArticle is completely gone; system is fully refactored

### Skills to Follow

- **code-conventions** — ensure SourceFetcherWorker, ArticleAnalysisWorker, PublicationWorker follow worker architecture (ExecuteAsync → ProcessAsync → ProcessXxxAsync)
- **ef-core-conventions** — SELECT FOR UPDATE locking, ExecuteUpdateAsync patterns, pgvector for vector similarity search
- **mappers** — update ArticleMapper (Infrastructure and Api) to handle new fields
- **test-writer** — write concurrency tests for locking, integration tests for refactored workers

---

## Questions for Clarification (Resolved in This ADR)

**Q1: Should Article contain content directly, or remain a thin wrapper?**
- **Resolved:** Article contains content directly. RawArticle is eliminated.

**Q2: How do we prevent race conditions across workers?**
- **Resolved:** Database-level SELECT FOR UPDATE locking on batch queries. Single transaction per cycle.

**Q3: When should article title/content be generated?**
- **Resolved:** Deferred to publication time. Article stores raw source content.

**Q4: How often should Event embeddings be regenerated?**
- **Resolved:** Never. Use mathematical averaging when articles are added.

**Q5: Should EventUpdateWorker exist?**
- **Resolved:** No. Event updates are handled synchronously in ArticleAnalysisWorker.

**Q6: What happens to duplicate embedding generation?**
- **Resolved:** Single embedding per article (title + summary). Used for both deduplication and classification.
