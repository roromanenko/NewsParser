# Publish Selected Media to Telegram

## Status

Proposed

## Context

The publication pipeline redesign (ADR 0010) introduced `SelectedMediaFileIds` on the `Publication` domain model, allowing editors to select media files from an event's articles before sending a publication. The UI correctly saves selected media IDs via `PUT /publications/{id}/content`, and they are persisted as a JSONB array on the `publications` table via `UpdateContentAndMediaAsync`.

However, the selected media is never actually sent to the target platform. The `PublishingWorker` picks up `Approved` publications and passes them to the `IPublisher` implementation, but:

1. **`TelegramPublisher`** only calls the Telegram Bot API's `sendMessage` endpoint, which sends text only. It reads `publication.GeneratedContent` and `publication.PublishTarget.Identifier` but completely ignores `publication.SelectedMediaFileIds`.

2. **`IPublisher` interface** defines `PublishAsync(Publication, CancellationToken)` and `PublishReplyAsync(Publication, string, CancellationToken)`. The `Publication` domain model carries `SelectedMediaFileIds` (a `List<Guid>`), but the publisher has no way to resolve those IDs into actual media URLs because:
   - `IMediaFileRepository` has no `GetByIdsAsync` method to load `MediaFile` objects by a list of IDs.
   - Even if the publisher had `MediaFile` objects, it would need the R2 public base URL to construct download URLs.

3. **`PublishingWorker.GetPendingForPublishAsync`** loads publications with `Include(p => p.PublishTarget).Include(p => p.Article)` but does not resolve media files. The `SelectedMediaFileIds` column values are loaded (they are a direct property, not a navigation), but they remain unresolved GUIDs.

4. **`IMediaStorage`** only exposes `UploadAsync` -- there is no download method. However, media files are stored in Cloudflare R2 with a public base URL (`CloudflareR2Options.PublicBaseUrl`), so the publisher can reference media via `{PublicBaseUrl}/{R2Key}` URLs. The Telegram Bot API `sendPhoto` and `sendMediaGroup` endpoints accept URLs directly.

### Summary of the gap

The data is there (editor selections are persisted), but the publishing pipeline ignores it completely. The fix requires: resolving media file IDs to URLs, and calling the appropriate Telegram API method depending on whether media is present.

## Options

### Option 1 -- Resolve media in the worker, pass resolved URLs to the publisher

The `PublishingWorker` resolves `SelectedMediaFileIds` into a list of media URLs (using `IMediaFileRepository` + `CloudflareR2Options.PublicBaseUrl`) before calling the publisher. A new type (e.g., `ResolvedMedia`) carries the URL, content type, and kind. The `IPublisher` interface gains a new method or the existing methods accept an additional `List<ResolvedMedia>` parameter.

**Pros:** Publisher stays focused on platform I/O; media resolution is a single DB query shared across all publisher implementations; consistent with the worker pattern of preparing data before delegation.
**Cons:** Changes the `IPublisher` interface (affects all implementations); worker takes on media resolution responsibility.

### Option 2 -- Publisher resolves media internally via injected dependencies

Inject `IMediaFileRepository` and `CloudflareR2Options` into `TelegramPublisher`. The publisher reads `publication.SelectedMediaFileIds`, queries the DB, constructs URLs, and calls the correct Telegram API method.

**Pros:** Self-contained change in one class; no interface change needed.
**Cons:** Publisher gains DB access (breaks the pattern of publishers doing only platform I/O); couples publisher to R2 storage details; other future publishers would duplicate this resolution logic.

### Option 3 -- Add resolved media files to the Publication domain model

Extend `Publication` with a `List<MediaFile> SelectedMediaFiles` navigation-like property populated by the repository or worker. The publisher reads from this hydrated list.

**Pros:** Clean domain model; publisher has everything it needs.
**Cons:** `SelectedMediaFileIds` is a JSONB column, not an EF Core FK relationship -- EF cannot auto-include it; requires manual hydration in the repository or worker anyway; adds a "computed" property to the domain model that is only populated in certain flows.

## Decision

**Option 1 -- Resolve media in the worker, pass resolved URLs to the publisher.**

This aligns with the project's established patterns: workers prepare data, publishers handle platform I/O. The resolution logic lives once in the worker and benefits all future publisher implementations.

### Detailed Design

#### 1. New `ResolvedMedia` record in Core

**`Core/DomainModels/ResolvedMedia.cs`** (new):

```csharp
namespace Core.DomainModels;

public record ResolvedMedia(string Url, string ContentType, MediaKind Kind);
```

A simple value type carrying everything a publisher needs to send a media item. No infrastructure dependencies.

#### 2. Add `GetByIdsAsync` to `IMediaFileRepository`

**`Core/Interfaces/Repositories/IMediaFileRepository.cs`** -- add:

```csharp
Task<List<MediaFile>> GetByIdsAsync(List<Guid> ids, CancellationToken cancellationToken = default);
```

**`Infrastructure/Persistence/Repositories/MediaFileRepository.cs`** -- implement:

```csharp
public async Task<List<MediaFile>> GetByIdsAsync(List<Guid> ids, CancellationToken cancellationToken = default)
{
    var entities = await context.MediaFiles
        .Where(m => ids.Contains(m.Id))
        .ToListAsync(cancellationToken);

    return entities.Select(e => e.ToDomain()).ToList();
}
```

Follows the existing `GetByArticleIdAsync` pattern exactly.

#### 3. Expand `IPublisher` interface

**`Core/Interfaces/Publishers/IPublisher.cs`** -- change both methods to accept resolved media:

```csharp
public interface IPublisher
{
    Platform Platform { get; }

    Task<string> PublishAsync(
        Publication publication,
        List<ResolvedMedia> media,
        CancellationToken cancellationToken = default);

    Task<string> PublishReplyAsync(
        Publication publication,
        string replyToMessageId,
        List<ResolvedMedia> media,
        CancellationToken cancellationToken = default);
}
```

The `media` list may be empty, in which case the publisher falls back to text-only behavior (current behavior).

#### 4. Update `TelegramPublisher` to handle media

**`Infrastructure/Publishers/TelegramPublisher.cs`** -- the core change:

- When `media` is empty: call `sendMessage` (current behavior, unchanged).
- When `media` has exactly one image: call `sendPhoto` with the text as `caption` (Telegram supports captions up to 1024 chars on photos; if text exceeds 1024 chars, send the photo first then the text as a separate `sendMessage`).
- When `media` has multiple items: call `sendMediaGroup` with the text as `caption` on the first item (same 1024-char limit applies; if exceeded, send the media group captionless then a separate `sendMessage` with the full text).

The Telegram Bot API accepts URLs in the `photo` / `video` field of `sendPhoto`, `sendVideo`, and `sendMediaGroup`. Since R2 media is publicly accessible via `PublicBaseUrl`, no file upload is needed.

Methods to add:
- `SendPhotoAsync(string channelId, string photoUrl, string caption, string? replyToMessageId, CancellationToken)` -- calls `/sendPhoto`.
- `SendMediaGroupAsync(string channelId, List<ResolvedMedia> media, string caption, string? replyToMessageId, CancellationToken)` -- calls `/sendMediaGroup` with an `InputMediaPhoto`/`InputMediaVideo` array.

The `PublishAsync` and `PublishReplyAsync` methods become dispatchers:
1. If `media` is empty -> `SendMessageAsync` (existing).
2. If `media` has 1 image and caption fits 1024 chars -> `SendPhotoAsync`.
3. If `media` has 1 video -> `SendVideoAsync` (or `SendDocumentAsync` as fallback).
4. If `media` has 2-10 items -> `SendMediaGroupAsync` (Telegram limit is 10 items per group).
5. If `media` has >10 items -> `SendMediaGroupAsync` with first 10 (Telegram's hard limit).

In cases 2-5, if the generated content exceeds Telegram's caption limit (1024 chars), send the media first (captionless or with a truncated caption), then follow up with a full `sendMessage`.

The return value (`externalMessageId`) should be the message ID of the primary message (the one with the text content). For media groups, Telegram returns an array of message objects -- return the first one's ID.

#### 5. Update `PublishingWorker` to resolve media

**`Worker/Workers/PublishingWorker.cs`**:

- Resolve `IMediaFileRepository` and `CloudflareR2Options` in `ProcessBatchAsync` (via the scope, following the worker pattern).
- In `PublishSingleAsync`, after selecting the publisher:
  1. If `publication.SelectedMediaFileIds` is empty, pass an empty `List<ResolvedMedia>` to the publisher.
  2. Otherwise, call `IMediaFileRepository.GetByIdsAsync(publication.SelectedMediaFileIds)`.
  3. Construct `ResolvedMedia` instances: `new ResolvedMedia($"{publicBaseUrl.TrimEnd('/')}/{m.R2Key.TrimStart('/')}", m.ContentType, m.Kind)`.
  4. Pass the list to `publisher.PublishAsync` or `publisher.PublishReplyAsync`.

The URL construction pattern (`{publicBaseUrl}/{r2Key}`) is identical to `MediaFileMapper.BuildUrl` in `Api/Mappers/MediaFileMapper.cs`. To avoid duplication, extract a shared static helper. However, the Api mapper is in the Api layer and the worker is in the Worker layer -- neither can reference the other. Two options:
- Duplicate the one-liner (acceptable for a single string interpolation).
- Move the helper to a shared location in Core (e.g., a static method on `MediaFile` or a utility class).

Given the project's DRY boundaries (the code-conventions skill explicitly allows relaxed DRY for simple cases), duplicating the one-line URL builder in the worker is acceptable and avoids adding shared infrastructure for a trivial expression.

#### 6. Telegram caption length handling

Telegram's caption limit is 1024 characters for photos/videos and media groups. The `GeneratedContent` of a publication may exceed this. Strategy:

- Define a constant `TelegramCaptionMaxLength = 1024` in `TelegramPublisher`.
- If content length <= 1024: send it as the caption on the media message.
- If content length > 1024: send the media without a caption (or with a truncated caption ending in "..."), then send the full text as a separate `sendMessage` reply to the media message.

This ensures the full text is always published, regardless of length.

## Consequences

**Positive:**
- Editor-selected media will actually appear in Telegram publications.
- The `IPublisher` interface becomes media-aware, enabling future publishers (Website, Instagram) to handle media from the start.
- Media resolution in the worker is a single DB query per publication, efficient and centralized.

**Negative / risks:**
- The `IPublisher` interface change is breaking -- any future publisher implementation must accept the `media` parameter (even if it ignores it).
- Telegram API rate limits: sending photos/media groups counts as separate API calls. High-volume publishing could hit limits. Mitigated by the existing worker batch size and interval.
- Large media files: Telegram has a 20MB limit for photos sent via URL. The `CloudflareR2Options.MaxFileSizeBytes` is 50MB. If a selected media file exceeds 20MB, the `sendPhoto` call will fail. Consider adding a size filter when resolving media (skip files > 20MB with a warning log).

**Files affected:**
- `Core/DomainModels/ResolvedMedia.cs` (new)
- `Core/Interfaces/Repositories/IMediaFileRepository.cs` (add `GetByIdsAsync`)
- `Core/Interfaces/Publishers/IPublisher.cs` (add `List<ResolvedMedia> media` parameter)
- `Infrastructure/Persistence/Repositories/MediaFileRepository.cs` (implement `GetByIdsAsync`)
- `Infrastructure/Publishers/TelegramPublisher.cs` (major rewrite: media dispatch, `sendPhoto`, `sendMediaGroup`, caption handling)
- `Worker/Workers/PublishingWorker.cs` (resolve media files, construct URLs, pass to publisher)

## Implementation Notes

- Follow `.claude/skills/code-conventions/SKILL.md` for layer boundaries: `ResolvedMedia` goes in `Core/DomainModels/`, repository method follows EF Core conventions from `.claude/skills/ef-core-conventions/SKILL.md`.
- Follow `.claude/skills/clean-code/SKILL.md` for method extraction in `TelegramPublisher` -- the dispatch logic (text-only vs single-photo vs media-group) should be separate private methods, each under 20 lines.
- Follow `.claude/skills/mappers/SKILL.md` -- no mapping changes needed in Api/Mappers since this is a backend-only fix.
- The `IPublisher` interface change in `Core/Interfaces/Publishers/` must be done before updating `TelegramPublisher` (dependency order).
- The `GetByIdsAsync` repository method must be added before the worker changes (dependency order).
- The URL construction `$"{publicBaseUrl.TrimEnd('/')}/{r2Key.TrimStart('/')}"` can be duplicated in the worker -- it is a one-liner and does not warrant a shared utility class.
- Telegram API constants (caption limit 1024, media group limit 10, photo size limit 20MB) should be `private const` fields in `TelegramPublisher`, not in an Options class, since they are Telegram platform constraints, not configurable values.
- Update existing `PublishingWorkerTests` and `TelegramPublisher` tests (if any) to verify media is passed through and handled.
