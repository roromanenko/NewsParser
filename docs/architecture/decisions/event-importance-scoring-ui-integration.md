# Event Importance Scoring — UI Integration

## Context

The backend for Event Importance Scoring (see
`docs/architecture/decisions/event-importance-scoring.md`) is implemented and the OpenAPI client
has been regenerated. Confirmed in `UI/src/api/generated/api.ts`:

- `EventListItemDto` and `EventDetailDto` now carry `importanceTier?: string | null`,
  `importanceBaseScore?: number | null`, `distinctSourceCount?: number` (lines 116–118, 129–131).
- `EventsApi.eventsGet(page, pageSize, search, sortBy, tier, options)` now takes a fifth `tier`
  query-string argument (lines 1149–1150).

The UI must consume these additions in:
- `UI/src/features/events/EventsPage.tsx` — list with left filter aside (SORT, FILTER, TOTAL).
- `UI/src/features/events/EventDetailPage.tsx` — header card with status pill + stats tiles.
- `UI/src/features/events/useEvents.ts` — React Query wrapper over `eventsApi.eventsGet`.

Per the project-wide constraint "same approach, same design, same styles" no new design system
is introduced. The existing pattern for a sidebar chip group is already in
`UI/src/features/articles/ArticlesPage.tsx` (the `SENTIMENT` group, lines 117–140). The existing
pattern for an inline colored pill (status / article count) is in `EventsPage.tsx`'s `EventCard`
(lines 221–236) and `EventDetailPage.tsx`'s stats tiles (lines 502–545). Both patterns use CSS
variables in inline `style` props, not Tailwind variant classes. Note: `components/ui/Badge.tsx`
exists but targets a different (light-theme) visual system and is not used anywhere in events or
articles — reusing it would introduce a visual inconsistency. It must **not** be used here.

State management for the events list is local `useState` + React Query with
`queryKey: ['events', page, pageSize, search, sortBy]`. No URL state, no Zustand. Matches
`UI/CLAUDE.md` guidance.

## Options

### Option 1 — Mirror ArticlesPage exactly: tier chips in left aside, tier passed to API, no "High+"

Add a `FILTER / IMPORTANCE` section to the `EventsPage` left aside, below `SORT`, using the same
button-stack styling as `SORT_OPTIONS` and as `SENTIMENT_FILTERS` in `ArticlesPage`. Options:
`All / Breaking / High / Normal / Low` (five chips — no `High+`). Add `'importance'` to the
`SORT_OPTIONS` tuple. Extend `useEvents` to take `tier?: string` and pass it through to
`eventsApi.eventsGet`. React Query key becomes
`['events', page, pageSize, search, sortBy, tier]`.

Drop `High+` entirely. Rationale:
- The single-request server model (ADR 0015 established server-side search/sort/pagination
  as the contract) cannot honestly represent a two-tier filter with a single `tier` param.
- Two parallel requests merged client-side would break pagination: `totalCount`, `totalPages`,
  `hasNextPage` are per-request and cannot be meaningfully summed; deduplication is not needed
  but page alignment is impossible.
- Per-page client-side post-filter (the pattern `ArticlesPage` uses for sentiment, line 55–57)
  shrinks the visible page arbitrarily and shows a wrong `TOTAL`. That is already a mild anti-
  pattern in `ArticlesPage` but is acceptable there because sentiment is not a server filter —
  tier *is* a server filter, so there is no justification to repeat the anti-pattern.
- The task description explicitly allows option (b): "not offering High+ if the API doesn't
  support it." Five flat tiers matches the five API states (null + four tiers) 1:1.

Tier badge on each list card: new inline pill next to the existing status/article-count pills
in `EventCard` (around line 222), following the same `<span>` + `font-caps` + inline
`style={{ color, background }}` idiom. Color mapping in a local `tierColor(tier)` helper
alongside the existing `statusColor` helper (line 21).

Event detail page: add a tier pill and a `BASE SCORE` tile and `SOURCES` tile inside the
existing stats row (line 502–544) using the exact same `<div className="px-3 py-1.5" style={{
background: 'var(--near-black)' }}>` pattern — `ARTICLES`, `CONTRADICTIONS`, `RECLASSIFIED` are
all already there.

**Pros:**
- Zero new components, zero new tokens. Pure extension of existing patterns.
- One server round-trip per state change — pagination stays honest.
- Chip-group layout already proven in `ArticlesPage` (`SENTIMENT_FILTERS` lines 117–140).

**Cons:**
- No "High+" convenience chip. User must click `Breaking` separately from `High`. Accepted
  per task allowance; can be revisited when/if the API adds multi-tier filter support.

### Option 2 — Client-merge two requests for "High+"

Offer `All / Breaking / High+ / High / Normal / Low`. When `High+` is selected, run two parallel
`eventsApi.eventsGet` calls (one per tier), merge and sort results client-side by a local
best-effort score or timestamp, and synthesize pagination by concatenating items and summing
counts.

**Pros:**
- Exposes the conceptual "everything important or above" filter.

**Cons:**
- Breaks the server-side pagination contract from ADR 0015. `totalPages` and `hasNextPage` would
  be fiction.
- Introduces custom merge/sort logic that will silently diverge from the server's
  `sortBy=importance` decayed order.
- No existing precedent in the codebase for merging paginated responses.
- React Query cache shape becomes non-uniform (one key fans out to two queries).

### Option 3 — Sidebar chips use a new shared `FilterChipGroup` component

Extract a reusable component for the chip button stack, consumed by both `ArticlesPage`
(for sentiment) and `EventsPage` (for sort + tier).

**Pros:**
- DRY — three chip groups across two pages today.

**Cons:**
- Out of scope. The task description says "Reuse whatever badge/chip/filter components already
  exist." The chip pattern is currently inline and consistent enough that extraction is a
  separate refactor, not part of this feature. Doing it here muddies the diff.

## Decision

**Option 1** — mirror the `ArticlesPage` chip-group pattern, five flat tier chips, no `High+`,
no new components.

### File-change list

1. `UI/src/features/events/useEvents.ts`
   - Extend signature: `useEvents(page, pageSize, search, sortBy, tier?: string)`.
   - Pass `tier || undefined` (not empty string — generator treats `undefined` as omitted) into
     `eventsApi.eventsGet(page, pageSize, search || undefined, sortBy, tier || undefined)`.
   - Extend `queryKey` to `['events', page, pageSize, search, sortBy, tier]`.

2. `UI/src/features/events/EventsPage.tsx`
   - Add `'importance'` to `SORT_OPTIONS` tuple (line 10): `['newest', 'oldest', 'importance']`.
     Display label: `IMPORTANCE` (already `option.toUpperCase()` in the button, no extra code).
   - Add `TIER_FILTERS` tuple: `['all', 'Breaking', 'High', 'Normal', 'Low'] as const` and
     `type TierFilter = (typeof TIER_FILTERS)[number]`.
   - Add state: `const [tier, setTier] = useState<TierFilter>('all')`.
   - Reset page to 1 when `tier` changes (extend the existing effect at line 43–45 dependency
     array).
   - Add a third aside section "IMPORTANCE" below the `SORT` section (after line 106), mirroring
     `ArticlesPage` `SENTIMENT_FILTERS` layout (lines 117–140). Display label per chip is
     `chip.toUpperCase()`; `all` renders as `ALL`.
   - Call: `useEvents(page, PAGE_SIZE, debouncedSearch, sortBy, tier === 'all' ? undefined : tier)`.
   - Add a tier helper beside `statusColor` (line 21):
     ```
     function tierColor(tier?: string | null): string
     ```
     Returns: `Breaking → var(--crimson)`, `High → var(--rust)`, `Normal → #6b7280`,
     `Low → #4b5563`. These are the four CSS variables and gray shades already in the file
     (`--crimson` used for `Active`/`MERGE EVENTS`, `--rust` for contradictions/archive hover).
   - In `EventCard`, insert a new inline pill between the status label and the `ARTICLES` pill
     (around line 222–232). Render only when `event.importanceTier` is non-null:
     ```
     <span className="font-caps text-xs tracking-widest" style={{ color: tierColor(event.importanceTier) }}>
       {event.importanceTier.toUpperCase()}
     </span>
     ```
     Matches the `STATUS` label visual (line 223) — same font/size/tracking.

3. `UI/src/features/events/EventDetailPage.tsx`
   - Add `tierColor` helper beside `statusColor` (line 23), same mapping as in `EventsPage`.
     (Duplication is acceptable and matches existing codebase style — `statusColor` is already
     duplicated between these two files with slightly different sets. Extraction is a separate
     refactor.)
   - In the header card, add a tier label alongside the existing status label around line 449–453,
     inside the same `flex items-center gap-3 flex-wrap` row. Render only when
     `event.importanceTier` is non-null. Same `font-caps text-xs tracking-widest` styling.
   - In the stats row (line 502–544), add two tiles using the exact same
     `px-3 py-1.5 / var(--near-black) / font-caps [10px] + font-mono text-sm` pattern:
     - `SOURCES` — always shown, value `event.distinctSourceCount ?? 0`.
     - `BASE SCORE` — shown only when `event.importanceBaseScore != null`, value
       `event.importanceBaseScore.toFixed(1)` (e.g. `62.4`). Fixed one decimal because the
       backend score is `0–100` and one decimal matches the precision the formula yields.

### State-shape decisions

- **Tier filter state**: single local `useState<TierFilter>('all')`, sentinel `'all'` mapped to
  `undefined` on the API call. Matches the exact shape of `filterSentiment` in `ArticlesPage`
  (line 39).
- **Sort state**: append `'importance'` to the existing `SORT_OPTIONS` union. No structural
  change; the existing render loop (line 87–105) picks up the new option automatically.
- **React Query key**: append `tier` as the sixth element. Consistent with the
  `['resource', ...filters]` convention in `UI/CLAUDE.md`.
- **URL state**: not introduced. No other list page (articles, sources, users, publications)
  persists filter state to the URL. Staying consistent.
- **Page reset on filter/sort change**: extend the existing effect (line 43) by adding `tier` to
  its dependency array — identical to how `ArticlesPage` handles its own set.

### The "High+" question

**Not offered.** Five flat chips: `All / Breaking / High / Normal / Low`. Rationale above in
Option 1. If the product later needs High+, the right fix is a backend change to accept a
comma-separated or repeated `tier` param, then a one-line frontend change — not a client-side
merge that fights server pagination.

### Styling approach

- **Chip group**: inline `<button>` stack with `style={{ background: selected ? 'var(--burgundy)'
  : 'transparent', color: selected ? '#E8E8E8' : '#9ca3af' }}` — copy exactly from
  `ArticlesPage` `SENTIMENT_FILTERS` block (lines 121–139). Same hover effect.
- **Tier pill on list item**: inline `<span className="font-caps text-xs tracking-widest"
  style={{ color: tierColor(...) }}>` — copy exactly from the `event.status` label in
  `EventCard` (line 223). No background pill needed; the color alone carries the signal, same
  as status.
- **Stats tiles on detail**: inline `<div className="px-3 py-1.5" style={{ background:
  'var(--near-black)' }}>` + caps label + mono value — copy exactly from `ARTICLES` / `CONTRADICTIONS`
  / `RECLASSIFIED` tiles (lines 507–544).
- **Colors**: only variables already defined in the codebase — `--crimson`, `--rust`,
  `--caramel`, `--near-black`, `--burgundy` — plus the two neutral hex greys already used
  throughout (`#6b7280`, `#4b5563`, `#9ca3af`). **No new CSS variables, no new Tailwind classes,
  no `components/ui/Badge.tsx`.**

## Implementation Notes

### Order of changes (for feature-planner)

1. `UI/src/features/events/useEvents.ts` — extend signature + pass `tier` + extend query key.
2. `UI/src/features/events/EventsPage.tsx` — sort option, tier state, tier aside section,
   tier pill on card, `tierColor` helper.
3. `UI/src/features/events/EventDetailPage.tsx` — `tierColor` helper, tier label in header,
   `SOURCES` + `BASE SCORE` tiles.

### Skills for feature-planner to follow

- `.claude/skills/code-conventions/SKILL.md` — frontend feature placement (`features/events/`),
  hooks-encapsulate-queries rule.
- `.claude/skills/clean-code/SKILL.md` — tier mapping as a small helper not an inline switch,
  no magic numbers (the four tier names come from the API, `toFixed(1)` is the only literal —
  justified by score range).
- `UI/CLAUDE.md` — state layering (local `useState` for UI state, React Query for server data),
  query-key convention `['resource', ...filters]`, no manual edits to `src/api/generated/`.

### Risks / things to watch

- **Null tier**: every list item and detail may have a null `importanceTier` (events created
  before the backend rollout, or events that never received an analysis pass). Tier pill and
  `BASE SCORE` tile must render conditionally. `SOURCES` tile is always rendered (field is
  non-null `number`, defaulting to 0).
- **`sortBy=importance` on events with null score**: the backend already handles `DESC NULLS
  LAST` (per the backend ADR). The UI just passes the string through; no client-side handling
  needed.
- **Casing of tier values**: the generator types `importanceTier` as `string | null`. Backend
  sends title-case (`"Breaking"`, `"High"`, `"Normal"`, `"Low"` — derived from the C# enum
  via `.ToString()` per the backend ADR Api section). The API accepts case-insensitive `tier`
  query param (per the backend ADR, `Enum.TryParse(..., ignoreCase: true)`). Frontend sends the
  same title-case values it receives — symmetric and debuggable. No normalization layer needed.
- **React Query key extension**: already-cached queries under the old 5-element key will be
  evicted on first render after the upgrade. Acceptable; data is cheap to refetch.

### Out of scope (explicit)

- No new shared component extraction (`FilterChipGroup` belongs to a separate refactor).
- No tests (test-writer is skipped per the task description).
- No backend changes.
- No URL-state persistence for filters/sort.
- No `High+` multi-tier selector.
