# Display Media in Article and Event Views (UI)

## Context

ADR 0006 (`0006-media-files-cloudflare-r2.md`) landed the media ingestion
pipeline. Today the backend:

- Persists `MediaFileEntity` rows in `media_files` with `ArticleId`, `R2Key`,
  `OriginalUrl`, `ContentType`, `SizeBytes`, `Kind` (`Image`/`Video`),
  `CreatedAt`.
- Exposes `IMediaFileRepository.GetByArticleIdAsync(Guid articleId, ...)` but
  no call site uses it yet outside the ingestion flow.
- Defines `CloudflareR2Options.PublicBaseUrl` (e.g. `https://pub-xyz.r2.dev`)
  but **nothing in the codebase currently consumes it** — the public URL is
  never constructed. ADR 0006 §6 explicitly leaves this to "callers who need
  it (e.g. publishers)".

No navigation property `ArticleEntity.MediaFiles` exists — ADR 0006 §3
deferred it: *"If the Api layer later needs eager-loaded media on the article
detail view, a future ADR can add the navigation property and an
`Include`-based `GetDetailWithMediaAsync`."* **This is that future ADR.**

On the API side:

- `ArticleDetailDto` (`Api/Models/ArticleDetailDto.cs`) has no media field.
- `EventDetailDto` / `EventArticleDto` (`Api/Models/EventDtos.cs`) have no
  media fields.
- `ArticlesController.GetById` calls `articleRepository.GetByIdAsync` which
  does not load media.
- `EventsController.GetById` calls `eventRepository.GetDetailAsync` which
  includes `Articles` but not their media.

On the UI side (React 19 + TS):

- `src/features/articles/ArticleDetailPage.tsx` renders a header card and a
  left-column content area (Summary, Key Facts) plus a right-column sidebar.
  No media rendering anywhere.
- `src/features/events/EventDetailPage.tsx` renders a header card and three
  tabs: `timeline`, `updates`, `contradictions`. No media rendering.
- API client is auto-generated into `src/api/generated/` via
  `npm run generate-api`. **Manual edits are forbidden** (`UI/CLAUDE.md`).
- Base components live in `src/components/ui/` (CVA + Tailwind); shared
  structural components in `src/components/shared/`. No existing
  image/video/lightbox component.

The feature requirement:

- On the **Article view**: show that article's images and videos.
- On the **Event view**: show **all** media from **all** articles belonging
  to the event.
- Must follow existing UI style (dark burgundy cards, caramel accents,
  `font-display` / `font-mono` / `font-caps`).

### Constraints discovered during exploration

1. **Public URL construction must live somewhere.** `MediaFile` stores only
   `R2Key`; the full URL is `{PublicBaseUrl}/{R2Key}`. That construction
   must happen at the API boundary (on the way out) so the UI never learns
   about R2 keys, bucket layout, or any storage details.
2. **Api/Mappers is a pure function layer.** Per
   `.claude/skills/mappers/SKILL.md`: *"no logging, no try/catch, no side
   effects, no DI"*. A mapper cannot inject `IOptions<CloudflareR2Options>`.
   The public base URL must be passed **into** the mapper as a parameter.
3. **Api must not touch EF or DbContext.** Loading media rows must happen
   inside `Infrastructure/Persistence/Repositories/` — either via a new
   navigation property + `Include`, or via a separate
   `IMediaFileRepository` call from the controller (controllers calling
   repositories is already the project pattern, see
   `ArticlesController` → `IEventRepository.GetByIdAsync`).
4. **Per-article `GetByArticleIdAsync` already exists.** We do not need to
   add a new repository method for the Article view — it already works.
5. **Events can contain many articles.** N+1 calls to
   `GetByArticleIdAsync` for an event with 20+ articles is wasteful. A
   single `GetByArticleIdsAsync(IReadOnlyList<Guid>, ...)` is a small,
   focused addition that matches existing repository naming conventions.
6. **The generated TS client must be regenerated.** Any change to the DTOs
   requires `npm run generate-api` to be run — it is not optional and it
   is a human step, not a code change the implementer writes.
7. **No existing lightbox / gallery component.** The project has no
   dependency like `yet-another-react-lightbox`. Adding one is out of scope
   unless the "Option B" gallery route is chosen below. A plain CSS grid
   with native `<img>` / `<video controls>` is sufficient for v1 and
   matches the project's minimal aesthetic (dark burgundy cards, no heavy
   component libraries — only `lucide-react` icons and Tailwind).

## Options

### Option 1 — Separate `GET /articles/{id}/media` and `GET /events/{id}/media` endpoints

Keep `ArticleDetailDto` and `EventDetailDto` unchanged. Add two new
endpoints that return `List<MediaFileDto>`. The UI fires a second fetch
after the detail fetch and renders a media section.

**Pros:**
- Zero change to existing DTOs — no risk of breaking other consumers.
- Clean separation: media is an independent resource.
- Easier to cache and invalidate independently.

**Cons:**
- Two round trips per page render. For Event view, this also means the
  client has to wait until the first response before it even knows which
  article IDs to query — unless the media endpoint accepts the event ID
  directly, which duplicates logic already present on the server.
- Two new controller methods, two new hooks, two new query keys, two new
  loading states in the UI — noticeably more surface area than embedding.
- Goes against the shape of existing detail DTOs, which already embed
  related sub-objects (`ArticleDetailDto.Event`, `EventDetailDto.Articles`,
  `EventDetailDto.Contradictions`).

### Option 2 — Embed media directly in existing detail DTOs (chosen)

Extend `ArticleDetailDto` and `EventDetailDto` to carry their media inline.
Specifically:

- `ArticleDetailDto` gains `List<MediaFileDto> Media`.
- `EventArticleDto` (the per-article element inside `EventDetailDto`) gains
  `List<MediaFileDto> Media`. `EventDetailDto` itself does **not** gain a
  flat media field — media stays grouped by article on the wire so the UI
  can optionally attribute each media back to its source article. The Event
  view UI flattens + deduplicates on the client.
- A new `MediaFileDto` record carries the fully-qualified public URL
  (already concatenated with `PublicBaseUrl`), plus `Kind`, `ContentType`,
  `SizeBytes`, and the origin `ArticleId` for Event-level attribution.
- `ArticleRepository.GetByIdAsync` gains a media `Include` via a new nav
  property, **or** the controller loads media separately via
  `IMediaFileRepository.GetByArticleIdsAsync` and passes it to the mapper.
  Choice: **navigation property + Include**, because ADR 0006 already
  anticipated it ("a future ADR can add the navigation property") and it
  keeps the data loading in one place.
- `EventRepository.GetDetailAsync` gains
  `.Include(e => e.Articles).ThenInclude(a => a.MediaFiles)`.

**Pros:**
- Single round trip per view. Matches the existing detail-DTO shape
  (`Event` embeds `Articles`, `Contradictions`, `EventUpdates` all
  inline — media is no different).
- One new hook is not needed; existing `useArticleDetail` / `useEventDetail`
  simply expose a new `.media` field on their response.
- Regenerating the TS client via `npm run generate-api` is a single step.
- No new controller routes; no new query keys.

**Cons:**
- Every `GET /articles/{id}` and `GET /events/{id}` response grows by N
  rows of media metadata even for callers that don't need it. Acceptable —
  metadata is small (≈150 bytes per media row), and detail endpoints are
  only called from detail pages which do want the media.
- Adds a nav property `ArticleEntity.MediaFiles` which means the existing
  worker queries that load `ArticleEntity` without `Include` must still
  work correctly (they do — EF does not eager-load without `Include`).

### Option 3 — Inline media only on the Article endpoint; separate aggregate for the Event view

Ship Option 2 for `ArticleDetailDto` but, for the Event view, keep the
existing `EventDetailDto` unchanged and add a new
`GET /events/{id}/media` aggregate endpoint that returns a flattened,
deduplicated `List<MediaFileDto>`.

**Pros:**
- Decouples the Event "overview" from its media payload — callers who
  only want the timeline / contradictions pay no bandwidth cost for media.
- Deduplication happens server-side, which is closer to where the data
  lives and avoids a client-side algorithm.

**Cons:**
- Inconsistent: Articles embed, Events don't. Every future consumer has
  to remember the asymmetry.
- Still two round trips for the Event view.
- Event detail already embeds everything else (articles, updates,
  contradictions); carving out media as the one exception is not
  justified by any measurable payload concern.

## Decision

**Adopt Option 2.** Extend the existing detail DTOs with embedded media,
add a nav property + `Include`, and render media in the existing detail
pages using plain HTML elements styled to match the current theme.

### 1. Domain/Infrastructure — add `Article.MediaFiles` navigation

- **`Core/DomainModels/Article.cs`**: add
  `public List<MediaFile> MediaFiles { get; set; } = [];`
  (mutable, collection-initialized per
  `.claude/skills/code-conventions/SKILL.md` §Domain Model Conventions).
- **`Infrastructure/Persistence/Entity/ArticleEntity.cs`**: add
  `public List<MediaFileEntity> MediaFiles { get; set; } = [];`
- **`Infrastructure/Persistence/Configurations/ArticleConfiguration.cs`**:
  declare the relationship explicitly:
  ```csharp
  builder.HasMany(a => a.MediaFiles)
         .WithOne()
         .HasForeignKey(m => m.ArticleId)
         .OnDelete(DeleteBehavior.Cascade);
  ```
  This replaces the unidirectional `HasMany<MediaFileEntity>().WithOne()`
  declared in ADR 0006 §3. The cascade behaviour and FK stay identical —
  the only change is that the `HasMany` side now has a navigation
  expression. Verify with `dotnet ef migrations add` that EF generates an
  **empty** migration (schema unchanged). If EF instead generates a diff,
  add an empty migration by hand (ADR 0006 already created the FK).
- **`Infrastructure/Persistence/Mappers/ArticleMapper.cs`**: extend
  `ToDomain` to map `entity.MediaFiles?.Select(m => m.ToDomain()).ToList() ?? []`
  (follows the existing null-guarded collection pattern from
  `EventMapper.ToDomain`). `ToEntity` does **not** map `MediaFiles` —
  article writes never originate from the Api layer, and media rows are
  inserted exclusively by `MediaIngestionService`.

### 2. Infrastructure — load media on detail queries

- **`Infrastructure/Persistence/Repositories/ArticleRepository.cs`
  `GetByIdAsync`**: add `.Include(a => a.MediaFiles)` to the query used
  by `ArticlesController.GetById`. Do **not** add the include to
  `GetAnalysisDoneAsync`, `GetPendingAsync`, or
  `GetPendingForClassificationAsync` — those are worker/list queries
  and must not pay the cost.
- **`Infrastructure/Persistence/Repositories/EventRepository.cs`
  `GetDetailAsync`**: add `.ThenInclude(a => a.MediaFiles)` chained onto
  the existing `Include(e => e.Articles)`. Do **not** modify
  `GetPagedAsync`, `GetActiveEventsAsync`, `GetByIdAsync`,
  `FindSimilarEventsAsync`, or `GetWithContextAsync` — those are
  list/worker queries and media is only needed on the detail screen.

No new repository methods. No new navigation on `MediaFileEntity` side
(ADR 0006 never added a back-reference and we don't need one).

### 3. Api — DTOs

Add to **`Api/Models/ArticleDetailDto.cs`** (same file):

```csharp
public record MediaFileDto(
    Guid Id,
    Guid ArticleId,
    string Url,
    string Kind,
    string ContentType,
    long SizeBytes
);
```

And extend the existing records:

```csharp
public record ArticleDetailDto(
    Guid Id,
    string Title,
    string Category,
    List<string> Tags,
    string Sentiment,
    string Language,
    string? Summary,
    List<string> KeyFacts,
    DateTimeOffset ProcessedAt,
    string ModelVersion,
    string? OriginalUrl,
    DateTimeOffset? PublishedAt,
    ArticleEventDto? Event,
    List<MediaFileDto> Media          // NEW — always present, empty list if none
);
```

In **`Api/Models/EventDtos.cs`**, extend `EventArticleDto`:

```csharp
public record EventArticleDto(
    Guid ArticleId,
    string Title,
    string? Summary,
    List<string> KeyFacts,
    string Role,
    DateTimeOffset AddedAt,
    List<MediaFileDto> Media          // NEW — always present, empty list if none
);
```

`MediaFileDto` lives next to `ArticleDetailDto` (per the mappers skill:
*"Keep related sub-mappers in the same file as their parent aggregate
mapper"* — the same locality rule applies to DTOs). Do **not** create a
separate `MediaDtos.cs` file.

`Url` is the fully-qualified public URL already concatenated with
`PublicBaseUrl`, never the raw R2 key. The UI never needs to know a key,
a bucket, a base, or that R2 exists.

### 4. Api — Mappers

New file **`Api/Mappers/MediaFileMapper.cs`**:

```csharp
using Api.Models;
using Core.DomainModels;

namespace Api.Mappers;

public static class MediaFileMapper
{
    public static MediaFileDto ToDto(this MediaFile media, string publicBaseUrl) => new(
        media.Id,
        media.ArticleId,
        BuildUrl(publicBaseUrl, media.R2Key),
        media.Kind.ToString(),
        media.ContentType,
        media.SizeBytes
    );

    private static string BuildUrl(string publicBaseUrl, string r2Key)
        => $"{publicBaseUrl.TrimEnd('/')}/{r2Key.TrimStart('/')}";
}
```

Update **`Api/Mappers/ArticleMapper.cs`**:

- `ToDetailDto` gains a required `string publicBaseUrl` parameter (after
  `this`, after the optional `Event? evt`):
  ```csharp
  public static ArticleDetailDto ToDetailDto(
      this Article article,
      string publicBaseUrl,
      Event? evt = null)
  ```
  Rationale per `.claude/skills/mappers/SKILL.md` §"Extra parameters on
  `ToEntity`" (the same reasoning applies to `ToDetailDto`): *"When the
  entity requires context that the domain model doesn't carry, add
  required/optional parameters after `this`"*. `publicBaseUrl` is exactly
  that kind of context.
- Inside the mapper body:
  ```csharp
  Media = article.MediaFiles
      .Select(m => m.ToDto(publicBaseUrl))
      .ToList()
  ```

Update **`Api/Mappers/EventMapper.cs`**:

- `ToDetailDto` gains `string publicBaseUrl`:
  ```csharp
  public static EventDetailDto ToDetailDto(this Event evt, string publicBaseUrl)
  ```
- `ToEventArticleDto` gains `string publicBaseUrl`:
  ```csharp
  public static EventArticleDto ToEventArticleDto(this Article article, string publicBaseUrl)
  ```
  and the mapping body includes:
  ```csharp
  Media = article.MediaFiles
      .Select(m => m.ToDto(publicBaseUrl))
      .ToList()
  ```
- `ToListItemDto` is **not** modified — list views do not need media and
  adding the parameter there would force every caller to plumb the base
  URL through for no reason.

### 5. Api — Controllers

Both affected controllers inject the new options inline via
`IOptions<CloudflareR2Options>`:

- **`ArticlesController`**: add `IOptions<CloudflareR2Options> r2Options`
  to the primary constructor parameter list. Extract
  `private readonly string _publicBaseUrl = r2Options.Value.PublicBaseUrl;`
  as a field-like assignment in the primary constructor body pattern.
  Since primary constructors can't have a body, use a field initializer:
  ```csharp
  public class ArticlesController(
      IArticleRepository articleRepository,
      IEventRepository eventRepository,
      IOptions<CloudflareR2Options> r2Options) : BaseController
  {
      private readonly string _publicBaseUrl = r2Options.Value.PublicBaseUrl;
      ...
  }
  ```
  This matches how workers extract `_options = options.Value` (see
  `.claude/skills/code-conventions/SKILL.md` §Constructor Injection
  Style). Inside `GetById`:
  ```csharp
  return Ok(article.ToDetailDto(_publicBaseUrl, relatedEvent));
  ```

- **`EventsController`**: identical pattern — add
  `IOptions<CloudflareR2Options> r2Options` to the primary constructor,
  store `_publicBaseUrl`, and pass it into `evt.ToDetailDto(_publicBaseUrl)`
  inside `GetById`. `Approve` and `Reject` call
  `.ToListItemDto()` which does **not** take the base URL, so those
  endpoints are untouched.

No new routes, no new query parameters, no auth changes.
`CloudflareR2Options` is already registered in DI via
`InfrastructureServiceExtensions.AddStorage(configuration)` (see ADR 0006
§8), so `IOptions<CloudflareR2Options>` is already injectable into
controllers.

### 6. UI — Regenerate API client

After the backend changes compile, run `npm run generate-api` from
`UI/`. This regenerates `src/api/generated/` with the new `Media` fields
on `ArticleDetailDto` and `EventArticleDto`, and the new `MediaFileDto`
type. The implementer **must not** hand-edit generated files (enforced
by `UI/CLAUDE.md`).

### 7. UI — New component `MediaGallery`

Create **`UI/src/components/shared/MediaGallery.tsx`** — a presentational
component, no hooks, no state except a single `selectedIndex` for
future lightbox expansion (not implemented in v1). Props:

```ts
type MediaItem = {
  id: string
  url: string
  kind: 'Image' | 'Video'
  contentType: string
}

type Props = {
  items: MediaItem[]
  title?: string   // defaults to 'MEDIA'
}
```

Behavior for v1:

- If `items.length === 0`, render **nothing** (do not render an empty
  "MEDIA" header).
- Otherwise, render a card in the same visual idiom as the other section
  cards on `ArticleDetailPage` / `EventDetailPage`:
  ```tsx
  <div
    className="border p-5"
    style={{
      background: 'rgba(61,15,15,0.4)',
      borderColor: 'rgba(255,255,255,0.1)'
    }}
  >
    <p className="font-caps text-[10px] tracking-widest mb-3"
       style={{ color: '#6b7280' }}>
      {title ?? 'MEDIA'}
    </p>
    {/* grid */}
  </div>
  ```
- Grid layout: `grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3`.
  Each cell is `aspect-video` (16:9) with `object-cover`.
- `Image` items render as `<img src={item.url} alt="" loading="lazy"
  className="w-full h-full object-cover" />`.
- `Video` items render as `<video src={item.url} controls preload="metadata"
  className="w-full h-full object-cover" />`. Do **not** autoplay.
- Wrap each cell in a `border` with `borderColor: 'rgba(255,255,255,0.1)'`
  and `background: 'var(--near-black)'` for the letterbox fallback
  (matches the metadata chips in `ArticleDetailPage`).
- On image error (`onError`), replace the cell with a caramel-colored
  `BROKEN` caps label so dev-placeholder R2 keys do not render as the
  browser's broken-image icon. This is the minimum viable graceful
  degradation and requires no extra state machinery.
- No lightbox, no modal, no zoom. Click behavior: `<a href={item.url}
  target="_blank" rel="noopener noreferrer">` wrapping each `<img>` so
  users can open full-resolution in a new tab (consistent with the
  existing "SOURCE" link affordance on `ArticleDetailPage`).

The component lives in `src/components/shared/` (not `src/components/ui/`)
because it is a composite, not a primitive — same location as
`DataTable`, `PageHeader`, `Pagination`, `ConfirmDialog`, `SlideOver`.

### 8. UI — Article view integration

Edit **`UI/src/features/articles/ArticleDetailPage.tsx`**:

- Below the Key Facts card (still inside the `lg:col-span-2` column),
  render `<MediaGallery items={mediaItems} />` where `mediaItems` is
  derived inline:
  ```tsx
  const mediaItems = (article.media ?? []).map(m => ({
    id: m.id!,
    url: m.url!,
    kind: m.kind as 'Image' | 'Video',
    contentType: m.contentType!
  }))
  ```
- The derivation is trivial and one-shot, so no dedicated hook is needed.
  React Query's `useArticleDetail` already delivers the data.
- No layout restructure — the existing two-column grid still holds,
  the media card slots in as a new sibling of Summary / Key Facts.

### 9. UI — Event view integration

Edit **`UI/src/features/events/EventDetailPage.tsx`**:

- Add a **fourth tab** `'media'` to the existing `Tab` type:
  ```ts
  type Tab = 'timeline' | 'updates' | 'contradictions' | 'media'
  ```
- Add it to the `tabs` array (label: `'MEDIA'`, count: number of media
  items across all articles **after deduplication** — see below).
- Build the aggregated list inline in the page body:
  ```ts
  const mediaItems = useMemo(() => {
    const seen = new Set<string>()
    const out: MediaItem[] = []
    for (const a of articles) {
      for (const m of a.media ?? []) {
        if (!m.id || seen.has(m.id)) continue
        seen.add(m.id)
        out.push({
          id: m.id,
          url: m.url!,
          kind: m.kind as 'Image' | 'Video',
          contentType: m.contentType!
        })
      }
    }
    return out
  }, [articles])
  ```
  Deduplication is by `MediaFileDto.Id` (the primary key), not by URL —
  two different media files can legitimately share the same URL pattern
  during ingestion edge cases, but IDs are always unique.
- New `MediaTab` component in the same file (co-located, mirroring
  `TimelineTab`, `UpdatesTab`, `ContradictionsTab`):
  ```tsx
  function MediaTab({ items }: { items: MediaItem[] }) {
    if (items.length === 0) {
      return (
        <p className="font-mono text-sm text-center py-8"
           style={{ color: '#9ca3af' }}>
          No media attached to this event.
        </p>
      )
    }
    return <MediaGallery items={items} title="" />
  }
  ```
  The empty-state message matches the tone of the existing tab empty
  states. `title=""` suppresses the inner header since the tab chrome
  already labels the section.
- Rendered inside the existing tab-content panel — no restyling of the
  panel required.

### 10. Placeholder and dev-mode handling

The dev `CloudflareR2.PublicBaseUrl` is
`https://pub-placeholder.r2.dev` (ADR 0006 §12). In a dev environment
with no real R2 bucket, the constructed URLs will 404. The UI's
`onError` fallback (§7) is the only handling needed — no backend change,
no feature flag, no environment check. This keeps the dev experience
self-consistent with ADR 0006, which accepts that dev runs produce
unreachable media URLs.

## Consequences

### Positive

- Media is first-class on both detail views with one round trip per
  page — same pattern as every other embedded sub-object in the detail
  DTOs.
- No new endpoints, no new hooks, no new query keys. The TS client
  regeneration is the only "extra" step, and it is already a standard
  workflow (`UI/CLAUDE.md`).
- The R2 key → public URL conversion happens in exactly one place
  (`MediaFileMapper.BuildUrl`). Swapping R2 for another CDN, or adding
  a signed-URL flow later, touches a single method.
- Worker queries and list queries are untouched — they still pay zero
  cost for media.
- The `Article.MediaFiles` navigation property is additive — existing
  code that loads articles without `Include` continues to work because
  EF does not eager-load without an explicit `Include`. Worker queries
  (`GetPendingAsync`, `GetAnalysisDoneAsync`) are verified to not need
  media and will return empty `MediaFiles` lists.
- `MediaGallery` is reusable — future screens (publication preview,
  source detail, etc.) can drop it in with zero refactor.

### Negative / risks

- `ArticleDetailDto.ToDetailDto` signature changes (adds
  `publicBaseUrl`). Every caller must update. Today there is exactly
  one caller (`ArticlesController.GetById`), so the blast radius is
  small. Tests that call the mapper must be updated too.
- `EventMapper.ToDetailDto` and `ToEventArticleDto` signatures change.
  One production caller (`EventsController.GetById`) and potentially
  some tests. `ToListItemDto` is deliberately **unchanged** to keep the
  list-path blast radius at zero.
- `EventRepository.GetDetailAsync` now loads `MediaFiles` for every
  article of every event detail query. For events with many articles
  and many media files this increases payload size. Acceptable: the
  detail screen is the only caller, and the whole point of this feature
  is to show that data.
- The primary-constructor + field-initializer pattern for the
  controllers' `_publicBaseUrl` is slightly unusual for this project
  (controllers currently take zero `IOptions<T>` anywhere). It is
  still legal C# and matches the spirit of the Options pattern — the
  alternative of plumbing the string through every mapper call via a
  controller-local helper is worse. If the reviewer objects, the
  fallback is to resolve the string inline in each action method:
  `var baseUrl = r2Options.Value.PublicBaseUrl;` — same result, slightly
  more duplication.
- `MediaGallery` uses raw `<img>` and `<video>` with no placeholder
  skeletons. Acceptable for v1; a future UX pass can add
  `<Spinner />`-backed loading states if real CDN latency becomes
  visible. Not in scope.
- No lightbox / full-screen viewer for v1. Users open full-resolution
  images in a new tab via the anchor wrapper. Adding a lightbox is a
  separate follow-up and does **not** require another backend change.

### Files affected

**New files:**
- `Api/Mappers/MediaFileMapper.cs`
- `UI/src/components/shared/MediaGallery.tsx`

**Modified files — Core:**
- `Core/DomainModels/Article.cs` — add `List<MediaFile> MediaFiles`.

**Modified files — Infrastructure:**
- `Infrastructure/Persistence/Entity/ArticleEntity.cs` — add
  `List<MediaFileEntity> MediaFiles`.
- `Infrastructure/Persistence/Configurations/ArticleConfiguration.cs` —
  rewrite the existing unidirectional `HasMany<MediaFileEntity>` as a
  navigation-based `HasMany(a => a.MediaFiles)`.
- `Infrastructure/Persistence/Mappers/ArticleMapper.cs` — map
  `MediaFiles` in `ToDomain`; leave `ToEntity` untouched.
- `Infrastructure/Persistence/Repositories/ArticleRepository.cs` —
  `Include(a => a.MediaFiles)` on `GetByIdAsync` only.
- `Infrastructure/Persistence/Repositories/EventRepository.cs` —
  `.ThenInclude(a => a.MediaFiles)` on `GetDetailAsync` only.
- *(Optional, generated)* `Infrastructure/Persistence/Migrations/` — an
  empty migration documenting the navigation change. If `dotnet ef
  migrations add` produces a non-empty diff, investigate before
  proceeding.

**Modified files — Api:**
- `Api/Models/ArticleDetailDto.cs` — add `MediaFileDto` record and
  `Media` field on `ArticleDetailDto`.
- `Api/Models/EventDtos.cs` — add `Media` field on `EventArticleDto`.
- `Api/Mappers/ArticleMapper.cs` — new `publicBaseUrl` parameter on
  `ToDetailDto`, map `Media`.
- `Api/Mappers/EventMapper.cs` — new `publicBaseUrl` parameter on
  `ToDetailDto` and `ToEventArticleDto`, map `Media`. `ToListItemDto`
  unchanged.
- `Api/Controllers/ArticlesController.cs` — inject
  `IOptions<CloudflareR2Options>`, store `_publicBaseUrl`, pass it to
  the mapper.
- `Api/Controllers/EventsController.cs` — same.

**Modified files — UI:**
- `UI/src/api/generated/*` — regenerated via `npm run generate-api`.
  **Not** hand-edited.
- `UI/src/features/articles/ArticleDetailPage.tsx` — add `MediaGallery`
  below Key Facts.
- `UI/src/features/events/EventDetailPage.tsx` — add `media` tab and
  `MediaTab` subcomponent, add `mediaItems` memoization.

**Modified files — tests (delegated to `test-writer`):**
- `Tests/Api.Tests/Mappers/ArticleMapperTests.cs` (or equivalent) —
  verify `ToDetailDto` emits `Media` with the expected URL format.
- `Tests/Api.Tests/Mappers/EventMapperTests.cs` — same for
  `ToEventArticleDto` and the roll-up inside `ToDetailDto`.
- `Tests/Api.Tests/Mappers/MediaFileMapperTests.cs` (new) — unit tests
  for `BuildUrl`: trailing slash on base, leading slash on key, both,
  neither.
- `Tests/Infrastructure.Tests/Repositories/ArticleRepositoryTests.cs`
  (if one exists) — verify `GetByIdAsync` returns the `MediaFiles`
  collection.
- `Tests/Infrastructure.Tests/Repositories/EventRepositoryTests.cs` —
  verify `GetDetailAsync` returns media on nested articles.
- `Tests/Api.Tests/Controllers/ArticlesControllerTests.cs` and
  `EventsControllerTests.cs` — if they mock `IOptions<CloudflareR2Options>`
  setups must be added.

## Implementation Notes

### Order of changes (strict — each step builds on the previous)

1. **Core domain**: add `Article.MediaFiles` (empty init).
2. **Infrastructure entity + configuration**: add
   `ArticleEntity.MediaFiles`, rewrite the FK declaration in
   `ArticleConfiguration` to use the navigation expression. Run
   `dotnet ef migrations add DisplayMediaNavigation` and verify the
   diff is empty (or contains only a rename-safe change). If non-empty,
   stop and investigate.
3. **Infrastructure mapper**: map `MediaFiles` in `ArticleMapper.ToDomain`.
4. **Infrastructure repositories**: add `Include` on
   `ArticleRepository.GetByIdAsync` and `.ThenInclude` on
   `EventRepository.GetDetailAsync`. Verify every other method in both
   repos is **untouched**.
5. **Api DTO additions**: `MediaFileDto`, extended `ArticleDetailDto`,
   extended `EventArticleDto`.
6. **Api mapper additions**: `MediaFileMapper`, update `ArticleMapper`,
   update `EventMapper` (`ToDetailDto` + `ToEventArticleDto`, not
   `ToListItemDto`).
7. **Api controller plumbing**: inject `IOptions<CloudflareR2Options>`
   in `ArticlesController` and `EventsController`, store
   `_publicBaseUrl`, pass it to the mappers.
8. **Build the backend** (`dotnet build`) and confirm it compiles with
   no warnings beyond existing baseline.
9. **Run backend tests** to surface any mapper call sites that still
   pass the old signature.
10. **Regenerate TS client**: from `UI/`, run `npm run generate-api`.
    Confirm new fields appear in `src/api/generated/`.
11. **Create `UI/src/components/shared/MediaGallery.tsx`**.
12. **Wire `MediaGallery` into `ArticleDetailPage`** below Key Facts.
13. **Wire `MediaGallery` into `EventDetailPage`** as a new `media`
    tab with `MediaTab` subcomponent and `useMemo` aggregation.
14. **`npm run lint` and `npm run build`** — type-check passes.
15. **Manual verification**: load a seeded article and event in dev and
    confirm the `BROKEN` caps fallback appears (dev placeholder URLs).

### Skills to follow (MUST be read by `feature-planner` and `implementer`)

- **`.claude/skills/code-conventions/SKILL.md`** — layer boundaries (Api
  must not touch `DbContext`), controller primary-constructor style,
  Options pattern (`SectionName` const, defaults, `IOptions<T>` with
  `.Value` extraction), domain model conventions (mutable collection
  initialized empty).
- **`.claude/skills/ef-core-conventions/SKILL.md`** — `Include` /
  `ThenInclude` usage on detail queries only; keep list/worker queries
  unchanged; `CancellationToken cancellationToken = default` last
  parameter (no new methods here, but verification is part of the
  review).
- **`.claude/skills/mappers/SKILL.md`** — static class, static
  extension methods, no I/O, extra parameters after `this`, sub-DTO
  mapper (`MediaFileMapper`) in `Api/Mappers/`, collection mapping
  uses `.Select(m => m.ToDto(publicBaseUrl)).ToList()`, enum-to-string
  via `.ToString()`.
- **`.claude/skills/api-conventions/SKILL.md`** — `record` DTOs,
  camelCase over the wire, no inline `new XxxDto(...)` in controllers,
  no new endpoints needed (Option 2 expressly avoids adding routes).
- **`.claude/skills/clean-code/SKILL.md`** — `MediaGallery` is a thin
  presentational component, derivation logic stays inline or in a
  single `useMemo`, no premature abstraction into hooks, no magic
  numbers (grid column counts stay as Tailwind classes).
- **`UI/CLAUDE.md`** — **never** hand-edit `src/api/generated/`,
  always regenerate; features live under `src/features/<name>/`; shared
  composite components live under `src/components/shared/`.

### Testing (delegated to `test-writer`)

- `MediaFileMapperTests` — unit tests for `BuildUrl` covering trailing
  slash, leading slash, both, neither, and the full round trip from a
  `MediaFile` domain object to a `MediaFileDto` with every field
  populated.
- `ArticleMapperTests` — `ToDetailDto` emits `Media` as an empty list
  when `MediaFiles` is empty and as a populated list otherwise;
  `publicBaseUrl` is threaded through.
- `EventMapperTests` — `ToDetailDto` and `ToEventArticleDto` each
  populate `Media` per article; `ToListItemDto` is **not** affected.
- `ArticleRepositoryTests` — `GetByIdAsync` returns `MediaFiles` when
  the InMemory / SQLite provider is seeded with media rows.
- `EventRepositoryTests` — `GetDetailAsync` returns media on nested
  articles; `GetPagedAsync` and `GetActiveEventsAsync` do **not**
  (regression guard so media never leaks into list endpoints by
  accident).
- UI: no unit tests for `MediaGallery` in v1 (the project has no React
  test harness today); manual smoke via the detail pages is sufficient.

### Recommended next step

Pass this ADR to **feature-planner** to produce the atomic tasklist in
`docs/tasks/active/display-media-in-article-and-event-views.md`.
