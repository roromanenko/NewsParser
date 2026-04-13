# Publication Detail Page Redesign

## Goal

Restyle `PublicationDetailPage.tsx` to match the dark editorial theme of `ArticleDetailPage.tsx`
and `EventDetailPage.tsx`, replace the plain textarea with a Telegram-aware toolbar editor
component, and remove the Send button and its mutation since the `PublishingWorker` handles
publishing automatically.

## Affected Layers

- UI

---

## Tasks

### Step 1 — Remove Send Button and Mutation

- [x] **Modify `UI/src/features/publications/usePublicationMutations.ts`** — remove the `send`
      mutation (the `useMutation` block calling `POST /publications/{id}/send`) and remove `send`
      from the returned object. Remove any import that becomes unused.
      _Acceptance: TypeScript compiles; the hook no longer exports `send`; no other mutation is
      affected._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/publications/PublicationDetailPage.tsx`** — remove `send` from
      the `usePublicationMutations` destructure; remove `canSend` variable; remove the
      `{canSend && <Button onClick={() => send.mutate()} ...>Send</Button>}` JSX block.
      _Acceptance: TypeScript compiles; the page renders without a Send button; no runtime
      errors when `canSend` logic is absent._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step 2 — Create PublicationEditor Component

- [x] **Create `UI/src/features/publications/PublicationEditor.tsx`** — controlled component
      that accepts `value: string`, `onChange: (value: string) => void`, and
      `disabled: boolean` props. Internal state tracks `mode: 'edit' | 'preview'`.

      **Toolbar row (always visible):**
      - EDIT / PREVIEW tab toggle buttons styled with `font-caps text-xs tracking-widest` and
        a caramel bottom-border underline on the active tab (copy tab style from
        `EventDetailPage.tsx` tab bar, lines 553–583).
      - Four formatting buttons: BOLD (`<b>`), ITALIC (`<i>`), LINK (`<a href="">`), CODE
        (`<code>`). Each styled `font-caps text-[10px]` on a dark background with caramel hover.
      - Formatting buttons are hidden (or disabled + muted) when `disabled` is `true` or when
        `mode === 'preview'`.

      **Toolbar insertion logic (helper function `insertTag`):**
      - Receives the textarea DOM ref, the opening tag string, and the closing tag string.
      - Reads `selectionStart` / `selectionEnd` from the textarea.
      - If text is selected: wraps selection with tags; cursor placed after closing tag.
      - If nothing selected: inserts `<opening></closing>` and places cursor between the tags.
      - Calls `onChange` with the new string value.
      - For LINK: insert `<a href=""></a>` with cursor inside `href=""`.

      **Edit mode:** A styled `<textarea>` — dark background (`rgba(61,15,15,0.4)`), monospace
      font, `rgba(255,255,255,0.1)` border, `min-h-[280px]`, full width. Wired to `value` /
      `onChange`. `disabled` prop disables the textarea.

      **Preview mode:** A `<div>` rendering content via `dangerouslySetInnerHTML`. Before
      rendering, pass the value through a `sanitize(html: string): string` helper that strips
      all HTML tags except the Telegram-supported whitelist: `b`, `i`, `a` (preserving `href`),
      `code`, `pre`. All other tags are removed (replace with their text content). New lines
      (`\n`) are converted to `<br />`.

      The `sanitize` function must be a pure function defined in the same file, not inside the
      component body.

      _Acceptance: TypeScript compiles with no `any` types; the component renders standalone
      with a value prop; clicking BOLD wraps selected text in `<b></b>`; PREVIEW tab renders
      `<b>` as bold; `disabled=true` makes the textarea and format buttons inert; `sanitize`
      strips `<script>` and `<div>` tags but preserves `<b>`, `<i>`, `<a href="...">`,
      `<code>`, `<pre>`._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step 3 — Restyle PublicationDetailPage

- [x] **Modify `UI/src/features/publications/types.ts`** — add `eventTitle: string | null`
      field to `PublicationDetailDto`. This field is already present on
      `PublicationListItemDto`; it is needed in the detail header to display the event name.
      _Acceptance: TypeScript compiles; `PublicationDetailDto` has `eventTitle`; no other type
      is changed._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/publications/PublicationDetailPage.tsx`** — complete rewrite
      of the JSX and logic to match the dark editorial theme. All `Button` and `Textarea`
      component imports are removed and replaced with inline `<button>` and `<textarea>`
      elements. The `Spinner` import is replaced with a `font-mono text-sm animate-pulse`
      loading state. Import `useNavigate` from `react-router-dom`, `ArrowLeft` from
      `lucide-react`, and `PublicationEditor` from `./PublicationEditor`.

      **Status helpers (pure functions, defined outside the component):**
      - `statusAccentColor(status: string): string` — returns `'var(--caramel)'` for
        `ContentReady`, `'#22c55e'` for `Approved`/`Published`, `'var(--crimson)'` for
        `Rejected`/`Failed`, `'#6b7280'` for all others.
      - `formatDate(iso: string | null | undefined): string` — same implementation as
        `ArticleDetailPage.tsx` (`toLocaleDateString` with `month/day/year`).

      **State variables:**
      - `editedContent: string` — initialized to `''`; synced to `publication.generatedContent`
        on first load via a `useEffect` that fires when `publication` transitions from
        `undefined` to defined.
      - `selectedMediaIds: string[]` — initialized to `publication.selectedMediaFileIds` on
        first load via the same `useEffect`.
      - `rejectReason: string`
      - `showRejectInput: boolean`

      **Derived flags:**
      - `isContentReady = publication.status === 'ContentReady'`
      - `isApproved = publication.status === 'Approved'`
      - `canEdit = isContentReady`
      - `canApprove = isContentReady`
      - `canReject = isContentReady || isApproved`

      **Layout structure:**

      1. **Back button** — `<button onClick={() => navigate(-1)}>` with `ArrowLeft` icon +
         `"BACK TO PUBLICATIONS"` label. Copy the exact inline style + hover pattern from
         `ArticleDetailPage.tsx` (lines 62–71).

      2. **Header card** — dark card with status-colored left-border accent.
         - `background: 'rgba(61,15,15,0.4)'`, `borderColor: 'rgba(255,255,255,0.1)'`.
         - Left border `w-1` with `backgroundColor: statusAccentColor(publication.status)`.
         - Inside `p-6`:
           - Top row: status chip on the left; action buttons on the right.
           - Status chip: `font-caps text-xs tracking-widest` with `statusAccentColor`.
           - Action buttons (inline `<button>` elements, NOT the `Button` component):
             - APPROVE button (visible when `canApprove`): caramel border hover, calls
               `approve.mutate()`. Shows `approve.isPending` as `opacity-50`.
             - REJECT button (visible when `canReject`): rust border hover, toggles
               `showRejectInput`. Shows disabled state when `reject.isPending`.
             - Copy the exact button hover pattern from `EventDetailPage.tsx` (lines 455–489):
               `onMouseEnter` / `onMouseLeave` with inline style mutations.
           - Publication title: `font-display text-4xl` showing `publication.eventTitle` if
             present, otherwise `publication.targetName`.
           - Stats row (same pattern as `ArticleDetailPage.tsx` lines 124–160):
             - PLATFORM chip: shows `publication.platform`.
             - TARGET chip: shows `publication.targetName`.
             - STATUS chip: shows `publication.status.toUpperCase()` with `statusAccentColor`.

      3. **Rejection reason display** (shown when `publication.rejectionReason` is set) — dark
         card with `var(--rust)` left-border accent, `font-mono text-sm` text content.

      4. **Reject input section** (shown when `showRejectInput`) — dark card with `var(--rust)`
         left-border accent containing:
         - A label `"REJECTION REASON"` in `font-caps text-[10px] tracking-widest`.
         - A plain `<textarea>` styled consistently with the editor (dark bg, mono font,
           `rgba(255,255,255,0.1)` border).
         - CONFIRM REJECTION button (rust hover, disabled when `rejectReason.trim()` is empty,
           calls `handleReject`).
         - CANCEL button (neutral border hover, hides the section).

      5. **Status-aware content area** — conditional rendering based on `publication.status`:

         | Status | Rendered content |
         |--------|-----------------|
         | `Created` | Dark card with `font-mono text-sm` message: "Content generation pending. The worker will generate content shortly." |
         | `GenerationInProgress` | Same dark card with `animate-pulse` message: "Generating content…" |
         | `ContentReady` | `<PublicationEditor value={editedContent} onChange={setEditedContent} disabled={false} />` + SAVE CHANGES button below (caramel hover, calls `handleSaveContent`, shows `updateContent.isPending`) |
         | `Approved` | `<PublicationEditor value={editedContent} onChange={setEditedContent} disabled={true} />` + info message "Awaiting publication by worker." in `font-mono text-xs` with `'#6b7280'` color |
         | `Published` | `<PublicationEditor value={editedContent} onChange={setEditedContent} disabled={true} />` |
         | `Rejected` | `<PublicationEditor value={editedContent} onChange={setEditedContent} disabled={true} />` |
         | `Failed` | `<PublicationEditor value={editedContent} onChange={setEditedContent} disabled={true} />` |

         All cases are wrapped in a dark card (`rgba(61,15,15,0.4)` background,
         `rgba(255,255,255,0.1)` border).
         Section heading `"CONTENT"` in `font-caps text-[10px] tracking-widest` with
         `'#6b7280'` color.

      6. **Media selection section** (shown only when `publication.availableMedia.length > 0`):
         - Dark card with section heading `"MEDIA"`.
         - Grid of media items (3 columns). Each item:
           - Selected state: `2px solid var(--caramel)` border instead of `border-indigo-500`.
           - Unselected state: `1px solid rgba(255,255,255,0.1)` border.
           - Selection overlay: `background: 'rgba(180,100,40,0.25)'` (caramel-tinted) with
             a `"SELECTED"` label in `font-caps text-[10px]` with caramel background.
           - Video placeholder: dark background (`var(--near-black)`) instead of `bg-gray-100`.
           - Click handler only active when `canEdit`.

      7. **Metadata footer** — dark card with section heading `"METADATA"`:
         - `Created`, `Approved`, `Published` timestamps in `font-mono text-xs` with
           `'#6b7280'` color, formatted via `formatDate`.

      _Acceptance: TypeScript compiles with no `any` types and no references to the `Button` or
      `Textarea` or `Spinner` components; the page renders for each status value without runtime
      errors; `useEffect` correctly initializes `editedContent` and `selectedMediaIds` from the
      fetched publication; the back button navigates back; the dark editorial theme is
      visually consistent with `ArticleDetailPage.tsx` and `EventDetailPage.tsx`._
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

---

## Open Questions

- None. All design decisions are specified in ADR `0011-publication-detail-page-redesign.md`.
