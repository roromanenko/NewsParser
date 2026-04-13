# Fix: Media Selection Lost When Approving Publication

## Goal
Ensure that clicking APPROVE always persists the current media selection and edited content to the backend before transitioning the publication to Approved status.

## Affected Layers
- UI

## Tasks

### UI

- [x] **Modify `UI/src/features/publications/PublicationDetailPage.tsx`** — replace the inline `approve.mutate()` call on the APPROVE button with a named `handleApprove` function that first calls `updateContent.mutate({ content: editedContent, selectedMediaFileIds: selectedMediaIds })` and then, in the `onSuccess` callback of that mutation call, calls `approve.mutate()`.
      _Acceptance: clicking APPROVE without first clicking SAVE CHANGES still persists media selection; the APPROVE button's `onClick` references `handleApprove`, not an inline arrow calling `approve.mutate()` directly; no nested anonymous callbacks — the chained logic lives entirely in `handleApprove`; the button's `disabled` condition is extended to also disable while `updateContent.isPending`_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `UI/src/features/publications/__tests__/PublicationDetailPage.test.tsx`** — add a new `describe` block `PublicationDetailPage — approve saves content and media first` containing two tests: (1) clicking APPROVE calls `updateContent.mutate` with the publication's current `content` and `selectedMediaFileIds` before calling `approve.mutate`; (2) clicking APPROVE when media is pre-selected (non-empty `selectedMediaFileIds`) passes those IDs to `updateContent.mutate`.
      _Acceptance: both new tests pass with `npm run test` (or `vitest`); existing tests remain green; `approve.mutate` is NOT called unless `updateContent.mutate`'s `onSuccess` fires (verified via mock call order or by not calling the approve mock directly)_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

## Open Questions
- None — the ADR fully specifies the approach (chain `updateContent` → `approve` in the frontend; no backend changes).
