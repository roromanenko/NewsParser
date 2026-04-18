# Enrich RSS Articles with Full-Page Scraping

## Status
Proposed

## Context

`Infrastructure/Parsers/RssParser.cs` currently builds `Article` objects solely
from fields the RSS feed provides: `Title`, `OriginalContent` (from `item.Content`
or `item.Description`), `OriginalUrl`, `ExternalId`, `PublishedAt`, and
`MediaReferences` extracted from Media RSS / `<enclosure>` / `media:content`
elements. Most publications truncate their feed content to a short preview, so
the downstream pipeline (AI analysis, event classification, summary generation)
operates on impoverished input and quality suffers.

The feature requires that after reading the feed, the parser visit each
article URL, scrape the full article body plus author/tags/other usable
metadata, and merge that with whatever the feed already provided. Per-article
failures must not break the batch — a scrape that fails or times out must
leave the article intact with whatever RSS data was already extracted.

### Findings from codebase exploration

- **No HTML-parsing library is currently referenced** anywhere in the
  solution (`HtmlAgilityPack`, `AngleSharp`, `SmartReader` all absent). The
  feature adds a new dependency.
- **No Polly policies are configured** anywhere (`AddPolicyHandler`,
  `AsyncRetryPolicy`, etc. — zero hits). `IHttpClientFactory` is used in
  several places but always with raw configuration (see
  `InfrastructureServiceExtensions.AddStorage` registering the
  `"MediaDownloader"` client with only a `Timeout`). Introducing Polly would
  be a new pattern; the codebase currently relies on per-request
  `try/catch` + logging.
- **The feature description claims og:image fetching is "already
  implemented".** It is not: `RssParser.ExtractMediaReferences` only reads
  Media RSS, `<enclosure>`, and the `media:*` XML elements. No HTTP fetch of
  the article page currently occurs in the parser. The true current state
  is: Media RSS is implemented; og:image extraction does **not** exist. The
  ADR treats og:image as *new behavior to be added as part of the scrape*,
  but the scrape step must remain purely *additive* over the existing RSS
  media pipeline — any URL that the RSS feed already provided must still
  be preserved, and the scraped og:image (if any) is appended as an extra
  `MediaReference` rather than replacing RSS-supplied entries.
- **Parser contract is `ISourceParser.ParseAsync`** returning
  `List<Article>` — downstream callers (`SourceFetcherWorker`) iterate and
  hand the list to `IMediaIngestionService.IngestForArticleAsync` keyed by
  the article's `MediaReferences`. That list is already tolerant of
  duplicate URLs (deduplicated by `Url` in `MediaIngestionService`), which
  makes it safe for the scraper to add og:image entries that may already be
  present from Media RSS.
- **`MediaIngestionService` runs per-article after the parser returns**;
  media ingestion is fully decoupled from parsing. Scraped media must flow
  through `MediaReference` just like RSS-supplied media.
- **Worker concurrency today is sequential** — `SourceFetcherWorker.ProcessAsync`
  loops sources sequentially, and `RssParser.ParseAsync` currently performs
  exactly one network call (`FeedReader.ReadAsync`). Adding N HTTP requests
  per feed (one per article) is a large step up in total network activity;
  rate limiting and per-host concurrency become relevant.
- **Layering** — `Infrastructure/Parsers/` is where parser collaborators
  live (see ADR 0008 *Relocate Telegram Parser Helper Files* — the
  `ITelegramChannelReader` seam lives in `Infrastructure/Parsers/`, not
  `Core/`, because its signature leaks infrastructure types). The same
  reasoning applies to the scraper: it is a parser-only collaborator
  operating on strings, so it can live either in `Core/Interfaces/Parsers/`
  (if the contract stays primitive) or in `Infrastructure/Parsers/` if the
  contract leaks HTML types. The chosen contract in this ADR stays
  primitive and therefore its interface *can* live in `Core/`, matching
  `ISourceParser`'s own placement.
- **Options pattern is universal** — every tunable knob in the project
  lives in an Options class (see `RssFetcherOptions`, `CloudflareR2Options`,
  `ValidationOptions`). Scraper knobs (timeout, per-host concurrency, max
  HTML size, user agent) must follow the same pattern.

## Options

### Option 1 — Inline scrape inside `RssParser`

Add a private async helper inside `RssParser` that performs the HTTP fetch
and HTML parse, called immediately after the feed is read for each item.
Accept `IHttpClientFactory` and `IOptions<RssScraperOptions>` via primary
constructor.

**Pros:**
- Minimal layering changes — no new interface, no DI wiring beyond the
  Options class and an HTTP client name.
- All RSS logic stays in one file.

**Cons:**
- Violates single-responsibility for `RssParser`: it becomes a feed reader
  *and* an HTML scraper *and* an og:image extractor. The class length
  doubles.
- The scraper cannot be reused for other parsers (e.g., a future
  website-only parser or a reprocess-existing-articles tool).
- Unit testing the HTML scraping path requires mocking `IHttpClientFactory`
  inside `RssParser` tests, mixing concerns.
- No clean seam for substituting a different extraction strategy (e.g.,
  swapping HtmlAgilityPack for SmartReader in the future) — any change
  forces editing `RssParser` itself.

### Option 2 — Separate `IArticleContentScraper` service in Infrastructure/Parsers

Introduce a focused abstraction:

```csharp
// Core/Interfaces/Parsers/IArticleContentScraper.cs
public interface IArticleContentScraper
{
    Task<ScrapedArticle?> ScrapeAsync(string url, CancellationToken cancellationToken = default);
}

// Core/DomainModels/ScrapedArticle.cs — pure data record
public sealed record ScrapedArticle(
    string? FullContent,
    string? Author,
    IReadOnlyList<string> Tags,
    IReadOnlyList<MediaReference> DiscoveredMedia);
```

Implementation `HtmlArticleContentScraper` lives in
`Infrastructure/Parsers/`, uses `IHttpClientFactory` with a named client
`"ArticleContentScraper"` configured in `InfrastructureServiceExtensions`,
uses **HtmlAgilityPack** to parse the response HTML, extracts:

- Full content (prefer `<article>` or largest `<div>` containing `<p>`
  blocks; fall back to readability-style heuristic — *not* a full SmartReader
  port, just the body density heuristic the project can maintain).
- Author from `<meta name="author">`, `<meta property="article:author">`,
  or common JSON-LD `"author"` fields.
- Tags from `<meta property="article:tag">` (repeated), `<meta name="keywords">`
  (comma-split), or JSON-LD `"keywords"`.
- Extra media — `og:image`, `og:image:secure_url`, `twitter:image`,
  `twitter:image:src` — each appended as a `MediaReference(Url, MediaKind.Image, DeclaredContentType: null, SourceKind: MediaSourceKind.Http)`.

`RssParser` consumes the scraper and applies a **merge function** that
treats RSS values as the baseline and promotes scraped values only where
they add information (see merge rules in the Decision).

**Pros:**
- Clean SRP split: `RssParser` interprets RSS feeds, `HtmlArticleContentScraper`
  extracts structured data from HTML.
- Scraper is independently testable with a mocked `HttpMessageHandler`.
- Reusable for future parsers or a backfill worker.
- Matches the project's existing role-based layering in
  `Infrastructure/Parsers/` (same folder already holds `ITelegramChannelReader`
  as a parser-only collaborator).
- Library change becomes an internal detail of one file — swapping
  HtmlAgilityPack for SmartReader later touches only `HtmlArticleContentScraper`.

**Cons:**
- More moving parts: one interface, one implementation, one Options class,
  one named HttpClient, one DI registration.
- `ScrapedArticle` is a new Core domain record (trivially small — no
  infrastructure leakage, so `Core/` is the correct home).

### Option 3 — Use a readability-style extractor (SmartReader) end-to-end

Depend on `SmartReader` (a port of Mozilla Readability) to do content
extraction wholesale. The scraper service signature is the same as
Option 2, but the implementation defers all DOM traversal to SmartReader.

**Pros:**
- Best-of-breed content extraction heuristics for body text and lead image
  out of the box.
- Minimal DOM code to maintain in the project.

**Cons:**
- SmartReader is aggressive about what it returns — author and tags are
  extracted only when Readability's metadata pass succeeds; unconventional
  sites produce null. We would still need HtmlAgilityPack (or SmartReader's
  own internal document) for tag/metadata fallbacks, so the dependency
  set grows rather than shrinks.
- Readability's content selection sometimes removes media or navigation
  artifacts that legitimately belong in the article (e.g., pullquotes,
  inline photos with captions). The project already has curated downstream
  steps (AI summarization, event classification); over-aggressive
  extraction at the scrape stage loses information that the AI can use.
- Adds a less-maintained .NET dependency (SmartReader's activity is
  sporadic compared to HtmlAgilityPack).
- The og:image fallback still has to be written manually — SmartReader
  does not normalise Open Graph / Twitter Card images into a list of media
  references with type/kind metadata matching `MediaReference`.

## Decision

**Choose Option 2 — introduce `IArticleContentScraper` in
`Core/Interfaces/Parsers/` with `HtmlArticleContentScraper` (HtmlAgilityPack-backed)
in `Infrastructure/Parsers/`, and have `RssParser` consume it.**

This preserves `RssParser`'s single responsibility, keeps parser collaborators
grouped under `Infrastructure/Parsers/` (matching ADR 0008), and isolates the
HTML-extraction library behind an interface so it can be swapped later without
touching any other file. HtmlAgilityPack is chosen over AngleSharp because it
has broader .NET 10 support and the project needs only static DOM traversal
(no JavaScript execution, no CSS query engine). It is chosen over SmartReader
because we need structured metadata (author, tags, og:image normalised into
`MediaReference`) — not just readability body text — and implementing those
fallbacks in HtmlAgilityPack is straightforward.

### Interface placement

- `Core/Interfaces/Parsers/IArticleContentScraper.cs` — interface is pure:
  receives a `string url`, returns a `ScrapedArticle?` composed of primitive
  types and existing domain types (`MediaReference`). No HTML or
  HtmlAgilityPack types leak across the boundary. This mirrors
  `ISourceParser`'s location.
- `Core/DomainModels/ScrapedArticle.cs` — a sealed immutable record holding
  optional fields. It is not persisted and carries no infrastructure
  references, so `Core/` is the correct home.
- `Infrastructure/Parsers/HtmlArticleContentScraper.cs` — concrete
  implementation using HtmlAgilityPack; private helpers for
  `ExtractContent`, `ExtractAuthor`, `ExtractTags`, `ExtractOpenGraphMedia`.
- `Infrastructure/Configuration/ArticleScraperOptions.cs` — new Options
  class.

### Options class

```
public class ArticleScraperOptions
{
    public const string SectionName = "ArticleScraper";

    public bool Enabled { get; set; } = true;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int MaxHtmlSizeBytes { get; set; } = 2 * 1024 * 1024; // 2 MB cap
    public int MaxConcurrencyPerFeed { get; set; } = 4;
    public string UserAgent { get; set; } = "NewsParserBot/1.0";
}
```

All knobs have defaults; `Enabled=false` provides a fast kill-switch that
restores the pre-feature behaviour of skipping the scrape entirely. Follow
`code-conventions` — infrastructure config lives in `Infrastructure/Configuration/`,
the `SectionName` constant is required, values are extracted as `.Value` in
the consumer.

### HttpClient configuration

Register a named HTTP client in `InfrastructureServiceExtensions.AddParsers`:

```
services.AddHttpClient("ArticleContentScraper")
    .ConfigureHttpClient((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<ArticleScraperOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(opts.RequestTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(opts.UserAgent);
    });
```

**Polly is intentionally NOT added** for this feature:

- The project has no existing Polly usage — introducing it here sets a
  precedent we should justify with a broader use case.
- The required retry policy here is trivial ("one attempt, give up on
  failure, preserve RSS data") and is cleanly expressed by a
  `try/catch` around the scrape call — the same pattern
  `MediaIngestionService` already uses per-reference.
- If future requirements call for exponential backoff or circuit breaking
  across multiple feeds, a separate ADR should introduce
  `Microsoft.Extensions.Http.Resilience` (the project's .NET 10 default)
  project-wide — not as a one-off in the scraper.

### Concurrency and rate limiting

Inside `RssParser.ParseAsync`, after the feed is read and the item list is
materialised, process scrape calls with bounded parallelism using a
`SemaphoreSlim` sized from `ArticleScraperOptions.MaxConcurrencyPerFeed`.
This limits concurrent requests **to the same host** because one RSS feed
maps to one publication. Cross-host limits are not needed — different
sources are processed sequentially by `SourceFetcherWorker`.

The parser awaits all scrape tasks before returning; scrape failures are
caught per-item inside the loop and do not abort the feed batch.

### Fault tolerance and merge rules

Each scrape call is wrapped in a `try/catch` inside the parser:

- On `TaskCanceledException` (timeout), `HttpRequestException`, or any
  `HtmlAgilityPack`-surfaced exception: log a warning including the URL
  and **skip the merge** — the `Article` keeps all values supplied by
  the RSS feed. No retries.
- On success, merge `ScrapedArticle` into the already-built `Article`
  according to these rules:

| Field | Merge rule |
|---|---|
| `OriginalContent` | If scraped `FullContent` is non-empty *and* longer than the RSS content, replace. Otherwise keep RSS content. |
| `Tags` (new field on `Article`) | Union of RSS-supplied tags (none today) and scraped tags, distinct (case-insensitive), capped at a reasonable max (e.g. 20). |
| `Author` (new concept — stored as a free-form string on `Article` for future use) | Use scraped value if non-empty; otherwise leave default. This field is not yet present on `Article`; see *Out of scope* below. |
| `MediaReferences` | Append scraped media (og:image etc.) to the existing list. `MediaIngestionService` already deduplicates by URL, so duplicates are harmless. |
| `Title`, `OriginalUrl`, `PublishedAt`, `ExternalId`, `Language` | Never touched by the scraper — RSS is authoritative. |

### What does NOT change

- `ISourceParser` interface — signature stays the same.
- `TelegramParser` — not touched; scraping is RSS-only (Telegram posts are
  not HTML articles).
- `MediaIngestionService`, `IMediaContentDownloader` implementations, and
  the R2 storage path — already agnostic to the source of `MediaReference`.
- `SourceFetcherWorker` — unchanged. It continues to call
  `parser.ParseAsync` and pass resulting articles to media ingestion.
- `Core/DomainModels/Article.cs` already has `Tags` (a `List<string>`),
  so scraped tags merge directly into `article.Tags`. No schema
  migration is needed to land scraping tags.
- Database schema — no migration for this feature. An `Author` column on
  `articles` would be a separate ADR; for now `ScrapedArticle.Author` is
  surfaced into `ScrapedArticle` for future use but **not written to the
  domain model**, keeping the change small.

## Consequences

**Positive:**
- Downstream AI stages (Claude/Gemini analysis, embedding, event
  classification) operate on full article bodies, materially improving
  summary and classification quality.
- og:image becomes available as a media source — addresses the feature's
  "make sure og:image still works" intent even though it previously did
  not work at all.
- Library choice is encapsulated: replacing HtmlAgilityPack is a
  one-file change.
- Introduces a reusable `IArticleContentScraper` seam that a future backfill
  worker can drive over stored `Article.OriginalUrl`s to upgrade already-
  ingested records.

**Negative / risks:**
- Each RSS cycle now issues up to *N* extra HTTP requests (N = items per
  feed). A publication with 50 items and a 15 s timeout can hold the
  parser for ~200 s worst case with `MaxConcurrencyPerFeed = 4`. The
  `RssFetcherOptions.IntervalSeconds` default of 600 s absorbs this but
  ops should watch P95 feed duration.
- External sites may block or rate-limit the bot. A respectful
  `User-Agent` plus per-feed concurrency cap mitigates but does not
  eliminate this; 4xx/5xx responses become the dominant failure mode,
  which the fall-back path handles cleanly.
- HtmlAgilityPack introduces a dependency on a non-Microsoft library
  — widely used in .NET but still external.
- Two places now touch `MediaReference` (RSS extraction + og:image) —
  watch for duplicate URL surprises downstream; `MediaIngestionService`
  dedup is the safety net.

**Files affected:**

New:
- `Core/Interfaces/Parsers/IArticleContentScraper.cs`
- `Core/DomainModels/ScrapedArticle.cs`
- `Infrastructure/Parsers/HtmlArticleContentScraper.cs`
- `Infrastructure/Configuration/ArticleScraperOptions.cs`

Edited:
- `Infrastructure/Parsers/RssParser.cs` — primary constructor takes
  `IArticleContentScraper` and `IOptions<ArticleScraperOptions>`; new
  private helper performs the bounded-parallelism scrape + merge.
- `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` — in
  `AddParsers`: register `ArticleScraperOptions` via
  `services.Configure<ArticleScraperOptions>(...)`, register
  `IArticleContentScraper` as scoped, and add the named
  `"ArticleContentScraper"` `IHttpClientFactory` client.
- `Api/appsettings.Development.json` and `Worker/appsettings.Development.json`
  — add an `ArticleScraper` section with defaults (mirrors how
  `RssFetcher`, `CloudflareR2`, etc. are configured).
- `Infrastructure/Infrastructure.csproj` — add `HtmlAgilityPack` package
  reference.

Tests (delegated to `test-writer`, planned by `feature-planner`):
- `Tests/Infrastructure.Tests/Parsers/HtmlArticleContentScraperTests.cs`
  — NUnit + Moq `HttpMessageHandler` sink; cases for: happy path extracts
  content/author/tags/og:image; malformed HTML returns `null`
  gracefully; timeout returns `null`; non-200 returns `null`; oversize
  response is rejected; `User-Agent` header is sent.
- `Tests/Infrastructure.Tests/Parsers/RssParserTests.cs` — add cases:
  scraper success replaces RSS content when longer; scraper failure leaves
  RSS content intact; og:image merges with Media RSS media; disabled
  scraper is a no-op.

## Implementation Notes

### Skills `feature-planner` and `implementer` must follow

- **`code-conventions`** (`.claude/skills/code-conventions/SKILL.md`) —
  layer-boundaries table (interface allowed in `Core/` only if no
  infrastructure types leak); Options-pattern rules (`SectionName` constant,
  default values, extract `.Value` in constructor); interface organisation
  under `Core/Interfaces/Parsers/`; services registered via scoped DI in
  `InfrastructureServiceExtensions`.
- **`clean-code`** (`.claude/skills/clean-code/SKILL.md`) — method length
  ≤ 20 lines (split `HtmlArticleContentScraper` into `ExtractContent`,
  `ExtractAuthor`, `ExtractTags`, `ExtractOpenGraphMedia`); guard
  clauses at the top of `ScrapeAsync`; no magic numbers — every threshold
  comes from `ArticleScraperOptions`; no commented-out code.
- **`mappers`** (`.claude/skills/mappers/SKILL.md`) — if the merge logic
  inside `RssParser` grows beyond ~15 lines, extract it into a static
  helper `ScrapedArticleMerger` with an extension method like
  `Article.MergeScraped(this Article article, ScrapedArticle? scraped)`.
  Static, pure, no I/O — matches the mapper convention for
  Infrastructure-internal value transforms.
- **`api-conventions`** (`.claude/skills/api-conventions/SKILL.md`) — not
  directly triggered; no API endpoint changes.
- **`ef-core-conventions`** (`.claude/skills/ef-core-conventions/SKILL.md`)
  — not triggered; no persistence changes for this feature.

### Order of changes

1. Add `HtmlAgilityPack` package reference to `Infrastructure/Infrastructure.csproj`.
2. Create `Core/DomainModels/ScrapedArticle.cs` (record) and
   `Core/Interfaces/Parsers/IArticleContentScraper.cs`. Build.
3. Create `Infrastructure/Configuration/ArticleScraperOptions.cs`. Build.
4. Create `Infrastructure/Parsers/HtmlArticleContentScraper.cs` with the
   four private extractors and `ScrapeAsync`. Build.
5. Extend `InfrastructureServiceExtensions.AddParsers` with the named
   HTTP client, the Options registration, and the scraper DI. Build.
6. Update both `appsettings.Development.json` files with the
   `ArticleScraper` section.
7. Refactor `RssParser.ParseAsync`: after building the per-item base
   `Article` from RSS, drive the scrape with a `SemaphoreSlim`; apply the
   merge rules; swallow per-item exceptions with a warning log.
8. Add scraper unit tests (delegated to `test-writer`).
9. Update `RssParserTests.cs` with merge + fallback scenarios.
10. Run the full solution build and `Tests/Infrastructure.Tests` to
    confirm no regressions.

### Explicitly out of scope

- **Persisting author/extra metadata** — `ScrapedArticle.Author` is
  produced but not yet written to `Article` or the database. A follow-up
  ADR should add `Author` to `Article` and an SQL migration under
  `Infrastructure/Persistence/Sql/`.
- **Polly / resilience policies** — deferred to a project-wide decision.
- **Backfill of already-ingested articles** — the scraper is reusable, but
  introducing a backfill worker is a separate task.
- **Scraping Telegram posts** — `TelegramParser` is not touched; Telegram
  content is already full-text via MTProto.
- **Respecting `robots.txt`** — can be added via an `ArticleScraperOptions.RespectRobots`
  flag + a small fetcher, but it is a distinct concern and would expand
  this feature's blast radius. Defer unless a compliance need arises.
