# Telegram Media Extraction to MediaReferences

## Status

Proposed

## Context

ADR 0006 introduced the media ingestion pipeline for RSS: `RssParser` extracts
`MediaReference` values from feed enclosures, `SourceFetcherWorker` calls
`IMediaIngestionService.IngestForArticleAsync` after each article save, and
`MediaIngestionService` downloads each URL with an `HttpClient` and uploads the
bytes to Cloudflare R2 via `IMediaStorage`.

The Telegram path (`Infrastructure/Services/TelegramClientService.cs` →
`Infrastructure/Parsers/TelegramParser.cs`) currently ignores media entirely:

```csharp
// TelegramParser.cs — no MediaReferences populated
return messages.Select(msg => new Article { ... }).ToList();
```

We want parity: Telegram-sourced `Article`s should arrive at
`SourceFetcherWorker` with `MediaReferences` populated, and the existing
post-save call to `mediaIngestionService.IngestForArticleAsync` should
successfully upload Telegram photos and videos to R2 exactly as it does for
RSS enclosures.

### The core problem: Telegram media has no public HTTP URL

RSS ingestion works because the `MediaReference.Url` is a public HTTP URL that
`MediaIngestionService` can `GET` through the `"MediaDownloader"` named
`HttpClient`. Telegram media is different:

- A `Message.media` is either `MessageMediaPhoto` (wrapping a `Photo`) or
  `MessageMediaDocument` (wrapping a `Document`, which may be a video when its
  `MimeType` starts with `video/`).
- Bytes are retrieved through the authenticated MTProto session using
  `WTelegram.Client.DownloadFileAsync(InputFileLocationBase, Stream)` (or the
  typed `DownloadFileAsync(Photo, ...)` / `DownloadFileAsync(Document, ...)`
  overloads). There is no HTTP URL; the download must go through the existing
  singleton `TelegramClientService._client`.
- The only stable identifier across calls is `(Access Hash, File Reference,
  Id, Dc)`, and the `FileReference` may expire and require re-fetching the
  message. For our use case (ingest immediately after parsing) this is a
  non-issue.
- `t.me/{username}/{messageId}` is a UI link to the message, **not** a
  downloadable media URL. It is, however, a stable logical identifier we
  already store as `Article.OriginalUrl` and it is what we will use for the
  per-article URL-dedup in `MediaIngestionService.ExistsByArticleAndUrlAsync`.

Therefore `MediaIngestionService` — in its current shape, which assumes
`MediaReference.Url` is HTTP-downloadable — cannot handle Telegram media
without a change to how downloading is abstracted.

### Constraints from the existing codebase

- **Layering** (`.claude/skills/code-conventions/SKILL.md`): Worker cannot
  touch `DbContext` or HttpClient. All I/O stays in Infrastructure.
  `TelegramClientService` is already registered as a singleton
  (`InfrastructureServiceExtensions.AddParsers`), so any new method on it must
  remain thread-safe. WTelegram's `Client.DownloadFileAsync` is safe to call
  concurrently on the same client instance.
- **Ingestion best-effort contract** (ADR 0006 §9, §11): media failures never
  abort the article loop. This contract must be preserved end-to-end.
- **ADR 0006 explicitly deferred Telegram**: see "Transient `MediaReferences`
  property" in Consequences — `ISourceParser.ParseAsync` was left unchanged
  specifically because it also serves `TelegramParser`. This ADR now fills
  that gap without changing the parser interface.
- **`MediaReference` is a Core domain record**
  (`public record MediaReference(string Url, MediaKind Kind, string?
  DeclaredContentType);`). Core cannot depend on WTelegram types (`TL.Photo`,
  `TL.Document`, `InputFileLocationBase`). Any Telegram-specific handle must
  not leak into Core.
- **`MediaIngestionService` is scoped**; `TelegramClientService` is singleton.
  Resolving the singleton from inside a scoped ingestion service is allowed
  (captive-dependency rule only bars the reverse direction), and matches how
  `TelegramParser` (scoped) already injects `TelegramClientService`.

## Options

### Option 1 — Download bytes eagerly inside `TelegramParser`, pass through a parser-side cache

`TelegramParser` (or a helper inside `TelegramClientService`) downloads every
photo/video into memory during `ParseAsync`, materialises each as a
`MediaReference`, and stashes the raw bytes in a per-`Article` side-channel
(e.g. `Article.MediaReferences` carrying a `byte[] PreloadedContent` field or
a parallel `Dictionary<string, Stream>` attached to the worker scope).
`MediaIngestionService` is taught to check "do I already have the bytes?"
before calling the `HttpClient`.

**Pros:**
- `MediaIngestionService` change is minimal — one branch.
- Uses the already-open authenticated Telegram session directly at parse time.

**Cons:**
- Pollutes the Core domain model: `MediaReference` (a pure descriptor record)
  would have to carry bytes, or `Article` would need a transient media-bytes
  dictionary. Both break the "pure domain" rule from `code-conventions`.
- Memory pressure: a batch of 100 Telegram messages each with a 50 MB video
  means the parser buffers ~5 GB before any upload happens. RSS ingestion
  streams one file at a time; this regresses that guarantee.
- Bytes are held across the gap between parser return and the post-save
  `IngestForArticleAsync` call inside `SourceFetcherWorker.ProcessSourceAsync`
  — including while the article is being validated, deduplicated, persisted.
  For skipped articles, bytes are wasted download + GC churn.
- Couples `TelegramParser` (Core contract: "turn a source into articles")
  with large binary I/O. Violates the single-responsibility intent of the
  parser layer.

### Option 2 — Add a `SourceKind` discriminator to `MediaReference` and a dispatching downloader inside `MediaIngestionService` (chosen)

1. Extend the `MediaReference` Core record with two additional fields:
   - `MediaSourceKind SourceKind` — enum `{ Http, Telegram }`.
   - `string? ExternalHandle` — opaque-to-Core string encoding the
     provider-specific locator. For Telegram this is the channel access hash
     plus message id (e.g. `"{channelId}:{accessHash}:{messageId}"`);
     `MediaIngestionService` parses it only in the Telegram branch.
     For RSS this is `null`.
2. Introduce a narrow `IMediaContentDownloader` abstraction in
   `Core/Interfaces/Storage/`:
   ```csharp
   public interface IMediaContentDownloader
   {
       MediaSourceKind Kind { get; }
       Task<MediaDownloadResult?> DownloadAsync(
           MediaReference reference,
           CancellationToken cancellationToken = default);
   }

   public record MediaDownloadResult(
       Stream Content,
       string ContentType,
       long SizeBytes);
   ```
   Two implementations in Infrastructure, both registered as `IEnumerable<IMediaContentDownloader>`:
   - `HttpMediaContentDownloader` — encapsulates the existing HTTP path from
     `MediaIngestionService.IngestSingleReferenceAsync` (uses the
     `"MediaDownloader"` named `HttpClient`, enforces `MaxFileSizeBytes`,
     resolves `ContentType` from header/fallbacks).
   - `TelegramMediaContentDownloader` — depends on the singleton
     `TelegramClientService`, calls a new public method
     `DownloadMediaAsync(string externalHandle, Stream destination, CancellationToken)`
     that looks up the message via `Messages_GetHistory`/`Channels_GetMessages`,
     extracts the `Photo` or `Document` from `message.media`, calls
     `Client.DownloadFileAsync`, and returns content type + byte count.
3. `MediaIngestionService.IngestSingleReferenceAsync` is refactored: instead of
   inlining the HTTP download, it picks the `IMediaContentDownloader` whose
   `Kind` matches `reference.SourceKind` (a dictionary lookup built in the ctor)
   and calls `DownloadAsync`. The rest of the pipeline
   (`ExistsByArticleAndUrlAsync` dedup, `IMediaStorage.UploadAsync`,
   `IMediaFileRepository.AddAsync`, logging) is unchanged.
4. `TelegramParser.ParseAsync` is extended: for each returned `Message`, if
   `msg.media` is a `MessageMediaPhoto` or a `MessageMediaDocument` with a
   `video/*` or `image/*` mime type, produce one `MediaReference` with
   `SourceKind = Telegram`, `Url = $"https://t.me/{username}/{msg.id}#{mediaIndex}"`
   (stable logical URL used only for per-article dedup), `ExternalHandle`
   encoding the handle described above, and `Kind` set from the media type.
   Grouped Telegram albums (the same `grouped_id`) already arrive as separate
   `Message` objects in `Messages_GetHistory` and are each handled individually.

**Pros:**
- **`Core/` stays pure.** No byte arrays on domain models, no WTelegram types
  leak. `MediaReference` gains only primitives and one new Core enum.
- **Streaming preserved.** Telegram media bytes are downloaded one at a time
  inside `MediaIngestionService`, never held across the parser→worker
  boundary. Memory profile matches RSS.
- **No contract change on `ISourceParser`.** Only `TelegramParser`'s body is
  touched; `RssParser` keeps working unchanged.
- **Single ingestion policy.** `MaxFileSizeBytes`,
  `DownloadTimeoutSeconds`, the `image/*` + `video/*` allowlist, the
  per-article dedup in `ExistsByArticleAndUrlAsync`, the `Warning`-and-continue
  error handling, the R2 key format `articles/{articleId}/{mediaId}{ext}` —
  all stay in one place (`MediaIngestionService`) and automatically apply to
  Telegram. This is the "preserve the pipeline, swap the download step"
  property ADR 0006 aimed for.
- **Matches the existing keyed-parser pattern.** `InfrastructureService­
  Extensions.AddParsers` already registers multiple `ISourceParser`
  implementations and `SourceFetcherWorker` keys them by `SourceType`. The
  same idiom applied to `IMediaContentDownloader` is consistent, not novel.
- **Pluggable.** A future third source type (e.g. Mastodon API media) adds one
  new `IMediaContentDownloader` implementation — zero changes to
  `MediaIngestionService`.

**Cons:**
- More moving parts than Option 1: one new Core interface, one new DTO, two
  downloader classes, one small refactor inside `MediaIngestionService`.
- Requires a public `DownloadMediaAsync` on the singleton
  `TelegramClientService`, which slightly broadens its surface.

### Option 3 — Download Telegram media directly from `TelegramParser` and bypass `IMediaIngestionService` for Telegram articles

`TelegramParser` depends on `IMediaStorage` + `IMediaFileRepository` directly
(both already in Core). For each message with media, it downloads via
`TelegramClientService`, uploads to R2, persists the `MediaFile` row — all
inside `ParseAsync`, never populating `Article.MediaReferences`.
`SourceFetcherWorker`'s post-save `IngestForArticleAsync` call is a no-op for
Telegram because references are empty.

**Pros:**
- Zero change to `MediaIngestionService`.

**Cons:**
- **Order-of-operations bug.** Media rows would be written to the DB via
  `IMediaFileRepository.AddAsync` **before** the parent `Article` row exists
  (the article is saved later inside `SourceFetcherWorker.ProcessSourceAsync`
  after dedup checks). The FK `media_files.article_id → articles.id` would
  fail. Working around this requires either deferring the media writes
  (reinventing Option 2's queue) or adding media *after* dedup — but then the
  download work done in the parser is wasted for skipped articles.
- Parser now has three dependencies (`IMediaStorage`,
  `IMediaFileRepository`, `TelegramClientService`) and does upload +
  persistence — massively broader responsibility than `RssParser`. Breaks
  the parser symmetry that ADR 0006 was careful to preserve.
- Two completely different code paths for RSS and Telegram media. Any future
  ingestion policy change (e.g. a new size cap) would have to be made in two
  places.
- Violates the `code-conventions` Worker rule indirectly: the parser becomes
  a de-facto ingestion pipeline with its own failure semantics, duplicating
  what `MediaIngestionService` already enforces.

## Decision

**Adopt Option 2.** Extend `MediaReference` with a source discriminator,
introduce a keyed `IMediaContentDownloader` abstraction, refactor
`MediaIngestionService` to dispatch through it, add a `TelegramMediaContentDownloader`
that calls a new `TelegramClientService.DownloadMediaAsync`, and populate
`MediaReferences` from inside `TelegramParser`.

### 1. Core domain changes

**`Core/DomainModels/MediaReference.cs`** — extend the record. The two new
fields default so existing call sites (RSS) continue compiling without
modification:

```csharp
public record MediaReference(
    string Url,
    MediaKind Kind,
    string? DeclaredContentType,
    MediaSourceKind SourceKind = MediaSourceKind.Http,
    string? ExternalHandle = null);

public enum MediaSourceKind
{
    Http,
    Telegram
}
```

`Url` semantics are preserved: it remains the per-article dedup key used by
`IMediaFileRepository.ExistsByArticleAndUrlAsync`. For Telegram it is the
t.me deep link plus a media index suffix so multiple media items on the same
message (albums) produce distinct dedup keys. `Url` is never downloaded when
`SourceKind == Telegram`.

**`Core/Interfaces/Storage/IMediaContentDownloader.cs`** (new):

```csharp
public interface IMediaContentDownloader
{
    MediaSourceKind Kind { get; }

    Task<MediaDownloadResult?> DownloadAsync(
        MediaReference reference,
        CancellationToken cancellationToken = default);
}

public sealed record MediaDownloadResult(
    Stream Content,
    string ContentType,
    long SizeBytes);
```

Contract: `DownloadAsync` returns `null` when the download is rejected by the
downloader itself (404, oversize, unsupported mime) — the caller logs a
warning and continues. Throws only on truly unexpected errors, which the
outer `try/catch` in `MediaIngestionService.IngestForArticleAsync` already
catches. `MediaDownloadResult.Content` is a `MemoryStream` at position 0,
owned by the caller, which disposes it after upload.

`Core` does not know about `HttpClient` or `WTelegram`. Both implementations
live in `Infrastructure`.

### 2. Refactor `MediaIngestionService` to use keyed downloaders

Constructor gains `IEnumerable<IMediaContentDownloader> downloaders`, which is
materialised into `Dictionary<MediaSourceKind, IMediaContentDownloader>` in
the ctor body (matching how `SourceFetcherWorker` keys parsers by
`SourceType`).

`IngestSingleReferenceAsync` is restructured:

```csharp
private async Task IngestSingleReferenceAsync(
    Guid articleId,
    MediaReference reference,
    CancellationToken cancellationToken)
{
    if (await repository.ExistsByArticleAndUrlAsync(articleId, reference.Url, cancellationToken))
        return;

    if (!_downloaders.TryGetValue(reference.SourceKind, out var downloader))
    {
        logger.LogWarning("No downloader registered for media source kind {Kind}", reference.SourceKind);
        return;
    }

    var download = await downloader.DownloadAsync(reference, cancellationToken);
    if (download is null)
        return;

    try
    {
        if (download.SizeBytes > _options.MaxFileSizeBytes)
        {
            logger.LogWarning("Media {Url} exceeds size limit ({Size} bytes)", reference.Url, download.SizeBytes);
            return;
        }

        var kind = download.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            ? MediaKind.Video
            : MediaKind.Image;

        var ext = ResolveExtension(reference, download.ContentType);
        var r2Key = $"articles/{articleId}/{Guid.NewGuid()}{ext}";

        download.Content.Position = 0;
        await storage.UploadAsync(r2Key, download.Content, download.ContentType, cancellationToken);

        var mediaFile = new MediaFile
        {
            Id = Guid.NewGuid(),
            ArticleId = articleId,
            R2Key = r2Key,
            OriginalUrl = reference.Url,
            ContentType = download.ContentType,
            SizeBytes = download.SizeBytes,
            Kind = kind,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await repository.AddAsync(mediaFile, cancellationToken);
        logger.LogInformation("Ingested media {Url} for article {ArticleId} as {R2Key}", reference.Url, articleId, r2Key);
    }
    finally
    {
        await download.Content.DisposeAsync();
    }
}
```

Note: the `MaxFileSizeBytes` enforcement, the content-type allowlist, and the
r2-key format are **still owned by `MediaIngestionService`**, not by the
downloaders. Downloaders are dumb byte producers. This is what keeps a
single ingestion policy across RSS and Telegram.

The empty-list short-circuit, the intra-batch URL dedup, the outer/inner
`try/catch` structure, and the `IReadOnlyList<MediaReference> references`
shape are unchanged.

### 3. `HttpMediaContentDownloader` (Infrastructure/Storage/ or Services/)

Thin wrapper that pulls all of the current HTTP logic out of
`MediaIngestionService`:
- Uses `IHttpClientFactory.CreateClient("MediaDownloader")`.
- `GetAsync(reference.Url, HttpCompletionOption.ResponseHeadersRead, ct)`.
- Rejects non-2xx, over-`ContentLength`, and content that cannot be classified
  into `image/*` or `video/*` via `ResolveContentType` (header → declared →
  extension fallback). Rejection = return `null`, log `Warning`.
- Streams into a `MemoryStream`, returns `new MediaDownloadResult(stream,
  contentType, stream.Length)`.

`Kind => MediaSourceKind.Http`. Registered scoped (matches existing
`MediaIngestionService` lifetime).

### 4. `TelegramMediaContentDownloader` (Infrastructure/Services/)

```csharp
public class TelegramMediaContentDownloader(
    TelegramClientService telegramClient,
    ILogger<TelegramMediaContentDownloader> logger) : IMediaContentDownloader
{
    public MediaSourceKind Kind => MediaSourceKind.Telegram;

    public async Task<MediaDownloadResult?> DownloadAsync(
        MediaReference reference,
        CancellationToken cancellationToken = default) { ... }
}
```

Behaviour:
- If `telegramClient.IsReady` is `false`, log `Warning` and return `null`
  (matches the parser's own `IsReady` guard).
- If `reference.ExternalHandle` is `null` or cannot be parsed, log `Warning`
  and return `null`.
- Delegate to `telegramClient.DownloadMediaAsync(reference.ExternalHandle,
  destination, cancellationToken)`, which returns
  `(string contentType, long sizeBytes)` or `null`.
- Wrap in a `MemoryStream`, return the `MediaDownloadResult`.

Registered as `AddScoped<IMediaContentDownloader, TelegramMediaContentDownloader>()`.
Scoped is safe — it depends on the singleton `TelegramClientService`
(captive-dependency rule only bars scoped captured inside singleton).

### 5. `TelegramClientService.DownloadMediaAsync`

New public method on the existing singleton service:

```csharp
public async Task<TelegramMediaPayload?> DownloadMediaAsync(
    string externalHandle,
    Stream destination,
    CancellationToken cancellationToken);

public sealed record TelegramMediaPayload(string ContentType, long SizeBytes);
```

Implementation outline (Infrastructure-internal, uses `TL.*` types):

1. Parse `externalHandle` as `"{channelId}:{accessHash}:{messageId}:{mediaIndex}"`.
2. Build an `InputChannel(channelId, accessHash)`, call
   `_client.Channels_GetMessages(inputChannel, new InputMessageID { id = messageId })`.
3. Read `message.media`:
   - `MessageMediaPhoto { photo: Photo p }` → content type `"image/jpeg"`
     (Telegram always serves photos as JPEG), size from the largest
     `PhotoSize`, download via `_client.DownloadFileAsync(p, destination)`.
   - `MessageMediaDocument { document: Document d }` with
     `d.mime_type` starting with `video/` or `image/` → content type from
     `d.mime_type`, size from `d.size`, download via
     `_client.DownloadFileAsync(d, destination)`.
   - Anything else → return `null`.
4. Return `new TelegramMediaPayload(contentType, sizeBytes)`.
5. Catch `WTelegram.WTException` (file reference expired, channel
   inaccessible, etc.), log `Warning`, return `null`. Never throw out of this
   method — honour the best-effort contract.

The `mediaIndex` suffix in the handle is reserved for future album support
but, for v1, is always `0` because `Messages_GetHistory` returns each album
item as its own `Message` object.

### 6. `TelegramParser.ParseAsync` extension

After building the `Article` from `msg.message`, inspect `msg.media`:

- If `msg.media is MessageMediaPhoto { photo: Photo photo }` →
  produce one `MediaReference`:
  ```csharp
  new MediaReference(
      Url: $"https://t.me/{username}/{msg.id}#media-0",
      Kind: MediaKind.Image,
      DeclaredContentType: "image/jpeg",
      SourceKind: MediaSourceKind.Telegram,
      ExternalHandle: $"{channel.id}:{channel.access_hash}:{msg.id}:0")
  ```
- If `msg.media is MessageMediaDocument { document: Document doc }` and
  `doc.mime_type` starts with `image/` or `video/` → produce one
  `MediaReference` with `Kind` derived from the mime type prefix,
  `DeclaredContentType = doc.mime_type`, and the same handle format.
- All other media types (stickers, contact cards, polls, geo, webpage
  previews) → skip.

Because the channel handle is required for the `ExternalHandle`,
`TelegramClientService.GetChannelMessagesAsync` will return a
new `(Message message, long channelId, long channelAccessHash)` tuple shape,
or — preferred — a new Infrastructure-internal DTO:

```csharp
internal sealed record TelegramChannelMessage(
    Message Message,
    long ChannelId,
    long ChannelAccessHash);
```

`GetChannelMessagesAsync` returns `List<TelegramChannelMessage>`. The single
caller (`TelegramParser`) is updated. The DTO stays `internal` to
Infrastructure so WTelegram types do not leak anywhere else.

### 7. DI registration (`InfrastructureServiceExtensions`)

In `AddStorage` (kept cohesive with other media wiring) — or in a new tiny
`AddMediaDownloaders` helper chained into `AddInfrastructure`:

```csharp
services.AddScoped<IMediaContentDownloader, HttpMediaContentDownloader>();
services.AddScoped<IMediaContentDownloader, TelegramMediaContentDownloader>();
```

`MediaIngestionService` is already registered scoped; DI auto-injects the
`IEnumerable<IMediaContentDownloader>`.

### 8. Files affected

**New files:**
- `Core/Interfaces/Storage/IMediaContentDownloader.cs` — interface + `MediaDownloadResult` record.
- `Infrastructure/Services/HttpMediaContentDownloader.cs`
- `Infrastructure/Services/TelegramMediaContentDownloader.cs`

**Modified files:**
- `Core/DomainModels/MediaReference.cs` — add `SourceKind`, `ExternalHandle`,
  add `MediaSourceKind` enum.
- `Infrastructure/Services/MediaIngestionService.cs` — inject
  `IEnumerable<IMediaContentDownloader>`, move HTTP body into
  `HttpMediaContentDownloader`, switch to dispatcher pattern,
  keep policy (size cap, r2 key, dedup, logging).
- `Infrastructure/Services/TelegramClientService.cs` — add
  `DownloadMediaAsync` + `TelegramMediaPayload` record, change
  `GetChannelMessagesAsync` return type to
  `List<TelegramChannelMessage>` (internal DTO).
- `Infrastructure/Parsers/TelegramParser.cs` — build `MediaReference`s from
  `msg.media`, populate `Article.MediaReferences`.
- `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` — register
  both `IMediaContentDownloader` implementations.
- `Infrastructure/Parsers/RssParser.cs` — **no behaviour change**; the call
  sites that construct `new MediaReference(url, kind, declaredType)` continue
  to compile because the two new record positional parameters have defaults.
  Explicit verification: no source change needed.

**Not affected:**
- `Worker/Workers/SourceFetcherWorker.cs` — no changes. The same
  post-save `mediaIngestionService.IngestForArticleAsync(article.Id,
  article.MediaReferences, ct)` call now handles Telegram transparently.
- Database schema, migrations, `MediaFileEntity`, `MediaFileRepository`,
  `CloudflareR2Storage`, `CloudflareR2Options` — all unchanged.
- `ISourceParser` contract.

## Consequences

### Positive

- **Full parity with RSS media ingestion.** Telegram articles acquire
  `MediaReferences` at parse time and are uploaded by the same
  `MediaIngestionService` call the worker already makes. No duplication of
  ingestion policy.
- **Pluggable downloader pattern.** Adding a new source type's media
  ingestion (e.g. Mastodon) becomes a one-class change:
  implement `IMediaContentDownloader` and register it. `MediaIngestionService`
  never needs to learn about new sources.
- **Core stays pure.** No WTelegram types, no `HttpClient`, no byte[] on
  domain models. The extension to `MediaReference` is primitives only,
  defaulted so existing RSS code paths are unchanged.
- **Thread-safe.** `WTelegram.Client.DownloadFileAsync` supports concurrent
  calls; the singleton `TelegramClientService` remains safe when
  `TelegramMediaContentDownloader` is resolved into multiple concurrent
  scopes.
- **Best-effort contract preserved.** Every new component returns `null`
  rather than throwing on expected failures; `MediaIngestionService`'s
  existing inner and outer `try/catch` cover the truly unexpected cases.
- **No schema migration.** `media_files` table, entity, and repo are
  untouched.

### Negative / risks

- **`MediaReference` shape change.** A Core domain record gains two fields.
  Backwards-compatible (defaulted), but every tool that introspects the
  domain (mappers, serialization, tests) must re-verify. Impact is small
  because `MediaReference` is not persisted, is not DTO-serialized, and only
  `RssParser` constructs it today.
- **`TelegramClientService` surface grows.** A new public method and a new
  public return DTO. The singleton is now a producer of bytes, not just
  metadata. Mitigation: the download call is thin, delegates straight to
  `WTelegram.Client`, and has no state beyond the existing `_client` field.
- **FileReference expiry.** Telegram's `FileReference` bytes can expire;
  WTelegram's `Messages_GetHistory` already refreshes them, and
  `TelegramMediaContentDownloader` re-fetches the message via
  `Channels_GetMessages` each time — adding one round trip per media item.
  This is the correct trade-off for a best-effort, low-volume ingester; if
  it ever becomes a bottleneck, a future ADR can cache message lookups
  within one `GetChannelMessagesAsync` batch.
- **`ExternalHandle` is a string rather than a typed payload.** The
  `"channelId:accessHash:messageId:mediaIndex"` format is an
  Infrastructure-private convention that must be kept in sync between
  `TelegramParser` (writer) and `TelegramClientService.DownloadMediaAsync`
  (reader). Mitigation: a small internal static helper class
  `TelegramMediaHandle` with `Encode`/`TryDecode` methods lives in
  `Infrastructure/Services/` and is the single source of truth.
- **Captive-dependency inversion sensitivity.** Registering
  `TelegramMediaContentDownloader` as scoped while it depends on the
  singleton `TelegramClientService` is fine today. Flipping either lifetime
  in the future (e.g. making the Telegram client scoped) would require
  reviewing the captive rule. Documented here so the next maintainer does
  not break it unknowingly.

## Implementation Notes

### Order of changes (strict — each step builds on the previous)

1. Extend `Core/DomainModels/MediaReference.cs` with `MediaSourceKind` enum
   and the two defaulted fields. Build the solution and verify `RssParser`
   still compiles without change.
2. Add `Core/Interfaces/Storage/IMediaContentDownloader.cs` (+ `MediaDownloadResult`).
3. Extract the current HTTP body out of `MediaIngestionService` into
   `Infrastructure/Services/HttpMediaContentDownloader.cs`. Register it in
   `InfrastructureServiceExtensions`. Run `MediaIngestionServiceTests`
   (should still pass after the refactor; the test doubles replace the
   downloader instead of mocking `IHttpClientFactory`).
4. Refactor `MediaIngestionService` to the dispatcher shape (ctor takes
   `IEnumerable<IMediaContentDownloader>` and materialises it into a
   dictionary; `IngestSingleReferenceAsync` delegates). Policy concerns
   (`MaxFileSizeBytes`, r2 key format, logging) stay in the service.
   Re-run existing tests.
5. Introduce the internal `TelegramMediaHandle` helper in
   `Infrastructure/Services/` (encode + `TryDecode`).
6. Add `DownloadMediaAsync` and `TelegramMediaPayload` record to
   `TelegramClientService`. Change `GetChannelMessagesAsync` to return the
   new internal `TelegramChannelMessage` DTO so the channel handle is
   carried out to the parser.
7. Extend `TelegramParser.ParseAsync` to populate `MediaReferences` from
   `msg.media`.
8. Add `TelegramMediaContentDownloader` and register it in
   `InfrastructureServiceExtensions`. At this point the worker
   transparently ingests Telegram media.
9. Tests (delegated to `test-writer` — see below).

### Skills to follow (MUST be read by `feature-planner` and `implementer`)

- **`code-conventions`** (`.claude/skills/code-conventions/SKILL.md`) — layer
  boundaries (no WTelegram or HttpClient in `Core/`), Infrastructure service
  placement, primary-constructor style for services, worker rules remain
  off-limits because `Worker/` is not modified.
- **`ef-core-conventions`** (`.claude/skills/ef-core-conventions/SKILL.md`)
  — not expected to come into play (no repo changes), but confirm no drift
  if any `MediaFileRepository` method needs a new overload.
- **`test-writer`** (`.claude/skills/testing/SKILL.md`) — for the new tests
  below.

### Testing (delegated to `test-writer`)

- **`HttpMediaContentDownloaderTests`** — covers all the cases previously
  owned by `MediaIngestionServiceTests`' HTTP branch: 404, 2xx happy path,
  oversize via `Content-Length`, oversize after download, unsupported mime
  type rejected, header/declared/extension fallback for content type.
- **`TelegramMediaContentDownloaderTests`** — because
  `TelegramClientService` wraps a real `WTelegram.Client`, introduce a thin
  seam: either (a) extract the WTelegram surface this downloader needs
  behind a small `ITelegramMediaGateway` interface implemented by
  `TelegramClientService`, or (b) test the downloader against a stub
  `TelegramClientService` subclass. **Prefer (a)** — one more interface in
  `Core/Interfaces/` keeps the unit test simple and does not require mocking
  singleton lifecycle. Cases: `IsReady == false` returns `null`; malformed
  `ExternalHandle` returns `null`; `MessageMediaPhoto` returns `image/jpeg`
  payload; `MessageMediaDocument` video returns the declared mime type;
  unsupported media (sticker) returns `null`; `WTException` during download
  returns `null`.
- **`MediaIngestionServiceTests`** — refactor existing cases to inject stub
  `IMediaContentDownloader`s rather than mocking `IHttpClientFactory`.
  Add new case: "dispatches to the downloader whose `Kind` matches the
  reference's `SourceKind`"; "logs warning and skips when no downloader is
  registered for the kind". Preserve all existing policy assertions
  (size cap, per-article dedup, never throws).
- **`TelegramParserTests`** (new — directory exists:
  `Tests/Infrastructure.Tests/Parsers/`) — verify that
  `MessageMediaPhoto` yields a `MediaReference` with `SourceKind =
  Telegram`, `Kind = Image`, and the expected `ExternalHandle` encoding;
  verify `MessageMediaDocument` with `video/mp4` yields a video reference;
  verify unsupported media produces no reference. Uses the gateway seam
  introduced for point 2 to fake the Telegram client.
- **`SourceFetcherWorkerMediaTests`** (already exists) — extend with a
  Telegram-path scenario confirming that a Telegram article with one
  photo reference results in one `IMediaIngestionService.IngestForArticleAsync`
  call after save and that ingestion failure leaves the article saved.

### Recommended next step

Pass this ADR to **feature-planner** to produce the atomic tasklist in
`docs/tasks/active/telegram-media-extraction.md`.
