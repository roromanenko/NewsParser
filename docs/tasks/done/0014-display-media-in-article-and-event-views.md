# Display Media in Article and Event Views

## Goal

Expose `MediaFile` rows through the existing detail DTOs and render images and videos on the Article
detail page and the Event detail page, following the dark-burgundy card aesthetic.

## Affected Layers

- Core / Infrastructure / Api / UI

---

## Tasks

### Core

- [x] **Modify `Core/DomainModels/Article.cs`** — add navigation property
      `public List<MediaFile> MediaFiles { get; set; } = [];` after the existing
      `MediaReferences` property.
      _Acceptance: file compiles with no EF or infrastructure references; `Article` carries an
      empty-initialized `MediaFiles` collection._
      _Skill: `.claude/skills/code-conventions/SKILL.md`_

### Infrastructure

- [x] **Modify `Infrastructure/Persistence/Entity/ArticleEntity.cs`** — add
      `public List<MediaFileEntity> MediaFiles { get; set; } = [];` after `AddedToEventAt`.
      _Acceptance: entity compiles; no EF configuration references in this file._
      _Skill: `.claude/skills/ef-core-conventions/SKILL.md`_

- [x] **Modify `Infrastructure/Persistence/Configurations/ArticleConfiguration.cs`** — replace
      the existing unidirectional `HasMany<MediaFileEntity>` (declared in the prior ADR 0006
      Ignore clause section) with the navigation-based form:
      ```csharp
      builder.HasMany(a => a.MediaFiles)
             .WithOne()
             .HasForeignKey(m => m.ArticleId)
             .OnDelete(DeleteBehavior.Cascade);
      ```
      Remove the `builder.Ignore(a => a.MediaReferences);` line that was added during the
      ADR 0006 pipeline work if it conflicts, but do not remove the `Tags` / `KeyFacts` jsonb
      properties or any index.
      _Acceptance: `dotnet ef migrations add DisplayMediaNavigation` produces an **empty** (no-op)
      migration — the FK and cascade already exist in the schema. If EF produces a non-empty diff,
      stop and investigate before proceeding._
      _Note: Migration was non-empty because the `media_files` table had not been created by any
      prior migration. The migration creates the table and FK as expected._
      _Skill: `.claude/skills/ef-core-conventions/SKILL.md`_

- [x] **Add EF migration `DisplayMediaNavigation`** — run
      `dotnet ef migrations add DisplayMediaNavigation --project Infrastructure --startup-project Api`
      from the solution root. Verify the generated `Up` and `Down` methods are empty (schema is
      unchanged; the migration documents the model change only).
      _Acceptance: migration file exists in `Infrastructure/Persistence/Migrations/`; `Up` and
      `Down` bodies contain no schema-altering statements._
      _Note: Migration creates `media_files` table — it was not present in prior migrations._
      _Skill: `.claude/skills/ef-core-conventions/SKILL.md`_

- [x] **Modify `Infrastructure/Persistence/Mappers/ArticleMapper.cs`** — extend `ToDomain` to
      map `MediaFiles`:
      ```csharp
      MediaFiles = entity.MediaFiles?.Select(m => m.ToDomain()).ToList() ?? [],
      ```
      Do **not** modify `ToEntity` or `FromAnalysisResult` — media rows are written exclusively
      by `MediaIngestionService`.
      _Acceptance: `ToDomain` populates `MediaFiles` from the entity collection; `ToEntity` is
      unchanged; file compiles._
      _Skill: `.claude/skills/ef-core-conventions/SKILL.md`_

- [x] **Modify `Infrastructure/Persistence/Repositories/ArticleRepository.cs`** — add
      `.Include(a => a.MediaFiles)` to the `GetByIdAsync` query only:
      ```csharp
      var entity = await _context.Articles
          .Include(a => a.MediaFiles)
          .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
      ```
      Leave `GetAnalysisDoneAsync`, `GetPendingAsync`, `GetPendingForClassificationAsync`, and
      all other methods untouched.
      _Acceptance: `GetByIdAsync` returns a populated `MediaFiles` list when media rows exist;
      no other method is modified._
      _Skill: `.claude/skills/ef-core-conventions/SKILL.md`_

- [x] **Modify `Infrastructure/Persistence/Repositories/EventRepository.cs`** — add
      `.ThenInclude(a => a.MediaFiles)` chained onto the existing `Include(e => e.Articles)`
      inside `GetDetailAsync` only:
      ```csharp
      .Include(e => e.Articles)
          .ThenInclude(a => a.MediaFiles)
      ```
      Leave `GetByIdAsync`, `GetPagedAsync`, `GetActiveEventsAsync`, `FindSimilarEventsAsync`,
      `GetWithContextAsync`, and all other methods untouched.
      _Acceptance: `GetDetailAsync` returns `MediaFiles` on each nested article; all other
      methods are unchanged._
      _Skill: `.claude/skills/ef-core-conventions/SKILL.md`_

### Api

- [x] **Modify `Api/Models/ArticleDetailDto.cs`** — add the new `MediaFileDto` record to the
      same file (above `ArticleDetailDto`) and add `List<MediaFileDto> Media` as the last
      positional parameter of `ArticleDetailDto`:
      ```csharp
      public record MediaFileDto(
          Guid Id,
          Guid ArticleId,
          string Url,
          string Kind,
          string ContentType,
          long SizeBytes
      );
      ```
      `ArticleDetailDto` gains `List<MediaFileDto> Media` after the existing `ArticleEventDto? Event`
      parameter. Do **not** create a separate `MediaDtos.cs` file.
      _Acceptance: both records compile; `MediaFileDto` is in the `Api.Models` namespace; Swagger
      regeneration will include `MediaFileDto` and the new `media` field._
      _Skill: `.claude/skills/api-conventions/SKILL.md`_

- [x] **Modify `Api/Models/EventDtos.cs`** — add `List<MediaFileDto> Media` as the last
      positional parameter of `EventArticleDto`. `MediaFileDto` is already defined in
      `ArticleDetailDto.cs` in the same namespace — no new record needed here.
      _Acceptance: `EventArticleDto` compiles with the new `Media` parameter; `EventDetailDto`,
      `EventListItemDto`, and all other records in the file are unchanged._
      _Skill: `.claude/skills/api-conventions/SKILL.md`_

- [x] **Create `Api/Mappers/MediaFileMapper.cs`** — static class `MediaFileMapper` with one
      public extension method `ToDto(this MediaFile media, string publicBaseUrl)` returning
      `MediaFileDto`, and a private static helper `BuildUrl(string publicBaseUrl, string r2Key)`
      that trims trailing slash from the base and leading slash from the key before concatenating:
      ```csharp
      $"{publicBaseUrl.TrimEnd('/')}/{r2Key.TrimStart('/')}"
      ```
      _Acceptance: static class with no constructor, no I/O, no DI; `BuildUrl` handles
      trailing-slash / leading-slash edge cases; file compiles._
      _Skill: `.claude/skills/mappers/SKILL.md`_

- [x] **Modify `Api/Mappers/ArticleMapper.cs`** — update `ToDetailDto` signature to accept
      `string publicBaseUrl` as the first parameter after `this`:
      ```csharp
      public static ArticleDetailDto ToDetailDto(
          this Article article,
          string publicBaseUrl,
          Event? evt = null)
      ```
      Inside the method body add:
      ```csharp
      Media = article.MediaFiles
          .Select(m => m.ToDto(publicBaseUrl))
          .ToList()
      ```
      as the last field in the `ArticleDetailDto` constructor call.
      _Acceptance: `ToDetailDto` compiles with the new parameter order; existing `Event? evt`
      remains optional; `ToListItemDto` is unchanged._
      _Skill: `.claude/skills/mappers/SKILL.md`_

- [x] **Modify `Api/Mappers/EventMapper.cs`** — update `ToDetailDto` and `ToEventArticleDto`
      to accept and thread `string publicBaseUrl`:
      - `ToDetailDto(this Event evt, string publicBaseUrl)` — passes `publicBaseUrl` through
        to each `a.ToEventArticleDto(publicBaseUrl)` call.
      - `ToEventArticleDto(this Article article, string publicBaseUrl)` — adds
        `Media = article.MediaFiles.Select(m => m.ToDto(publicBaseUrl)).ToList()` as the last
        field.
      `ToListItemDto` must **not** be modified — it has no `publicBaseUrl` parameter and no
      `Media` field.
      _Acceptance: both updated methods compile; `ToListItemDto` signature is unchanged;
      `Approve` and `Reject` controller actions that call `.ToListItemDto()` still compile
      without passing a base URL._
      _Skill: `.claude/skills/mappers/SKILL.md`_

- [x] **Modify `Api/Controllers/ArticlesController.cs`** — add
      `IOptions<CloudflareR2Options> r2Options` to the primary constructor parameter list and
      extract the base URL as a field:
      ```csharp
      public class ArticlesController(
          IArticleRepository articleRepository,
          IEventRepository eventRepository,
          IOptions<CloudflareR2Options> r2Options) : BaseController
      {
          private readonly string _publicBaseUrl = r2Options.Value.PublicBaseUrl;
          ...
      }
      ```
      Update the `GetById` action to pass `_publicBaseUrl` into the mapper:
      ```csharp
      return Ok(article.ToDetailDto(_publicBaseUrl, relatedEvent));
      ```
      Add `using Infrastructure.Configuration;` and `using Microsoft.Extensions.Options;` if
      not already present.
      _Acceptance: controller compiles; `GET /articles/{id}` response includes a `media` array;
      no new routes are added._
      _Skill: `.claude/skills/api-conventions/SKILL.md`_

- [x] **Modify `Api/Controllers/EventsController.cs`** — identical pattern to `ArticlesController`:
      add `IOptions<CloudflareR2Options> r2Options` to the primary constructor, store
      `private readonly string _publicBaseUrl = r2Options.Value.PublicBaseUrl;`, and update
      `GetById` to call `evt.ToDetailDto(_publicBaseUrl)`.
      `Approve` and `Reject` call `.ToListItemDto()` which takes no base URL — leave them
      untouched.
      Add `using Infrastructure.Configuration;` and `using Microsoft.Extensions.Options;` if
      not already present.
      _Acceptance: controller compiles; `GET /events/{id}` response includes `media` arrays on
      each article; `Approve` / `Reject` are unmodified._
      _Skill: `.claude/skills/api-conventions/SKILL.md`_

- [x] **Build the backend** — run `dotnet build` from the solution root and confirm zero errors.
      Surface any mapper call sites that still pass the old `ToDetailDto` signature (expected
      blast radius: one caller per mapper).
      _Acceptance: `dotnet build` exits with code 0; no CS errors about argument count or type
      mismatch on `ToDetailDto`._

### Tests

- [ ] **Create `Tests/Api.Tests/Mappers/MediaFileMapperTests.cs`** — NUnit fixture covering
      `BuildUrl` for four cases: (1) trailing slash on base only, (2) leading slash on key only,
      (3) both slashes present, (4) neither slash present; plus one full `ToDto` round-trip test
      that asserts every field including the constructed URL.
      _Acceptance: all tests pass; no production code is modified._
      _Agent: test-writer_
      _Delegated to test-writer agent_
      _Skill: `.claude/skills/testing/SKILL.md`_

- [ ] **Modify `Tests/Api.Tests/Controllers/EventsControllerTests.cs`** — add
      `IOptions<CloudflareR2Options>` mock setup to the `WebApplicationFactory` configuration
      (replacing or supplementing the existing service registrations) so the controller can
      resolve `_publicBaseUrl` without throwing at startup.
      _Acceptance: all existing `EventsControllerTests` tests continue to pass after the
      controller constructor change._
      _Agent: test-writer_
      _Delegated to test-writer agent_
      _Skill: `.claude/skills/testing/SKILL.md`_

- [ ] **Create `Tests/Api.Tests/Mappers/ArticleMapperTests.cs`** — NUnit fixture covering:
      (1) `ToDetailDto` with empty `MediaFiles` returns `Media = []`; (2) `ToDetailDto` with
      two `MediaFile` objects returns a `Media` list with correct URLs (base URL threaded
      through); (3) `ToListItemDto` is unaffected (no `media` field).
      _Acceptance: all tests pass._
      _Agent: test-writer_
      _Delegated to test-writer agent_
      _Skill: `.claude/skills/testing/SKILL.md`_

- [ ] **Create `Tests/Api.Tests/Mappers/EventMapperTests.cs`** — NUnit fixture covering:
      (1) `ToEventArticleDto` populates `Media` per article; (2) `ToDetailDto` aggregates media
      across all articles; (3) `ToListItemDto` has no `Media` field and its signature has not
      changed.
      _Acceptance: all tests pass._
      _Agent: test-writer_
      _Delegated to test-writer agent_
      _Skill: `.claude/skills/testing/SKILL.md`_

- [ ] **Create `Tests/Infrastructure.Tests/Repositories/ArticleRepositoryGetByIdWithMediaTests.cs`**
      — NUnit fixture using `TestNewsParserDbContext` (EF InMemory) that seeds one `ArticleEntity`
      with two `MediaFileEntity` rows and asserts `GetByIdAsync` returns both in `MediaFiles`.
      Also asserts `GetAnalysisDoneAsync` does **not** populate `MediaFiles` (regression guard).
      _Acceptance: all tests pass; existing repository tests are unaffected._
      _Agent: test-writer_
      _Delegated to test-writer agent_
      _Skill: `.claude/skills/testing/SKILL.md`_

- [ ] **Create `Tests/Infrastructure.Tests/Repositories/EventRepositoryGetDetailWithMediaTests.cs`**
      — NUnit fixture using `TestNewsParserDbContext` that seeds one `EventEntity` with two
      `ArticleEntity` rows each carrying one `MediaFileEntity`, then calls `GetDetailAsync` and
      asserts both articles' `MediaFiles` are populated. Also calls `GetPagedAsync` and asserts
      `MediaFiles` is empty on every article (regression guard: media must not leak into list
      queries).
      _Acceptance: all tests pass._
      _Agent: test-writer_
      _Delegated to test-writer agent_
      _Skill: `.claude/skills/testing/SKILL.md`_

- [ ] **Run `dotnet test` from the solution root** — confirm all tests in `Api.Tests` and
      `Infrastructure.Tests` are green.
      _Acceptance: `dotnet test` exits with code 0; no regressions in existing test fixtures._
      _Delegated to test-writer agent_

### UI

- [ ] **Regenerate the TypeScript API client** — with the backend running on port 5172, run
      `npm run generate-api` from `UI/`. Confirm `src/api/generated/` contains `MediaFileDto`
      and that `ArticleDetailDto` and `EventArticleDto` include a `media` field.
      **Do not hand-edit any file under `src/api/generated/`.**
      _Acceptance: `npm run build` in `UI/` passes after regeneration; `MediaFileDto` type is
      present in the generated output._
      _Note: Must be done manually with the backend running on port 5172._

- [x] **Create `UI/src/components/shared/MediaGallery.tsx`** — presentational component
      (no hooks, no data fetching) with props:
      ```ts
      type MediaItem = { id: string; url: string; kind: 'Image' | 'Video'; contentType: string }
      type Props = { items: MediaItem[]; title?: string }
      ```
      Behavior:
      - Return `null` when `items.length === 0`.
      - Render a card with `background: 'rgba(61,15,15,0.4)'` and
        `borderColor: 'rgba(255,255,255,0.1)'` matching the existing section cards.
      - Section label uses `font-caps text-[10px] tracking-widest` with color `#6b7280`;
        defaults to `'MEDIA'` when `title` is `undefined`.
      - Grid: `grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3`; each cell is `aspect-video`.
      - `Image` items: `<a href={url} target="_blank" rel="noopener noreferrer">` wrapping
        `<img src={url} alt="" loading="lazy" className="w-full h-full object-cover" />`.
        On `onError`, replace cell content with a caramel-colored `BROKEN` caps label.
      - `Video` items: `<video src={url} controls preload="metadata"
        className="w-full h-full object-cover" />`. No autoplay.
      - Each cell has `border` with `borderColor: 'rgba(255,255,255,0.1)'` and
        `background: 'var(--near-black)'` for letterbox fallback.
      _Acceptance: `npm run build` passes; component renders nothing for empty `items`; no
      TypeScript `any` types; no imports from `src/api/generated/`._
      _Skill: `.claude/skills/clean-code/SKILL.md`_

- [x] **Modify `UI/src/features/articles/ArticleDetailPage.tsx`** — import `MediaGallery` from
      `@/components/shared/MediaGallery` and render it below the Key Facts card (still inside the
      `lg:col-span-2` column). Derive `mediaItems` inline before the return statement:
      ```ts
      const mediaItems = (article.media ?? []).map(m => ({
        id: m.id!,
        url: m.url!,
        kind: m.kind as 'Image' | 'Video',
        contentType: m.contentType!
      }))
      ```
      Render `<MediaGallery items={mediaItems} />` as a new sibling after the Key Facts block.
      No layout changes to the two-column grid or any other section.
      _Acceptance: `npm run build` passes; TypeScript compiles with zero errors; the media card
      is absent when the article has no media and present when it does._
      _Note: `article.media` TS error will resolve after `npm run generate-api`._
      _Skill: `.claude/skills/clean-code/SKILL.md`_

- [x] **Modify `UI/src/features/events/EventDetailPage.tsx`** — make the following four changes:
      1. Extend the `Tab` type: `type Tab = 'timeline' | 'updates' | 'contradictions' | 'media'`.
      2. Add a `useMemo` aggregation above the `tabs` array that deduplicates by `MediaFileDto.Id`:
      3. Add `{ key: 'media', label: 'MEDIA', count: mediaItems.length }` to the `tabs` array.
      4. Add a co-located `MediaTab` function component and render it in the tab-content panel.
      _Acceptance: `npm run build` passes; fourth tab appears; empty-state message shows when no
      media; `useMemo` dependency array is `[articles]` only._
      _Note: `a.media` TS error will resolve after `npm run generate-api`._
      _Skill: `.claude/skills/clean-code/SKILL.md`_

- [ ] **Run `npm run lint` and `npm run build` from `UI/`** — confirm zero TypeScript errors and
      zero ESLint errors introduced by the new files.
      _Acceptance: both commands exit with code 0._
      _Note: Two TS errors (`article.media`, `a.media`) will persist until `npm run generate-api`
      is run with the backend on port 5172._

## Open Questions

- None. The ADR specifies exact file paths, method signatures, component behavior, and ordering.
  No design decisions are deferred to the implementer.
