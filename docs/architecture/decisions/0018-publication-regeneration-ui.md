# Publication Regeneration UI

## Context

ADR `0017-publication-regeneration-with-editor-feedback.md` added a backend
endpoint `POST /publications/{id}/regenerate` with body `{ "feedback": "..." }`
(non-empty, max 2000 chars). Calling it transitions the publication's status
from `ContentReady` or `Failed` back to `Created`, the worker then moves it to
`GenerationInProgress`, and finally to `ContentReady` with new content. The
response body is the updated `PublicationDetailDto`, which gains one new
nullable field: `editorFeedback: string | null`. Status guards on the server:
allowed source statuses are `ContentReady` and `Failed`; everything else
returns 409.

The current `UI/src/features/publications/PublicationDetailPage.tsx` already
exposes Approve and Reject as the two editor actions, surfaces
`rejectionReason` as a read-only banner with a rust left-border accent
(lines 200-213), and uses an inline expanding panel for the rejection-reason
input (lines 216-271). It does not poll, does not handle backend status codes
specifically, and uses `useToast()` from `@/context/ToastContext` plus
`react-query`'s `invalidateQueries` for refresh. The page never imports the
light-theme `Modal`/`Button`/`Textarea` components — those are reserved for
admin pages (`GenerateContentModal.tsx` is the only consumer in this feature
and lives outside the detail page flow).

We need to add a third editor action ("Regenerate"), let the editor type
guidance up to 2000 chars, show the feedback used to produce the current
draft as a read-only banner once present, gate the action by status, and let
the page automatically reflect the new draft once the worker finishes.

## Options

### Option 1 — Modal dialog with `Modal` + `Textarea` from `components/ui`

Open the existing `Modal` component with a label, a `Textarea` (max 2000),
a character counter, and a Confirm/Cancel pair using `Button`.

**Pros:**
- Re-uses two existing primitives.
- Strong focus capture / Escape-to-close already wired in `Modal.tsx`.

**Cons:**
- The `Modal`/`Button`/`Textarea` primitives ship the **light theme**
  (white background, indigo accents, gray borders — see `Modal.tsx` line 33,
  `Button.tsx` line 11, `Textarea.tsx` lines 23-29). They are visually
  inconsistent with the dark editorial theme of the publication detail page,
  which is exactly why ADR `0011` deliberately replaced them with inline
  styled `<button>`s and a styled `<textarea>` for the rejection panel
  (`PublicationDetailPage.tsx` lines 225-269).
- Re-styling `Modal` to dark theme is out of scope here and would be a
  cross-cutting change.
- The rejection flow already proved that an inline expanding panel is
  sufficient for a single textarea-plus-confirm-cancel interaction.

### Option 2 — Inline expanding panel mirroring the existing rejection panel

Add a `showRegenerateInput` state on the page, render a sibling panel to
`{showRejectInput && ...}` styled identically but with the caramel accent
instead of rust. Re-use the same `<textarea>` styling, the same button
styling, and add a character counter underneath.

**Pros:**
- Identical interaction model to Reject — editors already know it.
- No new component, no new dependency, no dark-theme retrofit needed.
- Stays inside the existing layout flow so the regeneration banner / new
  status appears in place when the page re-renders.

**Cons:**
- Two side-by-side inline panels is unusual; we mitigate by ensuring only
  one is open at a time (clicking Regenerate closes the Reject panel and
  vice versa).

## Decision

**Option 2 — inline expanding panel mirroring the rejection panel.**

It is the smallest change that respects every established pattern on the
page: same dark card, same colored left-border accent (caramel for
regenerate, matching the Approve action's hover color), same textarea
styling, same Confirm/Cancel pair. The only additions on top of the existing
Reject pattern are a character counter (because the backend caps at 2000)
and a guard so opening one panel closes the other.

### Detailed design

#### 1. Type — `UI/src/features/publications/types.ts`

Add one nullable field to `PublicationDetailDto` (append at the end to keep
existing callers stable):

```ts
export interface PublicationDetailDto {
  // ...existing fields
  rejectionReason: string | null
  editorFeedback: string | null   // new
}
```

`PublicationListItemDto` is **not** extended — feedback is not a list-view
concern (the backend ADR makes the same call).

#### 2. Mutation hook — `UI/src/features/publications/usePublicationMutations.ts`

Add a fourth mutation `regenerate` next to `approve` and `reject`. Same
shape as `reject` (it takes a string and returns the updated detail DTO):

```ts
const regenerate = useMutation({
  mutationFn: (feedback: string) =>
    apiClient
      .post<PublicationDetailDto>(`/publications/${publicationId}/regenerate`, { feedback })
      .then(r => r.data),
  onSuccess: () => {
    toast('Regeneration requested', 'success')
    invalidateDetail()
  },
  onError: (error: unknown) => {
    // 409 = backend status guard rejected (e.g. worker already grabbed the row)
    const status = (error as { response?: { status?: number } })?.response?.status
    const message = status === 409
      ? 'Cannot regenerate: publication is no longer in a regeneratable state.'
      : 'Failed to request regeneration'
    toast(message, 'error')
  },
})

return { generateContent, updateContent, approve, reject, regenerate }
```

The 409-vs-other branch is **the only place** we special-case an HTTP status
in this file. We don't introduce an axios error helper for one site; if a
second consumer needs the same logic, extract it then. The narrow inline
type assertion `(error as { response?: { status?: number } })` matches the
sole existing precedent for status inspection in the repo
(`UI/src/lib/axios.ts` line 22 reaches into `error.response?.status` the
same way, untyped).

#### 3. Polling — `UI/src/features/publications/usePublicationDetail.ts`

Add `refetchInterval` so the page reflects the worker's progress through
`Created` → `GenerationInProgress` → `ContentReady` without manual reload.
Polling is the simplest fit because (a) the project has no SignalR / SSE
infrastructure, (b) React Query supports it natively as a query option, and
(c) the editor is already on the page when they trigger regeneration.

```ts
export function usePublicationDetail(id: string) {
  const { data, isLoading, error } = useQuery({
    queryKey: ['publication', id],
    queryFn: () =>
      apiClient
        .get<PublicationDetailDto>(`/publications/${id}`)
        .then(r => r.data),
    enabled: !!id,
    refetchInterval: (query) => {
      const status = query.state.data?.status
      return status === 'Created' || status === 'GenerationInProgress' ? 3000 : false
    },
  })

  return { publication: data, isLoading, error }
}
```

`3000ms` matches the human-perceivable cadence on the article detail / event
detail pages and is gentle on the API. Polling stops automatically as soon
as the status moves out of the in-flight pair, including on `Failed`. This
hook change is shared with the rest of the app (the article detail page does
not consume it), so behavior is purely additive.

No optimistic update on the mutation: the response from
`POST /regenerate` already returns the new `PublicationDetailDto` with
`status: "Created"` and `editorFeedback` populated, and `invalidateDetail()`
fires a refetch immediately afterward. Polling then takes over.

#### 4. Detail page — `UI/src/features/publications/PublicationDetailPage.tsx`

Four small changes inside the existing file:

**(a) Status gate for the action button.** Mirroring the `canApprove` /
`canReject` pattern around line 64:

```tsx
const isFailed = publication.status === 'Failed'
const canRegenerate = isContentReady || isFailed
```

**(b) Add the REGENERATE button** in the action button row (around
line 131, after Approve and Reject), styled like the existing Approve
button but with caramel hover (matches Approve's caramel accent — the
action is constructive iteration, not destruction):

```tsx
{canRegenerate && (
  <button
    onClick={() => {
      setShowRejectInput(false)   // mutual exclusion with Reject panel
      setShowRegenerateInput(v => !v)
    }}
    disabled={regenerate.isPending}
    className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors disabled:opacity-50"
    style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
    onMouseEnter={...caramel...}
    onMouseLeave={...reset...}
  >
    REGENERATE
  </button>
)}
```

The button is **disabled** while `regenerate.isPending`. It is **hidden**
for `Created` and `GenerationInProgress` (because `canRegenerate` is false
for those states). Approve and Reject are already hidden in those states by
the existing `canApprove` / `canReject` logic, so all three editor actions
disappear together while the worker is busy. No additional disabled-state
plumbing for in-flight regeneration is needed beyond what already exists for
the rejection panel — `canApprove` / `canReject` already key off
`status === 'ContentReady'` so they will turn off automatically the moment
the polling refetch surfaces `Created` or `GenerationInProgress`.

Also extend the existing Reject button click handler to call
`setShowRegenerateInput(false)` for symmetric mutual exclusion.

**(c) Add the editor-feedback read-only banner** next to the existing
rejection-reason banner (around line 200), shown whenever
`publication.editorFeedback` is non-null. Same card style, **caramel** left
border to distinguish it from the rust rejection banner:

```tsx
{publication.editorFeedback && (
  <div
    className="relative border p-5"
    style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
  >
    <div className="absolute left-0 top-0 bottom-0 w-1" style={{ backgroundColor: 'var(--caramel)' }} />
    <p className="font-caps text-[10px] tracking-widest mb-2" style={{ color: '#6b7280' }}>
      EDITOR FEEDBACK
    </p>
    <p className="font-mono text-sm whitespace-pre-wrap" style={{ color: '#9ca3af' }}>
      {publication.editorFeedback}
    </p>
  </div>
)}
```

`whitespace-pre-wrap` so multi-line feedback renders sensibly. The banner
is shown for every status the editor sees a draft in (`ContentReady`,
`Failed`, and even `Approved`/`Published` if they look back at it) — it is
historical context, identical surfacing semantics to `rejectionReason`.

**(d) Add the regenerate input panel** as a sibling to the existing reject
input panel (around line 216). Same structure, **caramel** accent, plus a
character counter and a max-length cap on the textarea:

```tsx
const REGEN_MAX = 2000

{showRegenerateInput && (
  <div
    className="relative border p-5 space-y-3"
    style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
  >
    <div className="absolute left-0 top-0 bottom-0 w-1" style={{ backgroundColor: 'var(--caramel)' }} />
    <p className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
      REGENERATION FEEDBACK
    </p>
    <textarea
      value={regenerateFeedback}
      onChange={e => setRegenerateFeedback(e.target.value.slice(0, REGEN_MAX))}
      rows={4}
      className="w-full font-mono text-sm resize-y p-3 focus:outline-none"
      style={{
        background: 'rgba(61,15,15,0.4)',
        border: '1px solid rgba(255,255,255,0.1)',
        color: '#E8E8E8',
      }}
      placeholder="Describe what to change: 'make it shorter', 'emphasize the second source'…"
    />
    <div className="flex items-center justify-between">
      <span className="font-mono text-[10px]" style={{ color: '#6b7280' }}>
        {regenerateFeedback.length} / {REGEN_MAX}
      </span>
      <div className="flex gap-2">
        <button
          onClick={handleRegenerate}
          disabled={!regenerateFeedback.trim() || regenerate.isPending}
          className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors disabled:opacity-40"
          style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
          onMouseEnter={...caramel...}
          onMouseLeave={...reset...}
        >
          {regenerate.isPending ? 'REQUESTING…' : 'REQUEST REGENERATION'}
        </button>
        <button
          onClick={() => setShowRegenerateInput(false)}
          className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors"
          style={...same as Reject Cancel...}
        >
          CANCEL
        </button>
      </div>
    </div>
  </div>
)}
```

`handleRegenerate` mirrors `handleReject`:

```ts
const handleRegenerate = () => {
  const trimmed = regenerateFeedback.trim()
  if (!trimmed) return
  regenerate.mutate(trimmed, {
    onSuccess: () => {
      setShowRegenerateInput(false)
      setRegenerateFeedback('')
    },
  })
}
```

The slice-on-change keeps the textarea hard-capped at 2000 chars even if a
user pastes a longer string, so the backend's `MaximumLength(2000)`
validator never fires for honest users. The button is disabled on an
empty/whitespace-only string (mirrors Reject's behavior) and while the
mutation is in flight.

#### 5. Status display during regeneration

The existing `ContentArea` component (lines 381-442) already handles the
`Created` and `GenerationInProgress` cases with messages
("Content generation pending. The worker will generate content shortly." /
"Generating content…"). No changes needed — those branches now also serve
the regeneration in-flight state because the lifecycle is identical. The
"REGENERATING" verbiage is conveyed implicitly: the editor knows they just
clicked Regenerate, and the existing message is generic enough.

If we later want a regen-specific message ("Regenerating with your
feedback…"), we differentiate by checking `editorFeedback != null` inside
the `GenerationInProgress` branch — but defer until asked.

#### 6. Test updates — `UI/src/features/publications/__tests__/PublicationDetailPage.test.tsx`

Extend the existing test file (do not create a parallel file). Add to
`mockMutations`:

```ts
regenerate: { mutate: vi.fn(), isPending: false },
```

Add `editorFeedback: null` to the `buildPublication` defaults. Add new
`describe` blocks for:
- REGENERATE button visibility (`ContentReady` and `Failed` show it; all
  other statuses hide it).
- Clicking REGENERATE opens the panel; clicking again closes it.
- Opening REGENERATE while REJECT is open closes REJECT (mutual exclusion),
  and vice versa.
- Confirm button disabled on empty / whitespace feedback.
- Confirm button calls `regenerate.mutate(trimmedFeedback, { onSuccess })`.
- `editorFeedback` banner renders when the field is populated.
- `editorFeedback` banner does **not** render when the field is null.
- Character counter shows current length.

## Implementation Notes

### Files affected

- `UI/src/features/publications/types.ts` — add `editorFeedback` field to
  `PublicationDetailDto` (append at end).
- `UI/src/features/publications/usePublicationMutations.ts` — add
  `regenerate` mutation with 409-aware error handling; export it from the
  return tuple.
- `UI/src/features/publications/usePublicationDetail.ts` — add
  `refetchInterval` that polls every 3000 ms while status is `Created` or
  `GenerationInProgress`.
- `UI/src/features/publications/PublicationDetailPage.tsx` — add
  `showRegenerateInput` / `regenerateFeedback` state; add `canRegenerate`
  flag; add REGENERATE button with mutual exclusion against Reject; add
  `EDITOR FEEDBACK` read-only banner mirroring `REJECTION REASON`; add
  regenerate input panel mirroring the rejection input panel with a
  character counter and 2000-char client cap.
- `UI/src/features/publications/__tests__/PublicationDetailPage.test.tsx`
  — extend `mockMutations` and `buildPublication`; add the test cases
  listed in section 6 above.

### Files NOT touched

- `UI/src/components/ui/Modal.tsx`, `Button.tsx`, `Textarea.tsx` — light
  theme primitives, deliberately not used on this dark page (precedent set
  by ADR 0011).
- `UI/src/api/generated/` — auto-generated. The user runs
  `npm run generate-api` separately when the backend Swagger is updated;
  the hand-written `types.ts` is the source of truth for this feature in
  the meantime, matching the pattern from ADRs 0011, 0014, 0015, 0016.
- `UI/src/features/publications/PublicationsPage.tsx` — list view does not
  surface `editorFeedback`; no change.
- `UI/src/lib/axios.ts` — keep the 401 interceptor as the only global
  status-handling. The 409 case for regeneration is local to one mutation;
  no global handler.

### Sequencing for feature-planner

1. **Type first.** Add `editorFeedback` to `PublicationDetailDto` so every
   subsequent step type-checks against the new field.
2. **Mutation hook.** Add `regenerate` to `usePublicationMutations` —
   returns the new state shape that the page will consume.
3. **Polling.** Add `refetchInterval` to `usePublicationDetail` —
   independently testable; safe to ship before the UI uses it.
4. **Page.** Wire state, button, banner, panel into
   `PublicationDetailPage`. Visual change goes live here.
5. **Tests.** Extend the existing `PublicationDetailPage.test.tsx` with the
   eight test cases above, and update `mockMutations` /
   `buildPublication`.

### Conventions to follow

- `UI/CLAUDE.md` — feature-based structure under `src/features/`; React
  Query for server state with key pattern `['publication', id]` and
  `invalidateQueries` on mutation success; toast via `useToast()` from
  `@/context/ToastContext`; no manual editing under `src/api/generated/`.
- Existing in-file precedent in `PublicationDetailPage.tsx` — inline-styled
  `<button>` with caramel/rust hover transitions, dark card pattern with
  colored left-border accent, `font-caps text-xs tracking-wider` for action
  labels. **Do not** introduce the light-theme `Button`/`Modal`/`Textarea`
  components on this page.
- Existing precedent in `usePublicationMutations.ts` — every mutation
  returns the updated `PublicationDetailDto`, calls `invalidateDetail()` on
  success, and surfaces failures via `useToast(..., 'error')`.

### Related ADRs

- `0017-publication-regeneration-with-editor-feedback.md` — the backend
  contract this UI consumes.
- `0011-publication-detail-page-redesign.md` — established the dark
  editorial theme and the inline-button / inline-panel patterns reused
  here, and explicitly removed the `Button`/`Textarea` primitives from
  this page.
- `0010-publication-pipeline-redesign.md` — defined the
  `Created → GenerationInProgress → ContentReady` lifecycle the polling
  hook keys off.
