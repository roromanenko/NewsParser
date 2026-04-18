# Fix: Media Extraction After Full-Page Scrape

## Goal

Restore article media by fixing two independent failure modes: (1) `HtmlArticleContentScraper`
emitting relative, scheme-relative, empty, or `data:` og:image URLs that `HttpMediaContentDownloader`
silently drops; and (2) `RssParser` never reaching the XML thumbnail fallback for `MediaRssFeedItem`
items that carry only `<media:thumbnail>`.

## Affected Layers

- Infrastructure
- Tests

## Tasks

### Infrastructure

- [x] **Modify `Infrastructure/Parsers/HtmlArticleContentScraper.cs`** — add a private static
      `ExtensionToMime` dictionary field and a private static `TryResolveUrl` helper; refactor
      `ExtractOpenGraphMedia` to accept a second `Uri articleUri` parameter; update `ScrapeAsync`
      to pass `response.RequestMessage?.RequestUri ?? new Uri(url)` as the second argument.

      Changes inside `ExtractOpenGraphMedia`:
      1. For each candidate meta-tag `content` value: skip if `IsNullOrWhiteSpace`; skip if
         starts with `data:`.
      2. Call `TryResolveUrl(rawValue, articleUri, out var absUri)` — returns `false` for
         unresolvable values; resolves relative (`/path`) and scheme-relative (`//cdn...`)
         URLs against `articleUri`.
      3. Infer `DeclaredContentType` from `absUri`'s path extension using `ExtensionToMime`
         (keys: `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`); leave `null` when extension is
         absent or unknown.
      4. Emit `new MediaReference(absUri.ToString(), MediaKind.Image, declaredContentType,
         MediaSourceKind.Http)`.

      Static map:
      ```
      private static readonly Dictionary<string, string> ExtensionToMime =
          new(StringComparer.OrdinalIgnoreCase)
          {
              { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" },
              { ".png", "image/png" }, { ".gif", "image/gif" },
              { ".webp", "image/webp" },
          };
      ```

      `TryResolveUrl` logic:
      - Try `Uri.TryCreate(rawValue, UriKind.Absolute, out var abs)`: if success and scheme
        is `http` or `https`, set `resolved = abs` and return `true`.
      - Else try `Uri.TryCreate(articleUri, rawValue, out var rel)`: if success, set
        `resolved = rel` and return `true`.
      - Otherwise return `false`.

      _Acceptance: solution builds; `ExtractOpenGraphMedia` no longer takes an `HtmlDocument`-
      only overload; all three previous call-site responsibilities (filter → resolve → classify)
      are encapsulated in `TryResolveUrl` + `ExtensionToMime`; no method body exceeds 20 lines._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/Parsers/RssParser.cs`** — in `ExtractFromMediaRssFeedItem`,
      after populating `refs` from `mediaItem.Media`, call
      `AddXmlMediaElements(mediaItem.Element, refs)` and change the return to
      `refs.DistinctBy(r => r.Url).ToList()`.

      No other method in the file changes.

      _Acceptance: solution builds; for a thumbnail-only `MediaRssFeedItem`, `ExtractMediaReferences`
      now returns the thumbnail as an `Image` reference instead of an empty list; for items with
      both `media:content` and `media:thumbnail` at the same URL, only one reference is returned._
      _Skill: .claude/skills/clean-code/SKILL.md_

### Tests

- [x] **Modify `Tests/Infrastructure.Tests/Parsers/HtmlArticleContentScraperTests.cs`** — add
      seven new test cases to the existing `HtmlArticleContentScraperTests` fixture
      (delegated to `test-writer`):

      1. `ScrapeAsync_WhenOgImageIsRelative_ResolvesAgainstArticleUrl` — handler returns HTML
         with `<meta property="og:image" content="/images/photo.jpg">`; assert
         `DiscoveredMedia[0].Url` equals `"https://example.com/images/photo.jpg"`.
      2. `ScrapeAsync_WhenOgImageIsSchemeRelative_ResolvesToHttps` — content is
         `//cdn.example.com/photo.jpg`; handler `RequestUri` is `https://example.com/article`;
         assert resolved URL is `"https://cdn.example.com/photo.jpg"`.
      3. `ScrapeAsync_WhenOgImageIsDataUri_IsSkipped` — content is
         `data:image/svg+xml;base64,abc`; assert `DiscoveredMedia` is empty.
      4. `ScrapeAsync_WhenOgImageIsEmpty_IsSkipped` — `content=""` (or whitespace); assert
         `DiscoveredMedia` is empty.
      5. `ScrapeAsync_WhenOgImageHasJpgExtension_PopulatesDeclaredContentType` — content is
         `https://cdn.example.com/photo.jpg`; assert `DiscoveredMedia[0].DeclaredContentType`
         equals `"image/jpeg"`.
      6. `ScrapeAsync_WhenOgImageIsExtensionless_LeavesDeclaredContentTypeNull` — content is
         `https://cdn.example.com/abc123`; assert `DiscoveredMedia[0].DeclaredContentType`
         is `null`.
      7. `ScrapeAsync_WhenResponseRedirected_ResolvesRelativeAgainstFinalUrl` — handler's
         `RequestUri` differs from the original URL (simulates a redirect); og:image is
         relative; assert the resolved URL uses the handler's `RequestUri` as the base, not
         the original string passed to `ScrapeAsync`.

      Use `Moq.Protected` on `HttpMessageHandler` to control `RequestMessage.RequestUri`
      on the response (existing pattern in the fixture).

      _Acceptance: all 7 new tests compile and pass; existing 12 tests continue to pass;
      no live network calls._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Modify `Tests/Infrastructure.Tests/Parsers/RssParserTests.cs`** — replace the existing
      test `ExtractMediaReferences_WhenMediaRssFeedItemHasThumbnailOnly_ReturnsEmpty` with two
      new cases (delegated to `test-writer`):

      1. `ExtractMediaReferences_WhenMediaRssFeedItemHasThumbnailOnly_ReturnsImageReference` —
         feed item carries only `<media:thumbnail url="https://cdn.example.com/thumb.jpg" />`;
         assert `refs` has exactly one entry with that URL and `MediaKind.Image`.
      2. `ExtractMediaReferences_WhenMediaRssFeedItemHasContentAndThumbnail_DedupesByUrl` —
         feed item carries both `<media:content url="https://cdn.example.com/img.jpg"
         type="image/jpeg" />` and `<media:thumbnail url="https://cdn.example.com/img.jpg" />`;
         assert `refs` has exactly one entry (the `media:content` entry — first wins via
         `DistinctBy`).

      Keep all other existing tests in `RssParserTests.cs` unchanged. Use the existing
      `BuildMediaRssFeed` / `InvokeExtract` helpers already present in the fixture.

      _Acceptance: both new tests compile and pass; the deleted documenting-the-bug test is
      gone; all remaining existing tests continue to pass._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

### Verification

- [x] **Run `dotnet build`** from the solution root — confirm zero errors.

      _Acceptance: exit code 0; no new warnings introduced by the changed files._

- [x] **Run `dotnet test Tests/Infrastructure.Tests/`** — confirm all tests pass including
      the 9 new cases (7 scraper + 2 RSS) and no regressions in the existing suite.

      _Acceptance: test run exits with code 0; the replaced thumbnail-only test is absent;
      pre-existing skipped/failing tests unrelated to this feature remain unchanged._

## Open Questions

- None. The ADR fully specifies which files change, the exact logic for URL resolution,
  the extension-to-MIME map contents, the RSS dedup strategy, and the precise test names.
  Implementation can proceed directly.
