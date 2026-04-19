# Event Importance Scoring — UI Integration

## Goal
Surface the backend's `importanceTier`, `importanceBaseScore`, and `distinctSourceCount` fields
in the Events list page (tier filter chips, sort option, card pill) and Event detail page (tier
label, SOURCES tile, BASE SCORE tile).

## Affected Layers
- UI

## Tasks

### `UI/src/features/events/useEvents.ts`

- [x] **Extend `useEvents` signature to accept `tier?: string`**
      Add `tier?: string` as the fifth parameter (default `undefined`).
      _Acceptance: function signature reads
      `useEvents(page, pageSize, search, sortBy, tier?: string)` with no compilation error._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Pass `tier` to `eventsApi.eventsGet` and extend query key**
      Inside `queryFn`, change the call to
      `eventsApi.eventsGet(page, pageSize, search || undefined, sortBy, tier || undefined)`.
      Change `queryKey` from `['events', page, pageSize, search, sortBy]` to
      `['events', page, pageSize, search, sortBy, tier]`.
      _Acceptance: both the call-site and the key include `tier`; TypeScript compiles without error._
      _Skill: .claude/skills/code-conventions/SKILL.md_

### `UI/src/features/events/EventsPage.tsx`

- [x] **Add `'importance'` to `SORT_OPTIONS` tuple (line 10)**
      Change `const SORT_OPTIONS = ['newest', 'oldest'] as const` to
      `['newest', 'oldest', 'importance'] as const`.
      _Acceptance: the aside SORT section renders a third button labelled `IMPORTANCE` with no
      extra code changes (existing `option.toUpperCase()` render loop picks it up automatically)._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Add `TIER_FILTERS` tuple and `TierFilter` type below `SORT_OPTIONS`**
      Insert:
      ```ts
      const TIER_FILTERS = ['all', 'Breaking', 'High', 'Normal', 'Low'] as const
      type TierFilter = (typeof TIER_FILTERS)[number]
      ```
      _Acceptance: `TierFilter` is `'all' | 'Breaking' | 'High' | 'Normal' | 'Low'`; file compiles._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Add `tierColor` helper function beside `statusColor`**
      Insert after `statusColor`:
      ```ts
      function tierColor(tier?: string | null): string {
        if (tier === 'Breaking') return 'var(--crimson)'
        if (tier === 'High') return 'var(--rust)'
        if (tier === 'Normal') return '#6b7280'
        if (tier === 'Low') return '#4b5563'
        return '#6b7280'
      }
      ```
      _Acceptance: function is present at module scope, all four tier values map to the
      documented CSS variables/hex codes, file compiles._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Add `tier` state and wire it to `useEvents` call**
      Inside `EventsPage`, add `const [tier, setTier] = useState<TierFilter>('all')`.
      Change the `useEvents` call to
      `useEvents(page, PAGE_SIZE, debouncedSearch, sortBy, tier === 'all' ? undefined : tier)`.
      _Acceptance: `tier` state exists; the hook call passes `undefined` when `tier === 'all'`
      and the title-case string otherwise; TypeScript compiles._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Add `tier` to the page-reset effect dependency array (line 43–45)**
      Change `}, [debouncedSearch, sortBy])` to `}, [debouncedSearch, sortBy, tier])`.
      _Acceptance: changing tier in the browser resets the page to 1; no lint warning about
      missing dependencies._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Add IMPORTANCE aside chip group below the SORT section**
      After the closing `</div>` of the SORT `<div className="space-y-1">` block (after line 106),
      insert a new `<div className="mt-6 space-y-1">` section that renders an `IMPORTANCE` label
      and one button per entry in `TIER_FILTERS`, using the exact same button markup, inline
      styles (`background: tier === s ? 'var(--burgundy)' : 'transparent'`, etc.), and
      `onMouseEnter`/`onMouseLeave` handlers as the SORT block directly above. Label text is
      `s.toUpperCase()`.
      _Acceptance: aside renders five chips (ALL / BREAKING / HIGH / NORMAL / LOW); active chip
      has `var(--burgundy)` background; clicking a chip updates `tier` state; SORT and TOTAL
      sections are still present and unmodified._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Add tier pill to `EventCard` top-row between status label and ARTICLES pill**
      Inside `EventCard`, between the `<span>` that renders `event.status?.toUpperCase()` and
      the `<span>` that renders `{event.articleCount ?? 0} ARTICLES`, insert:
      ```tsx
      {event.importanceTier && (
        <span
          className="font-caps text-xs tracking-widest"
          style={{ color: tierColor(event.importanceTier) }}
        >
          {event.importanceTier.toUpperCase()}
        </span>
      )}
      ```
      _Acceptance: cards with a non-null `importanceTier` display the tier in the correct color;
      cards with a null tier display nothing extra; existing status label and ARTICLES pill are
      unchanged._
      _Skill: .claude/skills/code-conventions/SKILL.md_

### `UI/src/features/events/EventDetailPage.tsx`

- [x] **Add `tierColor` helper beside `statusColor` (line 23)**
      Insert after `statusColor`:
      ```ts
      function tierColor(tier?: string | null): string {
        if (tier === 'Breaking') return 'var(--crimson)'
        if (tier === 'High') return 'var(--rust)'
        if (tier === 'Normal') return '#6b7280'
        if (tier === 'Low') return '#4b5563'
        return '#6b7280'
      }
      ```
      _Acceptance: identical mapping to the one in `EventsPage.tsx`; file compiles._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Add tier label in the header card status row**
      Inside the `<div className="flex items-center gap-3 flex-wrap">` that contains the status
      `<span>` (around line 449–453), append after the status span:
      ```tsx
      {event.importanceTier && (
        <span
          className="font-caps text-xs tracking-widest"
          style={{ color: tierColor(event.importanceTier) }}
        >
          {event.importanceTier.toUpperCase()}
        </span>
      )}
      ```
      _Acceptance: header card shows the tier label in the correct color when non-null; renders
      nothing when `importanceTier` is null; status label is unmodified._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Add SOURCES stats tile to the stats row (always shown)**
      Inside the stats `<div className="flex gap-4 pt-4 border-t flex-wrap">` (after line 504),
      append a new tile after the ARTICLES tile:
      ```tsx
      <div className="px-3 py-1.5" style={{ background: 'var(--near-black)' }}>
        <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
          SOURCES{' '}
        </span>
        <span className="font-mono text-sm" style={{ color: '#E8E8E8' }}>
          {event.distinctSourceCount ?? 0}
        </span>
      </div>
      ```
      _Acceptance: SOURCES tile is always visible on the detail page; shows `0` when
      `distinctSourceCount` is null/undefined; tile markup matches ARTICLES tile exactly._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Add BASE SCORE stats tile to the stats row (conditional on non-null score)**
      After the SOURCES tile, append:
      ```tsx
      {event.importanceBaseScore != null && (
        <div className="px-3 py-1.5" style={{ background: 'var(--near-black)' }}>
          <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
            BASE SCORE{' '}
          </span>
          <span className="font-mono text-sm" style={{ color: '#E8E8E8' }}>
            {event.importanceBaseScore.toFixed(1)}
          </span>
        </div>
      )}
      ```
      _Acceptance: tile is hidden when `importanceBaseScore` is null; displays one decimal place
      (e.g. `62.4`) when non-null; tile markup matches the RECLASSIFIED tile pattern._
      _Skill: .claude/skills/code-conventions/SKILL.md_

### Verification

- [x] **Run `npm run typecheck` in `UI/`**
      _Acceptance: exits with code 0 and no type errors in the three changed files._
      _Note: no `typecheck` script exists; `npm run build` runs `tsc -b` — used instead. Exit 0._

- [x] **Run `npm run lint` in `UI/`**
      _Acceptance: exits with code 0; no new ESLint warnings or errors._
      _Note: 26 pre-existing problems (22 errors, 4 warnings) — identical count before and after
      changes. Zero new issues introduced by this task._

- [x] **Run `npm run build` in `UI/`**
      _Acceptance: production bundle compiles successfully._
      _Result: `tsc -b` clean, vite build succeeded (573 kB bundle, built in 5.10s)._

## Open Questions
_(none — ADR fully resolves all design questions including "High+" which is explicitly not offered)_
