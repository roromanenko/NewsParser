# Server-Side Search, Sort, and Pagination for Articles and Events

## Context

Search on the Articles page is currently performed client-side: the API returns a page of 20 items, and the UI filters that array by title match (`ArticlesPage.tsx:39-44`). This means the user can only search within the currently loaded page, not across all records. The Events page has no search at all.

Sorting is hardcoded: articles sort by `ProcessedAt DESC`, events sort by `LastUpdatedAt DESC`. There is no way for the user to change the sort order. The feature request calls for newest/oldest sorting now, with future semantic sort options (priority, relevance) planned.

Additionally, there is a bug in `EventsController.GetAll`: it calls `CountActiveAsync` (which filters by `Status == Active`) for the total count, but `GetPagedAsync` returns events of all statuses. The count and data are inconsistent.

**Affected layers:** Core (interfaces), Infrastructure (repositories), Api (controllers), UI (hooks, pages).

## Options

### Option 1 -- PostgreSQL ILIKE for search

Add a `search` query parameter. In the repository, apply `WHERE Title ILIKE '%search%' OR Summary ILIKE '%search%'` using EF Core's `EF.Functions.ILike`. Simple, no schema changes, good enough for title/summary substring matching on datasets of this size.

**Pros:** No migration needed. Simple implementation. EF Core supports `EF.Functions.ILike` natively with Npgsql.
**Cons:** No ranking/relevance scoring. Slower on very large tables without a trigram index. Cannot match across word boundaries intelligently.

### Option 2 -- PostgreSQL full-text search (tsvector/tsquery)

Add `tsvector` columns to Article and Event, create GIN indexes, use `EF.Functions.ToTsVector` and `EF.Functions.WebSearchToTsQuery` for ranked search.

**Pros:** Proper ranking. Handles word stems, language-aware. Scales better on large datasets.
**Cons:** Requires migration to add `tsvector` columns and GIN indexes. More complex query logic. Overkill for current dataset size and search requirements (title/summary substring is sufficient).

## Decision

**Option 1 -- ILIKE search.** The dataset is small (hundreds to low thousands of articles/events), and the search requirement is simple substring matching on title and summary. ILIKE is the pragmatic choice. If performance becomes an issue later, a trigram GIN index (`pg_trgm`) or full-text search can be added without changing the API contract.

### API Contract Changes

Both `GET /articles` and `GET /events` will accept these new query parameters:

| Parameter | Type | Default | Description |
|---|---|---|---|
| `page` | int | 1 | Page number (existing) |
| `pageSize` | int | 20 | Items per page (existing) |
| `search` | string? | null | Substring search across Title and Summary (case-insensitive) |
| `sortBy` | string? | null | Sort field name (see below) |
| `sortDirection` | string? | "desc" | `asc` or `desc` |

### Sort Options

Sort options are categorized for future extensibility:

**Basic sorts** (implemented now):
- `newest` -- `ProcessedAt DESC` for articles, `LastUpdatedAt DESC` for events (default)
- `oldest` -- `ProcessedAt ASC` for articles, `LastUpdatedAt ASC` for events

**Semantic sorts** (future, not implemented now):
- `priority`, `relevance` -- will require additional scoring logic

In the API, `sortBy` accepts a string value. The controller validates it against a known set. For now, only `"newest"` and `"oldest"` are accepted. The default when `sortBy` is null is `"newest"` (preserving current behavior). The `sortDirection` parameter is not needed separately because `newest`/`oldest` encode direction intrinsically. However, to support future semantic sorts that may need direction control, the sort value itself carries the direction: `newest` = date descending, `oldest` = date ascending.

Simplification: since `newest`/`oldest` encode direction, there is no separate `sortDirection` parameter for now. The single `sortBy` parameter is sufficient. When semantic sorts are added, a `sortDirection` parameter can be introduced if needed.

Final query parameters:

| Parameter | Type | Default |
|---|---|---|
| `page` | int | 1 |
| `pageSize` | int | 20 |
| `search` | string? | null |
| `sortBy` | string? | "newest" |

### Repository Changes

**`IArticleRepository`** -- replace `GetAnalysisDoneAsync` and `CountAnalysisDoneAsync` with:
```
GetAnalysisDoneAsync(int page, int pageSize, string? search, string sortBy, CancellationToken)
CountAnalysisDoneAsync(string? search, CancellationToken)
```

**`IEventRepository`** -- replace `GetPagedAsync` and `CountActiveAsync` with:
```
GetPagedAsync(int page, int pageSize, string? search, string sortBy, CancellationToken)
CountAsync(string? search, CancellationToken)
```

The `CountAsync` method replaces `CountActiveAsync` and no longer filters by Active status only -- it counts all events matching the search filter, consistent with `GetPagedAsync` which returns all statuses. This fixes the existing count/data mismatch bug.

In the repository implementations:
- Apply `EF.Functions.ILike(entity.Title, $"%{search}%")` and similar for Summary when `search` is not null/empty.
- Apply sort ordering based on the `sortBy` string using a switch expression.
- Sanitize the search string to escape LIKE wildcards (`%`, `_`) in user input.

### Controller Changes

**`ArticlesController.GetAnalysisDone`** -- add `[FromQuery] string? search = null` and `[FromQuery] string? sortBy = "newest"` parameters. Validate `sortBy` against allowed values. Pass to repository.

**`EventsController.GetAll`** -- same pattern. Replace `CountActiveAsync` call with `CountAsync(search, ...)`.

### UI Changes

**`useArticles.ts`** and **`useEvents.ts`** -- accept `search` and `sortBy` parameters, include them in query key and API call.

**`ArticlesPage.tsx`**:
- Remove the client-side `filtered` array logic (lines 39-44).
- Debounce the search input (300-500ms) before updating state that triggers the API call.
- Reset `page` to 1 when search or sort changes.
- Add sort toggle (newest/oldest) to the filter sidebar.

**`EventsPage.tsx`**:
- Add search input and sort toggle (mirroring the articles page sidebar pattern).
- Reset `page` to 1 when search or sort changes.

After backend changes, regenerate the API client with `npm run generate-api`.

## Implementation Notes

### Files to change

**Core layer:**
- `Core/Interfaces/Repositories/IArticleRepository.cs` -- update `GetAnalysisDoneAsync` and `CountAnalysisDoneAsync` signatures
- `Core/Interfaces/Repositories/IEventRepository.cs` -- update `GetPagedAsync` signature, rename `CountActiveAsync` to `CountAsync` with search parameter

**Infrastructure layer:**
- `Infrastructure/Persistence/Repositories/ArticleRepository.cs` -- implement search + sort in `GetAnalysisDoneAsync` and `CountAnalysisDoneAsync`
- `Infrastructure/Persistence/Repositories/EventRepository.cs` -- implement search + sort in `GetPagedAsync`, replace `CountActiveAsync` with `CountAsync`

**Api layer:**
- `Api/Controllers/ArticlesController.cs` -- add `search` and `sortBy` query parameters to `GetAnalysisDone`
- `Api/Controllers/EventsController.cs` -- add `search` and `sortBy` query parameters to `GetAll`, fix count call

**UI layer:**
- `UI/src/features/articles/useArticles.ts` -- accept and pass search/sortBy
- `UI/src/features/events/useEvents.ts` -- accept and pass search/sortBy
- `UI/src/features/articles/ArticlesPage.tsx` -- remove client-side search, add debounced server search, add sort toggle, reset page on filter change
- `UI/src/features/events/EventsPage.tsx` -- add search input, sort toggle, debounced search, reset page on filter change
- Regenerate API client after backend changes

### Skills to follow
- `.claude/skills/ef-core-conventions/SKILL.md` -- repository method naming, CancellationToken placement, Include patterns
- `.claude/skills/api-conventions/SKILL.md` -- query parameter conventions, pagination guard pattern, PagedResult usage
- `.claude/skills/code-conventions/SKILL.md` -- layer boundaries, primary constructor style
- `.claude/skills/clean-code/SKILL.md` -- no magic strings for sort values (use constants or a validated set), early returns for validation

### Key implementation details
- Use `EF.Functions.ILike` (not `string.Contains`) for case-insensitive PostgreSQL search.
- Escape LIKE wildcards in user input before passing to ILIKE.
- Sort switch should use a `switch` expression mapping `"newest"` / `"oldest"` to the appropriate `OrderBy`/`OrderByDescending` call. Unknown values should fall back to `"newest"`.
- The debounce in the UI should be 300ms to avoid excessive API calls while typing.
- The sentiment filter on the Articles page remains client-side for now (it filters the current page only) -- this is acceptable as it is a secondary filter and the dataset per page is small.
