# Custom Publication Media Upload

## Goal

Allow editors and admins to upload their own media files (images, mp4 video) to a specific
publication, see those files alongside the event's media pool, and have them sent to Telegram
when the publication is published — all without changing the existing publisher, worker, or
`SelectedMediaFileIds` resolution pipeline.

## Affected Layers

- Core
- Infrastructure
- Api
- UI

---

## Open Questions

None — ADR `docs/architecture/decisions/0019-custom-publication-media.md` fully specifies every
design decision, file path, algorithm, SQL, and status guard.

---

## Tasks

### Phase 1 — Database migration

- [x] **Create `Infrastructure/Persistence/Sql/0004_add_publication_media.sql`** — forward-only
      DbUp migration (embedded resource) containing exactly the SQL specified in ADR §1:
      1. `ALTER TABLE media_files ADD COLUMN IF NOT EXISTS "OwnerKind" TEXT NOT NULL DEFAULT 'Article'`
      2. `ALTER TABLE media_files ADD COLUMN IF NOT EXISTS "PublicationId" UUID NULL`
      3. `ALTER TABLE media_files ADD COLUMN IF NOT EXISTS "UploadedByUserId" UUID NULL`
      4. `ALTER TABLE media_files ALTER COLUMN "ArticleId" DROP NOT NULL`
      5. FK `"FK_media_files_publications_PublicationId"` ON DELETE CASCADE
      6. FK `"FK_media_files_users_UploadedByUserId"` ON DELETE SET NULL
      7. CHECK `"CK_media_files_owner_exclusive"` — exactly one of (ArticleId, PublicationId) is
         non-null and matches OwnerKind
      8. `DROP INDEX IF EXISTS "IX_media_files_ArticleId_OriginalUrl"` then re-create as partial
         (`WHERE "ArticleId" IS NOT NULL`)
      9. `CREATE INDEX IF NOT EXISTS "IX_media_files_PublicationId"` (`WHERE "PublicationId" IS NOT NULL`)

      The file is picked up automatically by the `<EmbeddedResource Include="Persistence/Sql/*.sql" />`
      glob in `Infrastructure.csproj` and applied at startup by `DbUpMigrator.Migrate()`.
      _Acceptance: file exists; SQL runs on a local Postgres without error; all existing rows remain
      valid because `OwnerKind` defaults to `'Article'`; the CHECK constraint accepts an existing
      article-owned row and rejects a row with both FKs set._
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

---

### Phase 2 — Domain model and entity

- [x] **Modify `Core/DomainModels/MediaFile.cs`** — apply the following changes:
      1. Change `public Guid ArticleId { get; init; }` to `public Guid? ArticleId { get; init; }`
      2. Add `public Guid? PublicationId { get; init; }` after `ArticleId`
      3. Add `public MediaOwnerKind OwnerKind { get; init; }` after `PublicationId`
      4. Add `public Guid? UploadedByUserId { get; init; }` after `OwnerKind`
      5. Add the new enum in the same file (or alongside it in the same namespace):
         ```csharp
         public enum MediaOwnerKind { Article, Publication }
         ```
      _Acceptance: `Core` project compiles; `MediaFile` has all four new/modified properties;
      `MediaOwnerKind` enum is visible in `Core.DomainModels`; no infrastructure references._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Entity/MediaFileEntity.cs`** — mirror the domain changes:
      1. Change `public Guid ArticleId { get; set; }` to `public Guid? ArticleId { get; set; }`
      2. Add `public Guid? PublicationId { get; set; }` after `ArticleId`
      3. Add `public string OwnerKind { get; set; } = string.Empty;` after `PublicationId`
         (stored as string in DB, per dapper-conventions enum-as-string rule)
      4. Add `public Guid? UploadedByUserId { get; set; }` after `OwnerKind`
      _Acceptance: `Infrastructure` project compiles; entity has four new/modified properties;
      no domain model imports._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Mappers/MediaFileMapper.cs`** — update both mapping
      directions and repair the compile break caused by the nullable `ArticleId`:
      - `ToDomain`: add `ArticleId = entity.ArticleId,`, `PublicationId = entity.PublicationId,`,
        `OwnerKind = Enum.Parse<MediaOwnerKind>(entity.OwnerKind),`,
        `UploadedByUserId = entity.UploadedByUserId,`
      - `ToEntity`: add `ArticleId = domain.ArticleId,`, `PublicationId = domain.PublicationId,`,
        `OwnerKind = domain.OwnerKind.ToString(),`, `UploadedByUserId = domain.UploadedByUserId,`
      _Acceptance: both mapping methods compile and round-trip all four new fields; no inline logic
      beyond `.ToString()` / `Enum.Parse`._
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Modify `Infrastructure/Services/MediaIngestionService.cs`** — in the `MediaFile` object
      initialiser inside `IngestSingleReferenceAsync`, add the three new fields:
      ```csharp
      OwnerKind = MediaOwnerKind.Article,
      PublicationId = null,
      UploadedByUserId = null,
      ```
      This is a purely additive field write; no behavior changes.
      _Acceptance: `Infrastructure` project compiles; existing ingestion logic is unchanged;
      article-owned media rows will be written with `OwnerKind = 'Article'`._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Build checkpoint — Core + Infrastructure** — run `dotnet build NewsParser.slnx`
      _Acceptance: zero new errors; `Core` and `Infrastructure` projects compile cleanly after
      the Phase 2 changes._

---

### Phase 3 — SQL constants

- [x] **Modify `Infrastructure/Persistence/Repositories/Sql/MediaFileSql.cs`** — update existing
      constants and add three new ones:
      1. Update `Insert` — add `"OwnerKind", "PublicationId", "UploadedByUserId"` to the column
         list and `@OwnerKind, @PublicationId, @UploadedByUserId` to the VALUES list.
      2. Update `GetByArticleId` SELECT column list to include `"OwnerKind"`, `"PublicationId"`,
         `"UploadedByUserId"`.
      3. Update `GetByIds` SELECT column list to include the same three columns.
      4. Add `GetByPublicationId` constant — full SELECT of all columns filtered by
         `"PublicationId" = @publicationId ORDER BY "CreatedAt"` (exact SQL from ADR §3).
      5. Add `GetById` constant — full SELECT of all columns filtered by `"Id" = @id LIMIT 1`
         (exact SQL from ADR §3).
      6. Add `Delete` constant — `DELETE FROM media_files WHERE "Id" = @id` (exact SQL from ADR §3).
      _Acceptance: file compiles; all six SELECT queries include all eleven columns
      (`Id`, `ArticleId`, `PublicationId`, `OwnerKind`, `UploadedByUserId`, `R2Key`, `OriginalUrl`,
      `ContentType`, `SizeBytes`, `Kind`, `CreatedAt`); no raw SQL strings in any repository method._
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

---

### Phase 4 — Repository interface and implementation

- [x] **Modify `Core/Interfaces/Repositories/IMediaFileRepository.cs`** — add three method
      signatures (ADR §3):
      ```csharp
      Task<List<MediaFile>> GetByPublicationIdAsync(Guid publicationId, CancellationToken cancellationToken = default);
      Task<MediaFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
      Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
      ```
      _Acceptance: interface compiles; no implementation in Core._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/MediaFileRepository.cs`** — three changes:
      1. Update `AddAsync` — include `OwnerKind`, `PublicationId`, `UploadedByUserId` in the
         anonymous parameters object passed to `CommandDefinition`.
      2. Implement `GetByPublicationIdAsync` — `QueryAsync<MediaFileEntity>` using
         `MediaFileSql.GetByPublicationId`, map via `.ToDomain()`.
      3. Implement `GetByIdAsync` — `QueryFirstOrDefaultAsync<MediaFileEntity>` using
         `MediaFileSql.GetById`, map via `entity?.ToDomain()`.
      4. Implement `DeleteAsync` — `ExecuteAsync` using `MediaFileSql.Delete` with `new { id }`.
      All methods use `IDbConnectionFactory.CreateOpenAsync` and `CommandDefinition` with
      `cancellationToken`.
      _Acceptance: class satisfies the full `IMediaFileRepository` interface; `AddAsync` passes
      the three new columns; no raw SQL strings in method bodies._
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Build checkpoint — Infrastructure repositories** — run `dotnet build NewsParser.slnx`
      _Acceptance: zero new errors; `Infrastructure` project compiles._

---

### Phase 5 — Storage interface and implementation

- [x] **Modify `Core/Interfaces/Storage/IMediaStorage.cs`** — add one method to the interface:
      ```csharp
      Task DeleteAsync(string key, CancellationToken cancellationToken = default);
      ```
      _Acceptance: interface compiles; `IDisposable` and `UploadAsync` are unchanged._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Storage/CloudflareR2Storage.cs`** — implement `DeleteAsync`:
      Use `AmazonS3Client.DeleteObjectAsync` with `BucketName` and the provided `key`.
      R2 returns 204 for both success and "not found", so no special-casing is needed —
      deletion is idempotent.
      _Acceptance: class satisfies the updated `IMediaStorage` interface; method compiles;
      no new fields introduced._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Build checkpoint — Core + Infrastructure storage** — run `dotnet build NewsParser.slnx`
      _Acceptance: zero new errors._

---

### Phase 6 — Options class

- [x] **Create `Infrastructure/Configuration/PublicationMediaOptions.cs`** — new options class
      (exact content from ADR §6):
      ```csharp
      public class PublicationMediaOptions
      {
          public const string SectionName = "PublicationMedia";
          public long MaxUploadBytes { get; set; } = 20 * 1024 * 1024;
          public int MaxFilesPerPublication { get; set; } = 10;
          public List<string> AllowedContentTypes { get; set; } =
          [
              "image/jpeg", "image/png", "image/webp", "image/gif", "video/mp4"
          ];
      }
      ```
      _Acceptance: file compiles in `Infrastructure.Configuration` namespace; `SectionName` const
      is `"PublicationMedia"`; no behavior, no DI wiring yet._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — in `AddStorage`,
      after the existing `CloudflareR2Options` registration, add:
      ```csharp
      services.Configure<PublicationMediaOptions>(configuration.GetSection(PublicationMediaOptions.SectionName));
      ```
      Registration of `IPublicationMediaService` comes in Phase 7 after the class exists.
      _Acceptance: file compiles; `PublicationMediaOptions` is bound from configuration._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Api/appsettings.Development.json`** — add `PublicationMedia` section with explicit
      defaults (values must match the class defaults so ops can tune without code change):
      ```json
      "PublicationMedia": {
        "MaxUploadBytes": 20971520,
        "MaxFilesPerPublication": 10,
        "AllowedContentTypes": ["image/jpeg","image/png","image/webp","image/gif","video/mp4"]
      }
      ```
      _Acceptance: JSON is valid; section name matches `PublicationMediaOptions.SectionName`._

---

### Phase 7 — Publication media service

- [x] **Create `Core/Interfaces/Services/IPublicationMediaService.cs`** — new interface with two
      methods (exact signatures from ADR §5):
      ```csharp
      Task<MediaFile> UploadAsync(
          Guid publicationId, Guid uploadedByUserId,
          Stream content, string fileName, string contentType,
          long sizeBytes, CancellationToken cancellationToken = default);

      Task DeleteAsync(
          Guid publicationId, Guid mediaFileId,
          Guid requestedByUserId, CancellationToken cancellationToken = default);
      ```
      _Acceptance: interface compiles; no implementation in Core._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Infrastructure/Services/PublicationMediaService.cs`** — implement
      `IPublicationMediaService` using primary-constructor style. Full algorithms are specified in
      ADR §5:

      **Constructor parameters:**
      `IPublicationRepository`, `IMediaFileRepository`, `IMediaStorage`,
      `IOptions<PublicationMediaOptions>`, `ILogger<PublicationMediaService>`

      **`UploadAsync` — 11 steps from ADR §5:**
      1. Load publication; throw `KeyNotFoundException` if null.
      2. Status guard: allowed statuses are `Created`, `ContentReady`, `Failed`;
         throw `InvalidOperationException` otherwise.
      3. Size guard: `sizeBytes > options.MaxUploadBytes` → `ArgumentException`.
      4. Content-type guard: `contentType` must be in `AllowedContentTypes`; for
         `application/octet-stream` fall back to extension sniffed from `fileName`;
         → `ArgumentException` if disallowed.
      5. Count guard: `GetByPublicationIdAsync` count >= `MaxFilesPerPublication`
         → `InvalidOperationException`.
      6. Derive `MediaKind` from `contentType` (`image/*` → `Image`, `video/*` → `Video`).
      7. Build `r2Key = $"publications/{publicationId}/{Guid.NewGuid()}{ext}"`.
      8. `content.Position = 0; await storage.UploadAsync(r2Key, ...)`.
      9. Construct the `MediaFile` object with **all** required fields set explicitly, then persist
         via `mediaFileRepository.AddAsync`. Use a compensating R2 delete if the DB insert fails:
         ```csharp
         var mediaFile = new MediaFile
         {
             Id = mediaId,
             OwnerKind = MediaOwnerKind.Publication,
             PublicationId = publicationId,
             UploadedByUserId = uploadedByUserId,
             ArticleId = null,
             OriginalUrl = string.Empty,
             CreatedAt = DateTimeOffset.UtcNow,
             R2Key = r2Key,
             ContentType = contentType,
             SizeBytes = sizeBytes,
             Kind = derivedKind
         };
         try { await mediaFileRepository.AddAsync(mediaFile, ct); }
         catch { await storage.DeleteAsync(r2Key, CancellationToken.None); throw; }
         ```
         Setting `ArticleId = null` and `OriginalUrl = string.Empty` is **required** by the
         CHECK constraint and the partial unique index introduced in the Phase 1 migration.
      10. Log at Information with all relevant fields.
      11. Return the saved `MediaFile`.

      **`DeleteAsync` — 7 steps from ADR §5:**
      1. Load media by id; throw `KeyNotFoundException` if null.
      2. Authorize: `OwnerKind != Publication || PublicationId != publicationId`
         → `InvalidOperationException`.
      3. Load publication; check mutable status (same whitelist); throw `InvalidOperationException`
         if not mutable.
      4. Try `storage.DeleteAsync(r2Key)` — on failure log a warning but continue (idempotent S3).
      5. `await mediaFileRepository.DeleteAsync(media.Id, ct)`.
      6. If the deleted ID is present in `publication.SelectedMediaFileIds`, strip it and call
         `publicationRepository.UpdateContentAndMediaAsync` with the updated list.
      7. Log at Information.

      _Acceptance: class satisfies `IPublicationMediaService`; step 9 initialises all eleven
      `MediaFile` fields explicitly (including `ArticleId = null` and `OriginalUrl = string.Empty`);
      compensating delete is present; no direct call to `IMediaStorage` from any controller; all
      guard clauses use the exact exception types from the ADR._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — in `AddStorage`,
      after the `PublicationMediaOptions` registration added in Phase 6, add:
      ```csharp
      services.AddScoped<IPublicationMediaService, PublicationMediaService>();
      ```
      _Acceptance: `IPublicationMediaService` resolves from DI; `Infrastructure` project compiles._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Build checkpoint — Core + Infrastructure services** — run `dotnet build NewsParser.slnx`
      _Acceptance: zero new errors; all three projects (Core, Infrastructure, Api) compile._

---

### Phase 8 — PublicationService eligibility validation

- [x] **Modify `Infrastructure/Services/PublicationService.cs`** — two changes:
      1. Add `IMediaFileRepository mediaFileRepository` to the primary constructor.
      2. In `UpdateContentAsync`, before the existing `UpdateContentAndMediaAsync` call, insert the
         eligibility validation pass from ADR §11:
         ```csharp
         var eligibleIds = (await mediaFileRepository.GetByPublicationIdAsync(publicationId, cancellationToken))
             .Select(m => m.Id)
             .Concat(publication.Event?.Articles.SelectMany(a => a.MediaFiles).Select(m => m.Id) ?? [])
             .ToHashSet();

         var invalid = selectedMediaFileIds.Where(id => !eligibleIds.Contains(id)).ToList();
         if (invalid.Count > 0)
             throw new ArgumentException($"Media file ids not eligible for this publication: {string.Join(",", invalid)}");
         ```
      _Acceptance: `PublicationService` compiles with the new constructor parameter;
      `UpdateContentAsync` rejects media ids from other publications/events; all other methods
      are unchanged._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Build checkpoint — Infrastructure** — run `dotnet build NewsParser.slnx`
      _Acceptance: zero new errors._

---

### Phase 9 — API layer

- [x] **Modify `Api/Models/ArticleDetailDto.cs`** — update `MediaFileDto` record:
      1. Change `Guid ArticleId` to `Guid? ArticleId`
      2. Add `string OwnerKind` as the last positional parameter
      Resulting signature:
      ```csharp
      public record MediaFileDto(
          Guid Id,
          Guid? ArticleId,
          string Url,
          string Kind,
          string ContentType,
          long SizeBytes,
          string OwnerKind
      );
      ```
      _Acceptance: file compiles; `ArticleDetailDto` and `EventDtos` that reference `MediaFileDto`
      continue to compile (positional record, callers must pass the new arg)._
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Mappers/MediaFileMapper.cs`** — update `ToDto` to pass `OwnerKind` as the
      seventh argument:
      ```csharp
      public static MediaFileDto ToDto(this MediaFile media, string publicBaseUrl) => new(
          media.Id,
          media.ArticleId,
          BuildUrl(publicBaseUrl, media.R2Key),
          media.Kind.ToString(),
          media.ContentType,
          media.SizeBytes,
          media.OwnerKind.ToString()
      );
      ```
      _Acceptance: mapper compiles; no inline mapping logic in controllers._
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Modify `Api/Controllers/PublicationsController.cs`** — four changes:
      1. Add `IMediaFileRepository mediaFileRepository` and `IPublicationMediaService publicationMediaService`
         to the primary constructor (alongside the existing parameters).
      2. Convert `ExtractAvailableMedia` from a `private static` sync method to a
         `private async Task<List<MediaFile>>` method named `ExtractAvailableMediaAsync`:
         ```csharp
         private async Task<List<MediaFile>> ExtractAvailableMediaAsync(
             Publication publication,
             CancellationToken cancellationToken)
         {
             var eventMedia = publication.Event is null
                 ? []
                 : publication.Event.Articles.SelectMany(a => a.MediaFiles).ToList();
             var customMedia = await mediaFileRepository.GetByPublicationIdAsync(
                 publication.Id, cancellationToken);
             return [.. eventMedia, .. customMedia];
         }
         ```
      3. Update all six existing callers of the old `ExtractAvailableMedia` (`GetById`,
         `UpdateContent`, `Approve`, `Reject`, `Send`, `Regenerate`) to `await ExtractAvailableMediaAsync(detail, cancellationToken)`.
      4. Add two new action methods (ADR §7):
         ```csharp
         [HttpPost("{id:guid}/media")]
         [RequestSizeLimit(25 * 1024 * 1024)]
         [Consumes("multipart/form-data")]
         public async Task<ActionResult<MediaFileDto>> UploadMedia(
             Guid id, IFormFile file,
             CancellationToken cancellationToken = default)

         [HttpDelete("{id:guid}/media/{mediaId:guid}")]
         public async Task<IActionResult> DeleteMedia(
             Guid id, Guid mediaId,
             CancellationToken cancellationToken = default)
         ```
         `UploadMedia` body:
         - Return `Unauthorized()` if `UserId is null`.
         - Return `BadRequest` if `file is null || file.Length == 0`.
         - Call `publicationMediaService.UploadAsync(id, UserId.Value, file.OpenReadStream(), file.FileName, file.ContentType, file.Length, cancellationToken)`.
         - Map result via `MediaFileMapper.ToDto(_publicBaseUrl)`.
         - Return `CreatedAtAction(nameof(GetById), new { id }, dto)`.

         `DeleteMedia` body:
         - Return `Unauthorized()` if `UserId is null`.
         - Call `publicationMediaService.DeleteAsync(id, mediaId, UserId.Value, cancellationToken)`.
         - Return `NoContent()`.

         Service exceptions (`KeyNotFoundException` → 404, `InvalidOperationException` → 409,
         `ArgumentException` → 400) are handled by `ExceptionMiddleware` — no try/catch in the
         controller.
      _Acceptance: Swagger shows `POST /publications/{id}/media` and
      `DELETE /publications/{id}/media/{mediaId}`; all existing endpoints compile and still return
      the combined event + custom media in `availableMedia`; no direct `IMediaStorage` call in the
      controller._
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Build checkpoint — full solution** — run `dotnet build NewsParser.slnx`
      _Acceptance: zero new errors across Core, Infrastructure, Api, and Worker._

---

### Phase 10 — UI

- [x] **Regenerate the OpenAPI client** — run `npm run generate-api` inside `UI/` after the Phase 9
      backend changes are built and the API is running (or a fresh Swagger JSON is available).
      _Acceptance: `UI/src/api/generated/` is updated; the generated `MediaFileDto` type has
      `articleId: string | null` and `ownerKind: string`; TypeScript compiles after regeneration
      with no new errors. If `UI/src/features/publications/types.ts` is a hand-edited mirror that
      must also be kept in sync, reconcile it after this step (see next task)._

- [x] **Modify `UI/src/features/publications/types.ts`** — two changes in `MediaFileDto`:
      1. Change `articleId: string` to `articleId: string | null`
      2. Add `ownerKind: string` as a new field
      _Acceptance: TypeScript compiles (`npm run build`); no `any` types; the shape matches the
      newly regenerated generated client; existing usages of `MediaFileDto` in the UI that do not
      dereference `articleId` continue to compile._

- [x] **Modify `UI/src/features/publications/usePublicationMutations.ts`** — add two new
      mutations (ADR §13.5):
      ```ts
      const uploadMedia = useMutation({
        mutationFn: (file: File) => {
          const form = new FormData()
          form.append('file', file)
          return apiClient
            .post<MediaFileDto>(`/publications/${publicationId}/media`, form, {
              headers: { 'Content-Type': 'multipart/form-data' },
            })
            .then(r => r.data)
        },
        onSuccess: () => {
          toast('Media uploaded', 'success')
          invalidateDetail()
        },
        onError: () => toast('Failed to upload media', 'error'),
      })

      const deleteMedia = useMutation({
        mutationFn: (mediaId: string) =>
          apiClient
            .delete(`/publications/${publicationId}/media/${mediaId}`)
            .then(r => r.data),
        onSuccess: () => {
          toast('Media deleted', 'success')
          invalidateDetail()
        },
        onError: () => toast('Failed to delete media', 'error'),
      })
      ```
      Export both from the return object alongside the existing mutations.
      _Acceptance: TypeScript compiles; both mutations invalidate `['publication', publicationId]`
      on success._

- [x] **Modify `UI/src/features/publications/PublicationDetailPage.tsx`** — three UI additions in
      the MEDIA section (lines 412–462, ADR §13):
      1. **Upload button** — a styled "UPLOAD CUSTOM MEDIA" button that triggers a hidden
         `<input type="file" accept="image/*,video/mp4" />`. On file selection, validate size
         (≤ 20 MB) and count (existing `ownerKind === 'Publication'` tiles + 1 ≤ 10) client-side
         as a UX hint, then call `uploadMedia.mutate(file)`. Button is disabled when `!canEdit`
         or `uploadMedia.isPending`.
      2. **Per-tile delete button** — render an `×` overlay on each media tile where
         `media.ownerKind === 'Publication'`. Clicking (when `canEdit`) calls
         `deleteMedia.mutate(media.id)` after user confirmation via the existing `ConfirmDialog`
         pattern. Disable the tile's click-to-select during pending delete.
      3. **CUSTOM badge** — on publication-owned tiles, render a small `CUSTOM` caps-tag using the
         same inline-style typography as the existing `SELECTED` badge, positioned in the top-left
         corner.
      Ensure the MEDIA section renders even when `availableMedia` is empty if `canEdit` is true
      (to allow uploading the first file on a publication with no event media).
      _Acceptance: TypeScript compiles (`npm run build`); upload button visible and functional
      when `canEdit`; `CUSTOM` badge appears only on `ownerKind === 'Publication'` tiles; delete
      `×` button appears only on publication-owned tiles; event-pool tiles are unchanged._

- [x] **Build checkpoint — UI** — run `npm run build` inside `UI/`
      _Acceptance: TypeScript type-check and Vite build complete with zero errors._

---

### Phase 11 — Tests (delegated to test-writer)

- [ ] **Create `Tests/Infrastructure.Tests/Services/PublicationMediaServiceTests.cs`** _Delegated to test-writer agent_ — unit tests
      for `PublicationMediaService` with mocked dependencies:
      - Happy-path upload: file written to R2, row persisted, returned `MediaFile` has
        `OwnerKind = Publication`.
      - Size exceeds `MaxUploadBytes` → `ArgumentException`.
      - Content-type not in `AllowedContentTypes` → `ArgumentException`.
      - Per-publication count at `MaxFilesPerPublication` → `InvalidOperationException`.
      - Publication not found → `KeyNotFoundException`.
      - Publication in non-mutable status → `InvalidOperationException`.
      - DB insert failure triggers compensating `storage.DeleteAsync`.
      - Delete happy path: R2 deleted, DB row deleted, `SelectedMediaFileIds` updated.
      - Delete of article-owned media via the publication endpoint → `InvalidOperationException`.
      - Delete when media id is in `SelectedMediaFileIds` strips it from the list.
      _Acceptance: all cases pass; Moq used for all interfaces; AAA pattern; no real DB or R2._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Infrastructure.Tests/Services/PublicationServiceTests.cs`** — add one new _Delegated to test-writer agent_
      test case for `UpdateContentAsync`: selecting a media id that belongs to neither the
      publication's event media nor its custom media → `ArgumentException`.
      Also update any existing test fixtures that construct `MediaFile` objects to set the new
      required fields (`OwnerKind = Article`, `PublicationId = null`, `UploadedByUserId = null`).
      _Acceptance: new case passes; no existing tests broken; fixtures compile._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Create `Tests/Infrastructure.Tests/Repositories/MediaFileRepositoryContractTests.cs`** _Delegated to test-writer agent_
      (or modify if it already exists) — repository contract tests against a real test database:
      - `AddAsync` with `OwnerKind = Publication` round-trips correctly.
      - `GetByPublicationIdAsync` returns only publication-owned rows.
      - CHECK constraint rejects a row with both FKs set (or neither set).
      - `DeleteAsync` removes the row; second call is idempotent (no exception).
      _Acceptance: all tests pass against a local Postgres with the migration applied._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Api.Tests/Controllers/PublicationsControllerTests.cs`** _Delegated to test-writer agent_ — add tests for
      the two new endpoints:
      - `POST /{id}/media` multipart → 201 with `MediaFileDto`.
      - `POST /{id}/media` by non-editor → 403.
      - `POST /{id}/media` with unknown publication id → 404.
      - `POST /{id}/media` with empty file → 400.
      - `DELETE /{id}/media/{mediaId}` happy path → 204.
      - `DELETE /{id}/media/{mediaId}` for article-owned media → 409.
      Also update any fixture `MediaFile` objects to include the new fields.
      _Acceptance: all new tests pass; existing tests unchanged._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify existing worker/ingestion tests** _Delegated to test-writer agent_ — update any test fixture that creates a
      `MediaFile` or calls `GetByIdsAsync` to include `OwnerKind`, `PublicationId`,
      `UploadedByUserId` in the fixture data so previously-passing tests continue to pass.
      Specifically check `Tests/Worker.Tests/` and any `MediaIngestionService` tests.
      _Acceptance: `dotnet test` reports zero failures._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Create `UI/src/features/publications/__tests__/PublicationDetailPage.test.tsx`** _Delegated to test-writer agent_ (or modify
      if the file already exists) — React Testing Library tests covering the custom media UI (ADR
      §13 and §Testing):
      - Upload button triggers `uploadMedia` mutation when a file is selected.
      - Per-tile delete `×` button is visible only when `media.ownerKind === 'Publication'`; it is
        absent on event-pool tiles.
      - Clicking `×` on a publication-owned tile opens `ConfirmDialog`; confirming calls
        `deleteMedia` mutation with the correct `mediaId`.
      - Upload button and delete `×` are absent (or disabled) when `canEdit` is `false`.
      _Acceptance: all four cases pass; no real network calls (mutations mocked via
      `vi.mock`/`jest.mock` or MSW); TypeScript compiles._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 12 — Final build and test run

- [x] **Run `dotnet build NewsParser.slnx`** — full solution build after all phases.
      _Acceptance: `Build succeeded` with 0 errors across all projects._

- [x] **Run `dotnet test`** — all tests pass including new ones.
      _Acceptance: 0 failures; new test classes appear in the output._
