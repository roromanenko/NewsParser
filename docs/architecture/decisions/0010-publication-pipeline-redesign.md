# Publication Pipeline Redesign

## Status

Proposed

## Context

The current publication pipeline is fully automated: when an editor approves an Event, the `EventApprovalService` creates `Publication` entities in `Pending` status, the `PublicationWorker` generates content (Phase A), moves them to `ContentReady`, then immediately publishes them (Phase B). The editor has no opportunity to review, edit, or select media for the generated content before it goes live.

The `EventStatus` enum currently has five values: `Active`, `Approved`, `Rejected`, `Resolved`, `Archived`. The `Resolved` status is vestigial -- contradictions are resolved at the contradiction level (`Contradiction.IsResolved`), not at the event level. The `Approved` and `Rejected` statuses on Event were introduced to gate the publication pipeline, but this redesign moves that gating to the Publication entity itself.

The `PublicationStatus` enum currently has four values: `Pending`, `ContentReady`, `Published`, `Failed`. The flow is linear and fully automated: `Pending -> ContentReady -> Published` (or `Failed`).

The desired new behavior:
- Events only need two statuses: `Active` and `Archived`.
- Publications gain a richer status lifecycle: `Created -> GenerationInProgress -> ContentReady -> Approved -> Published` (with `Rejected` as an alternative terminal state and `Failed` kept for publish errors).
- Content generation is triggered explicitly by an editor from the Event page (not by approving the event).
- After content is generated (`ContentReady`), the editor can review, edit text, and select media files before sending.
- The `PublicationWorker` only handles the `GenerationInProgress` phase (AI content generation). Publishing is triggered by the editor via an API endpoint.

### Relationship to Existing ADRs

The `event-based-publication-with-key-facts.md` ADR introduced event-level approval (`IEventApprovalService`) and event-based content generation. This ADR supersedes the approval flow established there. The key facts extraction and event-based content generation concepts remain valid, but the approval/publication lifecycle changes fundamentally:

- **Before (current):** Approve Event -> create Pending publications -> worker generates content -> worker publishes automatically.
- **After (this ADR):** Editor triggers "Generate Content" on Event -> creates Created publication -> worker generates content -> editor reviews/edits -> editor sends -> API publishes.

## Options

### Option 1 -- Two Dedicated Workers: Generation + Publishing

The editor clicks "Generate Content" on the Event page, choosing a publish target. The API creates a Publication in `Created` status. A dedicated `PublicationGenerationWorker` picks up `Created` publications, transitions them to `GenerationInProgress`, generates AI content, then moves them to `ContentReady`. The editor reviews the publication, edits text, selects media, and clicks "Send" -- which calls an API endpoint that transitions the publication to `Approved`. A second dedicated `PublishingWorker` picks up `Approved` publications and sends them to the platform, transitioning to `Published` (or `Failed` on error).

**Pros:** Full separation of concerns -- generation and publishing are independent, retryable background processes; consistent with the project's worker pattern; AI work and platform I/O are both off the API thread; each worker can have its own retry/interval configuration; no synchronous blocking in the API.
**Cons:** Two workers to maintain instead of one; slight latency between editor clicking "Send" and actual delivery (worker polling interval).

### Option 2 -- Fully API-Driven Pipeline (No Worker)

All steps happen via API endpoints: generate content (synchronous AI call from API), review/edit, publish. No worker involvement.

**Pros:** Simpler mental model; no worker coordination needed; editor gets immediate feedback.
**Cons:** AI content generation on the API thread blocks the request (potentially 10-30 seconds); contradicts the project's established pattern of keeping AI work in workers; no retry mechanism for failed generations; API timeout risk.

### Option 3 -- Queue-Based with SignalR Notifications

Editor triggers generation via API, a message queue (or database queue) holds the job, worker processes it, and SignalR pushes a notification to the editor when content is ready.

**Pros:** Real-time feedback to editor; clean async separation.
**Cons:** Introduces SignalR (new infrastructure dependency not in the project); over-engineered for the current scale; the editor can simply poll or refresh.

## Decision

**Option 1 -- Two Dedicated Workers: Generation + Publishing.**

This option aligns with the project's established patterns: all async, potentially slow, or retryable work happens in workers. The key changes are:

1. The editor triggers publication creation via API (not the approval flow).
2. `PublicationGenerationWorker` handles `Created → GenerationInProgress → ContentReady`.
3. The editor reviews, edits text, selects media, then clicks "Send" (API sets status to `Approved`).
4. `PublishingWorker` handles `Approved → Published` (or `Failed`).

### Detailed Design

#### 1. EventStatus Enum Simplification

**`Core/DomainModels/Event.cs`** -- reduce `EventStatus` to two values:

```
public enum EventStatus
{
    Active,
    Archived
}
```

Remove `Approved`, `Rejected`, and `Resolved`. Events are either actively collecting articles (`Active`) or archived (`Archived`). The publication lifecycle is now entirely on the `Publication` entity.

This requires a database migration to convert existing `Approved`/`Rejected`/`Resolved` events. Strategy: migrate `Approved` and `Resolved` to `Active`, migrate `Rejected` to `Archived`.

#### 2. PublicationStatus Enum Expansion

**`Core/DomainModels/Publication.cs`** -- expand `PublicationStatus`:

```
public enum PublicationStatus
{
    Created,
    GenerationInProgress,
    ContentReady,
    Approved,
    Rejected,
    Published,
    Failed
}
```

- `Created` -- publication entity exists, waiting for worker to pick up (replaces `Pending`).
- `GenerationInProgress` -- worker is actively generating content (new, prevents double-processing).
- `ContentReady` -- AI content generated, awaiting editor review.
- `Approved` -- editor approved the content (intermediate state before publish).
- `Rejected` -- editor rejected the content.
- `Published` -- successfully sent to platform.
- `Failed` -- publish attempt failed.

The `Pending` status is removed and replaced by `Created`. The database migration should convert existing `Pending` values to `Created`.

#### 3. Publication Domain Model Changes

**`Core/DomainModels/Publication.cs`** -- add fields for media selection and editor review:

```
public List<Guid> SelectedMediaFileIds { get; set; } = [];
public Guid? ReviewedByEditorId { get; set; }
public DateTimeOffset? RejectedAt { get; set; }
public string? RejectionReason { get; set; }
```

- `SelectedMediaFileIds` -- IDs of `MediaFile` entities the editor selected for this publication (stored as jsonb array, same pattern as `Tags` and `KeyFacts`).
- `ReviewedByEditorId` -- the editor who approved/rejected the publication content.
- `RejectedAt` -- timestamp when content was rejected.
- `RejectionReason` -- why the editor rejected the generated content.

The existing `ApprovedAt` field is kept for when the editor approves.

#### 4. PublicationEntity Changes

**`Infrastructure/Persistence/Entity/PublicationEntity.cs`** -- add corresponding columns:

```
public List<Guid> SelectedMediaFileIds { get; set; } = [];
public Guid? ReviewedByEditorId { get; set; }
public DateTimeOffset? RejectedAt { get; set; }
public string? RejectionReason { get; set; }
```

#### 5. Remove IEventApprovalService and Event Approve/Reject Flow

**Delete:**
- `Core/Interfaces/Services/IEventApprovalService.cs`
- `Infrastructure/Services/EventApprovalService.cs`

**Remove from `Api/Controllers/EventsController.cs`:**
- `POST /events/{id}/approve` endpoint
- `POST /events/{id}/reject` endpoint
- Remove `IEventApprovalService` from constructor injection

**Remove from `Api/Models/EventDtos.cs`:**
- `ApproveEventRequest` record
- `RejectEventRequest` record

**Remove from UI:**
- `UI/src/features/events/ApproveEventModal.tsx`
- `UI/src/features/events/RejectEventModal.tsx`
- Remove approve/reject buttons and modal state from `EventDetailPage.tsx`
- Remove `approveEvent` and `rejectEvent` mutations from `useEventMutations.ts`

**Update DI registration** to remove `IEventApprovalService`.

#### 6. New IPublicationService Interface

**`Core/Interfaces/Services/IPublicationService.cs`** (new):

```
public interface IPublicationService
{
    Task<Publication> CreateForEventAsync(
        Guid eventId, Guid publishTargetId, Guid editorId,
        CancellationToken cancellationToken = default);
    
    Task<Publication> UpdateContentAsync(
        Guid publicationId, string content, List<Guid> selectedMediaFileIds,
        CancellationToken cancellationToken = default);
    
    Task<Publication> ApproveAsync(
        Guid publicationId, Guid editorId,
        CancellationToken cancellationToken = default);
    
    Task<Publication> RejectAsync(
        Guid publicationId, Guid editorId, string reason,
        CancellationToken cancellationToken = default);
    
    Task<Publication> PublishAsync(
        Guid publicationId, Guid editorId,
        CancellationToken cancellationToken = default);
}
```

**`Infrastructure/Services/PublicationService.cs`** (new):

- `CreateForEventAsync`:
  1. Load Event with Articles via `IEventRepository.GetDetailAsync`.
  2. Validate Event exists and is `Active`.
  3. Load and validate PublishTarget.
  4. Find initiator article (for `ArticleId` FK).
  5. Create Publication with status `Created`, set `EventId`.
  6. Persist via `IPublicationRepository.AddAsync`.
  7. Return the created Publication.

- `UpdateContentAsync`:
  1. Load Publication, validate it is in `ContentReady` status.
  2. Update `GeneratedContent` and `SelectedMediaFileIds`.
  3. Return the updated Publication.

- `ApproveAsync`:
  1. Load Publication, validate it is in `ContentReady` status.
  2. Update status to `Approved`, set `ApprovedAt` and `ReviewedByEditorId`.
  3. Return the updated Publication.

- `RejectAsync`:
  1. Load Publication, validate it is in `ContentReady` or `Approved` status.
  2. Update status to `Rejected`, set `RejectedAt`, `RejectionReason`, `ReviewedByEditorId`.
  3. Return the updated Publication.

- `PublishAsync` (renamed to `SendAsync` for clarity):
  1. Load Publication, validate it is in `ContentReady` or `Approved` status.
  2. Update status to `Approved`, set `ApprovedAt` and `ReviewedByEditorId`.
  3. Return the updated Publication.
  4. The `PublishingWorker` will pick it up asynchronously and send it to the platform.

#### 7. IPublicationRepository Changes

**`Core/Interfaces/Repositories/IPublicationRepository.cs`** -- changes:

**Remove:**
- `AddRangeAsync` -- no longer bulk-creating publications during approval.

**Rename:**
- `GetPendingForContentGenerationAsync` -> `GetPendingForGenerationAsync` -- fetches publications with status `Created` (not `Pending`).
- `GetReadyForPublishAsync` -> `GetPendingForPublishAsync` -- fetches publications with status `Approved` (picked up by the new `PublishingWorker`).

**Add:**
- `Task AddAsync(Publication publication, CancellationToken cancellationToken = default)` -- single publication creation.
- `Task<Publication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)` -- single publication load with PublishTarget.
- `Task<Publication?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)` -- full load with Event, Articles, MediaFiles, PublishTarget, PublishLogs.
- `Task<List<Publication>> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default)` -- all publications for an event.
- `Task UpdateContentAndMediaAsync(Guid id, string content, List<Guid> mediaFileIds, CancellationToken cancellationToken = default)` -- editor's text/media edits.
- `Task UpdateApprovalAsync(Guid id, Guid editorId, DateTimeOffset approvedAt, CancellationToken cancellationToken = default)` -- approval fields.
- `Task UpdateRejectionAsync(Guid id, Guid editorId, string reason, DateTimeOffset rejectedAt, CancellationToken cancellationToken = default)` -- rejection fields.

**Keep:**
- `UpdateStatusAsync`, `UpdateGeneratedContentAsync`, `UpdatePublishedAtAsync`, `AddPublishLogAsync`, `GetExternalMessageIdAsync`, `GetOriginalEventPublicationAsync`, `AddEventUpdatePublicationAsync`.

#### 8. PublicationGenerationWorker (renamed from PublicationWorker)

**Rename `Worker/Workers/PublicationWorker.cs` → `Worker/Workers/PublicationGenerationWorker.cs`** and simplify to only handle content generation:

- Remove `PublishReadyAsync` and `ProcessPublicationAsync` methods entirely.
- `GetPendingForGenerationAsync` fetches publications with status `Created`.
- The worker transitions:
  1. Sets status to `GenerationInProgress` immediately after picking up the publication (prevents double-processing on next worker cycle).
  2. Runs AI content generation.
  3. On success: sets status to `ContentReady`, stores the generated content.
  4. On failure: sets status to `Failed`, logs the error. The worker does NOT retry automatically -- if the editor wants to retry, they can trigger a new publication.
- The `GenerationInProgress` status acts as a processing lock for the worker loop.

#### 8b. New PublishingWorker

**`Worker/Workers/PublishingWorker.cs`** (new) -- dedicated worker for sending approved publications to platforms:

- Follows the same three-level structure as all other workers (`ExecuteAsync` → `ProcessBatchAsync` → `ProcessSingleAsync`).
- `IPublicationRepository.GetPendingForPublishAsync` fetches publications with status `Approved`.
- The worker transitions:
  1. Sets status to `Published` or `Failed` after attempting to send.
  2. Reuses the existing `IPublisher` / `TelegramPublisher` logic currently in `PublicationWorker.ProcessPublicationAsync`.
  3. Creates `PublishLog` entries on both success and failure (same as current behavior).
- Configured via a new `PublishingWorkerOptions` (interval, batch size) in `Worker/appsettings*.json`.
- Registered in `Worker/Program.cs` via `AddHostedService<PublishingWorker>()`.

#### 9. New API Controller: PublicationsController

**`Api/Controllers/PublicationsController.cs`** (new):

```
[ApiController]
[Route("publications")]
[Authorize(Roles = nameof(UserRole.Editor) + "," + nameof(UserRole.Admin))]
public class PublicationsController(
    IPublicationService publicationService,
    IPublicationRepository publicationRepository) : BaseController
```

Endpoints:

| Method | Route | Purpose | Request Body | Response |
|--------|-------|---------|--------------|----------|
| POST | `/publications/generate` | Trigger content generation | `CreatePublicationRequest(Guid EventId, Guid PublishTargetId)` | `201 PublicationDto` |
| GET | `/publications/{id:guid}` | Get publication detail | -- | `200 PublicationDetailDto` |
| GET | `/publications/by-event/{eventId:guid}` | List publications for an event | -- | `200 List<PublicationListItemDto>` |
| PUT | `/publications/{id:guid}/content` | Edit generated text and media selection | `UpdatePublicationContentRequest(string Content, List<Guid> SelectedMediaFileIds)` | `200 PublicationDetailDto` |
| POST | `/publications/{id:guid}/approve` | Approve content | -- | `200 PublicationDetailDto` |
| POST | `/publications/{id:guid}/reject` | Reject content | `RejectPublicationRequest(string Reason)` | `200 PublicationDetailDto` |
| POST | `/publications/{id:guid}/send` | Approve and queue for publishing (worker sends) | -- | `200 PublicationDetailDto` |

#### 10. New API DTOs

**`Api/Models/PublicationDtos.cs`** (new):

```
record PublicationListItemDto(Guid Id, string Status, string TargetName, string Platform, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);
record PublicationDetailDto(Guid Id, string Status, string TargetName, string Platform, string GeneratedContent, List<MediaFileDto> AvailableMedia, List<Guid> SelectedMediaFileIds, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? PublishedAt, string? RejectionReason);
record CreatePublicationRequest(Guid EventId, Guid PublishTargetId);
record UpdatePublicationContentRequest(string Content, List<Guid> SelectedMediaFileIds);
record RejectPublicationRequest(string Reason);
```

`AvailableMedia` in `PublicationDetailDto` is the union of all `MediaFile` entities from all Articles belonging to the Publication's Event. This lets the editor choose which images/videos to include.

#### 11. New API Mapper

**`Api/Mappers/PublicationMapper.cs`** (new):

- `ToListItemDto(this Publication pub)` -- lightweight for list views.
- `ToDetailDto(this Publication pub, List<MediaFile> availableMedia, string publicBaseUrl)` -- full detail with available media for editor review.

#### 12. EventsController Changes

**`Api/Controllers/EventsController.cs`** -- changes:

- Remove `approve` and `reject` endpoints.
- Remove `IEventApprovalService` dependency.
- Keep `UpdateStatus` endpoint (still needed for archiving).
- The `UpdateStatus` endpoint now only accepts `Active` and `Archived` as valid statuses.

#### 13. UI Changes

**Remove:**
- `UI/src/features/events/ApproveEventModal.tsx`
- `UI/src/features/events/RejectEventModal.tsx`

**Modify `EventDetailPage.tsx`:**
- Remove Approve/Reject buttons.
- Add "Generate Content" button (visible when event is `Active`). Clicking opens a modal/slide-over to select a publish target, then calls `POST /publications/generate`.
- Add a "Publications" tab showing the list of publications for this event (calls `GET /publications/by-event/{eventId}`).

**New feature directory `UI/src/features/publications/`:**
- `usePublications.ts` -- React Query hook for fetching publications by event.
- `usePublicationDetail.ts` -- React Query hook for single publication detail.
- `usePublicationMutations.ts` -- mutations for update content, approve, reject, publish.
- `PublicationDetailPage.tsx` -- full editor page: shows generated content in an editable textarea, media gallery with checkboxes for selection, approve/reject/send buttons.
- `GenerateContentModal.tsx` -- modal for selecting publish target and triggering generation.

**Router addition:**
- `publications/:id` route pointing to `PublicationDetailPage`.

#### 14. IPublisher Interface -- Media Support

The `IPublisher.PublishAsync` currently only sends text. To support media in publications, the interface needs to accept media information. However, this is a significant change to `IPublisher` and `TelegramPublisher`. 

**Decision: Defer media sending to a follow-up ADR.** This ADR establishes the pipeline where the editor can select media files and they are stored on the Publication, but the actual sending of media alongside text to Telegram (using `sendPhoto`/`sendMediaGroup` instead of `sendMessage`) is a separate concern. The `SelectedMediaFileIds` are persisted and available for when media publishing is implemented.

#### 15. Database Migration

One migration covering:
- `PublicationEntity`: add `SelectedMediaFileIds` (jsonb), `ReviewedByEditorId` (Guid?), `RejectedAt` (DateTimeOffset?), `RejectionReason` (string?).
- Data migration: convert `Publication.Status` from `Pending` to `Created`.
- Data migration: convert `Event.Status` from `Approved`/`Resolved` to `Active`, from `Rejected` to `Archived`.
- Remove the `Approved`, `Rejected`, `Resolved` values from any event status checks in code.

## Consequences

### Positive

1. **Editor control over publications** -- editors review, edit, and approve generated content before it reaches the audience.
2. **Media selection** -- editors choose which images/videos accompany a publication, drawn from all articles in the event.
3. **Simpler event model** -- events are either active or archived; the complexity of approval/rejection moves to where it belongs (publications).
4. **Cleaner separation of concerns** -- content generation (`PublicationGenerationWorker`) and publishing (`PublishingWorker`) are independent, separately configurable background processes.
5. **Retry-friendly** -- if content generation fails, the publication stays in `GenerationInProgress` and the worker retries; if publishing fails, the editor can retry via the API.

### Negative / Risks

1. **Breaking change to existing flow** -- the current automated publish pipeline stops working; editors must manually send each publication.
2. **Removal of event approval** -- existing `Approved`/`Rejected` events need data migration.
3. **New UI pages required** -- publication detail page with text editing and media selection is net-new UI work.
4. **Media sending deferred** -- the `IPublisher` interface does not yet support media; selected media files are stored but not sent until a follow-up change.
5. **Latency for editor** -- after triggering generation, the editor must wait for the worker cycle (configurable interval) before content appears.

### Files Affected

**Core (domain models and interfaces):**
- `Core/DomainModels/Event.cs` -- simplify `EventStatus` enum
- `Core/DomainModels/Publication.cs` -- expand `PublicationStatus` enum, add new fields
- `Core/Interfaces/Services/IPublicationService.cs` -- new interface
- `Core/Interfaces/Services/IEventApprovalService.cs` -- DELETE
- `Core/Interfaces/Repositories/IPublicationRepository.cs` -- add/remove/rename methods

**Infrastructure:**
- `Infrastructure/Persistence/Entity/PublicationEntity.cs` -- add new columns
- `Infrastructure/Persistence/Mappers/PublicationMapper.cs` -- map new fields
- `Infrastructure/Persistence/Repositories/PublicationRepository.cs` -- implement new methods, remove old ones
- `Infrastructure/Services/PublicationService.cs` -- new implementation
- `Infrastructure/Services/EventApprovalService.cs` -- DELETE
- `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` -- update DI registrations
- `Infrastructure/Persistence/Migrations/` -- new migration

**Worker:**
- `Worker/Workers/PublicationWorker.cs` -- rename to `PublicationGenerationWorker.cs`, remove publish phase, update status to use `Created`/`GenerationInProgress`/`ContentReady`
- `Worker/Workers/PublishingWorker.cs` -- new worker, picks up `Approved` publications and sends to platform
- `Worker/Program.cs` -- register `PublishingWorker` as hosted service, add `PublishingWorkerOptions`

**API:**
- `Api/Controllers/PublicationsController.cs` -- new controller
- `Api/Controllers/EventsController.cs` -- remove approve/reject endpoints, restrict status values
- `Api/Models/PublicationDtos.cs` -- new DTOs
- `Api/Models/EventDtos.cs` -- remove `ApproveEventRequest`, `RejectEventRequest`
- `Api/Mappers/PublicationMapper.cs` -- new mapper (Api layer)
- `Api/Mappers/EventMapper.cs` -- no structural changes, but `statusColor` in UI changes

**UI:**
- `UI/src/features/events/ApproveEventModal.tsx` -- DELETE
- `UI/src/features/events/RejectEventModal.tsx` -- DELETE
- `UI/src/features/events/EventDetailPage.tsx` -- replace approve/reject with generate content + publications tab
- `UI/src/features/events/useEventMutations.ts` -- remove approve/reject mutations
- `UI/src/features/publications/` -- new feature directory (hooks, pages, modals)
- `UI/src/router/index.tsx` -- add publications route
- Regenerate `UI/src/api/generated/` via `npm run generate-api`

## Implementation Notes

### For Feature-Planner

This change should be sequenced in four phases to keep the system working throughout:

**Phase 1 -- Domain and Database (non-breaking groundwork):**
1. Expand `PublicationStatus` enum with new values (`Created`, `GenerationInProgress`, `Approved`, `Rejected`) -- keep `Pending` temporarily for backward compatibility.
2. Add new fields to `Publication` domain model and `PublicationEntity`.
3. Update `PublicationMapper` (Infrastructure layer) for new fields.
4. Create database migration (add new columns, convert `Pending` to `Created` in data, convert event statuses).
5. Simplify `EventStatus` enum to `Active` and `Archived`.

**Phase 2 -- Backend Services and Repository (new functionality):**
6. Add new methods to `IPublicationRepository` and implement them.
7. Create `IPublicationService` interface and `PublicationService` implementation.
8. Rename `PublicationWorker` → `PublicationGenerationWorker`; update to handle `Created → GenerationInProgress → ContentReady` only.
9. Create new `PublishingWorker` that picks up `Approved` publications and sends them to platforms (reuse existing `IPublisher` logic).
10. Remove `IEventApprovalService` and `EventApprovalService`.
11. Update DI registrations.

**Phase 3 -- API Layer (expose new endpoints):**
12. Create `PublicationsController` with all endpoints.
13. Create `PublicationDtos.cs` request/response models.
14. Create `Api/Mappers/PublicationMapper.cs` for domain-to-DTO mapping.
15. Update `EventsController` -- remove approve/reject endpoints, restrict status values in `UpdateStatus`.
16. Remove `ApproveEventRequest` and `RejectEventRequest` from `EventDtos.cs`.

**Phase 4 -- UI (new publication workflow):**
17. Remove `ApproveEventModal.tsx` and `RejectEventModal.tsx`.
18. Create `UI/src/features/publications/` with hooks and components.
19. Update `EventDetailPage.tsx` -- add "Generate Content" button and publications tab.
20. Create `PublicationDetailPage.tsx` -- editor review page with text editing and media selection.
21. Add route for `publications/:id` in router.
22. Regenerate API client (`npm run generate-api`).

### Skills to Follow

- `.claude/skills/code-conventions/SKILL.md` -- worker architecture (three-level structure), Options pattern, interface organization, domain model conventions, enum storage as strings.
- `.claude/skills/clean-code/SKILL.md` -- method length limits for `PublicationService`, guard clauses, naming conventions.
- `.claude/skills/api-conventions/SKILL.md` -- controller structure, route naming (lowercase plural), DTO naming (`PublicationDetailDto`, `CreatePublicationRequest`), HTTP methods and status codes, `BaseController` usage.
- `.claude/skills/ef-core-conventions/SKILL.md` -- `ExecuteUpdateAsync` for all updates, enum filtering with `.ToString()`, include patterns for detail vs list vs worker queries, `CancellationToken` as last parameter.
- `.claude/skills/mappers/SKILL.md` -- new `Api/Mappers/PublicationMapper.cs` follows `ToListItemDto`/`ToDetailDto` pattern, `Infrastructure/Persistence/Mappers/PublicationMapper.cs` updated for new fields.
