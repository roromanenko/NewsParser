# Publication Pipeline Redesign

## Goal

Replace the automated event-approval-to-publish pipeline with an editor-controlled workflow
where editors explicitly trigger content generation per event, review the result, and send it
for publishing via a dedicated background worker.

## Affected Layers

- Core, Infrastructure, Worker, Api, UI

---

## Tasks

### Phase 1 — Domain and Database

#### Core

- [x] **Modify `Core/DomainModels/Publication.cs`** — expand `PublicationStatus` enum:
  add `Created`, `GenerationInProgress`, `Approved`, `Rejected`; keep `ContentReady`,
  `Published`, `Failed`; **keep `Pending` temporarily** (removed after data migration task).
  _Acceptance: enum compiles with all 8 values; no other file changed yet._
  _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/DomainModels/Publication.cs`** — add new domain fields to `Publication` class:
  `List<Guid> SelectedMediaFileIds`, `Guid? ReviewedByEditorId`,
  `DateTimeOffset? RejectedAt`, `string? RejectionReason`.
  _Acceptance: class compiles; no EF or infrastructure references inside Core._
  _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/DomainModels/Event.cs`** — simplify `EventStatus` enum to only
  `Active` and `Archived` (remove `Approved`, `Rejected`, `Resolved`).
  _Acceptance: enum has exactly 2 values; compiler errors in callers are expected and tracked
  by subsequent tasks._
  _Skill: .claude/skills/code-conventions/SKILL.md_

#### Infrastructure — Entity and Mapper

- [x] **Modify `Infrastructure/Persistence/Entity/PublicationEntity.cs`** — add matching
  columns: `List<Guid> SelectedMediaFileIds`, `Guid? ReviewedByEditorId`,
  `DateTimeOffset? RejectedAt`, `string? RejectionReason`.
  _Acceptance: entity class compiles; no domain model references inside it._
  _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Configurations/PublicationConfiguration.cs`** —
  configure `SelectedMediaFileIds` as jsonb column (same pattern as `Tags`/`KeyFacts` on
  Article). Map `ReviewedByEditorId`, `RejectedAt`, `RejectionReason` as nullable columns.
  _Acceptance: `OnModelCreating` compiles; migration can be generated without errors._
  _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Mappers/PublicationMapper.cs`** — add new fields
  to both `ToDomain` and `ToEntity` mappings:
  `SelectedMediaFileIds`, `ReviewedByEditorId`, `RejectedAt`, `RejectionReason`.
  _Acceptance: both methods compile and include all new properties; no properties are left
  unmapped._
  _Skill: .claude/skills/mappers/SKILL.md_

#### Infrastructure — Migration

- [x] **Add EF Core migration `AddPublicationPipelineRedesign`** — covers:
  1. Add columns `SelectedMediaFileIds` (jsonb), `ReviewedByEditorId` (uuid?),
     `RejectedAt` (timestamptz?), `RejectionReason` (text?) to `publications` table.
  2. Data migration SQL: `UPDATE publications SET "Status" = 'Created' WHERE "Status" = 'Pending'`.
  3. Data migration SQL: `UPDATE events SET "Status" = 'Active' WHERE "Status" IN ('Approved', 'Resolved')`.
  4. Data migration SQL: `UPDATE events SET "Status" = 'Archived' WHERE "Status" = 'Rejected'`.
  Run `dotnet ef migrations add AddPublicationPipelineRedesign` from `Infrastructure/`, then
  add the four SQL statements inside `migrationBuilder.Sql(...)` calls in the `Up` method,
  and their reverses in `Down`.
  _Acceptance: migration file exists under `Infrastructure/Persistence/Migrations/`;
  `Up` and `Down` are non-empty and include all data-migration SQL._
  _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Core/DomainModels/Publication.cs`** — remove the now-migrated `Pending`
  value from `PublicationStatus` enum.
  _Acceptance: `Pending` is gone; project still compiles (no remaining references after
  subsequent tasks update callers)._
  _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 2 — Backend Services and Repository

#### Core — Interfaces

- [x] **Modify `Core/Interfaces/Repositories/IPublicationRepository.cs`** — apply all
  interface changes from the ADR:
  - **Remove:** `AddRangeAsync`.
  - **Rename:** `GetPendingForContentGenerationAsync` → `GetPendingForGenerationAsync`
    (doc comment: fetches `Created` status).
  - **Rename:** `GetReadyForPublishAsync` → `GetPendingForPublishAsync`
    (doc comment: fetches `Approved` status).
  - **Add:** `Task AddAsync(Publication publication, CancellationToken ct = default)`.
  - **Add:** `Task<Publication?> GetByIdAsync(Guid id, CancellationToken ct = default)`.
  - **Add:** `Task<Publication?> GetDetailAsync(Guid id, CancellationToken ct = default)`.
  - **Add:** `Task<List<Publication>> GetByEventIdAsync(Guid eventId, CancellationToken ct = default)`.
  - **Add:** `Task UpdateContentAndMediaAsync(Guid id, string content, List<Guid> mediaFileIds, CancellationToken ct = default)`.
  - **Add:** `Task UpdateApprovalAsync(Guid id, Guid editorId, DateTimeOffset approvedAt, CancellationToken ct = default)`.
  - **Add:** `Task UpdateRejectionAsync(Guid id, Guid editorId, string reason, DateTimeOffset rejectedAt, CancellationToken ct = default)`.
  - **Keep unchanged:** `UpdateStatusAsync`, `UpdateGeneratedContentAsync`,
    `UpdatePublishedAtAsync`, `AddPublishLogAsync`, `GetExternalMessageIdAsync`,
    `GetOriginalEventPublicationAsync`, `AddEventUpdatePublicationAsync`.
  _Acceptance: interface-only file; no implementation; compiles._
  _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Create `Core/Interfaces/Services/IPublicationService.cs`** — new interface with
  five methods as specified in the ADR:
  `CreateForEventAsync`, `UpdateContentAsync`, `ApproveAsync`, `RejectAsync`, `SendAsync`.
  _Acceptance: interface-only file; no implementation; compiles._
  _Skill: .claude/skills/code-conventions/SKILL.md_

#### Infrastructure — Repository

- [x] **Modify `Infrastructure/Persistence/Repositories/PublicationRepository.cs`** —
  implement all `IPublicationRepository` changes:
  - Remove `AddRangeAsync`.
  - Rename `GetPendingForContentGenerationAsync` → `GetPendingForGenerationAsync`;
    change status filter from `Pending` to `Created`; add `UpdateStatusAsync` call to set
    `GenerationInProgress` immediately after locking rows (prevents double-processing).
  - Rename `GetReadyForPublishAsync` → `GetPendingForPublishAsync`;
    change status filter from `ContentReady` to `Approved`; load `PublishTarget`.
  - Implement `AddAsync`: single `db.Publications.AddAsync` + `SaveChangesAsync`.
  - Implement `GetByIdAsync`: load with `PublishTarget` only; `AsNoTracking`.
  - Implement `GetDetailAsync`: load with `Event` (with Articles), `PublishTarget`,
    `PublishLogs`; `AsNoTracking`.
  - Implement `GetByEventIdAsync`: filter by `EventId`; order by `CreatedAt`; load
    `PublishTarget`; `AsNoTracking`.
  - Implement `UpdateContentAndMediaAsync`: `ExecuteUpdateAsync` setting `GeneratedContent`
    and `SelectedMediaFileIds`.
  - Implement `UpdateApprovalAsync`: `ExecuteUpdateAsync` setting `Status = Approved`,
    `ApprovedAt`, `ReviewedByEditorId`.
  - Implement `UpdateRejectionAsync`: `ExecuteUpdateAsync` setting `Status = Rejected`,
    `RejectedAt`, `RejectionReason`, `ReviewedByEditorId`.
  _Acceptance: class satisfies interface; all existing kept methods still compile; no raw SQL
  outside the FOR UPDATE SKIP LOCKED pattern._
  _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Write repository tests for `PublicationRepository`** — cover the new and renamed
  methods with EF Core InMemory or a real test DB according to project conventions.
  _Acceptance: tests pass; all new public methods have at least one test._
  _Agent: test-writer_
  _Skill: .claude/skills/testing/SKILL.md_
  _Delegated to test-writer agent_

#### Infrastructure — Service

- [x] **Create `Infrastructure/Services/PublicationService.cs`** — implement
  `IPublicationService` exactly as described in the ADR:
  - `CreateForEventAsync`: load Event via `IEventRepository.GetDetailAsync`; validate `Active`
    status; load and validate `PublishTarget`; find initiator article; create `Publication`
    with `Created` status; persist via `IPublicationRepository.AddAsync`; return domain model.
  - `UpdateContentAsync`: load via `GetByIdAsync`; validate `ContentReady` status; call
    `UpdateContentAndMediaAsync`; return updated domain.
  - `ApproveAsync`: load via `GetByIdAsync`; validate `ContentReady` status; call
    `UpdateApprovalAsync`; return updated domain.
  - `RejectAsync`: load via `GetByIdAsync`; validate `ContentReady` or `Approved` status;
    call `UpdateRejectionAsync`; return updated domain.
  - `SendAsync`: load via `GetByIdAsync`; validate `ContentReady` or `Approved` status; call
    `UpdateApprovalAsync`; return updated domain. (Worker picks it up from `Approved` queue.)
  _Acceptance: implementation compiles; guard clauses throw `KeyNotFoundException` for missing
  entities and `InvalidOperationException` for wrong status; no direct DB access (only via
  repository interfaces)._
  _Skill: .claude/skills/clean-code/SKILL.md_

- [ ] **Write unit tests for `PublicationService`** — mock all repository dependencies; cover
  happy path and each guard clause for all five methods.
  _Acceptance: tests pass; each invalid-status case has its own test._
  _Agent: test-writer_
  _Skill: .claude/skills/testing/SKILL.md_
  _Delegated to test-writer agent_

#### Worker — Rename and Simplify

- [x] **Create `Worker/Workers/PublicationGenerationWorker.cs`** (replaces `PublicationWorker`)
  — three-level structure (`ExecuteAsync` → `ProcessBatchAsync` →
  `GenerateContentForPublicationAsync`):
  - Calls `GetPendingForGenerationAsync` to fetch `Created` publications.
  - Immediately sets status to `GenerationInProgress` via `UpdateStatusAsync` before
    processing (prevents double-pick on next cycle).
  - Runs AI content generation via `IContentGenerator`.
  - On success: calls `UpdateGeneratedContentAsync` then `UpdateStatusAsync(ContentReady)`.
  - On failure: calls `UpdateStatusAsync(Failed)`; logs error; does NOT retry.
  - Uses `ArticleProcessingOptions.PublicationWorkerIntervalSeconds` for poll interval.
  _Acceptance: file compiles; old `PublishReadyAsync`/`ProcessPublicationAsync` methods are
  absent; worker starts without exception in Development._
  _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Delete `Worker/Workers/PublicationWorker.cs`**.
  _Acceptance: file no longer exists in the repository._

- [x] **Create `Worker/Configuration/PublishingWorkerOptions.cs`** — Options class with
  `SectionName = "PublishingWorker"`, `IntervalSeconds` (default 30), `BatchSize` (default 10).
  _Acceptance: class follows the pattern of `RssFetcherOptions`; compiles._
  _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Worker/Workers/PublishingWorker.cs`** — new worker; three-level structure:
  - Calls `GetPendingForPublishAsync` to fetch `Approved` publications.
  - For each publication, reuses the publish logic extracted from the old
    `PublicationWorker.ProcessPublicationAsync`: resolves `IPublisher` by platform,
    handles `ParentPublicationId` (reply), calls `publisher.PublishAsync` or
    `publisher.PublishReplyAsync`, creates `PublishLog`, calls `UpdateStatusAsync(Published)`
    and `UpdatePublishedAtAsync`. On failure: creates failure `PublishLog`, sets
    `UpdateStatusAsync(Failed)`.
  - Uses `PublishingWorkerOptions` for interval and batch size.
  _Acceptance: file compiles; all publish logic is self-contained; worker starts without
  exception in Development._
  _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/Extensions/WorkerServiceExtensions.cs`** — replace
  `AddHostedService<PublicationWorker>()` with `AddHostedService<PublicationGenerationWorker>()`;
  add `AddHostedService<PublishingWorker>()`; register
  `services.Configure<PublishingWorkerOptions>(configuration.GetSection(PublishingWorkerOptions.SectionName))`.
  _Acceptance: file compiles; both workers are registered._
  _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/appsettings.Development.json`** — add `"PublishingWorker"` section
  with `"IntervalSeconds": 30` and `"BatchSize": 10`.
  _Acceptance: key exists in the JSON; worker reads it without exception._

#### Infrastructure — Remove Approval Service

- [x] **Delete `Infrastructure/Services/EventApprovalService.cs`**.
  _Acceptance: file no longer exists._

- [x] **Delete `Core/Interfaces/Services/IEventApprovalService.cs`**.
  _Acceptance: file no longer exists._

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — in
  `AddServices`: remove `services.AddScoped<IEventApprovalService, EventApprovalService>()`;
  add `services.AddScoped<IPublicationService, PublicationService>()`.
  _Acceptance: file compiles; no reference to `IEventApprovalService` remains._
  _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 3 — API Layer

- [x] **Create `Api/Models/PublicationDtos.cs`** — define all five records:
  `PublicationListItemDto`, `PublicationDetailDto`, `CreatePublicationRequest`,
  `UpdatePublicationContentRequest`, `RejectPublicationRequest`.
  Use the exact shapes from the ADR section 10.
  `PublicationDetailDto` includes `List<MediaFileDto> AvailableMedia`.
  _Acceptance: file compiles; all records are in the `Api.Models` namespace._
  _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Create `Api/Mappers/PublicationMapper.cs`** (Api layer) — static extension class with:
  - `ToListItemDto(this Publication pub)` — lightweight for list views.
  - `ToDetailDto(this Publication pub, List<MediaFile> availableMedia, string publicBaseUrl)`
    — full detail; `AvailableMedia` maps each `MediaFile` via existing `MediaFileMapper.ToDto`.
  _Acceptance: no inline mapping in controllers; references only `Core.DomainModels` and
  `Api.Models`._
  _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Create `Api/Controllers/PublicationsController.cs`** — 7 endpoints as per ADR
  section 9:
  - `POST /publications/generate` → `CreateForEventAsync`; returns `201 PublicationDto`.
  - `GET /publications/{id:guid}` → `GetDetailAsync`; returns `200 PublicationDetailDto`.
  - `GET /publications/by-event/{eventId:guid}` → `GetByEventIdAsync`; returns
    `200 List<PublicationListItemDto>`.
  - `PUT /publications/{id:guid}/content` → `UpdateContentAsync`; returns
    `200 PublicationDetailDto`.
  - `POST /publications/{id:guid}/approve` → `ApproveAsync`; returns `200 PublicationDetailDto`.
  - `POST /publications/{id:guid}/reject` → `RejectAsync`; returns `200 PublicationDetailDto`.
  - `POST /publications/{id:guid}/send` → `SendAsync`; returns `200 PublicationDetailDto`.
  All endpoints require `Editor` or `Admin` role. Inject `IPublicationService` and
  `IPublicationRepository`. Derive from `BaseController` for `UserId`.
  _Acceptance: Swagger shows all 7 endpoints; each returns correct HTTP status codes;
  `UserId` is passed for editor-identity endpoints._
  _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Controllers/EventsController.cs`** — remove constructor injection of
  `IEventApprovalService`; remove the `Approve` and `Reject` action methods; in
  `UpdateStatus`, restrict accepted enum values to `Active` and `Archived` only (return 400
  for any other value).
  _Acceptance: controller compiles with no reference to `IEventApprovalService`; Swagger no
  longer shows `POST /events/{id}/approve` or `POST /events/{id}/reject`._
  _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Models/EventDtos.cs`** — remove `ApproveEventRequest` and
  `RejectEventRequest` records.
  _Acceptance: file compiles; no other file references these records._
  _Skill: .claude/skills/api-conventions/SKILL.md_

- [ ] **Write API tests for `PublicationsController`** — cover at minimum: generate (201),
  get detail (200 / 404), get by event (200), update content (200 / 400 wrong status),
  approve (200), reject (200), send (200).
  _Acceptance: tests pass; each endpoint has at least one happy-path and one error test._
  _Agent: test-writer_
  _Skill: .claude/skills/testing/SKILL.md_
  _Delegated to test-writer agent_

---

### Phase 4 — UI

- [x] **Delete `UI/src/features/events/ApproveEventModal.tsx`**.
  _Acceptance: file no longer exists._

- [x] **Delete `UI/src/features/events/RejectEventModal.tsx`**.
  _Acceptance: file no longer exists._

- [x] **Modify `UI/src/features/events/useEventMutations.ts`** — remove `approveEvent` and
  `rejectEvent` mutations and their `eventsApi` calls. Remove any unused imports.
  _Acceptance: hook compiles; no reference to `eventsIdApprovePost` or `eventsIdRejectPost`._

- [ ] **Regenerate API client** — SKIPPED: backend not running on port 5172. Must be run manually.
  New publications hooks use direct axios calls with local typed interfaces instead of the generated client.
  `npm run generate-api` from `UI/`. Commit the updated files under `UI/src/api/generated/`.
  _Acceptance: `npm run build` succeeds with no TypeScript errors after regeneration; new
  `PublicationsApi` client class is present in generated output._

- [x] **Create `UI/src/features/publications/usePublications.ts`** — React Query hook that
  calls `GET /publications/by-event/{eventId}`. Query key: `['publications', 'by-event', eventId]`.
  Returns `{ publications, isLoading, error }`.
  _Acceptance: TypeScript compiles; no `any` types; hook is exported._

- [x] **Create `UI/src/features/publications/usePublicationDetail.ts`** — React Query hook
  that calls `GET /publications/{id}`. Query key: `['publication', id]`.
  Returns `{ publication, isLoading, error }`.
  _Acceptance: TypeScript compiles; no `any` types._

- [x] **Create `UI/src/features/publications/usePublicationMutations.ts`** — mutations for:
  `generateContent` (POST /publications/generate), `updateContent` (PUT content),
  `approve` (POST approve), `reject` (POST reject), `send` (POST send).
  `generateContent` invalidates `['publications', 'by-event', eventId]` on success.
  The other four invalidate `['publication', id]` and `['publications', 'by-event', ...]`
  on success and show a toast.
  _Acceptance: TypeScript compiles; no `any` types; all five mutations exported._

- [x] **Create `UI/src/features/publications/GenerateContentModal.tsx`** — modal component
  (uses `Modal` from `@/components/ui/Modal`) that:
  - Shows a dropdown/select of available publish targets (fetched from existing
    publish-targets API or passed as props).
  - On confirm calls the `generateContent` mutation from `usePublicationMutations`.
  - On success closes the modal and shows a toast.
  _Acceptance: TypeScript compiles; component renders without errors; no `any` types._

- [x] **Create `UI/src/features/publications/PublicationDetailPage.tsx`** — full editor
  review page:
  - Fetches publication detail via `usePublicationDetail`.
  - Shows generated content in an editable `Textarea`.
  - Shows media gallery (`MediaGallery`) of `availableMedia` with checkboxes to select/deselect.
  - Shows Approve, Reject, and Send buttons (conditionally visible based on status).
  - Calls appropriate mutations from `usePublicationMutations`.
  _Acceptance: TypeScript compiles; page renders; no `any` types._

- [x] **Modify `UI/src/features/events/EventDetailPage.tsx`** — apply all UI changes:
  - Remove imports of `ApproveEventModal`, `RejectEventModal`.
  - Remove approve/reject button rendering and all modal open/close state for those modals.
  - Remove `approveEvent` and `rejectEvent` from the `useEventMutations` destructure.
  - Add `statusColor` update: remove branches for `'Approved'` and `'Rejected'` status
    strings (enum no longer has these values).
  - Add "Generate Content" button (visible when `event.status === 'Active'`); clicking opens
    `GenerateContentModal`.
  - Add a "Publications" tab to the existing tab system; the tab renders the list of
    publications from `usePublications(eventId)`, each linking to
    `/publications/{publication.id}`.
  _Acceptance: TypeScript compiles; page renders; no references to deleted components or
  removed mutations remain._

- [x] **Modify `UI/src/router/index.tsx`** — import `PublicationDetailPage`; add route
  `publications/:id` inside the protected dashboard route, pointing to
  `PublicationDetailPage`.
  _Acceptance: TypeScript compiles; navigating to `/publications/<uuid>` renders the detail
  page._

---

## Open Questions

- The ADR defers media sending to a follow-up ADR. The `PublishingWorker` should send only
  text (current `IPublisher.PublishAsync` signature). Confirm this is the intended behavior
  before implementing `PublishingWorker`.
- `PublicationDetailDto.AvailableMedia` is described as "the union of all MediaFile entities
  from all Articles belonging to the Publication's Event". The `GetDetailAsync` repository
  method must load `Event → Articles → MediaFiles`. Confirm whether this eager-load chain
  should live in the repository query or be fetched separately in the service layer.
