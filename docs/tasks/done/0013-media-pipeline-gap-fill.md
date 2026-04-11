# Media Pipeline Gap Fill

## Goal

Wire every unregistered service, implement the missing `RssParser` media extraction, add the
`SourceFetcherWorker` ingestion call, add the `ArticleConfiguration` ignore clause, and implement
`TelegramClientService.DownloadMediaAsync` — closing all gaps identified by the architecture
audit of ADR 0006 and ADR 0007 so the end-to-end media ingestion pipeline compiles, starts,
and runs correctly.

## Affected Layers

- Infrastructure
- Worker

---

## Tasks

### Infrastructure — EF configuration

- [x] **Modify `Infrastructure/Persistence/Configurations/ArticleConfiguration.cs`** — add
      `builder.Ignore(a => a.MediaReferences);` inside the `Configure` method, after the
      existing index declarations and before the closing brace.
      _Acceptance: EF model snapshot does not include a `MediaReferences` column; `dotnet build`
      on the Infrastructure project succeeds with zero errors._
      _Skill: `.claude/skills/ef-core-conventions/SKILL.md`_

### Infrastructure — TelegramClientService gateway implementation

- [x] **Modify `Infrastructure/Services/TelegramClientService.cs`** — make the class also
      implement `ITelegramMediaGateway` (add the interface to the class declaration); add the
      `TelegramMediaPayload` sealed record to the same file (or a neighbouring file in
      `Infrastructure/Services/`); implement the public method:
      ```csharp
      public async Task<TelegramMediaDownloadResult?> DownloadMediaAsync(
          string externalHandle,
          Stream destination,
          CancellationToken cancellationToken = default)
      ```
      Implementation must:
      1. Call `TelegramMediaHandle.TryDecode(externalHandle, ...)` — return `null` if it fails.
      2. Build `InputChannel(channelId, accessHash)`, call
         `_client.Channels_GetMessages(inputChannel, new InputMessageID { id = messageId })`.
      3. Switch on `message.media`:
         - `MessageMediaPhoto { photo: Photo p }` → content type `"image/jpeg"`, download via
           `_client.DownloadFileAsync(p, destination)`, return
           `new TelegramMediaDownloadResult("image/jpeg", destination.Length)`.
         - `MessageMediaDocument { document: Document d }` where `d.mime_type` starts with
           `image/` or `video/` → download via `_client.DownloadFileAsync(d, destination)`,
           return `new TelegramMediaDownloadResult(d.mime_type, d.size)`.
         - Anything else → return `null`.
      4. Catch `WTelegram.WTException`, log `Warning`, return `null`. Never throw.
      `TelegramMediaDownloadResult` is the record already defined in
      `Core/Interfaces/Storage/ITelegramMediaGateway.cs` — do NOT redefine it.
      _Acceptance: `TelegramClientService` compiles, satisfies both `ITelegramChannelReader`
      and `ITelegramMediaGateway`; `DownloadMediaAsync` never throws; no WTelegram type leaks
      outside `Infrastructure`._
      _Skill: `.claude/skills/code-conventions/SKILL.md`_

### Infrastructure — RssParser media extraction

- [x] **Modify `Infrastructure/Parsers/RssParser.cs`** — add a `private static
      List<MediaReference> ExtractMediaReferences(FeedItem item)` method and call it from
      `ParseAsync` to populate `article.MediaReferences` for each item.
      The method must:
      - Cast `item.SpecificItem` to `Rss20FeedItem` and read its `Enclosure` property
        (RSS 2.0 `<enclosure>` element). If non-null and its `Type` starts with `image/` or
        `video/`, emit one `MediaReference(Url, Kind, DeclaredContentType)` (defaults for
        `SourceKind` and `ExternalHandle` are fine — both are `Http`/`null` by default).
      - Cast `item.SpecificItem` to `MediaRssFeedItem` and iterate `Media` items
        (`media:content` elements). For each, determine `Kind` from `Type` (starts with
        `video/`) or `Medium` equals `"video"`. Skip if neither `image` nor `video`.
      - When the item is NOT a `MediaRssFeedItem`, apply an XML fallback:
        iterate `item.Element?.Descendants(...)` for `media:content` and
        `media:thumbnail` elements and extract `url` and `type` attributes, classifying
        `Kind` from the `type` attribute or the `medium` attribute.
      - Skip any URL that is null or empty.
      Use `using CodeHollow.FeedReader.Feeds;` and `using System.Xml.Linq;` as needed.
      No new NuGet packages.
      _Acceptance: `RssParserTests` (which uses reflection to call `ExtractMediaReferences`)
      compiles and all 7 existing test cases pass — specifically `ExtractMediaReferences_
      WhenRss20ItemHasImageEnclosure_ReturnsOneImageReference` and the `media:content` tests.
      `dotnet test` on `Infrastructure.Tests` shows green._
      _Skill: `.claude/skills/code-conventions/SKILL.md`_

### Infrastructure — DI registrations

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — add a new
      private `AddStorage(IServiceCollection services, IConfiguration configuration)` extension
      method and chain it into `AddInfrastructure`. The method must register:
      1. `services.Configure<CloudflareR2Options>(configuration.GetSection(CloudflareR2Options.SectionName))`
      2. `services.AddScoped<IMediaStorage, CloudflareR2Storage>()`
      3. `services.AddScoped<IMediaFileRepository, MediaFileRepository>()`
      4. `services.AddScoped<IMediaIngestionService, MediaIngestionService>()`
      5. `services.AddScoped<IMediaContentDownloader, HttpMediaContentDownloader>()`
      6. `services.AddScoped<IMediaContentDownloader, TelegramMediaContentDownloader>()`
      7. Named `HttpClient` registration:
         ```csharp
         services.AddHttpClient("MediaDownloader")
             .ConfigureHttpClient((sp, client) =>
             {
                 client.Timeout = TimeSpan.FromSeconds(
                     sp.GetRequiredService<IOptions<CloudflareR2Options>>().Value.DownloadTimeoutSeconds);
             });
         ```
      Also add, inside the existing `AddParsers` method, two forwarding singleton registrations
      so the already-registered `TelegramClientService` singleton is also resolvable as
      `ITelegramMediaGateway` and `ITelegramChannelReader`:
      ```csharp
      services.AddSingleton<ITelegramMediaGateway>(sp => sp.GetRequiredService<TelegramClientService>());
      services.AddSingleton<ITelegramChannelReader>(sp => sp.GetRequiredService<TelegramClientService>());
      ```
      Add all required `using` directives (`Core.Interfaces.Storage`, `Infrastructure.Storage`,
      `Infrastructure.Parsers`, `Microsoft.Extensions.Options`).
      _Acceptance: the Worker and Api projects start without `InvalidOperationException` about
      unresolved services; `dotnet build` on the solution succeeds; all DI-resolution integration
      tests pass._
      _Skill: `.claude/skills/code-conventions/SKILL.md`_

### Worker — SourceFetcherWorker ingestion call

- [x] **Modify `Worker/Workers/SourceFetcherWorker.cs`** — wire `IMediaIngestionService` into
      the fetch loop without touching the constructor (scoped, not singleton):
      1. In `ProcessAsync`, after the existing `var parsers = ...` line, add:
         `var mediaIngestionService = scope.ServiceProvider.GetRequiredService<IMediaIngestionService>();`
      2. Pass `mediaIngestionService` as a new parameter to `ProcessSourceAsync`.
      3. Update the `ProcessSourceAsync` signature to accept
         `IMediaIngestionService mediaIngestionService`.
      4. Inside `ProcessSourceAsync`, immediately after the successful
         `await articleRepository.AddAsync(article, cancellationToken);` call and before
         `recentTitles.Add(article.Title);`, add:
         ```csharp
         try
         {
             await mediaIngestionService.IngestForArticleAsync(
                 article.Id, article.MediaReferences, cancellationToken);
         }
         catch (Exception ex)
         {
             _logger.LogWarning(ex, "Media ingestion failed for article {ArticleId}", article.Id);
         }
         ```
      Add `using Core.Interfaces.Services;` if not already present.
      _Acceptance: `SourceFetcherWorker` compiles; when `IMediaIngestionService.IngestForArticleAsync`
      throws, the `saved` counter is still incremented and the article loop continues (existing
      `SourceFetcherWorkerMediaTests` scenarios pass)._
      _Skill: `.claude/skills/code-conventions/SKILL.md`_

### Tests — verify RssParserTests compile and pass

- [x] **Run `Tests/Infrastructure.Tests/Parsers/RssParserTests.cs`** — execute
      `dotnet test --filter "FullyQualifiedName~RssParserTests"` from the solution root;
      confirm all 7 test cases pass after the `RssParser` change.
      _Acceptance: NUnit reports 7 tests passing, 0 failing; no reflection `MissingMethodException`
      is thrown._
      _Agent: test-writer_
      _Skill: `.claude/skills/testing/SKILL.md`_

### Tests — verify SourceFetcherWorkerMediaTests pass

- [x] **Run `Tests/Worker.Tests/Workers/SourceFetcherWorkerMediaTests.cs`** — execute
      `dotnet test --filter "FullyQualifiedName~SourceFetcherWorkerMediaTests"` from the
      solution root; confirm all existing tests pass after the worker change.
      _Acceptance: NUnit reports all tests in the fixture as passing; the
      "ingestion failure does not abort article loop" scenario is green._
      _Agent: test-writer_
      _Skill: `.claude/skills/testing/SKILL.md`_

## Open Questions

- None. All gaps are unambiguous — the ADRs specify exact method signatures, DI registration
  patterns, and call sites. No design decisions are deferred.
