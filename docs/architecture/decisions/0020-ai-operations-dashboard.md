# 0020 — AI Operations Dashboard (Admin)

## Status
Accepted

## Context

ADR `ai-request-logging-and-cost-tracking.md` (commit `210aff4`) added per-call persistence
of every AI request to the `ai_request_log` table. Each row carries: `Timestamp`, `Worker`,
`Provider`, `Operation`, `Model`, `InputTokens`, `OutputTokens`, `CacheCreationInputTokens`,
`CacheReadInputTokens`, `TotalTokens`, `CostUsd` (NUMERIC(18,8)), `LatencyMs`, `Status`
(`Success | Error`), `ErrorMessage`, `CorrelationId`, `ArticleId`. Indexes cover
`Timestamp`, `Provider`, `Worker`, `Model`, `ArticleId`, `CorrelationId`.

That ADR explicitly closed itself with: "No UI / API read endpoints for the log. That's a
later task." — and "No aggregation queries; when added they belong in a new method on
`IAiRequestLogRepository`, covered by a future ADR."

**This ADR is that later task — but only the frontend half of it.** The Api/ controller and
the new `IAiRequestLogRepository` aggregation methods landed in commit `8a2c2c9 add AI
operations endpoints`; this ADR designs the React dashboard that consumes them.

### Backend contract — verified against `feature/ai-request-logging`

The backend half is now in place. Verified files:

- `Api/Controllers/AiOperationsController.cs` — three endpoints, `[Authorize(Roles = nameof(UserRole.Admin))]` at the controller level.
- `Api/Models/AiOperationsDtos.cs` — `AiOperationsMetricsDto`, `AiMetricsTimeBucketDto`, `AiMetricsBreakdownRowDto`, `AiRequestLogDto`, plus query records `AiOperationsMetricsQuery`, `AiRequestsListQuery`.
- `Api/Mappers/AiOperationsMapper.cs` — domain → DTO extension methods.
- `Core/Interfaces/Repositories/IAiRequestLogRepository.cs` — adds `GetMetricsAsync`, `GetPagedAsync`, `CountAsync`, `GetByIdAsync`.
- `Core/DomainModels/AiRequestLogFilter.cs` — `record AiRequestLogFilter(From, To, Provider, Worker, Model, Status, Search)`.
- `Api/Validators/AiOperationsMetricsQueryValidator.cs` and `AiRequestsListQueryValidator.cs` — FluentValidation rules; `Status` is constrained to `"Success" | "Error"`, `PageSize` to `[1, 100]`, `Page >= 1`, `Search` max length 200.

The generated TypeScript client (`UI/src/api/generated/api.ts`) was regenerated and now
exposes:

- Class: **`AiOperationsApi`** (object-oriented client, instantiated as
  `new AiOperationsApi(undefined, '', apiClient)`).
- Methods (all return `AxiosPromise<T>`; arguments are positional, not object-shaped):
  - `aiOperationsMetricsGet(from?, to?, provider?, worker?, model?, options?) → AxiosPromise<AiOperationsMetricsDto>`
  - `aiOperationsRequestsGet(from?, to?, provider?, worker?, model?, status?, search?, page?, pageSize?, options?) → AxiosPromise<AiRequestLogDtoPagedResult>`
  - `aiOperationsRequestsIdGet(id, options?) → AxiosPromise<AiRequestLogDto>`
- Types:
  - `AiOperationsMetricsDto` — every field optional (`field?: number`), arrays nullable
    (`Array<...> | null`).
  - `AiMetricsTimeBucketDto { bucket?: string; provider?: string | null; costUsd?: number; calls?: number; tokens?: number }`.
  - `AiMetricsBreakdownRowDto { key?: string | null; calls?: number; costUsd?: number; tokens?: number }`.
  - `AiRequestLogDto` — all 17 backend fields, all optional.
  - `AiRequestLogDtoPagedResult { items?: Array<AiRequestLogDto> | null; page?, pageSize?, totalCount?, totalPages?, hasNextPage?, hasPreviousPage? }` — note the **generated name is `AiRequestLogDtoPagedResult`**, not `PagedResult<AiRequestLogDto>`.

### Differences from the original D5 contract sketch

The shipped contract is very close to what this ADR originally prescribed; the
implementer should only be aware of the following minor differences:

1. **PagedResult naming.** Generated as `AiRequestLogDtoPagedResult`, not the generic
   `PagedResult<AiRequestLogDto>`. Field names (`items`, `page`, `pageSize`, `totalCount`,
   `hasNextPage`, `hasPreviousPage`, `totalPages`) match the existing `PagedResult<T>` in
   `Api/Models/PagedResult.cs`.
2. **All generated DTO fields are optional.** Every field is typed `field?: T` and arrays
   are `Array<...> | null`. The view-model mapping layer in `useAiRequestMetrics.ts` /
   `useAiRequestList.ts` (D8) must defensively default these (`?? 0`, `?? []`, `?? ''`).
3. **`bucket` is `string` (ISO `DateTimeOffset`), not a date-only string.** The chart's
   X-axis formatter must parse via `new Date(bucket)` and format to a day-granularity
   label (e.g. `Apr 18` via `Intl.DateTimeFormat`). The backend buckets by day, so the
   value is at midnight UTC, but the type is full ISO.
4. **Metrics endpoint does not accept `status` or `search` filters.** The controller
   builds an `AiRequestLogFilter` for `/metrics` with `Status: null, Search: null`
   regardless of caller input. This is intentional: KPIs/chart aggregate across both
   success and error rows. Therefore:
   - `AiOpsFilterBar` exposes `status` and `search` only to `AiRequestsTable`, not to the
     metrics fetch.
   - `useAiRequestMetrics` filters: `{ from, to, provider, worker, model }` only.
   - `useAiRequestList` filters: `{ from, to, provider, worker, model, status, search }`.
5. **Validator-imposed constraints** (the UI must respect these to avoid 400s):
   - `pageSize ∈ [1, 100]` — page size selector capped at 100.
   - `page >= 1`.
   - `search` max length 200 — limit the input via `maxLength={200}` on the input.
   - `status ∈ {"Success", "Error"}` (or empty for "All").

### Existing admin-page patterns the dashboard must match

The dashboard must look and behave like a NewsParser admin page. Reference pages:

- **`UI/src/features/sources/SourcesPage.tsx`** — canonical admin list page: `<h1>` with
  `font-display text-5xl text-white`, subtitle in `font-mono text-sm text-gray-400`,
  filter bar of `<select>` + search input, custom 12-column grid table (NOT the older
  `components/shared/DataTable` — that one is light-themed and unused by current admin
  pages), `SourceStatsCards` row built from `<StatCard>` mini-components styled with
  `background: rgba(61,15,15,0.4)` and `border: 1px solid rgba(255,255,255,0.1)`.
  Reference path for the StatCard pattern: `UI/src/features/sources/SourceStatsCards.tsx`
  (sibling of the page, not nested under a `components/` subfolder).
- **`UI/src/features/sources/useSources.ts`** — canonical hook pattern: instantiate the
  generated `SourcesApi` once at module level with `apiClient` from `@/lib/axios`
  (note: not `@/lib/apiClient`), expose a single `useQuery` with key
  `['sources', ...filters]` that calls the typed endpoint.
- **`UI/src/features/publications/PublicationsPage.tsx`** — example of a list page with a
  left sidebar filter rail (`aside w-56`) and a paginated content area.
- **`UI/src/features/publishTargets/PublishTargetsPage.tsx`** — example of a non-tabular
  admin overview built from cards in a `grid grid-cols-3 gap-6` plus a 4-card analytics
  strip — closest visual analogue to a dashboard.
- **`UI/src/features/publications/types.ts`** — example of local view-model types that
  re-state DTO shapes with non-optional fields, insulating components from the generated
  client's optional-everywhere typing.

Theme tokens (`var(--burgundy)`, `var(--crimson)`, `var(--caramel)`, `var(--rust)`,
`var(--near-black)`) and font classes (`font-display`, `font-caps`, `font-mono`) are
established and must be reused.

### Routing and access control

Routing is centralised in `UI/src/router/index.tsx`. Admin-only routes are wrapped with
the `<AdminRoute>` component (defined inline in the same file, lines 23–28). The sidebar
in `UI/src/layouts/Sidebar.tsx` reads `usePermissions().isAdmin` to filter `navItems`
and adds an `adminOnly: true` flag on each entry. The new dashboard is admin-only, so
it must add an `adminOnly: true` nav entry and an `<AdminRoute>` route.

### State management

Per `UI/CLAUDE.md`:
- Server data → TanStack React Query, query keys `['resource', ...filters]`.
- Auth → Zustand (`src/store/`).
- UI state (filters, modals, pagination) → local `useState`.

There is no Zustand store for any feature page today; all page-level state lives in
`useState` inside the page component. The dashboard must follow this pattern.

---

## Options

### Option A — One single-page dashboard with KPIs + chart + table

A single route `/ai-operations` renders, top-to-bottom:
1. Date-range selector + manual refresh button.
2. KPI strip (4–6 stat cards): total cost USD, total tokens, request count, success rate,
   average latency, Anthropic cache-hit ratio.
3. Time-series chart (cost or token usage by day, stacked by provider).
4. Two breakdown panels side-by-side: cost-by-model, cost-by-worker.
5. Recent-requests table (paginated, filterable by provider/worker/status), with a row
   click opening a details slide-over.

**Pros:**
- Fastest path to operator value: every question the ADR was commissioned to answer
  ("cost by week", "cost by worker", "is caching working", "which call failed and why")
  is on one screen.
- Mirrors the layout style already used for `PublishTargetsPage` (cards over a strip)
  and `SourcesPage` (table + stats), so visual conventions hold.
- Single route, single feature folder, single state container.

**Cons:**
- Dense screen — needs careful section spacing to avoid feeling busy.
- A drill-down panel for a single request must fit somewhere; a `<SlideOver>` is the
  natural choice (already used in Sources / Users / PublishTargets).

### Option B — Multi-route hub: `/ai-operations` (overview) + `/ai-operations/requests` (table) + `/ai-operations/:id` (detail)

Three routes, three pages. Overview shows KPIs and charts only; a separate route hosts
the full requests table; a third route is the per-request detail page.

**Pros:**
- Each screen has one job; less dense.
- URL is shareable for a specific request (`/ai-operations/<id>`) — useful when
  troubleshooting a specific failure.

**Cons:**
- Three times the routing/navigation surface for what is, in practice, one tool used by
  one role to answer cost questions.
- Detail is short-lived data (latest cycles) — a slide-over from the list is just as
  useful and avoids extra route boilerplate.
- Out of step with how the rest of the admin section is structured (each admin domain
  has exactly one page).

### Option C — Embed AI metrics into existing pages (e.g. cost per article on `ArticleDetailPage`)

Skip a dedicated dashboard; surface AI cost contextually on existing pages.

**Pros:**
- No new route, no charting library.

**Cons:**
- **Does not solve the actual problem.** The ADR was commissioned to enable
  cross-cutting cost questions ("how much did Haiku cost last week?", "is caching
  working at all?"). Per-article context cannot answer those.
- Would still leave Anthropic cache effectiveness invisible (no per-article view of
  `cache_creation_input_tokens` is meaningful in isolation).

### Charting library — Recharts vs. visx vs. Chart.js vs. roll-your-own SVG

No charting library is installed in `UI/package.json` today (`recharts` is not yet a
dependency, confirmed against `feature/ai-request-logging`). We need one chart minimum
(time-series of cost/tokens/requests). Candidates:

- **Recharts** — declarative React-component API (`<LineChart><Line/></LineChart>`),
  widely used, tiny mental model, ~96 kB gzipped, plays well with React 19 and
  Tailwind (charts inherit styling via inline props or CSS variables). **Recommended.**
- **visx** — lower-level d3 wrappers; far more control, but the team would write its
  own `<XAxis>`, `<Tooltip>`, etc. — overkill for one time-series + two bar charts.
- **Chart.js (with `react-chartjs-2`)** — imperative canvas. Renders to canvas, so
  no SVG-friendly Tailwind theming, and feels foreign in a project where every other
  visual is plain JSX + CSS variables.
- **Roll your own SVG** — viable for *one* simple line, but two bar charts plus a line
  chart with tooltips is the threshold where a library wins. Tooltips, axis ticks,
  responsive resizing, and "nice" tick generation are non-trivial.

Recharts is the clear pick: smallest API surface, declarative React, and easy to theme
with the existing CSS variables (`var(--caramel)`, `var(--crimson)`, etc.) by passing
`stroke="var(--caramel)"` directly to its components.

---

## Decision

**Option A — single-page dashboard at `/ai-operations`, with Recharts for visualisations
and a `<SlideOver>` for per-request drill-down.**

### D1. Route and access control

- Add `/ai-operations` to `UI/src/router/index.tsx`, wrapped with `<AdminRoute>` (sibling
  of `/sources`, `/users`, `/publish-targets`).
- Add a sidebar entry in `UI/src/layouts/Sidebar.tsx`:
  `{ to: '/ai-operations', icon: Activity, label: 'AI Operations', adminOnly: true }`.
  `Activity` from `lucide-react` matches the metrics theme; if the team prefers
  `BarChart3` or `Cpu`, those are also fine — picked at implementation time.
- Importing `AiOperationsPage` lazily is **not** required (no other admin route is lazy);
  use a normal `import` to match the file's existing style.

### D2. Feature folder layout

```
UI/src/features/aiOperations/
├── AiOperationsPage.tsx              # composition root, owns filter state
├── useAiRequestMetrics.ts            # React Query hook → metrics endpoint, maps DTO → view-model
├── useAiRequestList.ts               # React Query hook → paginated list endpoint, maps DTO → view-model
├── useAiRequestDetail.ts             # React Query hook → single-request endpoint
├── AiOpsFilterBar.tsx                # date range + provider/worker/model selects + refresh
├── AiOpsKpiStrip.tsx                 # 6 stat cards (mirrors SourceStatsCards style)
├── AiOpsCostTimeChart.tsx            # Recharts time-series (line, stacked by provider)
├── AiOpsBreakdownPanel.tsx           # generic horizontal-bar list, reused twice
├── AiRequestsTable.tsx               # paginated requests table (12-col grid like Sources)
├── AiRequestDetailSlideOver.tsx      # per-request drill-down via <SlideOver>
└── types.ts                          # local view-model types only (NOT API types)
```

Naming follows the existing convention (`features/<area>/<Area>Page.tsx` +
`use<Resource>.ts`, kebab-cased filenames are not used — match `SourcesPage`,
`PublicationsPage`).

### D3. Page composition

`AiOperationsPage.tsx` (the composition root) renders top-to-bottom:

```
<div className="p-8">
  {/* Page Header — same style as SourcesPage */}
  <Header />                                        // h1 "AI Operations" + subtitle "<n> calls in last 7 days · $<x>"

  <AiOpsFilterBar ... />                            // date range, provider, worker, model, status, search, refresh

  <AiOpsKpiStrip metrics={metrics.data} />          // grid grid-cols-2 sm:grid-cols-6 gap-4

  <AiOpsCostTimeChart series={metrics.data?.timeSeries} />  // 1 row, full width, ~h-72

  <div className="grid grid-cols-2 gap-4 mt-4">
    <AiOpsBreakdownPanel title="COST BY MODEL"   rows={metrics.data?.byModel} />
    <AiOpsBreakdownPanel title="COST BY WORKER"  rows={metrics.data?.byWorker} />
  </div>

  <AiRequestsTable
    page={page} pageSize={pageSize}
    filters={tableFilters}                          // includes status + search; metrics filters do NOT
    onRowClick={openDetail}
  />

  <AiRequestDetailSlideOver isOpen={!!detailId} requestId={detailId} onClose={closeDetail} />
</div>
```

All section spacing uses `mb-6` / `mb-8` per existing conventions.

### D4. Components — concrete shape

**`AiOpsKpiStrip.tsx`** — six `StatCard`s in a grid, identical visual to
`SourceStatsCards.StatCard`. Cards (left to right):
1. TOTAL COST — `$1,234.56` formatted with `Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })`.
2. TOTAL CALLS — integer.
3. SUCCESS RATE — `99.2%`, red if < 95% (use `var(--crimson)`).
4. AVG LATENCY — `1,420 ms`.
5. TOTAL TOKENS — integer (sum of `totalInputTokens + totalOutputTokens`) with `Intl.NumberFormat`.
6. CACHE HIT — `cacheReadTokens / (cacheReadTokens + cacheCreationTokens + nonCachedInputTokens)`,
   shown as percentage. Anthropic-only — explain "Anthropic only" in subtitle.

**`AiOpsCostTimeChart.tsx`** — Recharts `<ResponsiveContainer><LineChart>...`:
- X axis: `bucket` parsed via `new Date(...)`, formatted at day granularity
  (e.g. `Apr 18`) using `Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric' })`.
- Y axis: cost in USD (or tokens, or calls — see SegmentedControl below).
- One `<Line>` per provider (`Anthropic`, `Gemini`), stroke = `var(--crimson)` and
  `var(--caramel)`. Hooks must reshape the flat `timeSeries` array (one row per
  bucket+provider) into a wide format suitable for Recharts (one row per bucket with one
  field per provider) — this transformation lives in `useAiRequestMetrics.ts`.
- Tooltip formatter prepends `$` and uses 2-decimal precision.
- Height fixed at `h-72`; container background `rgba(61,15,15,0.3)`,
  `border: 1px solid rgba(255,255,255,0.1)`.
- Y axis label switchable via a small `<SegmentedControl>`-style tab row (`COST` |
  `TOKENS` | `CALLS`) — implemented as 3 `<button>`s using the same tab style as
  `PublicationsPage`'s status filter rail. The selected metric drives the data
  selector applied to the (already reshaped) time series.

**`AiOpsBreakdownPanel.tsx`** — a generic horizontal-bar list. Props:
`{ title: string; rows: Array<{ key: string; label: string; cost: number; calls: number }> }`.
Renders one row per item: label on the left, a horizontal bar whose width is
`(row.cost / max).cost * 100%` filled with `var(--caramel)` at `0.4` opacity, the cost
label on the right. Sort by cost desc, top 10. No charting library needed for this —
it is plain JSX + Tailwind (cheaper than a Recharts BarChart for a list view, and
visually closer to the project's typographic style).

**`AiRequestsTable.tsx`** — same 12-column grid pattern as `SourcesPage` table:
- `TIME` (relative + absolute on hover), `PROVIDER`, `WORKER`, `OPERATION`, `MODEL`,
  `TOKENS` (input/output), `LATENCY`, `COST`, `STATUS` (badge), action column for the
  drill-down click.
- Click anywhere on a row opens `AiRequestDetailSlideOver`.
- `Status` rendered as a badge: `Success` → `var(--caramel)` border, `Error` →
  `var(--crimson)` border.
- Pagination via the existing `<Pagination>` component from
  `@/components/shared/Pagination`. **Note:** the current Pagination component is
  light-themed (gray-100 hover, indigo-600 active). For a v1 of this dashboard we accept
  that visual mismatch — restyling Pagination would touch other admin pages and is
  explicitly out of scope here. Track this as a follow-up if it becomes ugly.

**`AiRequestDetailSlideOver.tsx`** — uses `<SlideOver>` from
`@/components/shared/SlideOver` (signature: `{ isOpen, onClose, title, children }`).
Internally it calls `useAiRequestDetail(requestId)` and shows all fields of the row,
plus the full `ErrorMessage` if status is `Error`, plus the `CorrelationId` and
`ArticleId` (linked to `/articles/<id>` if non-null), and a `COPY ID` button for the
request's `Id` (helpful for log searches).

**`AiOpsFilterBar.tsx`** — controls:
- Date range: two `<input type="date">`s, defaulting to last 7 days. Quick buttons:
  `24H | 7D | 30D | 90D` (a row of 4 small `<button>`s).
- Provider `<select>`: `All | Anthropic | Gemini`.
- Worker `<select>`: populated dynamically from the metrics response's `byWorker` keys
  to avoid hardcoding.
- Model `<select>`: populated from `byModel` keys.
- Status `<select>`: `All | Success | Error` — applies to the requests table only,
  not to the metrics fetch (per the verified backend contract; see "Differences from
  the original D5 contract sketch", point 4).
- Search `<input>` with `maxLength={200}` — applies to the requests table only.
- A manual `REFRESH` button (calls `queryClient.invalidateQueries({ queryKey: ['ai-ops'] })`).
- All controls styled identically to `SourcesPage`'s filter row (`background: var(--near-black)`, etc.).

### D5. Data fetching contract — the verified API surface

The endpoints are already implemented and the generated client exposes them via the
`AiOperationsApi` class. The hooks must wrap these methods and adapt the optional-
everywhere generated DTOs into non-optional view-models in `types.ts`.

**Endpoint 1 — Metrics (aggregated)**
- Generated method: `aiOperationsMetricsGet(from?, to?, provider?, worker?, model?)`.
- Wire route: `GET /ai-operations/metrics?from&to&provider&worker&model`.
- Response: `AiOperationsMetricsDto` —
  ```ts
  {
    totalCostUsd?: number;
    totalCalls?: number;
    successCalls?: number;
    errorCalls?: number;
    averageLatencyMs?: number;
    totalInputTokens?: number;
    totalOutputTokens?: number;
    totalCacheCreationInputTokens?: number;
    totalCacheReadInputTokens?: number;
    timeSeries?: Array<{ bucket?: string; provider?: string | null; costUsd?: number; calls?: number; tokens?: number }> | null;
    byModel?:    Array<{ key?: string | null; calls?: number; costUsd?: number; tokens?: number }> | null;
    byWorker?:   Array<{ key?: string | null; calls?: number; costUsd?: number; tokens?: number }> | null;
    byProvider?: Array<{ key?: string | null; calls?: number; costUsd?: number; tokens?: number }> | null;
  }
  ```
- Filters NOT supported by this endpoint: `status`, `search` (controller passes them as
  `null` regardless of input). Hooks must not pass them.

**Endpoint 2 — Paginated request list**
- Generated method:
  `aiOperationsRequestsGet(from?, to?, provider?, worker?, model?, status?, search?, page?, pageSize?)`.
- Wire route: `GET /ai-operations/requests?from&to&provider&worker&model&status&search&page&pageSize`.
- Response: `AiRequestLogDtoPagedResult` —
  `{ items?: AiRequestLogDto[] | null; page?, pageSize?, totalCount?, totalPages?, hasNextPage?, hasPreviousPage? }`.
- Server enforces `pageSize ∈ [1, 100]`, defaults to 20; `page >= 1`, defaults to 1;
  `status ∈ {"Success", "Error"}` or empty; `search` max length 200. UI must respect
  these constraints to avoid 400 responses.

**Endpoint 3 — Single request detail**
- Generated method: `aiOperationsRequestsIdGet(id)`.
- Wire route: `GET /ai-operations/requests/{id}` (id is `Guid`; route constraint
  `{id:guid}`).
- Response: `AiRequestLogDto` (same shape as list items). Returns 404 if the id is
  unknown.

The `AiRequestLogDto` shape (all fields optional in the generated client):
```ts
{
  id?: string;                            // Guid as string
  timestamp?: string;                     // ISO DateTimeOffset
  worker?: string | null;
  provider?: string | null;
  operation?: string | null;
  model?: string | null;
  inputTokens?: number;
  outputTokens?: number;
  cacheCreationInputTokens?: number;
  cacheReadInputTokens?: number;
  totalTokens?: number;
  costUsd?: number;
  latencyMs?: number;
  status?: string | null;                 // "Success" | "Error"
  errorMessage?: string | null;
  correlationId?: string;                 // Guid as string
  articleId?: string | null;              // Guid as string, nullable
}
```

If a future regeneration changes any name or shape, the implementer **adapts the hooks**,
not the components — components consume local `types.ts` view-models (D8), shielded from
the wire shape.

### D6. Hooks and query keys

All three hooks instantiate `AiOperationsApi` once at module level, mirroring
`useSources.ts`:

```ts
import { AiOperationsApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'

const aiOpsApi = new AiOperationsApi(undefined, '', apiClient)
```

- `useAiRequestMetrics(filters)`
  - `useQuery({ queryKey: ['ai-ops', 'metrics', filters], queryFn, staleTime: 30_000 })`.
  - `queryFn` calls
    `aiOpsApi.aiOperationsMetricsGet(filters.from, filters.to, filters.provider, filters.worker, filters.model)`,
    then maps the response to the `AiOpsMetricsView` view-model in `types.ts`.
  - The reshape from flat `timeSeries` (one row per bucket+provider) to wide format
    (one row per bucket with `Anthropic` and `Gemini` cost/token fields) is also done
    here.
- `useAiRequestList(page, pageSize, filters)`
  - `useQuery({ queryKey: ['ai-ops', 'requests', page, pageSize, filters], staleTime: 10_000, placeholderData: keepPreviousData })`.
  - `queryFn` calls
    `aiOpsApi.aiOperationsRequestsGet(filters.from, filters.to, filters.provider, filters.worker, filters.model, filters.status, filters.search, page, pageSize)`.
- `useAiRequestDetail(id)`
  - `useQuery({ queryKey: ['ai-ops', 'request', id], enabled: !!id })`.
  - `queryFn` calls `aiOpsApi.aiOperationsRequestsIdGet(id)`.

`staleTime: 30_000` for metrics is the principle: an ops dashboard should not refetch on
every tab focus, but it should be fresh enough to be trustworthy. Manual refresh + a
30-second stale window is the sweet spot; the user explicitly chose between auto-refresh
and manual — manual wins because (a) the data is rarely changing per second, and (b)
auto-polling is an unnecessary network/DB cost for a low-traffic admin tool.

**Refresh cadence summary:**
- No `refetchInterval`. No window-focus auto-refetch beyond React Query defaults.
- Manual `REFRESH` button → `queryClient.invalidateQueries({ queryKey: ['ai-ops'] })`
  invalidates everything in the dashboard at once.

### D7. Filter state — local `useState`, not Zustand

The filters (date range, provider, worker, model, status, search, page) live in
`AiOperationsPage` as `useState`, passed down to `AiOpsFilterBar`, `AiOpsKpiStrip`, etc.
This matches every existing admin page (`SourcesPage`, `PublishTargetsPage`,
`UsersPage`, `PublicationsPage`) — none of them use Zustand for page state.

A single `useState<AiOpsFilters>` object is preferable to seven separate `useState`s —
the filter object is also the React Query key suffix, so keeping it as one object
simplifies cache keys.

The metrics hook receives only `{ from, to, provider, worker, model }`; the table hook
receives the full set. The page must select the correct subset before passing to each
hook (a small helper such as `pickMetricsFilters(filters)` is fine).

### D8. View-model types

`UI/src/features/aiOperations/types.ts` defines the shapes the components consume. The
generated DTOs make every field optional, so the view-model layer's job is to default
nullables and rename where useful. Suggested shapes:

```ts
export interface AiOpsFilters {
  from: string;              // ISO date
  to: string;                // ISO date
  provider: '' | 'Anthropic' | 'Gemini';
  worker: string;            // '' = all
  model: string;             // '' = all
  status: '' | 'Success' | 'Error';
  search: string;
}

export interface AiOpsKpis {
  totalCostUsd: number;
  totalCalls: number;
  successCalls: number;
  errorCalls: number;
  successRate: number;       // [0, 1]
  averageLatencyMs: number;
  totalTokens: number;
  cacheHitRate: number;      // [0, 1] — Anthropic only
}

export interface AiOpsTimeBucket {
  bucket: string;            // ISO
  anthropicCost: number;
  geminiCost: number;
  anthropicTokens: number;
  geminiTokens: number;
  anthropicCalls: number;
  geminiCalls: number;
}

export interface AiOpsBreakdownRow {
  key: string;
  calls: number;
  costUsd: number;
  tokens: number;
}

export interface AiOpsRequestRow {
  id: string;
  timestamp: string;
  worker: string;
  provider: string;
  operation: string;
  model: string;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  costUsd: number;
  latencyMs: number;
  status: 'Success' | 'Error';
  errorMessage: string | null;
  correlationId: string;
  articleId: string | null;
}
```

The hook layer maps from generated DTOs into these. This insulates components from any
later renaming on the backend, mirroring the discipline used in
`UI/src/features/publications/types.ts`.

### D9. Charting library — Recharts

Add `recharts` to `UI/package.json` dependencies (latest 3.x; not yet present, confirmed
2026-04-25). Used only by `AiOpsCostTimeChart.tsx` in this ADR; if a future ADR needs
more chart types it is already in place.

### D10. Empty/loading/error states

- Loading: KPI cards show `—`, chart and table show pulsing skeletons (use the same
  `animate-pulse` placeholder pattern as `PublishTargetsPage`).
- Empty: chart shows "No data in selected range" centred. Table shows
  "No requests match your filters." matching `SourcesPage`'s empty state.
- Error: chart and table render a single-line `text-[var(--crimson)] font-mono` row:
  "Failed to load AI operations data." (no toast — the dashboard is read-only, so toast
  is not the right channel). The KPI cards show `—`.

---

## Consequences

**Positive:**
- One screen answers every operator question the AI-cost-tracking initiative was
  commissioned to enable: total cost, cost by provider/worker/model, cache effectiveness,
  failure rate, recent failures with full error message, drill-down on a single call.
- New feature folder is an exact clone of the established admin-page pattern — minimal
  cognitive cost for the next reviewer.
- Recharts is small, well-supported, and reusable for any future visualisation
  (publication throughput, article-pipeline funnels, etc.).
- All new code is read-only; no mutations, no Zustand, no risk of state corruption.

**Negative / risks:**
- New npm dependency (`recharts`) — adds ~96 kB gzipped to the bundle. Mitigation:
  scoped to one component; if bundle size becomes an issue later the chart can be
  lazy-loaded via `React.lazy` (not done in v1).
- The dashboard polls a table that grows unboundedly (`ai_request_log` is append-only).
  Aggregation queries on a year of data may slow over time. Mitigation belongs on the
  backend side (server-side time-bucket index, optional retention policy) — out of scope
  here; the frontend defaults to a 7-day window which keeps queries fast for a long
  time.
- "Cost by model" with many models (Haiku 4.5, Haiku 5, Sonnet 4.5, Gemini Flash, Gemini
  embedding, plus future ones) can outgrow ten rows. The breakdown panel takes top-10
  by cost; a "show all" expand is deferred unless an operator asks for it.
- Currency formatting is hard-coded to USD because the backend stores `CostUsd`. Fine
  for now.
- The shared `Pagination` component is light-themed; placing it under the dark
  dashboard will look slightly off. Accepted in v1; restyling is a separate concern
  that affects every admin list page.

**Backend follow-ups (call out, do not block this ADR):**
- **`/metrics` does not accept `status` or `search` filters.** That is correct for KPI
  rollups, but if an operator ever wants "show cost of *errors* only", the metrics
  controller would have to start passing them through. Out of scope for the dashboard;
  log it as a future backend ticket.
- The detail endpoint returns 404 on missing id but does not distinguish "deleted"
  from "never existed" — irrelevant for an append-only log, just noting it.

**Files affected:**

- **New files (all under `UI/src/features/aiOperations/`):**
  - `AiOperationsPage.tsx`
  - `AiOpsFilterBar.tsx`
  - `AiOpsKpiStrip.tsx`
  - `AiOpsCostTimeChart.tsx`
  - `AiOpsBreakdownPanel.tsx`
  - `AiRequestsTable.tsx`
  - `AiRequestDetailSlideOver.tsx`
  - `useAiRequestMetrics.ts`
  - `useAiRequestList.ts`
  - `useAiRequestDetail.ts`
  - `types.ts`
- **Modified:**
  - `UI/src/router/index.tsx` — add `/ai-operations` route inside `<AdminRoute>` block.
  - `UI/src/layouts/Sidebar.tsx` — add nav entry with `adminOnly: true`.
  - `UI/package.json` + `UI/package-lock.json` — add `recharts` dependency.
- **Untouched:**
  - `UI/src/api/generated/*` — never edited by hand; already regenerated.
  - All other feature folders.
  - All Core / Infrastructure / Api / Worker C# code (this is a frontend-only ADR; the
    backend half landed in commit `8a2c2c9`).

---

## Implementation Notes

### Order of work

1. Add `recharts` to `UI/package.json`, run `npm install`.
2. Add the route in `UI/src/router/index.tsx` and the sidebar entry in
   `UI/src/layouts/Sidebar.tsx`. Render a placeholder `AiOperationsPage` returning
   "Coming soon" — confirm that `<AdminRoute>` correctly redirects an Editor user.
3. Create `types.ts` with the view-model shapes from D8 first; this is the contract the
   rest of the feature consumes.
4. Build the hooks (`useAiRequestMetrics`, `useAiRequestList`, `useAiRequestDetail`)
   next; verify with React Query Devtools that keys are correct and no extra refetches
   happen. Each hook instantiates `AiOperationsApi` once at module level (mirror
   `useSources.ts`).
5. Build the visual components in this order: `AiOpsKpiStrip` → `AiOpsBreakdownPanel`
   → `AiRequestsTable` → `AiRequestDetailSlideOver` → `AiOpsCostTimeChart`. The
   chart is last because it is the only piece that introduces `recharts`.
6. Wire the filters through `AiOpsFilterBar` last — this exposes the cache-key shape
   to the hooks and is easier to validate once the rest of the UI exists.
7. End-to-end smoke test: log in as Admin, change date range, confirm metrics update;
   click a request row, confirm the slide-over shows full details including
   `errorMessage` for an error row.

### Skills `feature-planner` must consult

- `.claude/skills/code-conventions/SKILL.md` — file placement under
  `UI/src/features/<area>/`; nothing Zustand-related is needed for this feature.
- `.claude/skills/clean-code/SKILL.md` — keep page components composition-only
  (logic in hooks); avoid long inline className strings that can be promoted to
  module-level `const` (see the `inputClass` / `inputStyle` pattern in
  `UsersPage.tsx`); no magic numbers — `staleTime`, default page size, and the 7-day
  default range go to module consts.
- `.claude/skills/api-conventions/SKILL.md` — only relevant if the planner ends up
  needing to coordinate any backend tweak; for the UI portion the only constraint is
  that the auto-generated client is **never** edited by hand.
- `.claude/skills/mappers/SKILL.md` — not directly applicable (no C# DTOs created
  here), but the spirit applies: the tiny mapping layer in `useAiRequestMetrics.ts` /
  `useAiRequestList.ts` converts `AiOperationsMetricsDto` (generated, all-optional)
  into `AiOpsKpis` / `AiOpsTimeBucket[]` / `AiOpsRequestRow[]` (local view-models).
  Components must consume the view-models, not the generated DTOs directly — same
  insulation principle as in `UI/src/features/publications/types.ts`.
- `UI/CLAUDE.md` — query-key convention (`['resource', ...filters]`), state-management
  table (server data → React Query, UI state → `useState`), routing wrappers
  (`ProtectedRoute`, `AdminRoute`).

### Out of scope (do not expand)

- No write endpoints, no mutations on AI request logs.
- No retention/archival UI for `ai_request_log`.
- No alerting / threshold-breach notifications.
- No Excel/CSV export (deferred until an operator asks).
- No per-cycle correlation drill-down view (clicking a `CorrelationId` to see all
  requests in that cycle); the `CorrelationId` is shown in the slide-over for now,
  searchable via the search field. Promote to a dedicated view in a future ADR if
  operators request it.
- No backend changes — the controller, DTOs, and repository methods already exist
  and are not modified by this ADR. If a backend follow-up is needed (e.g. status
  filter on `/metrics`), it is a separate ADR.
- No restyling of the shared `<Pagination>` component (light-themed) to match the
  dashboard's dark theme — separate concern.
