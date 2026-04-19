# Publication Regeneration with Editor Feedback

## Status

Proposed

## Context

After the `PublicationGenerationWorker` produces a draft (Publication in `ContentReady`), the editor currently has three choices via `PublicationsController`: edit the text inline (`PUT /publications/{id}/content`), approve (`POST /publications/{id}/approve`), or reject (`POST /publications/{id}/reject`). There is no way to ask the AI to regenerate the post with guidance such as "make it shorter", "emphasize the second source", or "drop the political angle".

Today the only path to re-run the generator is to reject the publication and create a brand-new one via `POST /publications/generate`. That loses the editor's feedback (no history, no accountability) and re-runs the generator with the exact same prompt, so the same output is likely.

This ADR adds a dedicated regeneration flow. The backend scope only — no UI work.

### Existing patterns we will reuse

- **`PublicationStatus` lifecycle** (`Core/DomainModels/Publication.cs`) — `Created → GenerationInProgress → ContentReady → Approved → Published | Rejected | Failed`. Setting a publication back to `Created` is exactly what the existing `PublicationGenerationWorker` looks for (`PublicationSql.GetPendingForGeneration` filters on `"Status" = 'Created'` with `FOR UPDATE ... SKIP LOCKED`).
- **`IPublicationService`** (`Infrastructure/Services/PublicationService.cs`) — every editor-triggered state transition (create, update content, approve, reject, send) is a method on the service. Regeneration follows the same shape.
- **DbUp migrations** — `Infrastructure/Persistence/Sql/0001_baseline.sql`, `0002_add_event_importance.sql`. We will add `0003_add_publication_editor_feedback.sql`.
- **`IContentGenerator.GenerateForPlatformAsync(..., string? updateContext = null)`** — the generator already accepts an optional extra-context string, and `ClaudeContentGenerator` has a `BuildUpdatePrompt` branch for it.
- **`Api/Mappers/PublicationMapper.cs`** — `ToListItemDto` / `ToDetailDto`. New field surfaces through the same mapper.

### Fields on `Publication` we must NOT reuse

Two existing fields look tempting but carry different semantics and must not be overloaded:

1. **`ParentPublicationId`** — already used by `PublishingWorker.ResolveAndPublishAsync` (lines 176-190) and `TelegramPublisher.PublishReplyAsync` to send the post as a **Telegram reply** to the parent's message. Using it to mean "previous regeneration iteration" would either make the post publish as a reply to itself or break reply threading for event-update publications (`AddEventUpdatePublicationAsync`, `GetOriginalEventPublicationAsync`).
2. **`UpdateContext`** — already used by `ClaudeContentGenerator.BuildUpdatePrompt` to generate **short follow-up reply messages** ("THIS IS AN UPDATE to an existing published story. FORMAT: Short reply message, 1-3 sentences max."). Its meaning is "a new fact to append to an already-published story", which is the opposite of "regenerate the full draft with guidance".

Regeneration needs its own field with its own prompt branch.

---

## Options

### Option 1 — In-place regeneration with a single `EditorFeedback` field (chosen)

Add one nullable string field `EditorFeedback` to `Publication`. The editor calls `POST /publications/{id}/regenerate` with `{ "feedback": "make it shorter" }`. The service stores the feedback on the existing row, sets status back to `Created`, and clears `GeneratedContent`. The existing `PublicationGenerationWorker` picks the row up on its next cycle. `ClaudeContentGenerator` gets a new branch `BuildRegenerationPrompt(evt, target, feedback, previousContent)` (the previous content can be kept in-memory for the same worker cycle — we do not need to persist it separately because we want to regenerate, not amend).

**Pros:**
- Single row per publication — no new table, no cascade concerns, no join changes to `GetByEventIdAsync`.
- Reuses the existing `Created → GenerationInProgress → ContentReady` pipeline with zero worker changes (only the prompt branch changes).
- Keeps `PublicationDetailDto` flat — one extra `string? EditorFeedback` field.
- Consistent with how `UpdateContext` was added for event-update publications: one nullable string, one new prompt branch.

**Cons:**
- Only the **latest** feedback is retained. If an editor regenerates twice with different feedback, iteration #2's feedback overwrites iteration #1's. For an MVP backend feature this is acceptable — if audit history becomes a requirement later, we add `publication_regenerations` as a side table in a follow-up ADR.
- Previous generated content is lost (overwritten). This is a feature, not a bug: the editor explicitly asked to regenerate.

### Option 2 — Versioned publications via `ParentPublicationId` chain

Every regeneration inserts a **new** `Publication` row with `ParentPublicationId` pointing at the previous version. The old row stays in the DB for history.

**Pros:**
- Full history for free.
- Editor could diff versions.

**Cons:**
- `ParentPublicationId` already means "publish as Telegram reply to parent" (`PublishingWorker` + `TelegramPublisher.PublishReplyAsync`). Reusing it for regeneration lineage would silently cause regenerated posts to publish as replies to a draft that was never published — a data-consistency breakage.
- Would require a second lineage field (`RegeneratedFromPublicationId`) plus changes to all list/detail queries to filter out superseded drafts.
- Duplicates `EventId` / `PublishTargetId` / `SelectedMediaFileIds` per iteration.
- `GetByEventIdAsync` and the paged `GetAllAsync` would start returning draft noise unless every query learns the new "latest version only" filter.

### Option 3 — Side table `publication_regenerations` storing every (feedback, generatedContent, timestamp)

Keep the `Publication` row canonical; append one row per regeneration to a new table for full audit.

**Pros:**
- Full history, no semantic overloading of existing fields.
- Clean separation: `publications` holds the current state; `publication_regenerations` holds the log.

**Cons:**
- Two tables to keep in sync (transactional write on regenerate).
- Extra repository method + SQL + mapper + DTO surface.
- Over-engineered for the stated requirement (editors want the feature to work; history is not mentioned).
- Still need Option 1's `EditorFeedback`-style prompt input on the current iteration, so this is "Option 1 plus a log table", not an alternative.

---

## Decision

**Option 1 — single `EditorFeedback` field, in-place regeneration by resetting status to `Created`.**

Rationale:
- Matches the established pattern of "add one nullable string + one prompt branch" (how `UpdateContext` was introduced).
- Zero changes to the worker or the publishing pipeline — the state machine already supports `Created → GenerationInProgress → ContentReady`, so setting status back to `Created` is a legal transition.
- Keeps API surface minimal: one new endpoint, one new request DTO, one new field on the detail DTO.
- If audit history is later requested, Option 3's side table can be added in a follow-up ADR without migrating existing data.

### Detailed Design

#### 1. Domain model

**`Core/DomainModels/Publication.cs`** — add one field:

```
public string? EditorFeedback { get; set; }
```

No new status values. No new enums. `PublicationStatus` already has `Created` / `GenerationInProgress` / `ContentReady`.

#### 2. Persistence

**`Infrastructure/Persistence/Entity/PublicationEntity.cs`** — add the matching nullable column:

```
public string? EditorFeedback { get; set; }
```

**`Infrastructure/Persistence/Mappers/PublicationMapper.cs`** — add `EditorFeedback` to both `ToDomain` and `ToEntity`.

**`Infrastructure/Persistence/Sql/0003_add_publication_editor_feedback.sql`** (new DbUp script):

```sql
ALTER TABLE publications ADD COLUMN IF NOT EXISTS "EditorFeedback" TEXT NULL;
```

**Idempotency notes** (matches `0002_add_event_importance.sql` exactly):
- `ADD COLUMN IF NOT EXISTS` — re-running the script is a no-op if the column already exists. No error, no data loss.
- `TEXT NULL` (not `NOT NULL`) — backfill-free: existing rows get `NULL` automatically; no `UPDATE ... SET EditorFeedback = ''` step required.
- No index, no default, no constraint — nothing else to make idempotent. If a later change adds a constraint or index, use `CREATE INDEX IF NOT EXISTS` / `ALTER TABLE ... ADD CONSTRAINT IF NOT EXISTS` in a follow-up script rather than mutating this one (DbUp scripts are forward-only and `0003` may already have run in environments).

Embedded as a resource the same way as `0002_add_event_importance.sql` (set `<EmbeddedResource Include="Persistence/Sql/*.sql" />` already covers it in `Infrastructure.csproj`). Picked up automatically by `DbUpMigrator.Migrate()` at startup.

**`Infrastructure/Persistence/Repositories/Sql/PublicationSql.cs`** — update every SELECT list (`PublicationColumns`, `GetPendingForGeneration`, `GetPendingForPublish`, `GetById`, `GetDetailPublicationWithTarget`, `GetByEventId`, `GetAll`, `GetOriginalEventPublication`) and the `Insert` statement to include `EditorFeedback`. Add one new statement:

```
public const string RequestRegeneration = """
    UPDATE publications
    SET "Status"           = 'Created',
        "EditorFeedback"   = @feedback,
        "GeneratedContent" = ''
    WHERE "Id" = @id
    """;
```

Clearing `GeneratedContent` on regeneration request is deliberate: an empty string makes it obvious in the detail view that a new draft is pending. The worker overwrites it on success.

**`Core/Interfaces/Repositories/IPublicationRepository.cs`** — add one method:

```
Task RequestRegenerationAsync(
    Guid id,
    string feedback,
    CancellationToken cancellationToken = default);
```

**`Infrastructure/Persistence/Repositories/PublicationRepository.cs`** — implement `RequestRegenerationAsync` using the new SQL constant. Also update `BuildPublication`, `BuildPublicationWithoutArticle`, `BuildInsertParameters` to carry the new field.

#### 3. Service layer

**`Core/Interfaces/Services/IPublicationService.cs`** — add:

```
Task<Publication> RegenerateAsync(
    Guid publicationId,
    string feedback,
    CancellationToken cancellationToken = default);
```

**`Infrastructure/Services/PublicationService.cs`** — implement `RegenerateAsync`:

1. Load the publication via `publicationRepository.GetByIdAsync`. Throw `KeyNotFoundException` if null.
2. Validate `feedback` is not null/whitespace — throw `ArgumentException` (middleware maps to 400; controller also validates up-front for a cleaner error).
3. Validate status. Allowed source statuses: `ContentReady`, `Failed`. Forbidden: `Created`, `GenerationInProgress` (already queued), `Approved`, `Published`, `Rejected` (terminal or past the edit window). Throw `InvalidOperationException` otherwise.
4. Call `publicationRepository.RequestRegenerationAsync(id, feedback, cancellationToken)`.
5. Log at Information: publication id, event id, editor id (deferred — we do not have it here yet; see point 5 below).
6. Update the in-memory domain instance (`Status = Created`, `EditorFeedback = feedback`, `GeneratedContent = ""`) and return it.

**Editor attribution:** `PublicationService.UpdateContentAsync` already omits an editor id from its signature, and today only `ApproveAsync` / `RejectAsync` / `SendAsync` take one (because they set `ReviewedByEditorId`). Regeneration is intentionally **not** a review action — the editor is still iterating. We do **not** set `ReviewedByEditorId` on regeneration. The controller-level `[Authorize]` guarantees the request is authenticated, and the action is visible in application logs via `UserId`. If editor attribution on regeneration becomes a requirement, a follow-up change adds a `RegenerationRequestedByEditorId` column.

#### 4. AI prompt

**`Core/Interfaces/AI/IContentGenerator.cs`** — extend the signature with a new optional parameter:

```
Task<string> GenerateForPlatformAsync(
    Event evt,
    PublishTarget target,
    CancellationToken cancellationToken = default,
    string? updateContext = null,
    string? editorFeedback = null);
```

`updateContext` and `editorFeedback` are mutually exclusive scenarios and we do not combine them in a single call.

**`Infrastructure/AI/ClaudeContentGenerator.cs`** — extend the dispatcher:

```
var userPrompt = editorFeedback is not null
    ? BuildRegenerationPrompt(evt, target, editorFeedback)
    : updateContext is not null
        ? BuildUpdatePrompt(evt, target, updateContext)
        : BuildEventPrompt(evt, target);
```

Add `BuildRegenerationPrompt(Event evt, PublishTarget target, string editorFeedback)`. Structure:

```
CHANNEL: {target.Name}
This is a REGENERATION request. The previous draft was rejected by the editor.
EDITOR FEEDBACK (apply carefully, do not quote literally):
{editorFeedback}
EVENT TITLE: {evt.Title}
EVENT SUMMARY: {evt.Summary}
SOURCES:
{articlesSection}
```

The regeneration prompt reuses `BuildArticlesSection` so sources and key facts stay consistent with the initial generation. The system prompt (base + `target.SystemPrompt`) is unchanged — format/length rules still come from `telegram.txt`.

**No new prompt file.** The regeneration instruction is short and parameterized; splitting it into `Infrastructure/AI/Prompts/regeneration.txt` would be over-engineering and would require another `PromptsOptions` entry. If the prompt grows, we extract it later.

#### 5. Worker

**`Worker/Workers/PublicationGenerationWorker.cs`** — thread the new field through:

```
var content = await contentGenerator.GenerateForPlatformAsync(
    publication.Event,
    publication.PublishTarget,
    cancellationToken,
    updateContext: publication.UpdateContext,
    editorFeedback: publication.EditorFeedback);
```

No status-handling changes: the worker already transitions `Created → GenerationInProgress → ContentReady | Failed`. Because `EditorFeedback` is set **before** status flips to `Created`, the worker sees it on the very next poll.

On successful regeneration (i.e. after `UpdateGeneratedContentAsync` + `UpdateStatusAsync(ContentReady)`), `EditorFeedback` stays on the row. That is intentional: the detail DTO surfaces "this draft was regenerated following this feedback" so the editor remembers what they asked for. When a new regeneration is requested, `RequestRegenerationAsync` overwrites it. This matches Option 1's "latest feedback only" trade-off.

#### 6. API

**`Api/Models/PublicationDtos.cs`** — add one record:

```
public record RegeneratePublicationRequest(string Feedback);
```

Also extend `PublicationDetailDto` with `string? EditorFeedback` (append at the end to keep the positional record wire format stable for existing callers):

```
public record PublicationDetailDto(
    Guid Id,
    string Status,
    string TargetName,
    string Platform,
    string GeneratedContent,
    List<MediaFileDto> AvailableMedia,
    List<Guid> SelectedMediaFileIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PublishedAt,
    string? RejectionReason,
    string? EditorFeedback);
```

`PublicationListItemDto` is **not** extended — feedback is not a list-view concern.

**`Api/Validators/RegeneratePublicationRequestValidator.cs`** (new) — FluentValidation:

```
public class RegeneratePublicationRequestValidator : AbstractValidator<RegeneratePublicationRequest>
{
    public RegeneratePublicationRequestValidator()
    {
        RuleFor(x => x.Feedback).NotEmpty().MaximumLength(2000);
    }
}
```

The 2000-char cap is a safety rail against abusive prompt injection payloads — consistent with `ValidationOptions` usage elsewhere.

**`Api/Mappers/PublicationMapper.cs`** — add `EditorFeedback` to `ToDetailDto`:

```
public static PublicationDetailDto ToDetailDto(this Publication pub, List<MediaFile> availableMedia, string publicBaseUrl) => new(
    pub.Id,
    pub.Status.ToString(),
    pub.PublishTarget.Name,
    pub.PublishTarget.Platform.ToString(),
    pub.GeneratedContent,
    availableMedia.Select(m => m.ToDto(publicBaseUrl)).ToList(),
    pub.SelectedMediaFileIds,
    pub.CreatedAt,
    pub.ApprovedAt,
    pub.PublishedAt,
    pub.RejectionReason,
    pub.EditorFeedback);
```

**`Api/Controllers/PublicationsController.cs`** — add one endpoint:

```
[HttpPost("{id:guid}/regenerate")]
public async Task<ActionResult<PublicationDetailDto>> Regenerate(
    Guid id,
    [FromBody] RegeneratePublicationRequest request,
    CancellationToken cancellationToken = default)
{
    if (UserId is null)
        return Unauthorized();

    await publicationService.RegenerateAsync(id, request.Feedback, cancellationToken);

    var detail = await publicationRepository.GetDetailAsync(id, cancellationToken);
    if (detail is null)
        return NotFound();

    var availableMedia = ExtractAvailableMedia(detail);
    return Ok(detail.ToDetailDto(availableMedia, _publicBaseUrl));
}
```

Matches the shape of `Approve`, `Reject`, and `Send` — FluentValidation runs automatically on the request, service throws typed exceptions that `ExceptionMiddleware` maps (`KeyNotFoundException` → 404, `InvalidOperationException` → 409).

#### 7. Flow summary (happy path)

```
Editor clicks Regenerate (UI, future)
  → POST /publications/{id}/regenerate   body: { feedback: "make it shorter" }
  → PublicationsController.Regenerate
  → PublicationService.RegenerateAsync
      → IPublicationRepository.RequestRegenerationAsync   (status=Created, EditorFeedback set, GeneratedContent cleared)
  → 200 PublicationDetailDto                               (status: "Created", GeneratedContent: "", EditorFeedback: "...")

(next worker tick)
PublicationGenerationWorker.ProcessBatchAsync
  → picks up Created row
  → UpdateStatusAsync(GenerationInProgress)
  → IContentGenerator.GenerateForPlatformAsync(..., editorFeedback: "...")
      → ClaudeContentGenerator.BuildRegenerationPrompt
  → UpdateGeneratedContentAsync(new content)
  → UpdateStatusAsync(ContentReady)
```

#### 8. Edge cases and constraints

- **Double-click protection.** If the editor calls `/regenerate` twice in quick succession:
  - First call sets status `Created`, stores feedback A.
  - If the worker has already claimed the row (`FOR UPDATE ... SKIP LOCKED` + `UpdateStatusAsync(GenerationInProgress)`), the second call will see `GenerationInProgress` and be rejected with 409 by the status guard. This is the correct behavior.
  - If the worker has not yet claimed the row, the second call overwrites feedback A with feedback B — consistent with the "latest feedback wins" decision.
- **Regenerating a `Failed` publication.** Allowed. The existing create-a-new-publication workaround becomes unnecessary.
- **Regenerating after approval.** Not allowed. Once approved, the publication is either queued for sending (`Approved` → `PublishingWorker`) or already `Published`. If the editor changes their mind before send, they must `Reject` and create a new publication — a separate workflow we are not touching here.
- **Event archived between generations.** The worker already defends against missing/inactive events via the `publication.Event is null` check in `GenerateContentForPublicationAsync`. Regeneration reuses the same code path; no extra guard needed.

---

## Consequences

**Positive:**
- Editors can iterate on AI output instead of throwing drafts away. Directly addresses the stated need.
- Reuses the existing worker without any lifecycle changes — minimal risk to the publication pipeline.
- Additive schema change (one nullable column) — zero backfill, no data migration risk.
- The feedback is visible in the detail response, so the UI (future work) can show "last regenerated with feedback: ..." for context.

**Negative / Risks:**
- Only the latest feedback is stored. Editors lose the history of their iterations. Mitigation: if it becomes a real requirement, add `publication_regenerations` in a follow-up ADR.
- `PublicationDetailDto` gets one more field; any hand-written frontend consumer will need a regen (but the UI is out of scope for this ADR).
- The worker's retry-on-failure policy is unchanged (it sets `Failed` and does not auto-retry). Editors can now regenerate a `Failed` publication instead, which partly compensates.
- If AI usage is billed per call, this endpoint enables cheap loops of regeneration. Not a new risk (`POST /publications/generate` already allows this); no rate-limit work in scope.

**Files affected:**

*Core:*
- `Core/DomainModels/Publication.cs` — add `EditorFeedback`
- `Core/Interfaces/AI/IContentGenerator.cs` — add `editorFeedback` parameter
- `Core/Interfaces/Repositories/IPublicationRepository.cs` — add `RequestRegenerationAsync`
- `Core/Interfaces/Services/IPublicationService.cs` — add `RegenerateAsync`

*Infrastructure:*
- `Infrastructure/Persistence/Entity/PublicationEntity.cs` — add `EditorFeedback`
- `Infrastructure/Persistence/Mappers/PublicationMapper.cs` — map new field both directions
- `Infrastructure/Persistence/Repositories/PublicationRepository.cs` — implement `RequestRegenerationAsync`; carry field in all `BuildPublication*` helpers and `BuildInsertParameters`
- `Infrastructure/Persistence/Repositories/Sql/PublicationSql.cs` — extend every SELECT + the INSERT with `EditorFeedback`; add `RequestRegeneration` constant
- `Infrastructure/Persistence/Sql/0003_add_publication_editor_feedback.sql` — new DbUp migration (embedded resource)
- `Infrastructure/Services/PublicationService.cs` — implement `RegenerateAsync`
- `Infrastructure/AI/ClaudeContentGenerator.cs` — add `editorFeedback` parameter, `BuildRegenerationPrompt`, dispatcher update

*Worker:*
- `Worker/Workers/PublicationGenerationWorker.cs` — pass `publication.EditorFeedback` to the generator

*Api:*
- `Api/Models/PublicationDtos.cs` — add `RegeneratePublicationRequest`, extend `PublicationDetailDto` with `EditorFeedback`
- `Api/Validators/RegeneratePublicationRequestValidator.cs` — new FluentValidation validator
- `Api/Mappers/PublicationMapper.cs` — include `EditorFeedback` in `ToDetailDto`
- `Api/Controllers/PublicationsController.cs` — `POST /publications/{id}/regenerate`

*Tests (delegated to test-writer):*
- `Tests/Infrastructure.Tests/Services/PublicationServiceTests.cs` — RegenerateAsync: happy path, status guard (ContentReady/Failed allowed; Approved/Published/GenerationInProgress/Created rejected), not-found, empty feedback
- `Tests/Infrastructure.Tests/Repositories/PublicationRepositoryContractTests.cs` — `RequestRegenerationAsync` writes EditorFeedback, status goes to Created, GeneratedContent cleared
- `Tests/Worker.Tests/Workers/PublicationGenerationWorkerTests.cs` — worker passes EditorFeedback to `IContentGenerator`
- `Tests/Api.Tests/Controllers/PublicationsControllerTests.cs` — `POST /{id}/regenerate` returns 200, validator rejects empty/too-long feedback, 404 when missing

---

## Implementation Notes

### Sequencing for feature-planner

Order matters to keep the build green at every step.

1. **Schema + Domain groundwork (non-breaking).**
   - Add `EditorFeedback` to `Publication` domain model.
   - Add `EditorFeedback` to `PublicationEntity`.
   - Extend `PublicationMapper` (Infrastructure) for the new field, both directions.
   - Add `Infrastructure/Persistence/Sql/0003_add_publication_editor_feedback.sql` as an embedded resource and confirm it is picked up by DbUp.
   - Update every SQL in `PublicationSql.cs` plus `BuildPublication*` / `BuildInsertParameters` in the repository to include the new column. At this checkpoint the system behaves identically to before but the column is available.

2. **Repository method.** Add `RequestRegenerationAsync` to `IPublicationRepository` and implement it using the new `RequestRegeneration` SQL constant.

3. **AI generator.** Extend `IContentGenerator.GenerateForPlatformAsync` with the `editorFeedback` parameter. Add `BuildRegenerationPrompt` in `ClaudeContentGenerator` and update the dispatcher. Do this before the worker change so the worker compiles.

4. **Worker.** Pass `publication.EditorFeedback` in `PublicationGenerationWorker`.

5. **Service.** Add `RegenerateAsync` to `IPublicationService` and implement in `PublicationService` with status guards (`ContentReady` or `Failed` only) and not-found / empty-feedback checks.

6. **API.** Add `RegeneratePublicationRequest` and the `EditorFeedback` field on `PublicationDetailDto`. Add the validator. Extend `Api/Mappers/PublicationMapper.ToDetailDto`. Add the controller endpoint.

7. **Tests** (delegate to `test-writer` agent) — unit + contract + controller integration tests listed above.

### Skills to follow

- `.claude/skills/code-conventions/SKILL.md` — layer boundaries (no Dapper in Core; no business logic in controller), primary constructor style in the service, nullable string field convention.
- `.claude/skills/clean-code/SKILL.md` — guard clauses in `RegenerateAsync`, descriptive exception messages (`$"Publication {id} cannot be regenerated: status is {status}"`), no dead code from the removed workaround.
- `.claude/skills/api-conventions/SKILL.md` — `POST /publications/{id:guid}/regenerate` (kebab-case verb after `{id}`), `RegeneratePublicationRequest` record in `Api/Models/`, FluentValidation validator in `Api/Validators/`, `ActionResult<PublicationDetailDto>` return type, `UserId is null → Unauthorized()` guard, no inline DTO construction (use mapper).
- `.claude/skills/dapper-conventions/SKILL.md` — `RequestRegenerationAsync` uses `IDbConnectionFactory.CreateOpenAsync`, `CommandDefinition` with `cancellationToken`, SQL lives as a `const string` in `PublicationSql.cs` (not embedded in the repository method).
- `.claude/skills/mappers/SKILL.md` — one extension method per mapping direction, no logic inside mapper; add `EditorFeedback` at the end of `ToDetailDto`'s positional arguments.

### Related ADRs

- `docs/architecture/decisions/0010-publication-pipeline-redesign.md` — established the `Created → GenerationInProgress → ContentReady → Approved → Published` lifecycle this ADR builds on. Not superseded.
- `docs/architecture/decisions/event-based-publication-with-key-facts.md` — introduced `UpdateContext` and the event-update publication flow (Telegram reply semantics). This ADR explicitly does **not** overload `UpdateContext` or `ParentPublicationId`.
