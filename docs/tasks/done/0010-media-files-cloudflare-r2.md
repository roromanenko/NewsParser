# Media Files Support with Cloudflare R2 Storage

## Goal

Download images and short videos referenced by RSS feed items during article ingestion,
upload them to Cloudflare R2 (S3-compatible), and persist metadata in a new
`media_files` PostgreSQL table — best-effort, so media failures never block article saves.

## Affected Layers

- Core
- Infrastructure
- Worker

## ADR Reference

`docs/architecture/decisions/0006-media-files-cloudflare-r2.md`

---

## Tasks

### Step 1 — Configuration contract (lock first)

- [x] **Modify `Infrastructure/Configuration/CloudflareR2Options.cs`** (new file) — create
      the Options class with `SectionName = "CloudflareR2"` and properties: `AccountId`,
      `AccessKeyId`, `SecretAccessKey`, `BucketName`, `PublicBaseUrl` (all `string.Empty`
      defaults), `MaxFileSizeBytes` (`50 * 1024 * 1024`), `DownloadTimeoutSeconds` (`30`).
      _Acceptance: file compiles; class is in namespace `Infrastructure.Configuration`;
      `public const string SectionName = "CloudflareR2"` is present; all properties have
      sensible non-throwing defaults_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Api/appsettings.Development.json`** — add the `"CloudflareR2"` section with
      placeholder dev values:
      ```json
      "CloudflareR2": {
        "AccountId": "dev-placeholder-account-id",
        "AccessKeyId": "dev-placeholder-access-key",
        "SecretAccessKey": "dev-placeholder-secret-key",
        "BucketName": "newsparser-media-dev",
        "PublicBaseUrl": "https://pub-placeholder.r2.dev",
        "MaxFileSizeBytes": 52428800,
        "DownloadTimeoutSeconds": 30
      }
      ```
      _Acceptance: file is valid JSON; the `"CloudflareR2"` key appears at the root level;
      all other existing keys are unchanged_

- [x] **Modify `Worker/appsettings.Development.json`** — add the identical `"CloudflareR2"`
      placeholder section (same values as above).
      _Acceptance: file is valid JSON; the `"CloudflareR2"` key appears at the root level;
      all other existing keys are unchanged_

---

### Step 2 — NuGet package

- [x] **Modify `Infrastructure/Infrastructure.csproj`** — add
      `<PackageReference Include="AWSSDK.S3" Version="3.*" />` inside the existing
      `<ItemGroup>` that holds other `PackageReference` entries.
      _Acceptance: `dotnet restore` succeeds; `AWSSDK.S3` appears in the restored packages;
      no other `<PackageReference>` lines are changed_

---

### Step 3 — Core domain models

- [x] **Create `Core/DomainModels/MediaFile.cs`** — domain model with all `init` properties:
      `Guid Id`, `Guid ArticleId`, `string R2Key`, `string OriginalUrl`, `string ContentType`,
      `long SizeBytes`, `MediaKind Kind`, `DateTimeOffset CreatedAt`.
      Define `MediaKind` enum (`Image`, `Video`) in the same file.
      _Acceptance: file compiles; no references to EF Core, Infrastructure, or
      `System.Net.Http`; all properties are `init`; `MediaKind` has exactly two values_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/DomainModels/MediaReference.cs`** — immutable record:
      `public record MediaReference(string Url, MediaKind Kind, string? DeclaredContentType);`
      _Acceptance: file compiles; `MediaKind` from `MediaFile.cs` is reused; no Infrastructure
      references_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step 4 — Core interfaces

- [x] **Create `Core/Interfaces/Storage/IMediaStorage.cs`** — interface with one method:
      ```csharp
      Task UploadAsync(
          string key,
          Stream content,
          string contentType,
          CancellationToken cancellationToken = default);
      ```
      Place in new subdirectory `Core/Interfaces/Storage/`.
      _Acceptance: file compiles; interface is in namespace `Core.Interfaces.Storage`;
      no implementation details or Infrastructure references_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/Interfaces/Repositories/IMediaFileRepository.cs`** — interface with three
      methods:
      ```csharp
      Task AddAsync(MediaFile mediaFile, CancellationToken cancellationToken = default);
      Task<List<MediaFile>> GetByArticleIdAsync(Guid articleId, CancellationToken cancellationToken = default);
      Task<bool> ExistsByArticleAndUrlAsync(Guid articleId, string originalUrl, CancellationToken cancellationToken = default);
      ```
      _Acceptance: file compiles; interface is in namespace `Core.Interfaces.Repositories`;
      `CancellationToken cancellationToken = default` is the last parameter on every method_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Create `Core/Interfaces/Services/IMediaIngestionService.cs`** — interface with one
      method:
      ```csharp
      Task IngestForArticleAsync(
          Guid articleId,
          IReadOnlyList<MediaReference> references,
          CancellationToken cancellationToken = default);
      ```
      _Acceptance: file compiles; interface is in namespace `Core.Interfaces.Services`;
      no Infrastructure references_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step 5 — Infrastructure persistence layer

- [x] **Create `Infrastructure/Persistence/Entity/MediaFileEntity.cs`** — EF entity class
      with properties matching the `media_files` schema: `Guid Id`, `Guid ArticleId`,
      `string R2Key`, `string OriginalUrl`, `string ContentType`, `long SizeBytes`,
      `string Kind` (stored as string, not enum), `DateTimeOffset CreatedAt`.
      _Acceptance: file compiles; class is in namespace `Infrastructure.Persistence.Entity`;
      `Kind` is `string`, not `MediaKind`; no EF attributes on the class (all config goes in
      `MediaFileConfiguration`)_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Create `Infrastructure/Persistence/Configurations/MediaFileConfiguration.cs`** — EF
      `IEntityTypeConfiguration<MediaFileEntity>` that:
      - Sets PK on `Id`.
      - Maps to table `"media_files"` via `ToTable`.
      - Configures FK `ArticleId → articles.id` with `OnDelete(DeleteBehavior.Cascade)` on
        the `ArticleConfiguration` side (add `builder.HasMany<MediaFileEntity>().WithOne().HasForeignKey(m => m.ArticleId).OnDelete(DeleteBehavior.Cascade)` to `ArticleConfiguration.cs`).
      - Adds `HasIndex(m => m.ArticleId)`.
      - Adds composite unique index `HasIndex(m => new { m.ArticleId, m.OriginalUrl }).IsUnique()`.
      - Stores `Kind` as `text` with `HasConversion<string>()` (no-op since it is already
        `string`, but explicit for consistency).
      _Acceptance: `dotnet build` succeeds; `MediaFileConfiguration` is picked up automatically
      by `ApplyConfigurationsFromAssembly` in `NewsParserDbContext`_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Configurations/ArticleConfiguration.cs`** — add
      the FK relationship from `ArticleEntity` to `MediaFileEntity` with cascade delete,
      and add `builder.Ignore` for the upcoming `MediaReferences` transient property:
      ```csharp
      builder
          .HasMany<MediaFileEntity>()
          .WithOne()
          .HasForeignKey(m => m.ArticleId)
          .OnDelete(DeleteBehavior.Cascade);
      ```
      (The `builder.Ignore(a => a.MediaReferences)` line will be added in Step 10 when
      the property is added to `Article`; note it here so the implementer knows both changes
      touch this file.)
      _Acceptance: file compiles; no navigation collection on `ArticleEntity` is required;
      existing indexes and conversions are unchanged_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/DataBase/NewsParserDbContext.cs`** — add
      `public DbSet<MediaFileEntity> MediaFiles { get; set; }` after the existing
      `DbSet` properties.
      _Acceptance: file compiles; `MediaFiles` DbSet is present; no other properties changed_

- [x] **Create `Infrastructure/Persistence/Mappers/MediaFileMapper.cs`** — static class
      `MediaFileMapper` with:
      - `ToDomain(this MediaFileEntity entity) => new MediaFile { ... }` — expression body.
      - `ToEntity(this MediaFile domain) => new MediaFileEntity { ... }` — expression body.
      `Kind` maps via `domain.Kind.ToString()` (entity) and `Enum.Parse<MediaKind>(entity.Kind)`
      (domain). No I/O in either method.
      _Acceptance: file compiles; class is `static`; both methods are expression-bodied; no
      `HttpClient`, no `DbContext`, no `async`_
      _Skill: .claude/skills/mappers/SKILL.md_

---

### Step 6 — EF migration

- [x] **Generate EF migration `AddMediaFiles`** — run
      `dotnet ef migrations add AddMediaFiles --project Infrastructure --startup-project Api`
      from the solution root. Verify the generated `Up` method creates table `media_files`
      with columns `id`, `article_id`, `r2_key`, `original_url`, `content_type`,
      `size_bytes`, `kind`, `created_at`, index on `article_id`, and unique index on
      `(article_id, original_url)`.
      _Acceptance: migration file exists under
      `Infrastructure/Persistence/Migrations/<timestamp>_AddMediaFiles.cs`; `Up` and `Down`
      are non-empty; `dotnet build` succeeds after generation_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

---

### Step 7 — Repository implementation and DI registration

- [x] **Create `Infrastructure/Persistence/Repositories/MediaFileRepository.cs`** — implement
      `IMediaFileRepository` using primary-constructor style:
      ```csharp
      public class MediaFileRepository(NewsParserDbContext context) : IMediaFileRepository
      ```
      - `AddAsync`: `context.MediaFiles.Add(entity); await context.SaveChangesAsync(ct)`.
      - `GetByArticleIdAsync`: `Where(m => m.ArticleId == articleId).ToListAsync(ct)` mapped
        via `MediaFileMapper.ToDomain`.
      - `ExistsByArticleAndUrlAsync`: `AnyAsync(m => m.ArticleId == articleId && m.OriginalUrl == originalUrl, ct)`.
      _Acceptance: file compiles; implements `IMediaFileRepository`; no raw SQL; insert uses
      `Add + SaveChangesAsync` pattern; last parameter of every method is
      `CancellationToken cancellationToken = default`_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — in
      `AddRepositories`, add:
      ```csharp
      services.AddScoped<IMediaFileRepository, MediaFileRepository>();
      ```
      _Acceptance: project builds; `IMediaFileRepository` resolves from DI without exception;
      existing repository registrations are unchanged_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step 8 — Storage implementation and DI registration

- [x] **Create `Infrastructure/Storage/CloudflareR2Storage.cs`** — implement `IMediaStorage`
      using `AWSSDK.S3`. Constructor receives `CloudflareR2Options options` and constructs
      `AmazonS3Client` with `BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey)`
      and `AmazonS3Config { ServiceURL = $"https://{options.AccountId}.r2.cloudflarestorage.com", ForcePathStyle = true, AuthenticationRegion = "auto" }`.
      `UploadAsync` calls `PutObjectAsync` with `BucketName`, `key`, `content`, and
      `ContentType`. Single-part only — no multipart upload.
      _Acceptance: file compiles; class is in namespace `Infrastructure.Storage`; implements
      `IMediaStorage`; no multipart upload API used; `AmazonS3Client` is created inline
      (not injected) from options_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — add a new
      private `AddStorage(IConfiguration configuration)` extension method that:
      1. Registers `services.Configure<CloudflareR2Options>(configuration.GetSection(CloudflareR2Options.SectionName))`.
      2. Registers `IMediaStorage` as scoped, constructing `CloudflareR2Storage` from a
         resolved `IOptions<CloudflareR2Options>` in the factory lambda.
      Chain `.AddStorage(configuration)` into `AddInfrastructure` after `AddServices`.
      _Acceptance: project builds; `IMediaStorage` resolves from DI; `AddStorage` is a private
      static method following the existing pattern (same style as `AddAiServices`); `AddInfrastructure`
      chain includes `.AddStorage(configuration)`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step 9 — Ingestion service implementation and DI registration

- [x] **Create `Infrastructure/Services/MediaIngestionService.cs`** — implement
      `IMediaIngestionService`. Primary constructor receives `IMediaStorage storage`,
      `IMediaFileRepository repository`, `IHttpClientFactory httpClientFactory`,
      `IOptions<CloudflareR2Options> options`, `ILogger<MediaIngestionService> logger`.
      Algorithm per ADR §9:
      1. Early-return on empty `references`.
      2. De-duplicate `references` by `Url` within the batch.
      3. Per reference, inside `try/catch (Exception ex)` that logs `Warning` and continues:
         - Skip if `ExistsByArticleAndUrlAsync` is true.
         - Download via named client `"MediaDownloader"`, streaming to `MemoryStream`. Abort
           if `Content-Length` or actual bytes exceed `MaxFileSizeBytes`.
         - Resolve `ContentType` from response `Content-Type` header → declared content type →
           file extension guess. Reject if not `image/*` or `video/*`.
         - Generate `r2Key = $"articles/{articleId}/{Guid.NewGuid()}{ext}"`.
         - Call `storage.UploadAsync(r2Key, stream, contentType, ct)`.
         - Call `repository.AddAsync(new MediaFile { Id = Guid.NewGuid(), ArticleId = articleId, R2Key = r2Key, OriginalUrl = ref.Url, ContentType = contentType, SizeBytes = stream.Length, Kind = kind, CreatedAt = DateTimeOffset.UtcNow }, ct)`.
         - Log `Information` on success.
      4. Outer method has top-level `try/catch` that logs and swallows — caller treats this
         method as infallible.
      _Acceptance: file compiles; implements `IMediaIngestionService`; no exception propagates
      out of `IngestForArticleAsync`; named `HttpClient` `"MediaDownloader"` is used (not
      `CreateClient()` without name)_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — in
      `AddServices`, add:
      ```csharp
      services.AddScoped<IMediaIngestionService, MediaIngestionService>();
      ```
      Also register the named `HttpClient` `"MediaDownloader"` with a timeout sourced from
      `CloudflareR2Options.DownloadTimeoutSeconds`. Because `AddStorage` already registered the
      options, read the value via `configuration.GetSection(CloudflareR2Options.SectionName).Get<CloudflareR2Options>()`.
      Register the named client in `AddServices` (or `AddStorage` — keep it in whichever
      method the implementer finds most cohesive, but document the choice in a code comment):
      ```csharp
      services.AddHttpClient("MediaDownloader")
          .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(r2Options.DownloadTimeoutSeconds));
      ```
      _Acceptance: project builds; `IMediaIngestionService` resolves from DI; named client
      `"MediaDownloader"` resolves via `IHttpClientFactory.CreateClient("MediaDownloader")`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step 10 — Article domain model extension and mapper verification

- [x] **Modify `Core/DomainModels/Article.cs`** — add transient property:
      ```csharp
      public List<MediaReference> MediaReferences { get; set; } = [];
      ```
      Place it in the existing `//Service` or a new `//Transient (not persisted)` section.
      _Acceptance: file compiles; `MediaReferences` is present; no EF or Infrastructure
      references introduced into `Core/`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Configurations/ArticleConfiguration.cs`** — add
      `builder.Ignore(a => a.MediaReferences);` to the `Configure` method so EF does not
      attempt to map the transient property.
      _Acceptance: `dotnet ef migrations add` (dry-run) does not generate a column for
      `MediaReferences`; file compiles_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Verify `Infrastructure/Persistence/Mappers/ArticleMapper.cs`** — confirm that
      `ToDomain` and `ToEntity` do **not** reference `MediaReferences`. No code change is
      expected; this task is a deliberate guard step.
      _Acceptance: neither `ToDomain` nor `ToEntity` nor `FromAnalysisResult` mentions
      `MediaReferences`; file is unchanged after review_
      _Skill: .claude/skills/mappers/SKILL.md_

---

### Step 11 — RSS parser enclosure extraction

- [x] **Modify `Infrastructure/Parsers/RssParser.cs`** — extend `ParseAsync` to extract
      media references from each feed item and populate `article.MediaReferences`:
      - Cast `item.SpecificItem` to `Rss20FeedItem` to read `Enclosure` (URL + type).
      - Cast `item.SpecificItem` to `AtomFeedItem` to read `link rel="enclosure"`.
      - Fall back to `item.Element.Descendants(...)` XML traversal for `media:content` and
        `media:thumbnail` when the typed properties are null.
      - For each extracted URL, classify as `MediaKind.Image` or `MediaKind.Video` based on
        declared content type (first) or file extension (fallback). Skip anything that is
        neither image nor video.
      _Acceptance: file compiles; `article.MediaReferences` is populated before the list is
      returned; articles with no enclosures have an empty `MediaReferences` list (not null);
      the existing `Select` projection is updated or replaced to populate the new property_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step 12 — Worker integration

- [x] **Modify `Worker/Workers/SourceFetcherWorker.cs`** — wire `IMediaIngestionService`
      into `ProcessAsync` and call it after each successful article save:
      1. In `ProcessAsync`, resolve the service from scope:
         ```csharp
         var mediaIngestionService = scope.ServiceProvider.GetRequiredService<IMediaIngestionService>();
         ```
      2. Pass `mediaIngestionService` as a parameter into `ProcessSourceAsync`.
      3. Inside `ProcessSourceAsync`, **after** `await articleRepository.AddAsync(article, cancellationToken)`:
         ```csharp
         try
         {
             await mediaIngestionService.IngestForArticleAsync(article.Id, article.MediaReferences, cancellationToken);
         }
         catch (Exception ex)
         {
             _logger.LogWarning(ex, "Media ingestion failed for article {ArticleId}", article.Id);
         }
         ```
      Constructor signature is unchanged — no new singleton dependencies.
      _Acceptance: file compiles; constructor has no new parameters; `IMediaIngestionService`
      is resolved inside `ProcessAsync` (scoped, not constructor-injected); the try/catch
      wrapping `IngestForArticleAsync` is present; `saved++` count is incremented before the
      media call, so a media failure does not affect the counter_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step 13 — Tests (delegated to test-writer agent)

- [ ] **Create `Tests/Infrastructure.Tests/Repositories/MediaFileRepositoryTests.cs`** —
      EF Core InMemory fixture covering:
      - `AddAsync` persists a `MediaFile` row retrievable by `GetByArticleIdAsync`.
      - `GetByArticleIdAsync` returns empty list for unknown `articleId`.
      - `ExistsByArticleAndUrlAsync` returns `true` after insert, `false` before.
      _Acceptance: all tests pass; no live PostgreSQL; uses InMemory provider_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Create `Tests/Infrastructure.Tests/Services/MediaIngestionServiceTests.cs`** —
      mocked `IMediaStorage`, `IMediaFileRepository`, `IHttpClientFactory`
      (with `HttpMessageHandler` stub). Cases:
      - Empty `references` returns immediately without calling storage or repository.
      - Successful path: download → upload → `AddAsync` called with correct `ArticleId` and
        `OriginalUrl`.
      - HTTP 404 response is caught and logged; no `AddAsync` call; method does not throw.
      - File exceeds `MaxFileSizeBytes`; rejected; storage not called.
      - `ExistsByArticleAndUrlAsync` returns `true`; URL skipped; storage not called.
      - `IMediaStorage.UploadAsync` throws `AmazonS3Exception`; caught; method does not throw.
      - Non-`image/*`/`video/*` content type; URL rejected; storage not called.
      - Every branch: no exception propagates out of `IngestForArticleAsync`.
      _Acceptance: all tests pass; no live HTTP or R2 calls; `AmazonS3Exception` is used from
      `Amazon.S3` namespace_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Infrastructure.Tests/Parsers/RssParserTests.cs`** (create if not
      present) — add test cases for enclosure extraction:
      - RSS 2.0 item with `<enclosure>` tag → `MediaReferences` contains one image entry.
      - Item with `media:content` element → `MediaReferences` contains correct `Url` and
        `Kind`.
      - Item with `media:thumbnail` element → `MediaReferences` populated correctly.
      - Item with no media elements → `MediaReferences` is empty.
      - Item with unsupported MIME type (`application/pdf`) → entry is excluded from
        `MediaReferences`.
      _Acceptance: all new and existing parser tests pass_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Create `Tests/Worker.Tests/Workers/SourceFetcherWorkerMediaTests.cs`** — minimal
      fixture (following the pattern of existing worker tests) that verifies:
      - When `IMediaIngestionService.IngestForArticleAsync` throws, the article loop continues
        and the `saved` count is unaffected.
      - When `IngestForArticleAsync` succeeds, the article is saved and `saved++` is
        incremented.
      _Acceptance: both tests pass; no live dependencies; `IMediaIngestionService` is mocked_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

## Open Questions

- None. The ADR (§Implementation Notes) fully specifies interface signatures, implementation
  patterns, DB schema, EF conventions, DI wiring, config values, and test scope.
