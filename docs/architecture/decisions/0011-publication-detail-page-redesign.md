# Publication Detail Page Redesign

## Context

The current `PublicationDetailPage.tsx` was built as a quick functional page during the publication pipeline redesign (ADR `0010-publication-pipeline-redesign.md`). It uses the old light-theme UI components (`Button`, `Textarea` with gray/indigo/white styling) that do not match the dark editorial theme established in `ArticleDetailPage.tsx` and `EventDetailPage.tsx`. These two reference pages use a consistent design language: dark backgrounds (`rgba(61,15,15,0.4)`), CSS custom properties (`--caramel`, `--crimson`, `--rust`, `--burgundy`, `--near-black`), font classes (`font-display`, `font-mono`, `font-caps`), inline style-based hover interactions, and card-based layouts with colored left-border accents.

The current publication detail page also has a "Send" button that allows editors to manually trigger publishing. Per the updated workflow, publications only have two meaningful editor actions: approve and reject. Once approved, the `PublishingWorker` automatically polls for `Approved` publications and publishes them. The "Send" action is redundant and should be removed.

Additionally, the page uses a plain `<Textarea>` for content editing with no formatting tools and no way to preview how the final publication text will look. Editors need basic text editing tools (bold, italic, links, line breaks) and a preview mode to see the rendered output before approving.

## Options

### Option 1 -- Custom Toolbar with Plain Textarea (Markdown-like)

Add a toolbar above the existing textarea with buttons that insert markdown-style formatting tokens (e.g., `**bold**`, `_italic_`, `[link](url)`). Add a "Preview" toggle that renders the markdown to HTML using a lightweight library like `marked` or `react-markdown`.

**Pros:** No heavy editor dependency; the generated content from the AI is already plain text, so markdown tokens integrate naturally; small bundle size; the preview toggle is a simple state switch between textarea and rendered HTML.
**Cons:** Manual cursor manipulation for insertion; markdown syntax visible in edit mode may confuse non-technical editors; formatting is limited to what markdown supports; the toolbar is custom code that needs maintenance.

### Option 2 -- Rich Text Editor (Tiptap/ProseMirror)

Replace the textarea with a full rich text editor like Tiptap (built on ProseMirror). Provides WYSIWYG editing with a toolbar, and the output can be stored as HTML or converted to plain text for Telegram.

**Pros:** True WYSIWYG experience; mature ecosystem with extensions; familiar editing paradigm.
**Cons:** Heavy dependency (~200KB+ bundled); Telegram messages are plain text with limited HTML support (`<b>`, `<i>`, `<a>`, `<code>`) -- a full rich text editor is overkill; conversion from rich HTML to Telegram-compatible format adds complexity; does not match the minimalist design language of the rest of the UI.

### Option 3 -- Toolbar with Telegram-Specific Formatting + Preview

Build a lightweight custom toolbar that inserts only the formatting tokens that Telegram supports: `<b>bold</b>`, `<i>italic</i>`, `<a href="url">link</a>`, `<code>code</code>`, and line breaks. The toolbar buttons wrap selected text or insert empty tags at cursor position. The preview mode renders these HTML tags as styled text. No external editor library needed.

**Pros:** Format tokens map 1:1 to what Telegram actually renders -- no lossy conversion; tiny implementation footprint; no new dependencies; the preview shows exactly what Telegram will display; aligns with the project's minimalist approach.
**Cons:** Limited formatting options (by design -- Telegram only supports a few tags); custom toolbar code to maintain; less discoverable than a WYSIWYG editor for users unfamiliar with the workflow.

## Decision

**Option 3 -- Toolbar with Telegram-Specific Formatting + Preview.**

This option is the best fit because:

1. The publication target is Telegram, which supports only `<b>`, `<i>`, `<a>`, `<code>`, and `<pre>`. A rich text editor would generate HTML that must be stripped down -- the toolbar should only offer what the platform supports.
2. No new npm dependencies are needed. The project currently has zero editor libraries, and adding one for four formatting buttons is disproportionate.
3. The preview mode can render the limited HTML subset directly using `dangerouslySetInnerHTML` with a simple sanitizer, or by parsing the small tag set manually.
4. The design matches the existing minimalist editorial UI: a toolbar row of `font-caps` styled buttons above a styled textarea, with an edit/preview toggle.

### Detailed Design

#### 1. Restyle PublicationDetailPage to Match Dark Editorial Theme

Replace all light-theme elements (white backgrounds, gray borders, indigo accents, `Button`/`Textarea` components) with the dark editorial design language used in `ArticleDetailPage.tsx` and `EventDetailPage.tsx`:

- **Layout:** `max-w-4xl` or `max-w-5xl` container (matching article detail).
- **Back button:** `ArrowLeft` icon + `font-mono text-xs` uppercase label with caramel hover, same as article/event detail pages.
- **Header card:** Dark card (`rgba(61,15,15,0.4)` background, `rgba(255,255,255,0.1)` border) with a colored left-border accent (`var(--caramel)` for ContentReady, green for Approved, `var(--crimson)` for Rejected/Failed).
- **Publication title:** Show event title or target name in `font-display text-4xl`.
- **Stats row:** Status, platform, and target in stat chips (same pattern as article detail `CATEGORY`/`LANG`/`SENTIMENT` chips).
- **Section cards:** Each section (content editor, media, metadata) in dark-bordered cards matching the article detail Summary/Key Facts/Tags pattern.
- **Action buttons:** Use inline `<button>` elements with `font-caps text-xs tracking-wider` and border-based hover effects (caramel for approve, rust for reject), matching the event detail page buttons -- NOT the light-theme `Button` component.
- **Timestamps/metadata:** `font-mono text-xs` with `#6b7280` color, same as article detail.

#### 2. Remove Send Button and Mutation

- Remove the `send` mutation from `usePublicationMutations.ts`.
- Remove the "Send" button from the page.
- Remove `canSend` logic.
- The `POST /publications/{id}/send` API endpoint remains for now (backend cleanup is out of scope for this UI ADR), but the UI will not call it.
- Update `canApprove` to allow approval from `ContentReady` status only.
- When approved, show a status message indicating the worker will publish automatically.

#### 3. Editor Toolbar Component

Create a new component `PublicationEditor.tsx` inside `src/features/publications/` that encapsulates:

- **Toolbar row:** A row of small buttons for formatting actions: Bold (`<b>`), Italic (`<i>`), Link (`<a>`), Code (`<code>`). Each button styled with `font-caps text-[10px]` on a dark background, matching the tab bar design in `EventDetailPage.tsx`.
- **Edit/Preview toggle:** Two tab-like buttons ("EDIT" / "PREVIEW") in the toolbar, using the same tab styling as event detail tabs (caramel underline for active tab).
- **Edit mode:** A styled `<textarea>` (dark background, monospace font, subtle border) showing the raw text with HTML tags.
- **Preview mode:** A `<div>` rendering the content with `<b>`, `<i>`, `<a>`, `<code>` tags interpreted as HTML. Use a minimal sanitization function that strips all tags except the allowed Telegram set.
- **Text insertion logic:** When a toolbar button is clicked, wrap the selected text in the textarea with the appropriate tags, or insert empty tags at the cursor position.

The `PublicationEditor` component receives `value`, `onChange`, and `disabled` props -- it is a controlled component that can be used in place of the current `Textarea`.

#### 4. Media Selection with Dark Theme

Replace the current light-themed media grid (indigo borders, gray backgrounds) with the dark theme:

- Use the same card container style as article detail media gallery.
- Selected state: `var(--caramel)` border instead of indigo.
- Selection overlay: caramel-tinted instead of indigo-tinted.
- Checkbox/selection indicator: caramel accent.
- Media items should be clickable to toggle selection (same as current behavior, restyled).

#### 5. Rejection Reason Section

Restyle the rejection input area:
- Dark card background matching other sections.
- `var(--rust)` accent for the rejection context.
- Text input styled consistently with the editor textarea (dark background, monospace font).

#### 6. Status-Aware UI States

The page should adapt its layout based on publication status:

| Status | Content Area | Actions | Notes |
|--------|-------------|---------|-------|
| Created | "Content generation pending..." message | None | Worker will generate |
| GenerationInProgress | "Generating content..." with pulse animation | None | Worker is processing |
| ContentReady | Editable content with toolbar + media selection | Approve, Reject | Main editor workflow |
| Approved | Read-only content + preview | Reject | Show "Awaiting publication by worker" |
| Published | Read-only content + preview | None | Show published timestamp |
| Rejected | Read-only content with rejection reason | None | Show rejection details |
| Failed | Read-only content with error info | None | Show failure context |

#### 7. Files Changed

**Modified:**
- `src/features/publications/PublicationDetailPage.tsx` -- complete restyle to dark editorial theme, remove Send button, integrate new editor component, status-aware layout.
- `src/features/publications/usePublicationMutations.ts` -- remove `send` mutation.
- `src/features/publications/types.ts` -- add `eventTitle` field to `PublicationDetailDto` if not already present (needed for header display).

**New:**
- `src/features/publications/PublicationEditor.tsx` -- toolbar + edit/preview toggle + textarea component.

**Not changed:**
- `usePublicationDetail.ts` -- no changes needed, the hook is already correctly structured.
- `usePublications.ts` -- no changes.
- `GenerateContentModal.tsx` -- out of scope (separate component, triggers creation).
- `PublicationsPage.tsx` -- out of scope (list page, not detail page).
- Backend API -- no changes needed. The `POST /publications/{id}/send` endpoint remains but is no longer called from UI.

## Implementation Notes

### For Feature-Planner

This change should be sequenced in three steps:

1. **Remove Send button and mutation** -- smallest, safest change first. Remove `send` from `usePublicationMutations.ts` and the Send button from `PublicationDetailPage.tsx`. This is independently deployable.

2. **Create PublicationEditor component** -- build the toolbar + edit/preview component as an isolated unit in `src/features/publications/PublicationEditor.tsx`. The component should be testable standalone: it takes `value`/`onChange`/`disabled` props and manages its own edit/preview state internally.

3. **Restyle PublicationDetailPage** -- complete restyle following the dark editorial theme patterns from `ArticleDetailPage.tsx` and `EventDetailPage.tsx`. Integrate `PublicationEditor`, restyle media selection, add status-aware states, add back button, restructure layout into header card + content sections.

### Design Patterns to Follow

- Copy the exact card styling from `ArticleDetailPage.tsx`: `background: 'rgba(61,15,15,0.4)'`, `borderColor: 'rgba(255,255,255,0.1)'`.
- Copy the exact button hover pattern from `EventDetailPage.tsx`: inline `onMouseEnter`/`onMouseLeave` style mutations with CSS custom properties.
- Copy the exact stats chip pattern from `ArticleDetailPage.tsx`: `background: 'var(--near-black)'` with `font-caps text-[10px] tracking-widest` labels.
- Copy the section heading pattern: `font-caps text-[10px] tracking-widest` with `color: '#6b7280'`.
- Use `lucide-react` icons (already a dependency): `ArrowLeft` for back, `Eye`/`Pencil` for preview/edit toggle, `Bold`/`Italic`/`Link`/`Code` for toolbar.
- The `PublicationEditor` sanitizer must whitelist only: `b`, `i`, `a` (with `href`), `code`, `pre`. Strip all other HTML tags.

### Skills to Follow

- `.claude/skills/code-conventions/SKILL.md` -- feature-based directory structure, hooks encapsulate logic, components stay thin.
- `.claude/skills/clean-code/SKILL.md` -- method length limits (extract toolbar logic, sanitizer, status helpers into separate functions), guard clauses, naming conventions.
