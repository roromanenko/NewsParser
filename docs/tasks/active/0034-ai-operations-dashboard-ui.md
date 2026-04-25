# AI Operations Dashboard — Frontend

## Goal
Build the React admin dashboard at `/ai-operations` that visualises AI cost,
token usage, and request history by consuming the three endpoints shipped in
commit `8a2c2c9`, following ADR 0020 exactly.

## Affected Layers
- UI

---

## Tasks

### Phase 1 — Dependency and routing scaffold

- [x] **Modify `UI/package.json`** — add `"recharts": "^3.0.0"` (latest 3.x; ships its
      own TypeScript types) to `dependencies`. Run `npm install` in `UI/` after editing.
      _Acceptance: `recharts` appears in `node_modules/`; `npm run build` exits 0 with no
      TypeScript errors; no `@types/recharts` needed (package ships its own types)_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/router/index.tsx`** — add `import { AiOperationsPage } from
      '@/features/aiOperations/AiOperationsPage'` at the top (non-lazy, matching all
      other admin imports). Add a new `<Route path="ai-operations" element={<AdminRoute>
      <AiOperationsPage /></AdminRoute>} />` sibling to the existing `sources`, `users`,
      and `publish-targets` routes inside the `ProtectedRoute` subtree.
      For this task only, create a placeholder `AiOperationsPage.tsx` that returns
      `<div className="p-8 font-mono text-white">Coming soon</div>`.
      _Acceptance: navigating to `/ai-operations` as an Admin renders "Coming soon";
      navigating as an Editor redirects to `/articles`; `npm run build` exits 0_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/layouts/Sidebar.tsx`** — add an `Activity` icon import from
      `lucide-react` and append `{ to: '/ai-operations', icon: Activity, label: 'AI
      Operations', adminOnly: true }` to the `navItems` array, after the `publish-targets`
      entry and before `users`.
      _Acceptance: an Admin user sees "AI Operations" in the sidebar; an Editor user does
      not; active link highlights correctly; `npm run build` exits 0_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 2 — View-model types

- [x] **Create `UI/src/features/aiOperations/types.ts`** — define the seven interfaces
      from ADR D8 exactly as specified: `AiOpsFilters`, `AiOpsKpis`, `AiOpsTimeBucket`,
      `AiOpsBreakdownRow`, `AiOpsRequestRow`, plus a view-model aggregate
      `AiOpsMetricsView { kpis: AiOpsKpis; timeSeries: AiOpsTimeBucket[];
      byModel: AiOpsBreakdownRow[]; byWorker: AiOpsBreakdownRow[];
      byProvider: AiOpsBreakdownRow[] }` and a paged-list view-model
      `AiOpsRequestPage { items: AiOpsRequestRow[]; page: number; pageSize: number;
      totalCount: number; totalPages: number; hasNextPage: boolean;
      hasPreviousPage: boolean }`. All fields are non-optional (no `?`).
      No imports from `@/api/generated` — this file is the insulation layer.
      _Acceptance: `tsc --noEmit` in `UI/` exits 0; no generated-client type leaks
      into this file; file has zero runtime code (types only)_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 3 — Data-fetching hooks

- [x] **Create `UI/src/features/aiOperations/useAiRequestMetrics.ts`** — instantiate
      `AiOperationsApi` once at module level as `const aiOpsApi = new AiOperationsApi(
      undefined, '', apiClient)`. Export `useAiRequestMetrics(filters: Pick<AiOpsFilters,
      'from' | 'to' | 'provider' | 'worker' | 'model'>)`. Use `useQuery({
      queryKey: ['ai-ops', 'metrics', filters], staleTime: 30_000, queryFn })`.
      `queryFn` calls `aiOpsApi.aiOperationsMetricsGet(filters.from || undefined,
      filters.to || undefined, filters.provider || undefined, filters.worker || undefined,
      filters.model || undefined)`, then maps `res.data` to `AiOpsMetricsView`:
      - KPI fields default with `?? 0`; `successRate` computed as
        `successCalls / totalCalls` (guard divide-by-zero with `|| 0`);
        `cacheHitRate` computed from `cacheReadInputTokens / (cacheReadInputTokens +
        cacheCreationInputTokens + (totalInputTokens - cacheReadInputTokens -
        cacheCreationInputTokens))` (guard 0-denominator).
      - `timeSeries`: flat `AiMetricsTimeBucketDto[]` reshaped to `AiOpsTimeBucket[]`
        (one row per bucket, two provider columns) by iterating and merging rows with
        the same `bucket` value; all number fields default with `?? 0`.
      - `byModel`, `byWorker`, `byProvider`: map rows, default `key ?? ''` and
        number fields `?? 0`.
      Return type must be `UseQueryResult<AiOpsMetricsView>`.
      Module-level `const STALE_MS = 30_000` — no magic number inline.
      _Acceptance: `tsc --noEmit` exits 0; no `any` types; hook does not pass `status`
      or `search` to the metrics endpoint; React Query Devtools shows key
      `['ai-ops', 'metrics', {...}]`_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Create `UI/src/features/aiOperations/useAiRequestList.ts`** — same module-level
      `aiOpsApi` instance (or import the one from `useAiRequestMetrics` — accept either,
      but do not instantiate two separate instances in the same bundle; preferred: each
      hook file declares its own module-level const, which is identical to `useSources.ts`
      pattern). Export `useAiRequestList(page: number, pageSize: number,
      filters: AiOpsFilters)`. Use `useQuery({ queryKey: ['ai-ops', 'requests', page,
      pageSize, filters], staleTime: 10_000, placeholderData: keepPreviousData,
      queryFn })`. `queryFn` calls `aiOpsApi.aiOperationsRequestsGet(...)` with all nine
      positional arguments in the correct order (see ADR D6); maps `res.data` to
      `AiOpsRequestPage`: `items` mapped to `AiOpsRequestRow[]` with all fields
      defaulted non-optional (`?? ''`, `?? 0`, `?? 'Success' as const`); paging fields
      defaulted with `?? 0` / `?? false`.
      Module-level `const LIST_STALE_MS = 10_000` and `const DEFAULT_PAGE_SIZE = 20`.
      _Acceptance: `tsc --noEmit` exits 0; no `any`; `placeholderData: keepPreviousData`
      is used; `status` and `search` filters are passed to this hook but NOT to
      `useAiRequestMetrics`_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Create `UI/src/features/aiOperations/useAiRequestDetail.ts`** — same
      module-level `aiOpsApi` pattern. Export `useAiRequestDetail(id: string | null)`.
      Use `useQuery({ queryKey: ['ai-ops', 'request', id], enabled: !!id, queryFn })`.
      `queryFn` calls `aiOpsApi.aiOperationsRequestsIdGet(id!)` and maps `res.data` to
      `AiOpsRequestRow` with all fields defaulted.
      _Acceptance: `tsc --noEmit` exits 0; query is disabled when `id` is `null`;
      no `any`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 4 — Visual components (stateless, bottom-up)

- [x] **Create `UI/src/features/aiOperations/AiOpsKpiStrip.tsx`** — accepts
      `{ kpis: AiOpsKpis; isLoading: boolean }`. Renders six `StatCard` sub-components
      in a `div` with `className="grid grid-cols-2 sm:grid-cols-6 gap-4"`. `StatCard`
      is a module-private function component (not exported) styled identically to
      `SourceStatsCards.StatCard`: `background: rgba(61,15,15,0.4)`,
      `border: 1px solid rgba(255,255,255,0.1)`, label in `font-caps text-xs
      tracking-widest` with `color: var(--caramel)`, value in `font-display text-3xl`.
      Cards: TOTAL COST (formatted with `Intl.NumberFormat` currency USD), TOTAL CALLS
      (integer), SUCCESS RATE (percentage; `color: var(--crimson)` when below 0.95),
      AVG LATENCY (`<n> ms`), TOTAL TOKENS (integer with `Intl.NumberFormat`),
      CACHE HIT (percentage; subtitle "Anthropic only" in `font-mono text-xs
      text-gray-500`). When `isLoading` is true every value shows `—`.
      Module-level `const LOW_SUCCESS_THRESHOLD = 0.95` — no magic number inline.
      _Acceptance: `tsc --noEmit` exits 0; no `any`; no generated-client imports;
      loading state shows `—` for all six values_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Create `UI/src/features/aiOperations/AiOpsBreakdownPanel.tsx`** — accepts
      `{ title: string; rows: AiOpsBreakdownRow[]; isLoading: boolean }`. Renders a
      container with `background: rgba(61,15,15,0.3)` and `border: 1px solid
      rgba(255,255,255,0.1)` and inner padding `p-4`. Title in `font-caps text-xs
      tracking-widest text-[var(--caramel)] mb-3`. Shows at most 10 rows sorted by
      `costUsd` descending. Each row: label (`font-mono text-sm text-gray-300`) on the
      left, a horizontal bar whose width is `(row.costUsd / maxCost) * 100` percent
      (cap `maxCost` at 1 to avoid divide-by-zero), bar background `var(--caramel)` at
      0.4 opacity, cost formatted as `$X.XXXX` on the right in `font-mono text-xs
      text-gray-400`. When `isLoading` is true render 5 pulsing placeholder rows
      (`animate-pulse h-5 bg-[rgba(61,15,15,0.6)]`). When `rows` is empty render
      `font-mono text-sm text-gray-500 text-center py-4` "No data."
      _Acceptance: `tsc --noEmit` exits 0; no `any`; no recharts import; top-10 sort
      is applied; divide-by-zero is guarded_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Create `UI/src/features/aiOperations/AiRequestsTable.tsx`** — accepts
      `{ page: number; pageSize: number; data: AiOpsRequestPage | undefined;
      isLoading: boolean; isError: boolean; onPageChange: (p: number) => void;
      onRowClick: (id: string) => void }`. Renders the same 12-column grid pattern
      as `SourcesPage`: header row with `background: var(--burgundy)`, columns
      `TIME | PROVIDER | WORKER | OPERATION | MODEL | IN | OUT | LATENCY | COST |
      STATUS | (action)` labelled in `font-caps text-xs tracking-widest
      color: var(--caramel)`. Each data row is a `div.grid.grid-cols-12` with hover
      background `rgba(61,15,15,0.3)` and `cursor-pointer`; clicking calls
      `onRowClick(row.id)`. TIME column shows relative time (e.g. `5m ago`) using a
      module-level `formatTimeAgo(iso: string): string` helper (same logic as
      `SourcesPage`'s `formatTimeAgo`). STATUS column: badge using
      `border-[var(--caramel)]` for `Success` and `border-[var(--crimson)]` for
      `Error`. COST formatted `$X.XXXXXX` (6 decimal places). Pagination via
      `<Pagination>` from `@/components/shared/Pagination` passing `page`,
      `totalPages`, `hasNextPage`, `hasPreviousPage`, `onPageChange`.
      Loading: 8 pulsing skeleton rows with `animate-pulse`. Error: single-row
      `font-mono text-sm text-[var(--crimson)]` "Failed to load AI operations data."
      Empty: `font-mono text-sm text-gray-500` "No requests match your filters."
      _Acceptance: `tsc --noEmit` exits 0; no `any`; no generated-client imports;
      Pagination renders only when `totalPages > 1`; STATUS badge uses correct colors_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Create `UI/src/features/aiOperations/AiRequestDetailSlideOver.tsx`** — accepts
      `{ isOpen: boolean; requestId: string | null; onClose: () => void }`. Uses
      `<SlideOver>` from `@/components/shared/SlideOver` with `title="REQUEST DETAIL"`.
      Internally calls `useAiRequestDetail(requestId)`. Shows a loading skeleton when
      `isLoading`, an error message `font-mono text-sm text-[var(--crimson)]` when
      `isError`, and the full `AiOpsRequestRow` fields otherwise. Fields shown (each as
      a label/value pair in `font-caps text-xs text-[var(--caramel)]` /
      `font-mono text-sm text-gray-200`): ID (with a `COPY ID` button that calls
      `navigator.clipboard.writeText(row.id)`), TIMESTAMP, WORKER, PROVIDER, OPERATION,
      MODEL, INPUT TOKENS, OUTPUT TOKENS, CACHE CREATION TOKENS, CACHE READ TOKENS,
      TOTAL TOKENS, LATENCY, COST, STATUS, ARTICLE ID (as a `<Link to=
      {/articles/${row.articleId}}` if non-null, else `—`), CORRELATION ID.
      If `status === 'Error'` render a separate ERROR section below: label `ERROR MESSAGE`
      in `font-caps text-xs text-[var(--crimson)]`, value in `font-mono text-xs
      text-red-300 whitespace-pre-wrap break-words`.
      _Acceptance: `tsc --noEmit` exits 0; no `any`; slide-over does not fetch when
      `requestId` is null (hook `enabled: !!id`); COPY button uses clipboard API;
      error section only renders when status is `Error`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `UI/src/features/aiOperations/AiOpsCostTimeChart.tsx`** — accepts
      `{ series: AiOpsTimeBucket[]; isLoading: boolean; isError: boolean }`.
      Uses `recharts`: `<ResponsiveContainer width="100%" height={288}>
      <LineChart data={series}>`. Two `<Line>` components: `dataKey="anthropicCost"`
      with `stroke="var(--crimson)"` and `dataKey="geminiCost"` with
      `stroke="var(--caramel)"`. Both lines have `dot={false}` and `strokeWidth={2}`.
      `<XAxis dataKey="bucket">` with a `tickFormatter` that parses via
      `new Date(value)` and formats with `Intl.DateTimeFormat('en-US',
      { month: 'short', day: 'numeric' })`. `<YAxis>` with `tickFormatter` that
      prepends `$` and shows 2 decimal places. `<Tooltip>` with a `formatter` that
      returns `[$${Number(value).toFixed(2)}, providerLabel]`. `<Legend>` with labels
      `Anthropic` and `Gemini`. Container `div`: `background: rgba(61,15,15,0.3)`,
      `border: 1px solid rgba(255,255,255,0.1)`, padding `p-4`, `mb-6`.
      A three-button `<SegmentedControl>`-style row (`COST | TOKENS | CALLS`) above the
      chart; the selected metric drives which pair of fields (`anthropicCost/geminiCost`,
      `anthropicTokens/geminiTokens`, `anthropicCalls/geminiCalls`) the two `<Line>`s
      use. Buttons styled identically to the status filter rail in `PublicationsPage`
      (active: `background: var(--burgundy) color: #E8E8E8`; inactive: `color: #9ca3af`).
      Module-level `const CHART_HEIGHT = 288` — no magic number inline.
      Loading: pulsing skeleton `animate-pulse h-72`. Error: `font-mono text-sm
      text-[var(--crimson)]` "Failed to load AI operations data." Empty series: centred
      `font-mono text-sm text-gray-500` "No data in selected range."
      _Acceptance: `tsc --noEmit` exits 0; `recharts` types resolve; no `any`;
      SegmentedControl switches data without re-fetching; `isLoading` shows skeleton_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

---

### Phase 5 — Filter bar

- [x] **Create `UI/src/features/aiOperations/AiOpsFilterBar.tsx`** — accepts
      `{ filters: AiOpsFilters; workerOptions: string[]; modelOptions: string[];
      onChange: (patch: Partial<AiOpsFilters>) => void; onRefresh: () => void }`.
      Renders a `div.flex.flex-wrap.gap-3.mb-6.items-center` (same style as
      `SourcesPage` filter row). Controls:
      - Date range: two `<input type="date">` inputs for `from` and `to`, styled
        `background: var(--near-black) border: 1px solid rgba(255,255,255,0.1)
        font-mono text-sm text-gray-300`.
      - Quick-range buttons `24H | 7D | 30D | 90D`: each `<button>` sets both `from`
        and `to` by computing `new Date(Date.now() - N * 86_400_000).toISOString()
        .slice(0, 10)` for `from` and today for `to`. Module-level constants
        `RANGE_24H = 1`, `RANGE_7D = 7`, `RANGE_30D = 30`, `RANGE_90D = 90` (days).
      - Provider `<select>`: options `'' (All) | Anthropic | Gemini`.
      - Worker `<select>`: option `'' (All)` + one option per `workerOptions` item.
      - Model `<select>`: option `'' (All)` + one option per `modelOptions` item.
      - Status `<select>`: options `'' (All) | Success | Error` (applies to table only;
        component passes it through `onChange`; the page decides not to forward it to
        the metrics hook).
      - Search `<input type="text" maxLength={200}>` with `Search` icon from
        `lucide-react`, same styling as `SourcesPage`'s search input.
      - REFRESH `<button>` labelled `REFRESH` in `font-caps text-xs tracking-wider`,
        styled with `background: var(--crimson)`, calls `onRefresh`.
      All `<select>` elements use `focus:outline-none` and focus/blur border-color
      transitions identical to `SourcesPage` selects.
      _Acceptance: `tsc --noEmit` exits 0; no `any`; `maxLength={200}` on search
      input; quick-range buttons compute correct ISO date strings; `status` and
      `search` are present in props and forwarded via `onChange`_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

---

### Phase 6 — Page composition root

- [x] **Replace placeholder `UI/src/features/aiOperations/AiOperationsPage.tsx`** with
      the full composition root per ADR D3. The component owns all filter and pagination
      state with a single `useState<AiOpsFilters>` initialised to last-7-days defaults
      (module-level `const DEFAULT_PAGE_SIZE = 20` and `const DEFAULT_RANGE_DAYS = 7`;
      `from` = `new Date(Date.now() - DEFAULT_RANGE_DAYS * 86_400_000).toISOString()
      .slice(0, 10)`, `to` = today). Also owns `page` (`useState(1)`),
      `pageSize` (`useState(DEFAULT_PAGE_SIZE)`), and `detailId` (`useState<string |
      null>(null)`).
      Helper `pickMetricsFilters(f: AiOpsFilters)` extracts only
      `{ from, to, provider, worker, model }` — defined at module level, not inline.
      Calls `useAiRequestMetrics(pickMetricsFilters(filters))` and
      `useAiRequestList(page, pageSize, filters)`.
      `onRefresh` calls `queryClient.invalidateQueries({ queryKey: ['ai-ops'] })` via
      `useQueryClient()`.
      Layout top-to-bottom per ADR D3:
      1. Page header: `<h1 className="font-display text-5xl text-white mb-2">AI
         Operations</h1>` + subtitle `font-mono text-sm text-gray-400` showing
         total calls and total cost from KPIs (or "Loading…" when `isLoading`).
      2. `<AiOpsFilterBar>` with `workerOptions` derived from
         `metrics.data?.byWorker.map(r => r.key).filter(Boolean)` and `modelOptions`
         similarly from `byModel`.
      3. `<AiOpsKpiStrip kpis={metrics.data?.kpis} isLoading={metrics.isLoading} />`.
      4. `<AiOpsCostTimeChart series={metrics.data?.timeSeries ?? []}
         isLoading={metrics.isLoading} isError={metrics.isError} />`.
      5. Two-column breakdown grid: `<div className="grid grid-cols-2 gap-4 mb-6">
         <AiOpsBreakdownPanel title="COST BY MODEL" rows={metrics.data?.byModel ?? []}
         isLoading={metrics.isLoading} />
         <AiOpsBreakdownPanel title="COST BY WORKER" rows={metrics.data?.byWorker ?? []}
         isLoading={metrics.isLoading} /></div>`.
      6. `<AiRequestsTable page={page} pageSize={pageSize} data={list.data}
         isLoading={list.isLoading} isError={list.isError}
         onPageChange={p => setPage(p)} onRowClick={id => setDetailId(id)} />`.
      7. `<AiRequestDetailSlideOver isOpen={!!detailId} requestId={detailId}
         onClose={() => setDetailId(null)} />`.
      No business logic in the JSX — all logic lives in hooks or module-level helpers.
      _Acceptance: `tsc --noEmit` exits 0; no `any`; no inline DTO references; page
      renders without runtime errors with mocked network; `queryClient.invalidateQueries`
      is called only on `onRefresh`, not on every filter change_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

---

### Phase 7 — End-to-end smoke test

- [x] **Smoke test `UI/src/features/aiOperations/`** — with the backend running
      (`dotnet run --project Api`) and `npm run dev` in `UI/`:
      1. Log in as Admin; confirm "AI Operations" appears in the sidebar.
      2. Navigate to `/ai-operations`; confirm KPI strip renders (values or `—` if no
         data); no console errors.
      3. Change the date range; confirm the metrics and table queries refetch (React
         Query Devtools or Network tab).
      4. Click a table row; confirm the slide-over opens and shows all fields.
      5. If any `Error`-status row exists, confirm the ERROR section renders in the
         slide-over.
      6. Log in as Editor; confirm `/ai-operations` redirects to `/articles`.
      7. Run `npm run build`; confirm TypeScript compilation exits 0.
      _Acceptance: all seven checks pass; `npm run build` exits 0; no `any` in new
      files (confirmed by `tsc --noEmit`)_

## Open Questions
_None. ADR 0020 is fully resolved and the backend contract is verified._
