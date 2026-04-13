# Fix Media Publishing — Selected Media Files Not Sent to Telegram

## Goal

Resolve the gap where editor-selected media files on a publication are persisted but never
sent to Telegram: resolve selected media IDs to URLs in the worker, expand `IPublisher` to
accept resolved media, and update `TelegramPublisher` to dispatch `sendMessage`,
`sendPhoto`, `sendVideo`, or `sendMediaGroup` based on the resolved media list.

## Affected Layers

- Core, Infrastructure, Worker

---

## Tasks

### Core

- [x] **Create `Core/DomainModels/ResolvedMedia.cs`** — new value record with three
      properties: `string Url`, `string ContentType`, `MediaKind Kind`.
      _Acceptance: file compiles; no EF or infrastructure references; record is in the
      `Core.DomainModels` namespace._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/Interfaces/Repositories/IMediaFileRepository.cs`** — add method:
      `Task<List<MediaFile>> GetByIdsAsync(List<Guid> ids, CancellationToken cancellationToken = default);`
      _Acceptance: interface-only change; compiles; existing three methods are unchanged._
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Core/Interfaces/Publishers/IPublisher.cs`** — add `List<ResolvedMedia> media`
      parameter as the second argument to both `PublishAsync` and `PublishReplyAsync`
      (before `CancellationToken`).
      New signatures:
      - `Task<string> PublishAsync(Publication publication, List<ResolvedMedia> media, CancellationToken cancellationToken = default)`
      - `Task<string> PublishReplyAsync(Publication publication, string replyToMessageId, List<ResolvedMedia> media, CancellationToken cancellationToken = default)`
      _Acceptance: interface compiles; `Platform` property is unchanged; callers will fail to
      compile until updated in subsequent tasks — that is expected._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Infrastructure

- [x] **Modify `Infrastructure/Persistence/Repositories/MediaFileRepository.cs`** — implement
      `GetByIdsAsync`: query `context.MediaFiles.Where(m => ids.Contains(m.Id))`, call
      `.ToListAsync(cancellationToken)`, then map each entity with `.ToDomain()`.
      Follow the exact pattern of the existing `GetByArticleIdAsync` method.
      _Acceptance: class satisfies updated `IMediaFileRepository`; method returns only the
      requested IDs; compiles._
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Rewrite `Infrastructure/Publishers/TelegramPublisher.cs`** — implement media dispatch:

      **Constants** (private const fields):
      - `TelegramCaptionMaxLength = 1024`
      - `TelegramMediaGroupMaxSize = 10`
      - `TelegramPhotoMaxSizeBytes = 20 * 1024 * 1024`

      **`PublishAsync` / `PublishReplyAsync`** become dispatchers accepting `List<ResolvedMedia> media`:
      - `media` empty → `SendMessageAsync` (existing behavior, no change to that method body)
      - `media` has 1 item and `Kind == MediaKind.Image` → `SendPhotoAsync`
      - `media` has 1 item and `Kind == MediaKind.Video` → `SendVideoAsync`
      - `media` has 2–10 items → `SendMediaGroupAsync`
      - `media` has > 10 items → `SendMediaGroupAsync` with first `TelegramMediaGroupMaxSize` items

      **Caption length rule** (applies to all media paths):
      - `content.Length <= TelegramCaptionMaxLength` → send content as caption
      - `content.Length > TelegramCaptionMaxLength` → send media captionless, then call
        `SendMessageAsync` with full content as a reply to the media message; return the
        text message ID as `externalMessageId`

      **New private methods to add**:
      - `SendPhotoAsync(string channelId, string photoUrl, string caption, string? replyToMessageId, CancellationToken)` — calls `/sendPhoto`
      - `SendVideoAsync(string channelId, string videoUrl, string caption, string? replyToMessageId, CancellationToken)` — calls `/sendVideo`
      - `SendMediaGroupAsync(string channelId, List<ResolvedMedia> media, string caption, string? replyToMessageId, CancellationToken)` — calls `/sendMediaGroup` with `InputMediaPhoto`/`InputMediaVideo` array; returns first message ID from result array

      **Existing `SendMessageAsync`** — keep unchanged.

      _Acceptance: class satisfies updated `IPublisher`; `Platform` still returns
      `Platform.Telegram`; compiles; all private methods are under 20 lines each._
      _Skill: .claude/skills/clean-code/SKILL.md_

---

### Worker

- [x] **Modify `Worker/Workers/PublishingWorker.cs`** — resolve media before publishing:

      In `ProcessBatchAsync`, resolve two additional scoped services alongside the existing ones:
      - `IMediaFileRepository mediaFileRepository`
      - `IOptions<CloudflareR2Options> r2Options` (or `CloudflareR2Options` directly via
        `IOptions<CloudflareR2Options>.Value`)

      Pass both to `PublishSingleAsync` (or to the new helper described below).

      Add a `private const long MaxMediaFileSizeBytes = 20 * 1024 * 1024;` field to the
      worker class (mirrors the Telegram 20 MB photo limit; kept local because `Worker` cannot
      reference `Infrastructure` directly).

      In `ResolveAndPublishAsync` (or a new `ResolveMediaAsync` helper called before it):
      1. If `publication.SelectedMediaFileIds` is empty → use `new List<ResolvedMedia>()`.
      2. Otherwise → call `mediaFileRepository.GetByIdsAsync(publication.SelectedMediaFileIds, cancellationToken)`.
      3. Before mapping, skip any `MediaFile` where `m.SizeBytes > MaxMediaFileSizeBytes`,
         logging a warning via `_logger.LogWarning` that includes the file ID and size.
      4. Map each remaining `MediaFile m` to:
         `new ResolvedMedia($"{publicBaseUrl.TrimEnd('/')}/{m.R2Key.TrimStart('/')}", m.ContentType, m.Kind)`
         where `publicBaseUrl = r2Options.PublicBaseUrl`.

      Update both `publisher.PublishAsync(publication, resolvedMedia, cancellationToken)` and
      `publisher.PublishReplyAsync(publication, parentMessageId, resolvedMedia, cancellationToken)`
      calls to pass the resolved list.

      _Acceptance: worker compiles; both call sites pass the `List<ResolvedMedia>`; files with
      `SizeBytes > MaxMediaFileSizeBytes` are excluded from the resolved list and a warning is
      logged for each skipped file; no raw string concatenation for the URL other than the
      one-liner above; worker starts without exception in Development._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Tests

- [ ] **Modify `Tests/Infrastructure.Tests/Repositories/MediaFileRepositoryTests.cs`** — add
      tests for `GetByIdsAsync`:
      _Delegated to test-writer agent_
      - Returns only the requested IDs when multiple records exist.
      - Returns empty list when no IDs match.
      - Returns all items when all IDs match.
      _Acceptance: tests pass; AAA pattern; no `any` mocks; follows conventions in the
      existing file._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Create `Tests/Worker.Tests/Workers/PublishingWorkerTests.cs`** — unit tests for
      `PublishingWorker`:
      _Delegated to test-writer agent_
      - When `SelectedMediaFileIds` is empty, passes an empty `List<ResolvedMedia>` to the
        publisher.
      - When `SelectedMediaFileIds` is non-empty, calls `GetByIdsAsync` and constructs URLs
        using `{PublicBaseUrl}/{R2Key}` and passes the resolved list to the publisher.
      - When a media file has `SizeBytes > MaxMediaFileSizeBytes` (the worker's private const,
        equal to 20 MB), it is excluded from the resolved list passed to the publisher and a
        warning is logged.
      - When `ParentPublicationId` is set, calls `PublishReplyAsync` with the resolved media.
      _Acceptance: tests pass; `IPublisher`, `IMediaFileRepository`, and
      `IPublicationRepository` are mocked with Moq; no real DB or HTTP calls._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Create `Tests/Infrastructure.Tests/Publishers/TelegramPublisherTests.cs`** — unit
      tests for `TelegramPublisher`:
      _Delegated to test-writer agent_
      - Empty media → `sendMessage` endpoint called.
      - Single image media (`Kind == MediaKind.Image`), caption fits 1024 chars → `sendPhoto`
        called with caption.
      - Single video media (`Kind == MediaKind.Video`), caption fits 1024 chars → `sendVideo`
        called with caption.
      - Single image media, caption exceeds 1024 chars → `sendPhoto` called captionless, then
        `sendMessage` called as a reply; the returned `externalMessageId` must equal the text
        message's ID from the `sendMessage` response, NOT the photo message's ID.
      - Multiple media items (≤ 10) → `sendMediaGroup` called.
      - More than 10 media items → `sendMediaGroup` called with only first 10 items.
      Mock `HttpClient` via a test `HttpMessageHandler`.
      _Acceptance: tests pass; each scenario is a separate `[Test]` method; no real HTTP
      calls; the caption-overflow test explicitly asserts `result == textMessageId` where
      `textMessageId` is the ID returned by the mocked `sendMessage` response._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

## Open Questions

- None. The ADR is fully specified and no design decisions remain open.
