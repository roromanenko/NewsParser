# Publication Regeneration UI

## Goal
Surface the backend's regeneration endpoint in the publication detail page: add an `editorFeedback` type field, a `regenerate` mutation with 409-aware error handling, status-driven polling, and an inline expanding panel that lets the editor submit feedback and see the new draft appear automatically.

## Affected Layers
- UI

## Tasks

### `UI/src/features/publications/types.ts`

- [ ] **Add `editorFeedback: string | null` to `PublicationDetailDto`**
      Append the field at the end of the interface (after `rejectionReason`) so all existing
      positional consumers remain stable:
      ```ts
      export interface PublicationDetailDto {
        // ...existing fields
        rejectionReason: string | null
        editorFeedback: string | null   // new
      }
      ```
      `PublicationListItemDto` and `MediaFileDto` are **not** changed.
      _Acceptance: TypeScript compiles; `editorFeedback` is a nullable string on
      `PublicationDetailDto` only; no other interface is modified._
      _Skill: .claude/skills/code-conventions/SKILL.md_

### `UI/src/features/publications/usePublicationMutations.ts`

- [ ] **Add the `regenerate` mutation with 409-aware error handling**
      Add a fourth `useMutation` call after `reject`, matching the same shape:
      - `mutationFn`: `POST /publications/${publicationId}/regenerate` with body `{ feedback }`,
        returning `PublicationDetailDto`.
      - `onSuccess`: call `toast('Regeneration requested', 'success')` then `invalidateDetail()`.
      - `onError`: extract `(error as { response?: { status?: number } })?.response?.status`;
        if `409`, toast `'Cannot regenerate: publication is no longer in a regeneratable state.'`;
        otherwise toast `'Failed to request regeneration'`.
      Add `regenerate` to the return tuple: `return { generateContent, updateContent, approve, reject, regenerate }`.
      _Acceptance: hook exports `regenerate`; TypeScript compiles; the 409 branch produces a
      distinct message from the generic error message; no other mutations are modified._
      _Skill: .claude/skills/code-conventions/SKILL.md_

### `UI/src/features/publications/usePublicationDetail.ts`

- [ ] **Add `refetchInterval` to poll while status is `Created` or `GenerationInProgress`**
      Add `refetchInterval` as a query option, using the callback form:
      ```ts
      refetchInterval: (query) => {
        const status = query.state.data?.status
        return status === 'Created' || status === 'GenerationInProgress' ? 3000 : false
      },
      ```
      The rest of the hook (`queryKey`, `queryFn`, `enabled`) is unchanged.
      _Acceptance: the hook polls every 3000 ms when `status` is `Created` or
      `GenerationInProgress` and stops otherwise; TypeScript compiles; no other behavior changes._
      _Skill: .claude/skills/code-conventions/SKILL.md_

### `UI/src/features/publications/PublicationDetailPage.tsx`

- [ ] **Destructure `regenerate` from `usePublicationMutations` and add `canRegenerate` flag**
      Change line 33 from:
      ```ts
      const { updateContent, approve, reject } = usePublicationMutations(id)
      ```
      to:
      ```ts
      const { updateContent, approve, reject, regenerate } = usePublicationMutations(id)
      ```
      Add two state variables after the existing `showRejectInput` state (line 38):
      ```ts
      const [showRegenerateInput, setShowRegenerateInput] = useState(false)
      const [regenerateFeedback, setRegenerateFeedback] = useState('')
      ```
      Add `REGEN_MAX` constant (module scope, before the component):
      ```ts
      const REGEN_MAX = 2000
      ```
      Add `canRegenerate` and `isFailed` after the existing `canReject` flag (around line 68):
      ```ts
      const isFailed = publication.status === 'Failed'
      const canRegenerate = isContentReady || isFailed
      ```
      Add `handleRegenerate` handler after `handleReject`:
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
      _Acceptance: `regenerate` is destructured; `showRegenerateInput`, `regenerateFeedback`,
      `REGEN_MAX`, `canRegenerate`, `isFailed`, and `handleRegenerate` are all present and
      TypeScript compiles without error._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Add REGENERATE button to the action button row with mutual exclusion against REJECT**
      Inside the `<div className="flex items-center gap-2 flex-wrap">` button row (around line 131),
      append after the REJECT button:
      ```tsx
      {canRegenerate && (
        <button
          onClick={() => {
            setShowRejectInput(false)
            setShowRegenerateInput(v => !v)
          }}
          disabled={regenerate.isPending}
          className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors disabled:opacity-50"
          style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
          onMouseEnter={e => {
            e.currentTarget.style.borderColor = 'var(--caramel)'
            e.currentTarget.style.color = 'var(--caramel)'
          }}
          onMouseLeave={e => {
            e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
            e.currentTarget.style.color = '#9ca3af'
          }}
        >
          REGENERATE
        </button>
      )}
      ```
      Also update the existing REJECT button `onClick` handler to add `setShowRegenerateInput(false)`
      for symmetric mutual exclusion:
      ```ts
      onClick={() => {
        setShowRegenerateInput(false)
        setShowRejectInput(v => !v)
      }}
      ```
      _Acceptance: REGENERATE button appears only when `status` is `ContentReady` or `Failed`;
      clicking it closes the reject panel; clicking REJECT closes the regenerate panel; both panels
      are never open simultaneously; APPROVE and REJECT buttons are unchanged in all other respects;
      TypeScript compiles._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Add the EDITOR FEEDBACK read-only banner after the REJECTION REASON banner**
      After the closing `</div>` of the rejection-reason banner (around line 213), insert:
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
      The banner uses the caramel left-border accent (not rust) to distinguish it from the rejection
      banner. It is shown for any status where `editorFeedback` is non-null.
      _Acceptance: banner renders when `publication.editorFeedback` is a non-empty string; it is
      absent when `editorFeedback` is `null`; caramel left-border is present; `whitespace-pre-wrap`
      is applied; rejection-reason banner is unmodified; TypeScript compiles._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Add the regenerate input panel after the reject input panel**
      After the closing `</div>` of the `{showRejectInput && ...}` block (around line 271), insert:
      ```tsx
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
                onMouseEnter={e => {
                  e.currentTarget.style.borderColor = 'var(--caramel)'
                  e.currentTarget.style.color = 'var(--caramel)'
                }}
                onMouseLeave={e => {
                  e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
                  e.currentTarget.style.color = '#9ca3af'
                }}
              >
                {regenerate.isPending ? 'REQUESTING…' : 'REQUEST REGENERATION'}
              </button>
              <button
                onClick={() => setShowRegenerateInput(false)}
                className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors"
                style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
                onMouseEnter={e => {
                  e.currentTarget.style.borderColor = 'rgba(255,255,255,0.4)'
                  e.currentTarget.style.color = '#E8E8E8'
                }}
                onMouseLeave={e => {
                  e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
                  e.currentTarget.style.color = '#9ca3af'
                }}
              >
                CANCEL
              </button>
            </div>
          </div>
        </div>
      )}
      ```
      Key points: `onChange` slices input to `REGEN_MAX` chars to hard-cap at 2000; the character
      counter `{regenerateFeedback.length} / {REGEN_MAX}` is left-aligned; the REQUEST REGENERATION
      confirm button is disabled on empty/whitespace input and while `regenerate.isPending`; the
      confirm label changes to `REQUESTING…` while pending; CANCEL closes the panel without clearing
      `regenerateFeedback`; caramel accent mirrors the REGENERATE action button and the EDITOR
      FEEDBACK banner.
      _Acceptance: panel appears only when `showRegenerateInput` is true; textarea is hard-capped
      at 2000 chars via slice; character counter updates on every keystroke; REQUEST REGENERATION
      is disabled on empty/whitespace; label changes to `REQUESTING…` while pending; CANCEL closes
      the panel; `handleRegenerate` is called on confirm; after a successful mutation both
      `showRegenerateInput` and `regenerateFeedback` are reset; TypeScript compiles._
      _Skill: .claude/skills/code-conventions/SKILL.md_

### Verification

- [ ] **Run `npm run build` in `UI/`**
      Execute `npm run build` from `D:/PROGRAMING/Projects/NewsParser/UI/`.
      _Acceptance: `tsc -b` emits zero new type errors; Vite bundle compiles successfully; exit
      code 0._

- [ ] **Run `npm run lint` in `UI/`**
      Execute `npm run lint` from `D:/PROGRAMING/Projects/NewsParser/UI/`.
      _Acceptance: no new ESLint errors or warnings introduced by the changed files (compare
      count to the pre-existing baseline)._

## Open Questions
_(none — ADR 0018 fully specifies every design decision including styling, mutual-exclusion
behaviour, 409 error message copy, char cap, and polling interval)_
