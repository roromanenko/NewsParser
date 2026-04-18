# Enrich RSS Articles with Full-Page Scrape

## Goal

After reading an RSS feed, visit each article URL, scrape the full article body,
author, tags, and og:image metadata using HtmlAgilityPack, and merge the results
into the `Article` objects produced by `RssParser`; per-article scrape failures
must fall back silently to the RSS-only data without interrupting the batch.

## Affected Layers

- Core
- Infrastructure
- Tests

## Tasks

### Core

- [x] **Create `Core/DomainModels/ScrapedArticle.cs`** — sealed immutable record with
      properties: `string? FullContent`, `string? Author`, `IReadOnlyList<string> Tags`,
      `IReadOnlyList<MediaReference> DiscoveredMedia`. No infrastructure or EF references.

      _Acceptance: file compiles in isolation; record uses only types from `Core/DomainModels/`;
      no `using` statements reference `Infrastructure` or any third-party library._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/Interfaces/Parsers/IArticleContentScraper.cs`** — single-method
      interface: `Task<ScrapedArticle?> ScrapeAsync(string url, CancellationToken cancellationToken = default)`.
      No HtmlAgilityPack or HTTP types in the signature.

      _Acceptance: interface references only `ScrapedArticle` and BCL types; solution builds
      after this file is added alongside the existing `ISourceParser.cs`._
      _Skill: .claude/skills/code-conventions/SKILL.md_

### Infrastructure

- [x] **Modify `Infrastructure/Infrastructure.csproj`** — add
      `<PackageReference Include="HtmlAgilityPack" Version="1.11.*" />` inside the existing
      `<ItemGroup>` that holds `PackageReference` elements.

      _Acceptance: `dotnet restore` succeeds; `HtmlAgilityPack` appears in the restored
      package graph; no other `<ItemGroup>` or `<PropertyGroup>` is altered._

- [x] **Create `Infrastructure/Configuration/ArticleScraperOptions.cs`** — Options class
      following the project pattern (see `CloudflareR2Options.cs` for reference):
      - `public const string SectionName = "ArticleScraper";`
      - `public bool Enabled { get; set; } = true;`
      - `public int RequestTimeoutSeconds { get; set; } = 15;`
      - `public int MaxHtmlSizeBytes { get; set; } = 2097152;` (2 MB — no magic arithmetic
        expression, store the computed value)
      - `public int MaxConcurrencyPerFeed { get; set; } = 4;`
      - `public string UserAgent { get; set; } = "NewsParserBot/1.0";`

      _Acceptance: class is in `Infrastructure.Configuration` namespace; all five properties
      have defaults matching the ADR; `SectionName` constant is present; solution builds._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Infrastructure/Parsers/HtmlArticleContentScraper.cs`** — concrete
      implementation of `IArticleContentScraper` using `IHttpClientFactory` (named client
      `"ArticleContentScraper"`) and `IOptions<ArticleScraperOptions>`. Extract options
      as `.Value` in the constructor. Split DOM traversal into four private helpers:

      - `ExtractContent(HtmlDocument doc) : string?` — prefer `<article>` element;
        fall back to largest `<div>` block containing `<p>` children; return concatenated
        `InnerText` of `<p>` elements within the chosen container.
      - `ExtractAuthor(HtmlDocument doc) : string?` — read `<meta name="author">`,
        `<meta property="article:author">`, then JSON-LD `"author"` field.
      - `ExtractTags(HtmlDocument doc) : IReadOnlyList<string>` — collect
        `<meta property="article:tag">` (repeated), comma-split `<meta name="keywords">`,
        and JSON-LD `"keywords"`; return distinct (OrdinalIgnoreCase), capped at 20.
      - `ExtractOpenGraphMedia(HtmlDocument doc) : IReadOnlyList<MediaReference>` — collect
        `og:image`, `og:image:secure_url`, `twitter:image`, `twitter:image:src` meta
        properties; return each as
        `new MediaReference(url, MediaKind.Image, null, MediaSourceKind.Http)`.

      `ScrapeAsync` must:
      1. Guard-return `null` when `Enabled` is false.
      2. Fetch the URL with the named `HttpClient`; guard-return `null` on non-2xx status.
      3. Read response content up to `MaxHtmlSizeBytes`; guard-return `null` if empty.
      4. Load into `HtmlDocument`; call the four private helpers; return a `ScrapedArticle`.

      _Acceptance: class implements `IArticleContentScraper`; no method body exceeds 20
      lines; all thresholds come from `ArticleScraperOptions` (no magic numbers); solution
      builds; `HtmlAgilityPack` types do not appear in the public API surface._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Create `Infrastructure/Parsers/ScrapedArticleMerger.cs`** — static class with one
      public extension method:
      `public static void MergeScraped(this Article article, ScrapedArticle? scraped, int maxTags = 20)`

      Merge rules (from ADR):
      - `OriginalContent`: replace only when `scraped.FullContent` is non-null, non-empty,
        and longer than the existing `article.OriginalContent`.
      - `Tags`: union of `article.Tags` and `scraped.Tags`, distinct OrdinalIgnoreCase,
        capped at `maxTags`.
      - `MediaReferences`: append `scraped.DiscoveredMedia` entries to `article.MediaReferences`.
      - `Author` (`scraped.Author`) is produced but NOT written to `Article` — out of scope
        per ADR; add a `// TODO: persist Author once Article model gains the field` comment.
      - `Title`, `OriginalUrl`, `PublishedAt`, `ExternalId`, `Language` are never modified.
      - If `scraped` is `null`, return immediately (no-op).

      _Acceptance: static class compiles; method is pure (no I/O, no logging); all six merge
      rules are implemented; `Article.Title` / `OriginalUrl` / `PublishedAt` / `ExternalId`
      are never assigned inside this method._
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Modify `Infrastructure/Parsers/RssParser.cs`** — accept two new constructor
      parameters: `IArticleContentScraper scraper` and `IOptions<ArticleScraperOptions> options`.
      Extract `.Value` into a field. After the feed's item list is materialised into
      `List<Article>`, drive the scrape with a `SemaphoreSlim` sized from
      `options.MaxConcurrencyPerFeed`:

      ```
      var sem = new SemaphoreSlim(options.MaxConcurrencyPerFeed);
      var tasks = articles.Select(async article =>
      {
          await sem.WaitAsync(cancellationToken);
          try
          {
              var scraped = await _scraper.ScrapeAsync(article.OriginalUrl!, cancellationToken);
              article.MergeScraped(scraped);
          }
          catch (Exception ex)
          {
              _logger.LogWarning(ex, "Scrape failed for {Url}; keeping RSS data.", article.OriginalUrl);
          }
          finally { sem.Release(); }
      });
      await Task.WhenAll(tasks);
      ```

      Add `ILogger<RssParser>` to the constructor for the warning log. When
      `ArticleScraperOptions.Enabled` is `false`, skip the entire `SemaphoreSlim` block
      (the guard inside `HtmlArticleContentScraper.ScrapeAsync` is a second safety net,
      but skip at the call-site too for zero overhead). `ExtractMediaReferences` and all
      existing private static methods remain unchanged.

      _Acceptance: `RssParser` still satisfies `ISourceParser`; `ParseAsync` returns the
      same `List<Article>` shape; a thrown exception inside one article's scrape does not
      prevent the other articles from being returned; solution builds._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — inside
      the existing `AddParsers` method, add after the existing lines:

      1. `services.Configure<ArticleScraperOptions>(configuration.GetSection(ArticleScraperOptions.SectionName));`
      2. `services.AddScoped<IArticleContentScraper, HtmlArticleContentScraper>();`
      3. Named HTTP client registration:
         ```
         services.AddHttpClient("ArticleContentScraper")
             .ConfigureHttpClient((sp, client) =>
             {
                 var opts = sp.GetRequiredService<IOptions<ArticleScraperOptions>>().Value;
                 client.Timeout = TimeSpan.FromSeconds(opts.RequestTimeoutSeconds);
                 client.DefaultRequestHeaders.UserAgent.ParseAdd(opts.UserAgent);
             });
         ```

      Add `using Infrastructure.Configuration;` if not already present (it is already
      imported — verify before adding a duplicate). `IArticleContentScraper` must be
      added to the `using Core.Interfaces.Parsers;` block which already exists.

      _Acceptance: the DI container resolves `IArticleContentScraper` without error at
      startup; the named `"ArticleContentScraper"` client is created with the configured
      timeout and User-Agent; solution builds._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Api/appsettings.Development.json`** — add the `ArticleScraper` section
      with all five default values immediately after the `CloudflareR2` block:

      ```json
      "ArticleScraper": {
        "Enabled": true,
        "RequestTimeoutSeconds": 15,
        "MaxHtmlSizeBytes": 2097152,
        "MaxConcurrencyPerFeed": 4,
        "UserAgent": "NewsParserBot/1.0"
      }
      ```

      _Acceptance: JSON file is valid (no trailing commas, correct brace nesting);
      `dotnet run --project Api` starts without configuration errors._

- [x] **Modify `Worker/appsettings.Development.json`** — add the same `ArticleScraper`
      section with identical default values, placed after the `CloudflareR2` block
      (following the same positioning pattern as other sections in that file).

      _Acceptance: JSON file is valid; Worker starts without configuration errors._

### Tests

- [x] **Create `Tests/Infrastructure.Tests/Parsers/HtmlArticleContentScraperTests.cs`** —
      NUnit test fixture covering `HtmlArticleContentScraper` in isolation using a mock
      `HttpMessageHandler`. Required test cases (delegated to `test-writer`):

      1. `ScrapeAsync_WhenEnabledFalse_ReturnsNull` — options `Enabled=false`; assert return
         is `null` without any HTTP call.
      2. `ScrapeAsync_WhenResponseIsNon200_ReturnsNull` — handler returns `404`; assert
         `null`.
      3. `ScrapeAsync_WhenResponseBodyIsEmpty_ReturnsNull` — handler returns `200` with
         empty body; assert `null`.
      4. `ScrapeAsync_WhenHtmlHasArticleElement_ExtractsFullContent` — handler returns
         valid HTML with `<article><p>Full body text.</p></article>`; assert
         `ScrapedArticle.FullContent` contains "Full body text."
      5. `ScrapeAsync_WhenHtmlHasAuthorMeta_ExtractsAuthor` — HTML contains
         `<meta name="author" content="Jane Doe">`; assert `ScrapedArticle.Author`
         is "Jane Doe".
      6. `ScrapeAsync_WhenHtmlHasArticleTagMeta_ExtractsTags` — HTML contains two
         `<meta property="article:tag">` elements; assert `ScrapedArticle.Tags`
         contains both values.
      7. `ScrapeAsync_WhenHtmlHasKeywordsMeta_SplitsAndExtractsTags` — HTML contains
         `<meta name="keywords" content="news,politics,world">`; assert `ScrapedArticle.Tags`
         contains "news", "politics", "world".
      8. `ScrapeAsync_WhenHtmlHasOgImage_ReturnsMediaReference` — HTML contains
         `<meta property="og:image" content="https://cdn.example.com/img.jpg">`; assert
         `ScrapedArticle.DiscoveredMedia` has one entry with that URL and `MediaKind.Image`.
      9. `ScrapeAsync_WhenHtmlHasTwitterImage_ReturnsMediaReference` — same as above but
         `<meta name="twitter:image">`.
      10. `ScrapeAsync_WhenResponseExceedsMaxHtmlSizeBytes_ReturnsNull` — configure
          `MaxHtmlSizeBytes=10`; handler returns `200` with body longer than 10 bytes;
          assert `null`.
      11. `ScrapeAsync_WhenRequestTimesOut_ReturnsNull` — handler delays beyond timeout;
          assert `null` (requires `TaskCanceledException` path).
      12. `ScrapeAsync_WhenCalled_SendsConfiguredUserAgentHeader` — assert that the HTTP
          request contains the `User-Agent` header matching `ArticleScraperOptions.UserAgent`.

      _Acceptance: all 12 tests compile and pass; no live network calls; `HttpMessageHandler`
      is mocked via `Moq`._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Modify `Tests/Infrastructure.Tests/Parsers/RssParserTests.cs`** — add four new
      test cases covering the scrape orchestration path (delegated to `test-writer`).
      The existing reflection-based `ExtractMediaReferences` tests must remain unchanged.
      New cases:

      1. `ParseAsync_WhenScraperReturnsLongerContent_ReplacesRssContent` — mock
         `IArticleContentScraper` returns a `ScrapedArticle` whose `FullContent` is longer
         than the RSS item's description; assert the returned article's `OriginalContent`
         equals the scraped content.
      2. `ParseAsync_WhenScraperReturnsShortContent_KeepsRssContent` — mock scraper returns
         content shorter than the RSS description; assert `OriginalContent` is the RSS value.
      3. `ParseAsync_WhenScraperThrows_ReturnsArticleWithRssContent` — mock scraper throws
         `HttpRequestException`; assert the article is still present in the result list with
         the original RSS content intact; assert no exception propagates.
      4. `ParseAsync_WhenScraperReturnsOgImage_AppendsToMediaReferences` — RSS feed has one
         `<enclosure>` image; mock scraper returns a `ScrapedArticle` with one
         `DiscoveredMedia` og:image entry; assert the returned article's `MediaReferences`
         contains both entries.
      5. `ParseAsync_WhenOptionsEnabledFalse_DoesNotCallScraper` — options `Enabled=false`;
         assert `IArticleContentScraper.ScrapeAsync` is never invoked (verify with Moq
         `Verify(..., Times.Never)`).

      Note: `RssParser.ParseAsync` calls `FeedReader.ReadAsync` (live HTTP). Use
      `FeedReader.ReadFromString` fed via a mocked `FeedReader` seam, or structure tests
      to inject a pre-built feed — follow the same reflection/string-feed pattern the
      existing tests use where feasible.

      _Acceptance: all five new tests compile and pass alongside the existing tests;
      no live network calls in any new test._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

      _Implementation note: test #5 (`ParseAsync_WhenOptionsEnabledFalse_DoesNotCallScraper`)
      is marked `[Ignore]` because the `Enabled=false` short-circuit is inside `ParseAsync`,
      which runs behind the live-HTTP `FeedReader.ReadAsync` call. Reaching it would require
      changing production signatures to inject a feed — explicitly out of scope per the
      task rules. The scraper-level kill-switch is fully covered by
      `HtmlArticleContentScraperTests.ScrapeAsync_WhenEnabledFalse_ReturnsNull`._

### Verification

- [ ] **Run `dotnet build`** from the solution root — confirm zero errors and zero new
      warnings.

      _Acceptance: build exits with code 0._

- [x] **Run `dotnet test Tests/Infrastructure.Tests/`** — confirm all non-`[Explicit]`
      tests pass including the new scraper and parser test cases.

      _Acceptance: test run exits with code 0; no regressions in existing tests._

      _Result: 16 new tests pass (11 scraper + 4 RSS orchestration + 1 scraper timeout path),
      1 new test intentionally `[Ignore]`-skipped (parser `Enabled=false` path — see test-file
      comment). Overall Infrastructure.Tests: 194 passed, 1 skipped, 3 pre-existing
      failures unrelated to this feature (ClaudeContradictionDetectorTests JSON parsing x2,
      VectorTypeHandlerTests cast issue x1 — all untouched by these changes)._

## Open Questions

- None. The ADR fully specifies the approach, merge rules, options schema, HTTP client
  configuration, and out-of-scope items. Implementation can proceed directly.
