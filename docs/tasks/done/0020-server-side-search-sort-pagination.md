# Server-Side Search, Sort, and Pagination for Articles and Events

## Goal
Move article and event search to the server using PostgreSQL ILIKE, add `search` and `sortBy` (newest/oldest) query parameters to both list endpoints, fix the EventsController count/data mismatch bug, and expose debounced search + sort controls in the UI.

## Affected Layers
- Core
- Infrastructure
- Api
- UI

## Tasks

### Core

- [x] **Modify `Core/Interfaces/Repositories/IArticleRepository.cs`** — update `GetAnalysisDoneAsync` signature to `GetAnalysisDoneAsync(int page, int pageSize, string? search, string sortBy, CancellationToken cancellationToken = default)` and `CountAnalysisDoneAsync` to `CountAnalysisDoneAsync(string? search, CancellationToken cancellationToken = default)`
      _Acceptance: interface compiles; no implementation details; existing unrelated signatures are unchanged_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/Interfaces/Repositories/IEventRepository.cs`** — update `GetPagedAsync` signature to `GetPagedAsync(int page, int pageSize, string? search, string sortBy, CancellationToken cancellationToken = default)`, rename `CountActiveAsync` to `CountAsync` and change its signature to `CountAsync(string? search, CancellationToken cancellationToken = default)`
      _Acceptance: interface compiles; `CountActiveAsync` no longer exists in the interface; all other signatures are unchanged_
      _Skill: .claude/skills/code-conventions/SKILL.md_

### Infrastructure

- [x] **Modify `Infrastructure/Persistence/Repositories/ArticleRepository.cs`** — update `GetAnalysisDoneAsync` and `CountAnalysisDoneAsync` to accept the new parameters; apply `EF.Functions.ILike` on `Title` and `Summary` when `search` is not null/empty (escape `%` and `_` in the search string before use); apply sort using a switch expression mapping `"oldest"` to `OrderBy(a => a.ProcessedAt)` and all other values (including `"newest"`) to `OrderByDescending(a => a.ProcessedAt)`
      _Acceptance: both methods satisfy the updated `IArticleRepository` interface; `using Microsoft.EntityFrameworkCore` is present for `EF.Functions.ILike`; no raw SQL; wildcard characters in user input do not break the query_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/EventRepository.cs`** — update `GetPagedAsync` to accept the new parameters and apply ILIKE search on `Title` and `Summary` (same escaping pattern) and sort switch expression mapping `"oldest"` to `OrderBy(e => e.LastUpdatedAt)` and all other values to `OrderByDescending(e => e.LastUpdatedAt)`; replace the `CountActiveAsync` method with `CountAsync(string? search, CancellationToken)` that counts all events (no status filter) matching the search predicate
      _Acceptance: class satisfies the updated `IEventRepository` interface; `CountActiveAsync` is removed; no status filter in `CountAsync`; wildcard escaping applied_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

### Api

- [x] **Modify `Api/Controllers/ArticlesController.cs`** — add `[FromQuery] string? search = null` and `[FromQuery] string? sortBy = "newest"` parameters to `GetAnalysisDone`; validate `sortBy` against the allowed set `{ "newest", "oldest" }` (reset to `"newest"` if unrecognised); pass both to `GetAnalysisDoneAsync` and `CountAnalysisDoneAsync`
      _Acceptance: `GET /articles?search=foo&sortBy=oldest` returns filtered and sorted results; invalid `sortBy` falls back to `"newest"` rather than returning an error; Swagger shows both new query params_
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Controllers/EventsController.cs`** — add `[FromQuery] string? search = null` and `[FromQuery] string? sortBy = "newest"` parameters to `GetAll`; validate `sortBy` the same way; pass both to `GetPagedAsync`; replace the `CountActiveAsync(cancellationToken)` call with `CountAsync(search, cancellationToken)`
      _Acceptance: `GET /events?search=foo&sortBy=oldest` returns filtered and sorted results; `totalCount` in the response now reflects all events matching the search (not Active-only); Swagger shows both new query params_
      _Skill: .claude/skills/api-conventions/SKILL.md_

### UI

- [x] **Regenerate `UI/src/api/generated/api.ts`** — run `npm run generate-api` (backend must be running on port 5172) to pick up the new `search` and `sortBy` query parameters in `articlesGet` and `eventsGet`
      _Acceptance: `articlesApi.articlesGet` and `eventsApi.eventsGet` accept `search` and `sortBy` as optional parameters; no manual edits to files under `UI/src/api/generated/`_

- [x] **Modify `UI/src/features/articles/useArticles.ts`** — accept `search: string` and `sortBy: string` parameters alongside existing `page` and `pageSize`; include them in the `queryKey` array and pass them to `articlesApi.articlesGet`
      _Acceptance: TypeScript compiles with no `any`; changing `search` or `sortBy` produces a new cache entry (both are in the query key)_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/events/useEvents.ts`** — same pattern: accept `search: string` and `sortBy: string`, add to query key, pass to `eventsApi.eventsGet`
      _Acceptance: TypeScript compiles with no `any`; both params in the query key_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/articles/ArticlesPage.tsx`** — remove the client-side `filtered` array (lines 39–44); add a `sortBy` state (default `"newest"`); introduce a 300ms debounced value of the `search` state and use the debounced value (not the raw state) when calling `useArticles`; reset `page` to 1 whenever debounced search or `sortBy` changes (use a `useEffect` watching both); add a SORT section to the left sidebar with NEWEST and OLDEST buttons (same visual style as the SENTIMENT filter buttons); update the items-count line in the header to use `data?.totalCount` instead of `filtered.length`; pass `debounced search` and `sortBy` to `useArticles`
      _Acceptance: typing in the search box triggers an API call after 300ms, not on every keystroke; switching sort or clearing search resets the page to 1; the SENTIMENT filter continues to work client-side on the current page; no reference to a `filtered` variable remains in the component_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/events/EventsPage.tsx`** — add `search` state, `sortBy` state (default `"newest"`), and a 300ms debounced search value; add a left sidebar (`<aside>`) with a search input (same markup pattern as ArticlesPage) and a SORT section (NEWEST / OLDEST buttons); reset `page` to 1 when debounced search or `sortBy` changes; pass debounced search and `sortBy` to `useEvents`; keep the MERGE EVENTS button in the header
      _Acceptance: EventsPage now has a visible left sidebar with search and sort controls; typing debounces correctly; page resets on filter change; MergeEventsSlideOver is still rendered and functional; TypeScript compiles_
      _Skill: .claude/skills/code-conventions/SKILL.md_

## Open Questions
- None — the ADR fully specifies the approach, affected files, and all implementation details.
