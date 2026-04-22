# Custom Publication Media Upload

## Status

Proposed

## Context

Today, the "Media" section on the publication detail page (`UI/src/features/publications/PublicationDetailPage.tsx` lines 412–462) lets editors pick media from the event's media pool. That pool is computed as:

```csharp
// Api/Controllers/PublicationsController.cs
private static List<MediaFile> ExtractAvailableMedia(Publication publication)
    => publication.Event is null
        ? []
        : publication.Event.Articles.SelectMany(a => a.MediaFiles).ToList();
```

Every `MediaFile` in that pool is **auto-ingested from an article**: it is created by `MediaIngestionService` (`Infrastructure/Services/MediaIngestionService.cs`) during RSS / Telegram source fetching, stored in Cloudflare R2 (see ADR `0006-media-files-cloudflare-r2.md`), and persisted to the `media_files` table with a **mandatory `ArticleId` FK** (`media_files.ArticleId UUID NOT NULL` with `ON DELETE CASCADE`, see `Infrastructure/Persistence/Sql/0001_baseline.sql` lines 120–136).

The editor's selection is persisted as a JSONB `List<Guid>` on `publications.SelectedMediaFileIds`. On publishing, `PublishingWorker.ResolveMediaAsync` (lines 136–167) loads the selected files via `IMediaFileRepository.GetByIdsAsync`, builds `{PublicBaseUrl}/{R2Key}` URLs, and passes a `List<ResolvedMedia>` to `TelegramPublisher.DispatchAsync` (see ADR `0012-publish-selected-media-to-telegram.md`). The publisher dispatches to `sendPhoto` / `sendVideo` / `sendMediaGroup` depending on count and kind.

### Problem

Editors cannot upload custom media (cover image, a better-quality photo, a composed graphic). The workflow is limited to whatever the RSS fetcher happened to attach to the event's articles. Concrete gaps:

1. **No upload endpoint.** `PublicationsController` exposes no multipart action.
2. **`media_files` schema assumes article ownership.** `ArticleId` is `NOT NULL` and cascade-deletes with the article. Custom media has no article — it belongs to a *publication*.
3. **`IMediaStorage` is upload-only and has no delete method** (`Core/Interfaces/Storage/IMediaStorage.cs`). Custom files that get orphaned (publication rejected, replaced, or deleted) cannot be reclaimed from R2.
4. **`IMediaFileRepository` has no delete or "get by publication" method** (`Core/Interfaces/Repositories/IMediaFileRepository.cs`).
5. **UI** currently renders `publication.availableMedia` as a fixed grid of event media. There is no upload affordance, no "custom media" distinction, no per-item delete.
6. **Publication lifecycle** — `PublicationService.RegenerateAsync` resets `GeneratedContent` but does nothing to `SelectedMediaFileIds`; regeneration currently preserves selection, which is fine for event media but risky for custom media (files the editor uploaded for an obsolete draft).

### Constraints from existing conventions

- **Layer boundaries** (`.claude/skills/code-conventions/SKILL.md`): controllers must not call `IMediaStorage` directly; the orchestration belongs in a service. R2 details live in `Infrastructure/Storage/`.
- **Api conventions** (`.claude/skills/api-conventions/SKILL.md`): routes are lowercase plural nouns, actions use kebab-case sub-paths, `[Authorize(Roles = nameof(UserRole.Editor) + "," + nameof(UserRole.Admin))]` already applies on `PublicationsController`.
- **Dapper conventions** (`.claude/skills/dapper-conventions/SKILL.md`): SQL constants in `Infrastructure/Persistence/Repositories/Sql/MediaFileSql.cs`, updates via targeted `ExecuteAsync`, enum storage as strings.
- **Mappers** (`.claude/skills/mappers/SKILL.md`): Domain→DTO mappers in `Api/Mappers/`, `MediaFileMapper.ToDto(publicBaseUrl)` already exists.
- **Options pattern**: size limits, allowed MIME types, and any new per-publication quota live in an `Infrastructure/Configuration/*Options.cs` class with `SectionName`.
- **Storage model**: R2 keys for existing auto-ingested media follow `articles/{articleId}/{guid}{ext}` (see `MediaIngestionService.BuildR2Key`). Custom media needs a parallel key prefix so listing / cleanup is predictable.
- **DbUp migrations**: forward-only SQL in `Infrastructure/Persistence/Sql/NNNN_*.sql` (embedded resource). Next number is `0004`.

## Options

### Option 1 — Reuse `media_files` with `ArticleId` nullable, introduce a nullable `PublicationId` column

Make `media_files.ArticleId` nullable and add a nullable `PublicationId` FK. A media row belongs to either an article (auto-ingested) **or** a publication (custom). Add `OwnerKind TEXT` (`Article` | `Publication`) so queries don't need `COALESCE` logic. `SelectedMediaFileIds` on the publication covers **both** event-pool media and custom media uniformly — the publisher resolution pipeline does not change.

**Pros:**
- **One storage table, one domain model, one repository.** `GetByIdsAsync` keeps working for the worker — no new branching in `PublishingWorker`.
- Domain remains small. `MediaFile` gains one optional `PublicationId` field.
- The `UploadAsync → AddAsync` pattern is identical to what `MediaIngestionService` already does, so a new `PublicationMediaService` can follow its shape exactly.
- Publisher does not learn about custom vs event media — it just receives `List<ResolvedMedia>` like today.

**Cons:**
- Relaxing the `ArticleId NOT NULL` constraint weakens the invariant that article-ingested media is always anchored to an article. Mitigated by a `CHECK` constraint enforcing exactly one of (`ArticleId`, `PublicationId`) is non-null plus a matching `OwnerKind` discriminator.
- The existing unique index `(ArticleId, OriginalUrl)` must be converted to a partial index filtered on `ArticleId IS NOT NULL`, or we add a parallel `(PublicationId, OriginalUrl)` partial unique index (custom media has no meaningful `OriginalUrl` though — uploads don't come from a URL). Simpler: make `OriginalUrl` nullable too for custom uploads and make the existing unique index partial.

### Option 2 — Separate `publication_media_files` table + separate `PublicationMediaFile` domain model

A parallel table for custom uploads, entirely independent of `media_files`. `publications.SelectedMediaFileIds` stays untouched (event media) and a new `publications.CustomMediaFileIds` (or FK rows in `publication_media_files`) carries uploads.

**Pros:**
- No schema changes to `media_files` or the article-ingestion pipeline. Invariant preserved.
- Clear separation of concerns at the DB level.

**Cons:**
- **Publisher and UI now need dual resolution.** `PublishingWorker.ResolveMediaAsync` must union two repositories, two different URL builders, two different record types (or map both to `ResolvedMedia` at the edge). The UI must merge two ID lists before rendering selection and must send two lists on `PUT /publications/{id}/content`.
- Duplicates much of `MediaFileRepository`, `MediaFileMapper`, `MediaFileDto`, `MediaFileSql` — violates DRY across Core / Infrastructure / Api.
- Two tables, two migrations to maintain, two garbage-collection codepaths for orphans.
- `SelectedMediaFileIds` semantics become "a subset of the event pool" vs "all uploads are selected" — asymmetric, confusing.

### Option 3 — Store custom media in the `Article` of the publication's `Initiator` role

`PublicationService.CreateForEventAsync` already picks the event's `Initiator` article and assigns it to `publication.Article`. Custom media would be uploaded as `MediaFile` rows linked to that article, mixing in with auto-ingested article media.

**Pros:**
- Zero schema change. Zero new code path for resolution.

**Cons:**
- **Wrong lifecycle.** Custom uploads would survive the publication's deletion and pollute the article (which may be attached to other publications or event views). They would also appear in `ArticleDetailDto.MediaFiles` for every reader of the article.
- **Wrong ownership** from the editor's mental model: uploads are *per publication*, not per article.
- Cleanup on publication regeneration or rejection is not expressible — we can't safely delete "custom" media because we don't know which rows were uploads vs genuine article media.
- Breaks the user-facing requirement (custom media lives with a specific publication).

## Decision

**Adopt Option 1.** Relax the `media_files` schema to allow media owned by a **publication** instead of an article, with a discriminator column and a `CHECK` constraint so invariants remain strict. Add an upload / list / delete API, a thin `PublicationMediaService` in Infrastructure, and a UI upload affordance. The publisher, worker, and `SelectedMediaFileIds` flow are unchanged.

### 1. Database schema — migration `0004_add_publication_media.sql`

```sql
-- 1. Add ownership discriminator (defaults 'Article' for existing rows)
ALTER TABLE media_files
    ADD COLUMN IF NOT EXISTS "OwnerKind"     TEXT NOT NULL DEFAULT 'Article',
    ADD COLUMN IF NOT EXISTS "PublicationId" UUID NULL,
    ADD COLUMN IF NOT EXISTS "UploadedByUserId" UUID NULL;

-- 2. Relax ArticleId to nullable so publication-owned rows are allowed
ALTER TABLE media_files ALTER COLUMN "ArticleId" DROP NOT NULL;

-- 3. FK to publications with cascade delete (cleans up rows when a publication is deleted).
--    FK to users is SET NULL (editor account removal must not lose the file).
ALTER TABLE media_files
    ADD CONSTRAINT "FK_media_files_publications_PublicationId"
        FOREIGN KEY ("PublicationId") REFERENCES publications ("Id") ON DELETE CASCADE,
    ADD CONSTRAINT "FK_media_files_users_UploadedByUserId"
        FOREIGN KEY ("UploadedByUserId") REFERENCES users ("Id") ON DELETE SET NULL;

-- 4. Invariant: exactly one owner, and the discriminator matches
ALTER TABLE media_files
    ADD CONSTRAINT "CK_media_files_owner_exclusive"
        CHECK (
            ("OwnerKind" = 'Article'     AND "ArticleId"     IS NOT NULL AND "PublicationId" IS NULL)
         OR ("OwnerKind" = 'Publication' AND "PublicationId" IS NOT NULL AND "ArticleId"     IS NULL)
        );

-- 5. Replace the existing unique (ArticleId, OriginalUrl) index with a partial one
--    so publication-owned rows (where OriginalUrl is empty) don't collide.
DROP INDEX IF EXISTS "IX_media_files_ArticleId_OriginalUrl";
CREATE UNIQUE INDEX IF NOT EXISTS "IX_media_files_ArticleId_OriginalUrl"
    ON media_files ("ArticleId", "OriginalUrl")
    WHERE "ArticleId" IS NOT NULL;

-- 6. Index for fast "list custom media for this publication" lookups
CREATE INDEX IF NOT EXISTS "IX_media_files_PublicationId"
    ON media_files ("PublicationId")
    WHERE "PublicationId" IS NOT NULL;
```

Notes:
- `ON DELETE CASCADE` for the publication FK means that when a publication row is hard-deleted, its custom media rows vanish automatically. The R2 object cleanup is a separate concern handled by the application layer (see §8 orphan cleanup).
- `OriginalUrl` is kept `NOT NULL DEFAULT ''` (no schema change) — for custom uploads it will be `''`. The partial unique index prevents that empty string from colliding across all publications.
- The default `'Article'` for `OwnerKind` on existing rows preserves invariants.

### 2. Domain model changes (`Core/DomainModels/MediaFile.cs`)

```csharp
public class MediaFile
{
    public Guid Id { get; init; }
    public Guid? ArticleId { get; init; }          // was: Guid, now nullable
    public Guid? PublicationId { get; init; }      // new
    public MediaOwnerKind OwnerKind { get; init; } // new
    public Guid? UploadedByUserId { get; init; }   // new
    public string R2Key { get; init; } = string.Empty;
    public string OriginalUrl { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public MediaKind Kind { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public enum MediaOwnerKind
{
    Article,
    Publication
}
```

Rationale for `init` on the new fields: once a media row is written, ownership does not change.

**Impact sweep (must update all touch points):**
- `Infrastructure/Persistence/Entity/MediaFileEntity.cs` — add `ArticleId` nullable, `PublicationId`, `OwnerKind`, `UploadedByUserId`.
- `Infrastructure/Persistence/Mappers/MediaFileMapper.cs` — include new columns, parse/emit `OwnerKind` as string.
- `Infrastructure/Persistence/Repositories/Sql/MediaFileSql.cs` — update `Insert`, `GetByArticleId`, `GetByIds` column lists; add `GetByPublicationId`, `Delete`, `GetByIdForDeletion`.
- `MediaIngestionService.IngestSingleReferenceAsync` — set `OwnerKind = MediaOwnerKind.Article`, `PublicationId = null`, `UploadedByUserId = null`. This is a pure additive field write; no behavior change.
- `PublishingWorker.ResolveMediaAsync` — **no change**. It already calls `GetByIdsAsync` and constructs `ResolvedMedia` purely from `R2Key`, `ContentType`, `Kind`, `SizeBytes`. Ownership is irrelevant at send time.
- `Api/Controllers/PublicationsController.ExtractAvailableMedia` — change to include both event media and custom media (see §5).

### 3. New repository methods (`Core/Interfaces/Repositories/IMediaFileRepository.cs`)

Add three methods — names follow the repository catalogue in `.claude/skills/dapper-conventions/SKILL.md` §14:

```csharp
Task<List<MediaFile>> GetByPublicationIdAsync(Guid publicationId, CancellationToken cancellationToken = default);
Task<MediaFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
```

SQL constants in `MediaFileSql`:

```csharp
public const string GetByPublicationId = """
    SELECT "Id", "ArticleId", "PublicationId", "OwnerKind", "UploadedByUserId",
           "R2Key", "OriginalUrl", "ContentType", "SizeBytes", "Kind", "CreatedAt"
    FROM media_files
    WHERE "PublicationId" = @publicationId
    ORDER BY "CreatedAt"
    """;

public const string GetById = """
    SELECT "Id", "ArticleId", "PublicationId", "OwnerKind", "UploadedByUserId",
           "R2Key", "OriginalUrl", "ContentType", "SizeBytes", "Kind", "CreatedAt"
    FROM media_files
    WHERE "Id" = @id
    LIMIT 1
    """;

public const string Delete = """
    DELETE FROM media_files WHERE "Id" = @id
    """;
```

`GetByIdAsync` returns the full row so the deletion path can read `R2Key` (to clean up R2) and `OwnerKind` / `PublicationId` (to authorize the delete) before the SQL `DELETE`.

### 4. Storage interface — add `DeleteAsync`

Extend `Core/Interfaces/Storage/IMediaStorage.cs`:

```csharp
public interface IMediaStorage : IDisposable
{
    Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
```

Implementation in `Infrastructure/Storage/CloudflareR2Storage.cs` uses `AmazonS3Client.DeleteObjectAsync` with the same `BucketName`. R2 returns 204 on success and on "not found" alike, so deletion is idempotent — no special-case is needed.

### 5. New service — `IPublicationMediaService` in Core, `PublicationMediaService` in Infrastructure

Interface in `Core/Interfaces/Services/IPublicationMediaService.cs`:

```csharp
public interface IPublicationMediaService
{
    Task<MediaFile> UploadAsync(
        Guid publicationId,
        Guid uploadedByUserId,
        Stream content,
        string fileName,
        string contentType,
        long sizeBytes,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid publicationId,
        Guid mediaFileId,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default);
}
```

Implementation (`Infrastructure/Services/PublicationMediaService.cs`) — primary constructor style, per `code-conventions`:

```csharp
public class PublicationMediaService(
    IPublicationRepository publicationRepository,
    IMediaFileRepository mediaFileRepository,
    IMediaStorage storage,
    IOptions<PublicationMediaOptions> options,
    ILogger<PublicationMediaService> logger) : IPublicationMediaService
```

**`UploadAsync` algorithm:**

1. `var publication = await publicationRepository.GetByIdAsync(publicationId, ct) ?? throw new KeyNotFoundException(...)`.
2. Guard status — uploads are allowed only when the publication is mutable:
   `if (publication.Status is not PublicationStatus.ContentReady and not PublicationStatus.Created and not PublicationStatus.Failed) throw new InvalidOperationException(...)`.
   (Same spirit as `PublicationService.UpdateContentAsync` which requires `ContentReady`.)
3. Validate `sizeBytes <= options.MaxUploadBytes` → `ArgumentException` if exceeded.
4. Validate `contentType` against `options.AllowedContentTypes` (default: `image/jpeg`, `image/png`, `image/webp`, `image/gif`, `video/mp4`). Extension-sniff via `fileName` only as a fallback for `application/octet-stream`. → `ArgumentException` if disallowed.
5. Enforce per-publication count: `GetByPublicationIdAsync(publicationId)` count < `options.MaxFilesPerPublication` → `InvalidOperationException` otherwise.
6. Derive `MediaKind` from `contentType` (`image/*` → `Image`, `video/*` → `Video`).
7. Generate `var mediaId = Guid.NewGuid();` and `r2Key = $"publications/{publicationId}/{mediaId}{ext}"`. The **`publications/` prefix** makes custom-vs-article bucket browsing trivial and is parallel to the `articles/{articleId}/...` prefix used by `MediaIngestionService.BuildR2Key`.
8. `content.Position = 0; await storage.UploadAsync(r2Key, content, contentType, ct);`.
9. Persist the `MediaFile` domain object with `OwnerKind = Publication`, `PublicationId = publicationId`, `UploadedByUserId = uploadedByUserId`, `ArticleId = null`, `OriginalUrl = string.Empty`, `CreatedAt = DateTimeOffset.UtcNow`.
10. `logger.LogInformation("Custom media {MediaId} uploaded for publication {PublicationId} by {UserId} ({SizeBytes} bytes, {ContentType})", ...)`.
11. Return the saved `MediaFile`.

**`DeleteAsync` algorithm:**

1. `var media = await mediaFileRepository.GetByIdAsync(mediaFileId, ct) ?? throw new KeyNotFoundException(...)`.
2. Authorize — the media must belong to the publication in the route and be a `Publication`-owned row:
   - `if (media.OwnerKind != MediaOwnerKind.Publication || media.PublicationId != publicationId) throw new InvalidOperationException("Cannot delete this media via the publication endpoint")`.
3. Guard publication status (same whitelist as upload).
4. Try `await storage.DeleteAsync(media.R2Key, ct);` — failure here is logged as a warning but does not abort the DB delete: a missing R2 object is acceptable (idempotent S3 semantics); blocking the DB delete would leave a phantom row that the user cannot retry.
5. `await mediaFileRepository.DeleteAsync(media.Id, ct);`.
6. If the deleted ID was present in `publication.SelectedMediaFileIds`, also strip it — call `publicationRepository.UpdateContentAndMediaAsync(publication.Id, publication.GeneratedContent, updatedList, ct)`.
7. `logger.LogInformation(...)`.

Registered in `InfrastructureServiceExtensions.AddStorage` as `AddScoped<IPublicationMediaService, PublicationMediaService>()`.

### 6. New options class — `PublicationMediaOptions`

`Infrastructure/Configuration/PublicationMediaOptions.cs`:

```csharp
public class PublicationMediaOptions
{
    public const string SectionName = "PublicationMedia";
    public long MaxUploadBytes { get; set; } = 20 * 1024 * 1024; // Telegram's sendPhoto URL limit
    public int MaxFilesPerPublication { get; set; } = 10;         // Telegram media group limit
    public List<string> AllowedContentTypes { get; set; } =
    [
        "image/jpeg", "image/png", "image/webp", "image/gif",
        "video/mp4"
    ];
}
```

The defaults are **intentionally aligned with Telegram constraints** so that a valid upload is always sendable:
- 20 MB aligns with `TelegramPhotoMaxSizeBytes` in `TelegramPublisher.cs` and the `MaxMediaFileSizeBytes` skip rule in `PublishingWorker.cs` (20 MB there too).
- 10 files per publication aligns with `TelegramMediaGroupMaxSize`.

Registered in `AddStorage` next to `CloudflareR2Options`:

```csharp
services.Configure<PublicationMediaOptions>(configuration.GetSection(PublicationMediaOptions.SectionName));
```

Added to `Api/appsettings.Development.json` with the defaults explicit so ops can tune without code change.

### 7. API endpoints — three actions on `PublicationsController`

Routes follow the Api conventions: lowercase plural noun, kebab-case sub-paths, `{id:guid}` constraint.

```csharp
[HttpPost("{id:guid}/media")]
[RequestSizeLimit(25 * 1024 * 1024)]           // soft buffer above MaxUploadBytes (the options-bound check inside the service is the real gate)
[Consumes("multipart/form-data")]
public async Task<ActionResult<MediaFileDto>> UploadMedia(
    Guid id,
    IFormFile file,
    CancellationToken cancellationToken = default)

[HttpDelete("{id:guid}/media/{mediaId:guid}")]
public async Task<IActionResult> DeleteMedia(
    Guid id,
    Guid mediaId,
    CancellationToken cancellationToken = default)
```

The existing `GET /publications/{id:guid}` is reused to **list** media — the detail DTO already ships `availableMedia`; we just extend `ExtractAvailableMedia` to also include custom media (see §9). A separate list endpoint is unnecessary and would duplicate the happy path.

Authorization is inherited from the class-level `[Authorize(Roles = Editor,Admin)]`.

Controller action bodies must:
- Return `Unauthorized()` if `UserId is null`.
- Return `BadRequest` for empty file or zero length.
- Call the service (never touch `IMediaStorage` directly).
- Map the returned `MediaFile` via `MediaFileMapper.ToDto(_publicBaseUrl)`.
- Return `CreatedAtAction(nameof(GetById), new { id = publicationId }, dto)` on upload, `NoContent()` on delete.
- The service's typed exceptions (`KeyNotFoundException`, `InvalidOperationException`, `ArgumentException`) are mapped to 404/409/400 by `ExceptionMiddleware` — controllers do **not** try/catch.

### 8. FluentValidation for the upload

`IFormFile`-based input is best validated inside the controller or service, but the shared convention for request-level rules is FluentValidation. Because `IFormFile` itself is not bindable as a `record`, we validate in the controller (mime + non-empty + size) and let `PublicationMediaService` enforce the options-bound rules. This matches the existing inline-validation pattern in controllers documented in `api-conventions` §Validation (second tier).

### 9. `ExtractAvailableMedia` — include custom media

`Api/Controllers/PublicationsController.ExtractAvailableMedia` becomes:

```csharp
private async Task<List<MediaFile>> ExtractAvailableMediaAsync(
    Publication publication,
    IMediaFileRepository mediaFileRepository,
    CancellationToken cancellationToken)
{
    var eventMedia = publication.Event is null
        ? []
        : publication.Event.Articles.SelectMany(a => a.MediaFiles).ToList();

    var customMedia = await mediaFileRepository.GetByPublicationIdAsync(publication.Id, cancellationToken);

    return [.. eventMedia, .. customMedia];
}
```

The method becomes `async` and must be threaded through the existing `GetById`, `UpdateContent`, `Approve`, `Reject`, `Send`, `Regenerate` action methods (each already fetches the detail, then calls `ExtractAvailableMedia`). Inject `IMediaFileRepository` into the controller (already available via `publicationRepository` for other calls, but `IMediaFileRepository` is not — add it to the primary constructor).

### 10. DTO — extend `MediaFileDto` with an owner discriminator

`Api/Models/PublicationDtos.cs` — find `MediaFileDto` (or add it in the media-file mapper file; it currently lives in `Api/Models/` implied by `PublicationDetailDto`). Add one field:

```csharp
public record MediaFileDto(
    Guid Id,
    Guid? ArticleId,        // was non-nullable, now nullable
    string Url,
    string Kind,
    string ContentType,
    long SizeBytes,
    string OwnerKind        // new — "Article" or "Publication"
);
```

Update `Api/Mappers/MediaFileMapper.ToDto` accordingly.

Breaking-change note: `ArticleId` becomes nullable. The UI consumes it as `string` (see `UI/src/features/publications/types.ts` line 14). TypeScript codegen will emit `articleId: string | null`. The existing UI usage does not dereference `articleId` so the change is compatible, but `npm run generate-api` must be run after the backend change.

### 11. `SelectedMediaFileIds` semantics

No change. The JSONB array continues to reference any media row by `Id`, whether owned by an article or a publication. The publisher and worker are ignorant of ownership.

**New invariant enforced in `PublicationService.UpdateContentAsync`:** every `selectedMediaFileId` must belong to either the publication's event (via article media) or the publication itself (custom). Add a validation pass:

```csharp
// in UpdateContentAsync, before UpdateContentAndMediaAsync
var eligibleIds = (await mediaFileRepository.GetByPublicationIdAsync(publicationId, ct))
    .Select(m => m.Id)
    .Concat(publication.Event?.Articles.SelectMany(a => a.MediaFiles).Select(m => m.Id) ?? [])
    .ToHashSet();

var invalid = selectedMediaFileIds.Where(id => !eligibleIds.Contains(id)).ToList();
if (invalid.Count > 0)
    throw new ArgumentException($"Media file ids not eligible for this publication: {string.Join(",", invalid)}");
```

This blocks a malicious editor from selecting an arbitrary media file from another publication or event.

`PublicationService`'s constructor needs `IMediaFileRepository` — add it.

### 12. Lifecycle: regeneration, rejection, deletion

- **Regenerate** (`PublicationService.RegenerateAsync`): custom media survives regeneration. An editor who requested a new draft typically still wants their custom image. `SelectedMediaFileIds` is also preserved today — keep that behaviour. Custom media files are deleted only by explicit user action or when the publication itself is deleted.
- **Reject**: no automatic cleanup. Rejected publications remain browsable for the audit trail, including their custom media.
- **Publication hard delete**: out of scope — `IPublicationRepository` has no `DeleteAsync`. If/when added, the `ON DELETE CASCADE` on `media_files.PublicationId` takes care of the DB side; R2 objects for custom media would need an explicit sweep (see §Risks — orphan cleanup).

### 13. UI changes (`UI/src/features/publications/PublicationDetailPage.tsx`)

1. **Add `ownerKind: string` to `MediaFileDto`** in `types.ts` (will be emitted by the OpenAPI regen, but the manual type needs to match).
2. **Upload affordance** in the `MEDIA` section — a styled "UPLOAD CUSTOM MEDIA" button that opens a hidden `<input type="file" accept="image/*,video/mp4" />`. On change, `POST /publications/{id}/media` multipart, then invalidate the publication query key. Disabled when `!canEdit`.
3. **Per-item delete** — an `×` overlay on each media tile where `ownerKind === 'Publication'`. Clicking calls `DELETE /publications/{id}/media/{mediaId}` with a `ConfirmDialog`, then invalidates the publication query. Disabled when `!canEdit`.
4. **Visual differentiation** — custom media tiles carry a small `CUSTOM` caps-tag (same typography as existing `SELECTED` badge) so editors can tell at a glance which files are theirs.
5. **Hook work** — add `uploadMedia` and `deleteMedia` mutations to `usePublicationMutations.ts`. Both invalidate `['publication', id]` on success.
6. **File-size / count pre-check client-side** using the same 20 MB / 10-file limits that the backend enforces — purely a UX affordance. The backend is the authority.

All UI dark-theme + inline styles continue the pattern already used on the page; no `components/ui/*` primitives (per ADR `0018`'s precedent).

## Consequences

### Positive

- One domain type (`MediaFile`), one repository, one storage backend — publisher, worker, and `ResolvedMedia` pipeline are completely unchanged.
- R2 key prefix makes ownership self-describing (`articles/...` vs `publications/...`); ops can bulk-list, bulk-delete, or bulk-lifecycle one prefix without touching the other.
- `CHECK` constraint + partial unique index keeps the DB layer honest. Inserting a row with both `ArticleId` and `PublicationId` (or neither) is a constraint violation — not a runtime assertion.
- Per-publication cascade delete means future `DELETE /publications/{id}` (not in scope here) gets correct media cleanup for free.
- `UploadedByUserId` gives an audit trail for who uploaded what without adding a separate audit log.

### Negative / risks

- **Two-phase orphan risk — R2 succeeds, DB insert fails.** If `storage.UploadAsync` succeeds but the subsequent `mediaFileRepository.AddAsync` throws, the R2 object is orphaned. Mitigation: a tiny try/catch in `UploadAsync` that deletes the R2 object on DB insert failure (compensating action). This path is rare and a periodic cleanup (see below) is the backstop.
  ```csharp
  try { await mediaFileRepository.AddAsync(mediaFile, ct); }
  catch { await storage.DeleteAsync(r2Key, CancellationToken.None); throw; }
  ```
- **Orphan cleanup for publication hard-deletes.** `ON DELETE CASCADE` deletes the DB rows but NOT the R2 objects. Until we have a bulk delete in `IMediaStorage` and a scheduled sweeper worker, hard-deleting a publication leaves R2 objects behind. Out of scope for this ADR; explicitly tracked as a future ADR ("R2 orphan reaper worker"). This is acceptable because no code path currently hard-deletes publications.
- **Malicious uploads.** Mitigations (all enforced in `PublicationMediaService.UploadAsync`):
  - Size cap (`MaxUploadBytes`).
  - Strict MIME allow-list (`AllowedContentTypes`).
  - Per-publication count cap (`MaxFilesPerPublication`).
  - Role restriction (`Editor`/`Admin` only, inherited from the controller `[Authorize]`).
  - No user-supplied file name on the R2 key — the key is `publications/{publicationId}/{Guid.NewGuid()}{ext}`.
  - No server-side execution of the uploaded bytes. The files are only ever served as content to Telegram via R2's public URL.
  - Not done here: anti-malware scanning. If needed later, add `IMediaScanner` and run it before `storage.UploadAsync`.
- **Storage cost.** Each publication gets up to 10 × 20 MB = 200 MB of custom media, kept forever (no lifecycle rule). For a handful of publications a day this is negligible; if volume grows, a lifecycle rule on the `publications/` prefix (e.g., auto-delete custom media from publications older than N days where `Status = Failed|Rejected`) becomes attractive — tracked as a future ADR.
- **Breaking change to `MediaFileDto.ArticleId`** — becomes nullable. All callers in the UI are grep-clean (no dereference), but the OpenAPI-generated client must be regenerated. Document in the release notes.
- **Tests on existing `MediaIngestionService` and `PublishingWorker`** — must be updated to populate/consume the new `OwnerKind`, `PublicationId`, `UploadedByUserId` fields. The behavior is unchanged but the test fixtures need the new shape.

### Files affected

**New:**
- `Core/Interfaces/Services/IPublicationMediaService.cs`
- `Infrastructure/Services/PublicationMediaService.cs`
- `Infrastructure/Configuration/PublicationMediaOptions.cs`
- `Infrastructure/Persistence/Sql/0004_add_publication_media.sql`

**Modified — Core:**
- `Core/DomainModels/MediaFile.cs` — nullable `ArticleId`, new `PublicationId`, `OwnerKind`, `UploadedByUserId`, new `MediaOwnerKind` enum.
- `Core/Interfaces/Repositories/IMediaFileRepository.cs` — add `GetByPublicationIdAsync`, `GetByIdAsync`, `DeleteAsync`.
- `Core/Interfaces/Storage/IMediaStorage.cs` — add `DeleteAsync`.

**Modified — Infrastructure:**
- `Infrastructure/Persistence/Entity/MediaFileEntity.cs` — mirror domain.
- `Infrastructure/Persistence/Mappers/MediaFileMapper.cs` — new columns, enum as string.
- `Infrastructure/Persistence/Repositories/Sql/MediaFileSql.cs` — update `Insert`/`GetByArticleId`/`GetByIds`; add `GetByPublicationId`/`GetById`/`Delete`.
- `Infrastructure/Persistence/Repositories/MediaFileRepository.cs` — implement the three new methods.
- `Infrastructure/Storage/CloudflareR2Storage.cs` — implement `DeleteAsync` via `DeleteObjectAsync`.
- `Infrastructure/Services/MediaIngestionService.cs` — set `OwnerKind = Article` when creating `MediaFile`.
- `Infrastructure/Services/PublicationService.cs` — inject `IMediaFileRepository`, add eligibility validation in `UpdateContentAsync`.
- `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` — register `PublicationMediaOptions`, `IPublicationMediaService`.

**Modified — Api:**
- `Api/Controllers/PublicationsController.cs` — add `UploadMedia` and `DeleteMedia`; convert `ExtractAvailableMedia` to async and include custom media; inject `IMediaFileRepository`.
- `Api/Models/PublicationDtos.cs` — `MediaFileDto` gains `OwnerKind`, `ArticleId` becomes nullable.
- `Api/Mappers/MediaFileMapper.cs` — include `OwnerKind`.
- `Api/appsettings.Development.json` — add `PublicationMedia` section.
- `Worker/appsettings.Development.json` — same (worker needs `PublicationMediaOptions` only if `PublicationMediaService` were resolved there; since it's API-only, this file does **not** need the section, but the `CloudflareR2` section already exists and is shared).

**Modified — UI:**
- `UI/src/features/publications/types.ts` — `MediaFileDto.ownerKind: string`, `articleId: string | null`.
- `UI/src/features/publications/usePublicationMutations.ts` — `uploadMedia` and `deleteMedia` mutations.
- `UI/src/features/publications/PublicationDetailPage.tsx` — upload button, per-tile delete `×`, `CUSTOM` badge, file-input handling.
- `UI/src/features/publications/__tests__/PublicationDetailPage.test.tsx` — cover upload/delete, custom-tile rendering, status gating.

## Implementation Notes

### Order of changes (strict dependency order)

1. **DB migration** (`Infrastructure/Persistence/Sql/0004_add_publication_media.sql`) — apply locally, verify the `CHECK` constraint and partial indexes. Every existing row must remain valid with the `OwnerKind = 'Article'` default.
2. **Entity / domain / mapper** — update `MediaFileEntity`, `MediaFile`, `MediaFileMapper` together; compilation of `MediaIngestionService` must be repaired at this step (set `OwnerKind = Article`).
3. **SQL constants** in `MediaFileSql` — update `Insert`, `GetByArticleId`, `GetByIds` column lists; add the three new constants.
4. **Repository** — add the three new methods.
5. **Storage** — add `DeleteAsync` to `IMediaStorage` and implement in `CloudflareR2Storage`.
6. **Options** — add `PublicationMediaOptions`, register in DI.
7. **Service** — `IPublicationMediaService` + `PublicationMediaService` with the upload + delete algorithms and the compensating R2 delete on DB insert failure.
8. **PublicationService** — inject `IMediaFileRepository` and add eligibility validation in `UpdateContentAsync`.
9. **Controller** — add `UploadMedia` / `DeleteMedia`; refactor `ExtractAvailableMedia` to async + include custom media; inject `IMediaFileRepository`; update `MediaFileDto` + `MediaFileMapper`.
10. **UI** — regenerate the API client (`npm run generate-api`), update `types.ts` and mutations hook, wire the upload button and per-tile delete in `PublicationDetailPage.tsx`.
11. **Tests** — delegated to `test-writer` (see next section).

### Skills to follow (MUST be read by `feature-planner` and `implementer`)

- **`.claude/skills/code-conventions/SKILL.md`** — layer boundaries (controller must not call `IMediaStorage`), primary-constructor style for the service, Options class with `SectionName`, exception-to-HTTP mapping.
- **`.claude/skills/api-conventions/SKILL.md`** — route naming (`/publications/{id:guid}/media` and `/publications/{id:guid}/media/{mediaId:guid}`), `IFormFile` upload conventions, `CreatedAtAction` on creation, `NoContent` on delete, pagination guard not needed here.
- **`.claude/skills/dapper-conventions/SKILL.md`** — SQL constants in `MediaFileSql`, `CommandDefinition` usage, enum stored as string via `.ToString()`, partial unique index handled at migration layer, `DeleteAsync` naming from the catalogue.
- **`.claude/skills/mappers/SKILL.md`** — `MediaFileMapper.ToDto` stays static/extension, `OwnerKind` serialized via `.ToString()`, `Entity↔Domain` mapping only in `Infrastructure/Persistence/Mappers/`.
- **`.claude/skills/clean-code/SKILL.md`** — `PublicationMediaService.UploadAsync` is on the edge of the 20-line target; extract `ValidateUploadAsync`, `BuildR2Key`, `PersistMediaFileAsync` if the happy path grows past 20 lines.

### Testing (delegated to `test-writer`)

- `PublicationMediaServiceTests` — mocked `IMediaStorage`, `IMediaFileRepository`, `IPublicationRepository`, `IOptions<PublicationMediaOptions>`. Cases: happy-path upload, size too large → `ArgumentException`, content-type rejected → `ArgumentException`, per-publication cap exceeded → `InvalidOperationException`, publication not found → `KeyNotFoundException`, publication in non-mutable status → `InvalidOperationException`, DB insert failure triggers compensating R2 delete, delete happy path, delete of article-owned media through the publication endpoint rejected, delete when media in `SelectedMediaFileIds` strips the ID.
- `MediaFileRepositoryTests` — Dapper against a real local Postgres or the existing integration harness: `AddAsync` with `OwnerKind = Publication` round-trips, `GetByPublicationIdAsync` returns only publication rows, `CHECK` constraint rejects both-null / both-set rows, `DeleteAsync` is idempotent.
- `PublicationsControllerTests` — WebApplicationFactory: multipart upload 201, non-editor → 403, wrong publication id → 404, delete happy path → 204, delete foreign article media → 409.
- `PublicationService.UpdateContentAsyncTests` — new case: selecting a media id that does not belong to the publication's event or custom uploads → `ArgumentException`.
- `PublishingWorkerTests` (existing) — must still pass; no behaviour change, but fixture media rows must be updated to set `OwnerKind`.
- UI tests (`PublicationDetailPage.test.tsx`) — upload triggers mutation, per-tile delete visible only for `OwnerKind === 'Publication'`, delete confirmation path, disabled states match `canEdit`.

### Open questions

None that block implementation. The size / count limits default to Telegram's constraints and are options-bound, so tuning does not require a code change. Virus scanning, R2 lifecycle rules, and publication hard-delete with R2 sweep are deliberately deferred and will each get their own ADR when the need arises.

### Recommended next step

Pass this ADR to **feature-planner** to produce the atomic tasklist in `docs/tasks/active/custom-publication-media.md`.
