# Fix: Media Selection Lost When Approving Publication

## Context

Publications are sent to Telegram without media despite the editor selecting media files in the UI. The `PublishingWorker.ResolveMediaAsync` receives a `Publication` where `SelectedMediaFileIds` is empty.

### Root Cause Analysis

The full data path from UI to Worker was traced:

1. **UI state management** (`UI/src/features/publications/PublicationDetailPage.tsx`): When the editor toggles media items, the selection is stored in local React state (`selectedMediaIds` via `useState`, line 36). This state is only persisted to the backend when the editor explicitly clicks "SAVE CHANGES", which calls `updateContent.mutate({ content, selectedMediaFileIds })` (line 74).

2. **The approve action ignores media selection**: The APPROVE button (line 125) calls `approve.mutate()` which sends `POST /publications/{id}/approve` with no request body. The `PublicationService.ApproveAsync` (line 76 of `Infrastructure/Services/PublicationService.cs`) only updates status, `ApprovedAt`, and `ReviewedByEditorId` -- it does not touch `SelectedMediaFileIds`.

3. **The gap**: Both "SAVE CHANGES" and "APPROVE" buttons are visible simultaneously when the publication status is `ContentReady` (lines 64-67). The editor can select media, then click APPROVE directly without first clicking SAVE CHANGES. The media selection, which only exists in React state, is lost. The publication transitions to `Approved` with an empty `SelectedMediaFileIds` array, and the `PublishingWorker` correctly reads that empty array and publishes without media.

4. **Backend persistence is correct**: `PublicationRepository.UpdateContentAndMediaAsync` correctly persists `SelectedMediaFileIds` as JSONB. The `PublicationMapper.ToDomain` correctly maps `entity.SelectedMediaFileIds ?? []`. The `PublishingWorker.ResolveMediaAsync` correctly reads `publication.SelectedMediaFileIds` and resolves them to URLs. The only break in the chain is between step 1 (UI state) and step 2 (approve API call).

### What was verified to be working correctly

- `PUT /publications/{id}/content` correctly persists media IDs when "SAVE CHANGES" is clicked.
- `PublicationEntity.SelectedMediaFileIds` is `List<Guid>` with JSONB column type -- schema is correct.
- `PublicationMapper` maps `SelectedMediaFileIds` in both directions.
- `GetPendingForPublishAsync` loads the publication entity which includes the `SelectedMediaFileIds` column.
- `ResolveMediaAsync` in `PublishingWorker` correctly handles the `SelectedMediaFileIds` list.

## Decision

Fix the approve flow to persist the current media selection (and edited content) alongside the status transition. This requires changes in two places:

### 1. Frontend: Save content and media before approving

In `UI/src/features/publications/PublicationDetailPage.tsx`, modify the approve handler to first save the current content and media selection, then approve. Two approaches:

**Chosen approach -- Chain the mutations**: When the editor clicks APPROVE, call `updateContent.mutate(...)` first, then on success call `approve.mutate()`. This reuses the existing `PUT /publications/{id}/content` endpoint and keeps the backend approve endpoint focused on status transition.

This is preferred over modifying the backend `POST /publications/{id}/approve` endpoint to accept a request body with media IDs, because:
- The approve endpoint in `PublicationsController.cs` follows the project convention of action endpoints having no body or minimal body (see `api-conventions` skill -- action endpoints use POST with no body or a small dedicated request record).
- The `updateContent` mutation and endpoint already exist and work correctly.
- Chaining two sequential mutations in the frontend is a common React Query pattern.

### 2. Frontend: Also chain save-before-approve for the Send flow (if added later)

The `PublicationsController` has a `POST /publications/{id}/send` endpoint, but the frontend currently has no Send button (confirmed by tests in `__tests__/PublicationDetailPage.test.tsx`). If a Send button is added in the future, it must follow the same pattern: save content and media first, then send.

### 3. UX improvement: Disable APPROVE when unsaved changes exist

Track whether the editor has unsaved changes (content edited or media selection changed since last save). When unsaved changes exist, either:
- Auto-save before approving (the chosen approach above), or
- Show a visual indicator that changes are unsaved and require saving first.

The auto-save approach is simpler and prevents the editor from accidentally losing work.

## Implementation Notes

**Files to change:**
- `UI/src/features/publications/PublicationDetailPage.tsx` -- modify the approve button handler to call `updateContent.mutate(...)` first, then `approve.mutate()` in the `onSuccess` callback. Approximately 5-10 lines of change.
- `UI/src/features/publications/usePublicationMutations.ts` -- no structural changes needed; both `updateContent` and `approve` mutations already exist and work correctly.

**Files verified as correct (no changes needed):**
- `Api/Controllers/PublicationsController.cs` -- approve and content endpoints work correctly.
- `Infrastructure/Services/PublicationService.cs` -- `UpdateContentAsync` and `ApproveAsync` both work correctly individually.
- `Infrastructure/Persistence/Repositories/PublicationRepository.cs` -- `UpdateContentAndMediaAsync` and `UpdateApprovalAsync` both persist correctly.
- `Worker/Workers/PublishingWorker.cs` -- `ResolveMediaAsync` correctly resolves media from `SelectedMediaFileIds`.

**Skills to follow:**
- `.claude/skills/code-conventions/SKILL.md` -- no backend changes, but verify frontend changes follow the existing pattern of mutation chaining.
- `.claude/skills/clean-code/SKILL.md` -- keep the approve handler clean; extract the chained logic into a named function (e.g., `handleApprove`) rather than nesting callbacks inline.

**Testing:**
- Update `UI/src/features/publications/__tests__/PublicationDetailPage.test.tsx` to verify that clicking APPROVE triggers a content save (or at minimum, does not lose media selection).
