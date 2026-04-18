# Fix: Media Extraction After Full-Page Scrape

## Context

After ADR `enrich-rss-articles-with-full-page-scrape.md` landed, articles are
being saved with empty `MediaFiles` — the ingestion pipeline is producing no
media. Downstream Telegram publications therefore go out without images, and
the UI media gallery is empty.

The full-page scrape was intended to be **additive** over the existing RSS
media pipeline: every URL supplied by the RSS feed must still end up on the
`Article`, and any `og:image` / `twitter:image` discovered by the scrape is
an *extra* `MediaReference` appended to the list (see ADR
*enrich-rss-articles-with-full-page-scrape*, §Findings bullet about
"purely additive"). The media column on ingested articles going to zero
across the board means the wiring between the new scrape path and the
existing `MediaIngestionService` is producing references that fail to
ingest, losing references that previously existed, or both.

### Findings from codebase exploration

Reading the shipped code for the scrape feature reveals five concrete
failure modes, each of which independently reduces media to zero for a
subset of articles; combined they can explain an end-to-end "no media"
outcome:

1. **Relative og:image / twitter:image URLs are not resolved against the
   article base URL.** `HtmlArticleContentScraper.ExtractOpenGraphMedia`
   (lines 160-175) reads `<meta content="...">` raw and wraps it in a
   `MediaReference` without calling any base-URL resolution. Publications
   frequently emit relative (`/images/x.jpg`) or scheme-relative
   (`//cdn.example.com/x.jpg`) og:image values. Those URLs then flow into
   `HttpMediaContentDownloader.DownloadAsync`, where
   `httpClient.GetAsync(reference.Url, ...)` throws `UriFormatException` /
   `InvalidOperationException` for non-absolute URLs. `MediaIngestionService`
   catches the exception per reference and *logs a warning*, so the failure
   is silent from the article's perspective — the MediaFile is simply never
   created.
2. **Scraped `MediaReference` entries carry `DeclaredContentType: null`.**
   `HttpMediaContentDownloader.ResolveContentType` falls back through
   `response.Content.Headers.ContentType` → `reference.DeclaredContentType`
   → URL extension. For og:image URLs on a CDN
   (`https://cdn.example.com/abc123` with no extension and a generic
   `Content-Type: application/octet-stream` that many CDNs emit), **all
   three fallbacks fail**, `ResolveContentType` returns `null`, and the
   download is dropped with "Unsupported content type" — silently, again.
3. **Empty / placeholder og:image values are not filtered out.** The
   scraper only guards `string.IsNullOrWhiteSpace(url)`. A non-empty
   `content=""` quoted with whitespace passes, and sites that emit
   `content="data:image/svg+xml;..."` (base64 placeholders) pass too.
   Both produce a `MediaReference` that `HttpMediaContentDownloader` cannot
   handle; both end with the same silent MediaFile loss as (1) and (2).
4. **The yahoo media-RSS thumbnail-only path still drops media.** The
   existing `RssParser.ExtractMediaReferences` short-circuits to
   `ExtractFromMediaRssFeedItem` whenever `FeedReader` produces a
   `MediaRssFeedItem`. For items that carry only `<media:thumbnail>`
   (without `<media:content>`), `mediaItem.Media` is empty, the XML
   fallback `AddXmlMediaElements` is never reached, and zero references
   are emitted from the RSS side. Some publications in the active source
   list use thumbnail-only media-RSS; for those articles the scrape was
   the *only* source of media, compounding (1)-(3). This bug predates
   the scrape feature but is in scope here because the scrape was
   supposed to be the safety net and (1)-(3) make it ineffective.
5. **The merger's content-length rule can silently hide content-extraction
   failures.** `ScrapedArticleMerger.MergeContent` replaces only when the
   scraped content is *longer* than the RSS content. That is correct
   behaviour but irrelevant to media — flagged here so we do not
   accidentally change it while fixing media.

### What does NOT need to change

- `MediaIngestionService.IngestForArticleAsync` dedupe-by-URL logic
  already handles duplicates between RSS-extracted and scraped media
  (see `MediaIngestionService.cs` lines 32-35). A single URL present
  in both RSS and the scrape produces one `MediaFile`, not two.
- The DI wiring (`InfrastructureServiceExtensions.AddParsers`) is correct:
  `HtmlArticleContentScraper` is registered scoped, the
  `"ArticleContentScraper"` `HttpClient` is configured, options bound
  from config. No DI changes required.
- The `Article` domain model (`Core/DomainModels/Article.cs`) already
  has `List<MediaReference> MediaReferences` — no schema change.
- Database schema — `media_files` already stores what we need.

## Options

### Option 1 — Fix only the scraper (normalise og:image at extraction time)

Do all the normalisation inside `HtmlArticleContentScraper.ExtractOpenGraphMedia`:
resolve relative URLs against the article's final URL, strip `data:` URIs,
skip empty / whitespace values, and detect the MIME type from the URL
extension + a conservative allow-list, populating
`MediaReference.DeclaredContentType` so `HttpMediaContentDownloader`'s
second fallback succeeds.

**Pros:**
- Single-file fix; smallest blast radius.
- Keeps `MediaReference` as-produced-by-parser invariant: "URLs flowing
  out of `Infrastructure/Parsers/` are absolute and non-placeholder".
- Does not touch the merger, does not touch the downloader — both stay
  behaviourally the same.

**Cons:**
- Does not address failure mode (4) — yahoo media-RSS thumbnail-only
  items are still dropped before the scrape even runs. If a feed both
  uses thumbnail-only media-RSS *and* has an unscrapeable site, media
  is zero for that article.
- MIME-inference in the scraper duplicates the extension-to-MIME map
  that already exists in `HttpMediaContentDownloader` (the
  `ExtensionToMime` dictionary).

### Option 2 — Fix the scraper AND the RSS thumbnail fallback

Do Option 1 plus: in `RssParser.ExtractMediaReferences`, after extracting
from `MediaRssFeedItem.Media`, also drain `<media:thumbnail>` entries
from the same feed-item XML (`mediaItem.Element`). The result is the
union of content + thumbnail references, distinct by URL.

**Pros:**
- Addresses all four independent failure modes.
- Restores the pre-scrape thumbnail coverage that was never working in
  the first place — a long-standing bug documented in
  `RssParserTests.ExtractMediaReferences_WhenMediaRssFeedItemHasThumbnailOnly_ReturnsEmpty`.
- Keeps the scrape genuinely additive: even if a site is unscrapeable
  or returns no og:image, the RSS pipeline still yields everything
  that the feed advertises.

**Cons:**
- Two files changed instead of one.
- The thumbnail fallback is strictly a pre-existing RSS bug surfacing
  as a symptom now; bundling it with the scrape regression risks
  scope-creep. (Mitigation: the task description explicitly asks about
  all four edge cases — "RSS has media but scrape has none", etc. —
  so fixing thumbnail-only feeds is on-topic.)

### Option 3 — Move normalisation to a central `MediaReferenceNormalizer`

Introduce a new static helper
`Infrastructure/Parsers/MediaReferenceNormalizer.cs` with
`NormalizeOrNull(string? rawUrl, string? baseUrl) : MediaReference?`,
then call it from both `HtmlArticleContentScraper.ExtractOpenGraphMedia`
and (optionally) `RssParser.ExtractMediaReferences`. A single place
owns: empty-check, `data:` filter, scheme-relative resolution, relative
resolution against a base URL, extension-based `DeclaredContentType`
inference.

**Pros:**
- One location for all URL-normalisation logic; future callers (e.g. a
  backfill worker that reprocesses stored `Article.OriginalUrl`s) get
  it for free.
- Clear seam for unit-testing URL edge cases in isolation.

**Cons:**
- Introduces a new class for what is currently ~15 lines of logic used
  in exactly one call site.
- YAGNI: no second caller exists today. The scraper is the only code
  path that produces not-yet-normalised URLs — RSS `<enclosure>` /
  `media:content` URLs are already absolute by spec.
- Adds a shape that the `mappers` skill would mis-classify: it is not
  a mapper (it performs filtering and can return `null`), so it sits
  awkwardly between `Infrastructure/Parsers/` and
  `Infrastructure/Persistence/Mappers/`.

## Decision

**Choose Option 2 — fix `HtmlArticleContentScraper` to emit only
absolute, non-placeholder URLs with a best-effort
`DeclaredContentType`, AND fix `RssParser.ExtractMediaReferences` to
also drain `<media:thumbnail>` elements from media-RSS items.**

This directly addresses every edge case called out in the task:

| Edge case | Result after fix |
|---|---|
| RSS has media, scrape has none | RSS media survives (unchanged). Thumbnail-only RSS now surfaces too — (4). |
| Scrape has media, RSS has none | Scraped og:image survives (currently lost due to (1)-(3)). |
| Both have media | Both survive; `MediaIngestionService` dedupes by URL. |
| Neither | `Article.MediaReferences` is empty; `MediaIngestionService` returns early at line 29-30. No error. |

The merge precedence rule is **unchanged and explicit**: the two sources
are treated as a *union*, not a replacement. The RSS-derived reference
and the scrape-derived reference are both appended to
`Article.MediaReferences`; `MediaIngestionService.IngestForArticleAsync`
performs the final URL-level dedup (case-insensitive group-by at line
32-35). This is precisely the pattern called for in the original ADR's
"purely additive" language.

### Scraper-side fix (file: `Infrastructure/Parsers/HtmlArticleContentScraper.cs`)

`ExtractOpenGraphMedia` is refactored to take the final response URI as
a second argument:

```
private static IReadOnlyList<MediaReference> ExtractOpenGraphMedia(
    HtmlDocument doc, Uri articleUri)
```

`ScrapeAsync` passes `response.RequestMessage?.RequestUri ?? new Uri(url)`
so that server-side redirects are honoured when resolving relative URLs.

For each candidate meta tag:
1. Read `content` attribute.
2. `IsNullOrWhiteSpace` → skip.
3. If the value starts with `data:` → skip.
4. `Uri.TryCreate(..., UriKind.Absolute, out var abs)`:
   - If success and `abs.Scheme` is `http`/`https` → use `abs`.
   - Else try `Uri.TryCreate(articleUri, rawValue, out var resolved)` to
     handle relative and scheme-relative (`//cdn...`) URLs; if that
     fails → skip.
5. Infer `DeclaredContentType` from the resolved URL's extension using a
   small static map duplicated from `HttpMediaContentDownloader.ExtensionToMime`:
   ```
   private static readonly Dictionary<string, string> ExtensionToMime =
       new(StringComparer.OrdinalIgnoreCase)
       {
           { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" },
           { ".png", "image/png" }, { ".gif", "image/gif" },
           { ".webp", "image/webp" },
       };
   ```
   If the extension is absent / unknown, `DeclaredContentType` stays
   `null` — the downloader's primary path (HTTP `Content-Type` header)
   remains the first chance to classify. Duplication is intentional and
   acknowledged in *Consequences* below.
6. Emit `new MediaReference(absUrl, MediaKind.Image, declaredContentType,
   MediaSourceKind.Http)`.

### RSS-side fix (file: `Infrastructure/Parsers/RssParser.cs`)

Keep `ExtractFromMediaRssFeedItem` as-is, but also run
`AddXmlMediaElements(mediaItem.Element, refs)` on the *same* feed item
when it is a `MediaRssFeedItem`, then return `refs.DistinctBy(r =>
r.Url).ToList()`. This makes thumbnail-only media-RSS surface as an
Image reference (which already happens today for the non-media-RSS
path in `AddXmlMediaElements` lines 140-146) while still preferring
`media:content` entries when both exist — `DistinctBy` keeps the
first occurrence and `ExtractFromMediaRssFeedItem` populates `refs`
first.

### Merger (file: `Infrastructure/Persistence/Mappers/ScrapedArticleMerger.cs`)

No change required. The existing `MergeMedia` at line 34-37 is correct
— it is a blind `AddRange`, which is what we want once the scraper
stops emitting invalid references.

### MediaIngestionService / HttpMediaContentDownloader

No change required. `HttpMediaContentDownloader.ResolveContentType`'s
three-tier fallback already handles the scraper's populated
`DeclaredContentType` path (tier 2). For CDN URLs that also lack an
extension, the downloader will still drop the media — but only when
the HTTP server itself returns an unusable `Content-Type` *and* the URL
is extensionless, which is a long-tail case we explicitly accept.

## Consequences

**Positive:**
- Every edge case in the task description is handled with deterministic
  rules.
- Scraped og:image entries that used to be silently dropped now actually
  yield `MediaFile` rows.
- The pre-scrape thumbnail-only feeds regain media coverage as a
  side-effect.
- No change to the `MediaReference` contract, the merger, the
  downloader, or the ingestion service — all the complexity stays
  inside the two parser files that already own HTML / RSS concerns.

**Negative / risks:**
- Extension-to-MIME map is now duplicated in
  `HtmlArticleContentScraper` and `HttpMediaContentDownloader`. An
  entry added to one must be added to the other. This is acceptable
  because (a) only 5 image extensions are relevant at extraction time —
  og:image is always an image — and (b) factoring it into a shared
  static would force picking between `Core/`, `Infrastructure/Parsers/`,
  and `Infrastructure/Services/` with no clear winner. Defer the
  factoring until a third caller appears.
- Adding the thumbnail fallback widens the scope past "fix scrape";
  however, it is a direct subset of the task description's edge-case
  matrix ("RSS has media but scrape has none") and would otherwise
  remain dead code — the existing `AddXmlMediaElements` function is
  literally unreachable for the media-RSS path right now.
- The `DistinctBy(r => r.Url)` in the RSS extraction adds an O(N) pass
  per item. Items never have more than ~10 media entries — cost is
  negligible.

**Files affected:**

Edited:
- `Infrastructure/Parsers/HtmlArticleContentScraper.cs` — refactor
  `ExtractOpenGraphMedia` to accept the response `Uri`, resolve
  relative URLs, reject `data:` / empty, infer `DeclaredContentType`
  from extension. Update `ScrapeAsync` to pass
  `response.RequestMessage?.RequestUri ?? new Uri(url)`.
- `Infrastructure/Parsers/RssParser.cs` — in the `MediaRssFeedItem`
  branch of `ExtractMediaReferences`, also call
  `AddXmlMediaElements(mediaItem.Element, refs)` and return
  `refs.DistinctBy(r => r.Url).ToList()`.

Not edited:
- `Core/DomainModels/ScrapedArticle.cs`
- `Core/DomainModels/MediaReference.cs`
- `Core/Interfaces/Parsers/IArticleContentScraper.cs`
- `Infrastructure/Persistence/Mappers/ScrapedArticleMerger.cs`
- `Infrastructure/Services/MediaIngestionService.cs`
- `Infrastructure/Services/HttpMediaContentDownloader.cs`
- `Infrastructure/Configuration/ArticleScraperOptions.cs`
- `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`
- `Api/appsettings.Development.json`, `Worker/appsettings.Development.json`

Tests (delegated to `test-writer`, planned by `feature-planner`):
- `Tests/Infrastructure.Tests/Parsers/HtmlArticleContentScraperTests.cs`
  — add cases:
  - `ScrapeAsync_WhenOgImageIsRelative_ResolvesAgainstArticleUrl`
  - `ScrapeAsync_WhenOgImageIsSchemeRelative_ResolvesToHttps`
  - `ScrapeAsync_WhenOgImageIsDataUri_IsSkipped`
  - `ScrapeAsync_WhenOgImageIsEmpty_IsSkipped`
  - `ScrapeAsync_WhenOgImageHasJpgExtension_PopulatesDeclaredContentType`
  - `ScrapeAsync_WhenOgImageIsExtensionless_LeavesDeclaredContentTypeNull`
  - `ScrapeAsync_WhenResponseRedirected_ResolvesRelativeAgainstFinalUrl`
- `Tests/Infrastructure.Tests/Parsers/RssParserTests.cs` — replace the
  current "thumbnail-only returns empty" documenting-the-bug test with:
  - `ExtractMediaReferences_WhenMediaRssFeedItemHasThumbnailOnly_ReturnsImageReference`
  - `ExtractMediaReferences_WhenMediaRssFeedItemHasContentAndThumbnail_DedupesByUrl`

## Implementation Notes

### Skills `feature-planner` and `implementer` must follow

- **`code-conventions`** (`.claude/skills/code-conventions/SKILL.md`) —
  parser collaborators live in `Infrastructure/Parsers/`; no new
  interface or DI registration is introduced by this fix; Options
  values are extracted as `.Value` in the constructor (already true).
- **`clean-code`** (`.claude/skills/clean-code/SKILL.md`) — keep every
  method ≤ 20 lines; if the relative-URL resolution logic grows, factor
  it into a private static `TryResolveUrl(string raw, Uri baseUri, out
  Uri resolved) : bool`. No magic constants — the extension map is a
  named static field.
- **`mappers`** (`.claude/skills/mappers/SKILL.md`) — no mapper changes;
  `ScrapedArticleMerger.MergeMedia` stays as-is.
- **`api-conventions`** — not triggered.
- **`dapper-conventions`** — not triggered; no persistence changes.
- **`testing`** (`.claude/skills/testing/SKILL.md`) — use `Moq.Protected`
  `HttpMessageHandler` for all HTTP in scraper tests (existing pattern
  in `HtmlArticleContentScraperTests`); use inline XML strings via
  `FeedReader.ReadFromString` for parser tests (existing pattern in
  `RssParserTests`).

### Order of changes

1. Update `HtmlArticleContentScraper.ExtractOpenGraphMedia` and
   `ScrapeAsync` to pass the response `Uri`; add the static extension
   map; add `TryResolveUrl` if needed. Build.
2. Add the seven new scraper tests.
3. Update `RssParser.ExtractMediaReferences` (media-RSS branch) to also
   drain thumbnails and dedupe. Build.
4. Update the two `RssParserTests` cases listed above (the third case
   documenting the old bug is replaced).
5. Run `dotnet test Tests/Infrastructure.Tests/` — all existing tests
   plus new tests must pass. The previously-ignored
   `ParseAsync_WhenOptionsEnabledFalse_DoesNotCallScraper` remains
   ignored (out of scope per the original ADR).
6. Smoke-check locally against one live RSS source (optional, manual
   only): confirm at least one `MediaFile` row per article in a batch.

### Explicitly out of scope

- Refactoring the extension-to-MIME map into a shared helper — defer
  until there is a third caller.
- Centralising URL normalisation into a `MediaReferenceNormalizer`
  helper (Option 3) — YAGNI today.
- Changes to `HttpMediaContentDownloader` — its fallback logic is
  correct; the fix is to feed it better inputs, not to widen its
  tolerance.
- Adding video og:tags (`og:video`, `og:video:url`). Open Graph video
  semantics are materially different (direct video URL vs embed
  URL vs poster frame) and would need its own ADR.
- Resolving `srcset`/`<picture>` inside the article body. The scope
  here is metadata media (og:image / twitter:image + RSS
  enclosure/media:*); body-image extraction is a separate feature.
- Persisting `ScrapedArticle.Author` — already deferred by the parent
  ADR.
