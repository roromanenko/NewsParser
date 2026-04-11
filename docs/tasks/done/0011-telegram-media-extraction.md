# Telegram Media Extraction

## Goal

Extend `TelegramClientService` and `TelegramParser` to extract photos and videos
from Telegram messages and populate `Article.MediaReferences`, so that the existing
`MediaIngestionService` pipeline transparently uploads Telegram media to Cloudflare R2
on the same best-effort contract it already applies to RSS enclosures.

## Affected Layers

- Core
- Infrastructure

## ADR Reference

`docs/architecture/decisions/0007-telegram-media-extraction.md`

---

## Tasks

### Step 1 — Core domain: extend `MediaReference` with source discriminator

- [x] **Modify `Core/DomainModels/MediaReference.cs`** — add `MediaSourceKind` enum and
      two defaulted positional parameters to the `MediaReference` record:
      ```csharp
      public record MediaReference(
          string Url,
          MediaKind Kind,
          string? DeclaredContentType,
          MediaSourceKind SourceKind = MediaSourceKind.Http,
          string? ExternalHandle = null);

      public enum MediaSourceKind { Http, Telegram }
      ```
      _Acceptance: file compiles; `RssParser.cs` and `MediaIngestionServiceTests.cs` compile
      without any source changes (defaulted parameters preserve backwards compatibility);
      `MediaSourceKind` enum is defined in the same file as `MediaReference`; no
      Infrastructure or EF references introduced_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Build verification** — run `dotnet build` from the solution root.
      _Acceptance: zero errors; `RssParser`, `TelegramParser`, `MediaIngestionService`, and
      all test projects compile without change_

---

### Step 2 — Core interface: `IMediaContentDownloader`

- [x] **Create `Core/Interfaces/Storage/IMediaContentDownloader.cs`** — new interface plus
      result record:
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
      Contract note: `DownloadAsync` returns `null` on expected rejection (404, oversize,
      unsupported mime, not-ready). `Content` is a `MemoryStream` at position 0, owned by
      the caller, disposed after upload. No WTelegram or `HttpClient` references in this file.
      _Acceptance: file compiles; namespace is `Core.Interfaces.Storage`; `CancellationToken`
      has a default value; no Infrastructure references_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step 3 — Infrastructure: `HttpMediaContentDownloader`

- [x] **Create `Infrastructure/Services/HttpMediaContentDownloader.cs`** — extract the entire
      HTTP download body from `MediaIngestionService.IngestSingleReferenceAsync` into this
      new class:
      - Primary-constructor style: `IHttpClientFactory httpClientFactory`,
        `IOptions<CloudflareR2Options> options`, `ILogger<HttpMediaContentDownloader> logger`.
      - `Kind => MediaSourceKind.Http`.
      - `DownloadAsync`: creates the named client `"MediaDownloader"`, calls
        `GetAsync(reference.Url, HttpCompletionOption.ResponseHeadersRead, ct)`. Returns
        `null` on non-2xx, `Content-Length` over limit, stream length over limit, or
        unresolvable/unsupported content type. On success returns
        `new MediaDownloadResult(stream, contentType, stream.Length)`.
      - Move `ResolveContentType`, `IsAllowedMimeType`, `ExtensionToMime`, and
        `AllowedMimeRoots` from `MediaIngestionService` into this class (they are used only
        by the HTTP path). `ResolveContentType` signature becomes
        `private string? ResolveContentType(HttpResponseMessage response, MediaReference ref)`.
      _Acceptance: file compiles; implements `IMediaContentDownloader`; `Kind` returns
      `MediaSourceKind.Http`; all HTTP-specific logic is self-contained here; no
      WTelegram references; no R2 upload or repository calls_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step 4 — Infrastructure: refactor `MediaIngestionService` to dispatcher pattern

- [x] **Modify `Infrastructure/Services/MediaIngestionService.cs`** — replace the
      `IHttpClientFactory` constructor parameter with
      `IEnumerable<IMediaContentDownloader> downloaders`. Materialise it into
      `Dictionary<MediaSourceKind, IMediaContentDownloader> _downloaders` in the
      constructor body. Rewrite `IngestSingleReferenceAsync` to:
      1. Check `ExistsByArticleAndUrlAsync` (unchanged).
      2. Lookup `_downloaders[reference.SourceKind]`; log `Warning` and return if missing.
      3. Call `downloader.DownloadAsync(reference, ct)`; return if `null`.
      4. Enforce `MaxFileSizeBytes` against `download.SizeBytes`.
      5. Derive `kind` and `ext` from `download.ContentType`.
      6. Build `r2Key`, call `storage.UploadAsync`, call `repository.AddAsync`, log success.
      7. Dispose `download.Content` in `finally`.
      Remove `ResolveContentType`, `IsAllowedMimeType`, `ExtensionToMime`,
      `AllowedMimeRoots`, and the `IHttpClientFactory` field — all moved to
      `HttpMediaContentDownloader`. Keep the outer/inner `try/catch` structure, the
      empty-list short-circuit, and the intra-batch URL dedup unchanged.
      _Acceptance: file compiles; constructor no longer takes `IHttpClientFactory`; `_options`
      field still holds `CloudflareR2Options` (for `MaxFileSizeBytes`); no HTTP or WTelegram
      references in this file; policy (size cap, r2 key, dedup) is still owned here_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Build verification** — run `dotnet build` from the solution root.
      _Acceptance: zero errors across all projects_

---

### Step 5 — Infrastructure: `TelegramMediaHandle` internal helper

- [x] **Create `Infrastructure/Services/TelegramMediaHandle.cs`** — `internal static class`
      with `Encode` and `TryDecode` methods that are the single source of truth for the
      handle string format `"{channelId}:{accessHash}:{messageId}:{mediaIndex}"`:
      ```csharp
      internal static class TelegramMediaHandle
      {
          public static string Encode(long channelId, long accessHash, int messageId, int mediaIndex = 0)
              => $"{channelId}:{accessHash}:{messageId}:{mediaIndex}";

          public static bool TryDecode(
              string? handle,
              out long channelId,
              out long accessHash,
              out int messageId,
              out int mediaIndex) { ... }
      }
      ```
      `TryDecode` splits on `':'`, parses four segments with `long.TryParse` /
      `int.TryParse`, sets all `out` params to 0 and returns `false` on any failure.
      _Acceptance: file compiles; class is `internal static`; namespace is
      `Infrastructure.Services`; no WTelegram, HttpClient, or Core Interfaces references;
      `TryDecode` never throws_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step 6 — Infrastructure: extend `TelegramClientService`

- [x] **Modify `Infrastructure/Services/TelegramClientService.cs`** — make three changes:

      **6a — New internal DTO.** Add `internal sealed record TelegramChannelMessage` inside
      the `Infrastructure.Services` namespace (same file or a companion file
      `Infrastructure/Services/TelegramChannelMessage.cs`):
      ```csharp
      internal sealed record TelegramChannelMessage(
          TL.Message Message,
          long ChannelId,
          long ChannelAccessHash);
      ```

      **6b — Change `GetChannelMessagesAsync` return type** from `List<Message>` to
      `List<TelegramChannelMessage>`. Update the method body to project each `Message` into
      `new TelegramChannelMessage(msg, channel.id, channel.access_hash)`. The logic for
      `_lastMessageIds`, `min_id`, and `limit: 100` is unchanged.

      **6c — Add `DownloadMediaAsync`.** New public method and payload record:
      ```csharp
      public sealed record TelegramMediaPayload(string ContentType, long SizeBytes);

      public async Task<TelegramMediaPayload?> DownloadMediaAsync(
          string externalHandle,
          Stream destination,
          CancellationToken cancellationToken = default) { ... }
      ```
      Implementation:
      1. Guard `_client is null` → return `null`.
      2. Call `TelegramMediaHandle.TryDecode(externalHandle, ...)` → return `null` on failure.
      3. Build `InputChannel(channelId, accessHash)`, call
         `_client.Channels_GetMessages(inputChannel, new InputMessageID { id = messageId })`.
      4. Match `message.media`:
         - `MessageMediaPhoto { photo: Photo p }` → `contentType = "image/jpeg"`,
           `sizeBytes` = largest `PhotoSize` width×height approximation or `p.sizes.Last()`,
           download via `_client.DownloadFileAsync(p, destination)`.
         - `MessageMediaDocument { document: Document d }` where `d.mime_type` starts with
           `"image/"` or `"video/"` → `contentType = d.mime_type`, `sizeBytes = d.size`,
           download via `_client.DownloadFileAsync(d, destination)`.
         - Anything else → return `null`.
      5. Return `new TelegramMediaPayload(contentType, sizeBytes)`.
      6. Catch `WTelegram.WTException` + general `Exception`, log `Warning`, return `null`.
         Never throw out of this method.
      _Acceptance: file compiles; `GetChannelMessagesAsync` returns
      `List<TelegramChannelMessage>`; `DownloadMediaAsync` never throws; `TelegramMediaPayload`
      is a `public sealed record`; no new fields on the class; `IsReady` guard is respected_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Build verification** — run `dotnet build` from the solution root.
      _Acceptance: zero errors; the single caller of `GetChannelMessagesAsync`
      (`TelegramParser`) will fail to compile here — that is expected and is fixed in Step 7_

---

### Step 7 — Infrastructure: extend `TelegramParser`

- [x] **Modify `Infrastructure/Parsers/TelegramParser.cs`** — update the parser to consume
      `List<TelegramChannelMessage>` from `GetChannelMessagesAsync` and populate
      `Article.MediaReferences`:

      1. Change the `messages` variable from `List<Message>` to
         `List<TelegramChannelMessage>`.
      2. Replace the `messages.Select(msg => ...)` projection: iterate over
         `TelegramChannelMessage item` using the exposed `item.Message` and
         `item.ChannelId` / `item.ChannelAccessHash` fields.
      3. After building the `Article`, add media reference extraction:
         ```csharp
         var mediaRefs = ExtractMediaReferences(item, username);
         article.MediaReferences = mediaRefs;
         ```
      4. Add private static `List<MediaReference> ExtractMediaReferences(TelegramChannelMessage item, string username)`:
         - `MessageMediaPhoto { photo: TL.Photo photo }` → one `MediaReference`:
           `Url = $"https://t.me/{username}/{item.Message.id}#media-0"`,
           `Kind = MediaKind.Image`,
           `DeclaredContentType = "image/jpeg"`,
           `SourceKind = MediaSourceKind.Telegram`,
           `ExternalHandle = TelegramMediaHandle.Encode(item.ChannelId, item.ChannelAccessHash, item.Message.id, 0)`.
         - `MessageMediaDocument { document: TL.Document doc }` where `doc.mime_type` starts
           with `"image/"` or `"video/"` → one `MediaReference` with `Kind` derived from
           mime prefix, `DeclaredContentType = doc.mime_type`, same handle format.
         - All other media (stickers, polls, geo, webpage preview, contact) → skip.
         - Returns empty list when `item.Message.media` is null or unmatched.
      _Acceptance: file compiles; articles with no media have `MediaReferences = []`; the
      `OriginalUrl` field on `Article` is unchanged (`https://t.me/{username}/{msg.id}`);
      the new `#media-0` suffix is only on `MediaReference.Url`, not on `Article.OriginalUrl`;
      no WTelegram types leak to `Core`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Build verification** — run `dotnet build` from the solution root.
      _Acceptance: zero errors across all projects_

---

### Step 8 — Infrastructure: `TelegramMediaContentDownloader` + DI registration

- [x] **Create `Infrastructure/Services/TelegramMediaContentDownloader.cs`** — scoped
      implementation of `IMediaContentDownloader` for Telegram:
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
      Implementation of `DownloadAsync`:
      1. If `!telegramClient.IsReady` → log `Warning`, return `null`.
      2. If `reference.ExternalHandle` is null → log `Warning`, return `null`.
      3. Create a `MemoryStream destination`.
      4. Call `await telegramClient.DownloadMediaAsync(reference.ExternalHandle, destination, ct)`.
      5. If result is `null` → return `null`.
      6. Set `destination.Position = 0`.
      7. Return `new MediaDownloadResult(destination, result.ContentType, result.SizeBytes)`.
      _Acceptance: file compiles; `Kind` returns `MediaSourceKind.Telegram`; no `HttpClient`
      or WTelegram types referenced directly (delegates to `TelegramClientService`);
      `DownloadAsync` never throws_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — register
      both `IMediaContentDownloader` implementations. In `AddStorage`, append:
      ```csharp
      services.AddScoped<IMediaContentDownloader, HttpMediaContentDownloader>();
      services.AddScoped<IMediaContentDownloader, TelegramMediaContentDownloader>();
      ```
      Also remove the `IHttpClientFactory` argument from `MediaIngestionService`'s
      registration (it is now resolved via `IEnumerable<IMediaContentDownloader>` — the DI
      container auto-injects the `IEnumerable` from the two scoped registrations above).
      _Acceptance: project builds; `IEnumerable<IMediaContentDownloader>` resolves two
      items from DI; existing `MediaIngestionService` scoped registration in `AddServices`
      is unchanged except for the removed `IHttpClientFactory` dependency; the named client
      `"MediaDownloader"` registration in `AddStorage` remains (still used by
      `HttpMediaContentDownloader`)_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Build verification** — run `dotnet build` from the solution root.
      _Acceptance: zero errors and zero warnings across all projects; the end-to-end wiring
      is complete at this point_

---

### Step 9 — Tests (delegated to test-writer agent)

- [x] **Create `Tests/Infrastructure.Tests/Services/HttpMediaContentDownloaderTests.cs`** —
      unit tests covering every HTTP branch now owned by `HttpMediaContentDownloader`:
      - 404 response → `null` returned, no throw.
      - 200 with `image/jpeg` content type → `MediaDownloadResult` with correct `ContentType`
        and non-zero `SizeBytes`.
      - `Content-Length` header exceeds `MaxFileSizeBytes` → `null`.
      - Actual downloaded bytes exceed `MaxFileSizeBytes` → `null`.
      - Unsupported mime type (`application/pdf`) → `null`.
      - Content type resolved from `DeclaredContentType` fallback when header is absent.
      - Content type resolved from URL extension fallback when both header and declared are null.
      Uses `Mock<HttpMessageHandler>` (same pattern as existing `MediaIngestionServiceTests`).
      _Acceptance: all tests pass; no live HTTP calls; mocks `IHttpClientFactory`_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Modify `Tests/Infrastructure.Tests/Services/MediaIngestionServiceTests.cs`** —
      refactor existing tests to inject stub `IMediaContentDownloader`s instead of mocking
      `IHttpClientFactory`. Update `SetUp`: remove `_httpClientFactoryMock` and
      `_httpHandlerMock`; add `Mock<IMediaContentDownloader> _httpDownloaderMock` with
      `Kind = MediaSourceKind.Http`. Update `_sut` constructor call to pass
      `[_httpDownloaderMock.Object]`. Add two new test cases:
      - "Dispatches to the downloader whose `Kind` matches `reference.SourceKind`":
        register both an Http and a Telegram stub downloader; provide one reference of each
        kind; verify each stub's `DownloadAsync` was called exactly once.
      - "Logs warning and skips when no downloader is registered for the kind":
        provide a reference with a `SourceKind` not covered by any registered downloader;
        verify `storage.UploadAsync` is never called and no exception is thrown.
      Preserve all existing policy assertions (size cap checked in service, dedup, never
      throws, empty list early-return).
      _Acceptance: all pre-existing and new tests pass; `IHttpClientFactory` is no longer
      referenced in this test file_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Create `Tests/Infrastructure.Tests/Services/TelegramMediaContentDownloaderTests.cs`** —
      unit tests for `TelegramMediaContentDownloader`. Use a hand-rolled stub or
      `Mock<TelegramClientService>` (sealed class — prefer a thin `ITelegramMediaGateway`
      seam as described in ADR §Testing if `TelegramClientService` cannot be mocked directly;
      otherwise use `Moq` with `CallBase = false` on a partial mock). Cases:
      - `IsReady == false` → `DownloadAsync` returns `null`.
      - `reference.ExternalHandle == null` → `DownloadAsync` returns `null`.
      - Malformed handle (`TelegramMediaHandle.TryDecode` returns false) → `null`.
      - `telegramClient.DownloadMediaAsync` returns `TelegramMediaPayload("image/jpeg", 1024)`
        → `DownloadAsync` returns `MediaDownloadResult` with matching `ContentType` and
        `SizeBytes`, `Content.Position == 0`.
      - `telegramClient.DownloadMediaAsync` returns `null` → `DownloadAsync` returns `null`.
      - `DownloadAsync` never throws even when the inner call throws.
      _Acceptance: all tests pass; no live Telegram or network calls_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Create `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs`** — unit tests
      for the media extraction added to `TelegramParser.ParseAsync`. Fake the Telegram
      client using the same seam used in `TelegramMediaContentDownloaderTests`. Cases:
      - `MessageMediaPhoto` on a message → parsed article has one `MediaReference` with
        `SourceKind = Telegram`, `Kind = Image`, `DeclaredContentType = "image/jpeg"`,
        `ExternalHandle` matching `TelegramMediaHandle.Encode(channelId, accessHash, msgId, 0)`,
        `Url = $"https://t.me/{username}/{msgId}#media-0"`.
      - `MessageMediaDocument` with `mime_type = "video/mp4"` → one `MediaReference` with
        `Kind = Video`, `DeclaredContentType = "video/mp4"`.
      - `MessageMediaDocument` with `mime_type = "application/octet-stream"` → `MediaReferences`
        is empty.
      - Message with no `media` field → `MediaReferences` is empty.
      - Sticker (`MessageMediaDocument` with `mime_type = "image/webp"`) — note: stickers
        have `DocumentAttributeSticker` in `attributes`; the parser may rely solely on mime
        type; document the chosen behaviour in the test name.
      - `Article.OriginalUrl` must be `https://t.me/{username}/{msgId}` (without `#media-0`).
      _Acceptance: all tests pass; no live WTelegram calls_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Modify `Tests/Worker.Tests/Workers/SourceFetcherWorkerMediaTests.cs`** (file exists)
      — add or extend a Telegram-path scenario:
      - A Telegram article with one `MediaReference` (SourceKind = Telegram) arrives at the
        worker; after the article is saved, `IMediaIngestionService.IngestForArticleAsync`
        is called once with that article's `Id` and `MediaReferences`.
      - When `IngestForArticleAsync` throws for a Telegram article, the article remains saved
        and the loop continues (same best-effort assertion already present for the RSS path).
      _Acceptance: all tests in the file pass; `IMediaIngestionService` is mocked_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

## Open Questions

- None. The ADR fully specifies interface signatures, implementation behaviour, DI wiring,
  handle encoding format, and test scope.
