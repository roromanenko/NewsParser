# Media Files Support with Cloudflare R2 Storage

## Status

Proposed

## Context

The ingestion pipeline currently parses RSS feed items into `Article` rows (see `Infrastructure/Parsers/RssParser.cs` and `Worker/Workers/SourceFetcherWorker.cs`) but discards any media (images, videos) referenced by the feed. Downstream consumers — the approval UI and the `PublicationWorker` that pushes content to Telegram and other platforms — have no media to render or re-upload.

We need first-class support for media attached to RSS items:

- **Types:** images and short videos referenced by the feed item.
- **Download point:** inside the fetcher worker, best-effort, **after** the article row is saved, so a failure to fetch media never blocks article ingestion.
- **Storage:** Cloudflare R2 (S3-compatible object storage). Bucket already exists.
- **Metadata:** persisted in PostgreSQL, one row per media file, linked to `Articles.Id`.
- **Deduplication:** per-article only. Articles are already deduplicated upstream (ExternalId, URL, fuzzy title in `SourceFetcherWorker.ProcessSourceAsync`), so we never need cross-article media dedup. Within a single article we skip an URL that already appeared in the same batch.
- **Failure handling:** any failure in extract → download → upload → metadata write must be caught and logged; the article itself must remain saved.

### Constraints from the existing codebase

- **Layering** (`.claude/skills/code-conventions/SKILL.md`): domain models in `Core/`, EF entities + repositories + external clients in `Infrastructure/`, worker wiring in `Worker/`. No EF or HttpClient in `Core/`.
- **Workers** (`code-conventions` §Worker Architecture): singletons only in the constructor, scoped services resolved via `IServiceScopeFactory.CreateScope()` inside `ProcessAsync`, interval always from an Options class.
- **Repositories** (`ef-core-conventions` §§1–3): inserts via `Add + SaveChangesAsync`, updates via `ExecuteUpdateAsync`, enum storage as strings with `.ToString()`, `CancellationToken cancellationToken = default` as the last parameter.
- **Options pattern** (`code-conventions` §Configuration): `public const string SectionName`, sensible defaults, `Infrastructure/Configuration/` for infra-wide options.
- **Mappers** (`.claude/skills/mappers/SKILL.md` and `code-conventions` §Mapper Conventions): static `XxxMapper` class, `ToDomain` / `ToEntity` pair, no I/O.
- **EF conventions** (`ArticleConfiguration.cs`): `snake_case` tables, `HasIndex` for FK columns and query predicates, enum columns via `HasConversion<string>()`, string FKs nullable where optional.
- **RSS parser** currently uses `CodeHollow.FeedReader`, which exposes enclosures via `item.SpecificItem` (RSS 2.0 `enclosure`, Atom `link rel="enclosure"`, and `media:content` / `media:thumbnail` from Media RSS). These are already in-package; no new feed library is needed.
- **Worker naming inconsistency:** the feature description calls the worker `RssFetcherWorker`, but the actual class is `SourceFetcherWorker` in `Worker/Workers/SourceFetcherWorker.cs`. The options class is correctly named `RssFetcherOptions`. We integrate with `SourceFetcherWorker`.

## Options

### Option 1 — Inline in `SourceFetcherWorker` with direct `AmazonS3Client` usage

Extract enclosures inside `RssParser`, carry them on `Article` as a transient property, and inside `SourceFetcherWorker.ProcessSourceAsync` — after `articleRepository.AddAsync` succeeds — call `IAmazonS3.PutObjectAsync` and insert `MediaFileEntity` rows directly through the `DbContext` resolved from the worker scope.

**Pros:** Fewest new files. No new service abstraction.
**Cons:** Violates layering — worker would need direct `DbContext` access, which `code-conventions` explicitly forbids ("`Worker/` — DbContext direct access: Forbidden"). Transient non-persisted data piggy-backing on the `Article` domain model pollutes the model. R2 upload logic in the worker class makes it untestable and mixes concerns.

### Option 2 — `IMediaIngestionService` in Infrastructure, called from the worker (chosen)

- **Core** gains a pure `MediaFile` domain model, an `IMediaStorage` interface (upload abstraction), an `IMediaIngestionService` orchestration interface, and an `IMediaFileRepository`. A new `MediaReference` value type is added alongside `Article` to carry parsed enclosure URLs from the parser to the worker — kept **out** of the EF entity (not persisted on `Article`).
- **Infrastructure** gains `MediaFileEntity`, `MediaFileConfiguration`, `MediaFileRepository`, `MediaFileMapper`, `CloudflareR2Options`, `CloudflareR2Storage` (implements `IMediaStorage` using `AWSSDK.S3`), and `MediaIngestionService` (implements `IMediaIngestionService`). `RssParser` is extended to populate `MediaReference`s from the feed item.
- **Worker** (`SourceFetcherWorker.ProcessSourceAsync`) resolves `IMediaIngestionService` from the scope and, after each successful `articleRepository.AddAsync(article)`, calls `mediaIngestionService.IngestForArticleAsync(article.Id, article.MediaReferences, cancellationToken)` inside its own `try/catch`. Failures are logged and swallowed.
- **Migration** adds a `media_files` table with the fields listed in the Decision section.

**Pros:** Preserves layering — worker never touches `DbContext` or `HttpClient`. The service is unit-testable with mocked `IMediaStorage` and `IMediaFileRepository`. R2 can be swapped for any S3-compatible backend or a local fake without touching the worker. Follows the exact pattern used by `ClaudeContentGenerator`, `GeminiEmbeddingService`, etc. — a narrow Infrastructure service behind a Core interface.
**Cons:** More files created. Introduces one new NuGet package (`AWSSDK.S3`).

### Option 3 — Separate `MediaIngestionWorker` that polls for articles with un-ingested media

Add a new background worker that, like `ArticleAnalysisWorker`, polls `Articles` where media has not yet been ingested, fetches and uploads media asynchronously from the fetcher. Requires a status flag column on `Article` and a new "pending media ingestion" query pattern.

**Pros:** Cleanest decoupling — fetcher has zero media responsibility; failure modes are fully independent.
**Cons:** Requires schema change on `Article` (new status column or flag). Requires a mechanism to carry parsed enclosure URLs from the parser to the new worker — either by persisting them as a staging table (another new table) or re-parsing the RSS feed in the new worker (wasteful and can return different content on re-fetch). Adds operational surface (a new worker to monitor) and latency to media availability. The feature description explicitly asks for ingestion **inside** the fetcher worker, best-effort — Option 3 contradicts that requirement.

## Decision

**Adopt Option 2.** Add a new `MediaFile` domain model, a dedicated `media_files` table, and an `IMediaIngestionService` implemented in Infrastructure. The service is called by `SourceFetcherWorker` after each article is saved, inside a per-article `try/catch` so ingestion remains best-effort.

### 1. Domain model (`Core/DomainModels/MediaFile.cs`)

```csharp
public class MediaFile
{
    public Guid Id { get; init; }
    public Guid ArticleId { get; init; }
    public string R2Key { get; init; } = string.Empty;
    public string OriginalUrl { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public MediaKind Kind { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public enum MediaKind
{
    Image,
    Video
}
```

`MediaKind` is added as a stored enum so downstream publishers can filter images vs videos without re-parsing `ContentType`. All fields are `init` — media rows are immutable once written.

### 2. Transient `MediaReference` carried on `Article`

```csharp
// Core/DomainModels/MediaReference.cs
public record MediaReference(string Url, MediaKind Kind, string? DeclaredContentType);
```

Add a `public List<MediaReference> MediaReferences { get; set; } = [];` property to `Article`. **This property is not mapped to the EF entity** — it only carries parser output in-memory from `RssParser.ParseAsync` to `SourceFetcherWorker` to `IMediaIngestionService`. `ArticleMapper.ToEntity` / `ToDomain` do not touch it, and `ArticleConfiguration` ignores it via `builder.Ignore(a => a.MediaReferences)`.

Rationale: persisting a pending-media staging table (Option 3) adds schema complexity without benefit given that ingestion is synchronous-with-the-fetcher and best-effort. Keeping references transient is consistent with how `Article.Publications` is shaped during batch processing but stored via its own table.

### 3. Database schema (`media_files` table)

EF entity `MediaFileEntity` in `Infrastructure/Persistence/Entity/` and configuration in `Infrastructure/Persistence/Configurations/MediaFileConfiguration.cs`:

| Column | Type | Notes |
|---|---|---|
| `id` | `uuid` PK | |
| `article_id` | `uuid` NOT NULL, FK → `articles.id` `ON DELETE CASCADE` | Indexed |
| `r2_key` | `text` NOT NULL | Globally unique key inside the R2 bucket. Format: `articles/{articleId}/{mediaId}{ext}` |
| `original_url` | `text` NOT NULL | Source URL from the feed |
| `content_type` | `text` NOT NULL | Detected MIME type (e.g. `image/jpeg`) |
| `size_bytes` | `bigint` NOT NULL | Bytes actually uploaded |
| `kind` | `text` NOT NULL | `HasConversion<string>()` for the `MediaKind` enum, matching the `Sentiment` / `Status` / `Role` pattern in `ArticleConfiguration` |
| `created_at` | `timestamptz` NOT NULL | Set to `DateTimeOffset.UtcNow` on insert (consistent with §7 of `ef-core-conventions`) |

Indexes:
- `HasIndex(m => m.ArticleId)` — lookups from article detail view.
- `HasIndex(m => new { m.ArticleId, m.OriginalUrl }).IsUnique()` — enforces per-article deduplication at the DB level (cheap safety net on top of the in-memory check in `MediaIngestionService`).

FK configured on `ArticleEntity` side:
```csharp
builder
    .HasMany<MediaFileEntity>()
    .WithOne()
    .HasForeignKey(m => m.ArticleId)
    .OnDelete(DeleteBehavior.Cascade);
```

No navigation collection is added to `ArticleEntity.MediaFiles` in this ADR — article workers do not need to load media, and leaving it off keeps existing queries identical. If the Api layer later needs eager-loaded media on the article detail view, a future ADR can add the navigation property and an `Include`-based `GetDetailWithMediaAsync`.

Register `DbSet<MediaFileEntity> MediaFiles` in `NewsParserDbContext`.

The EF migration adds the `media_files` table and a unique index `ix_media_files_article_id_original_url`. Migration file name: `AddMediaFiles`.

### 4. Repository (`Infrastructure/Persistence/Repositories/MediaFileRepository.cs`)

Interface in `Core/Interfaces/Repositories/IMediaFileRepository.cs`. Use the newer primary-constructor style (per `ef-core-conventions` §1).

```csharp
public interface IMediaFileRepository
{
    Task AddAsync(MediaFile mediaFile, CancellationToken cancellationToken = default);
    Task<List<MediaFile>> GetByArticleIdAsync(Guid articleId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByArticleAndUrlAsync(Guid articleId, string originalUrl, CancellationToken cancellationToken = default);
}
```

- `AddAsync` uses `Add + SaveChangesAsync` (§2 Pattern B).
- `ExistsByArticleAndUrlAsync` uses `AnyAsync` (matches the `ExistsByUrlAsync` pattern in `ArticleRepository`).
- No update/delete methods in this ADR — media rows are write-once for v1.

Register in `InfrastructureServiceExtensions.AddRepositories()`.

### 5. Mapper (`Infrastructure/Persistence/Mappers/MediaFileMapper.cs`)

Static class with `ToDomain` and `ToEntity` extension methods — expression body (§Mapper Conventions), no I/O.

### 6. Storage abstraction — `IMediaStorage`

In `Core/Interfaces/Storage/IMediaStorage.cs` (new subdirectory in `Core/Interfaces/`, matching the pattern in `code-conventions` §Interface Organization):

```csharp
public interface IMediaStorage
{
    Task UploadAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);
}
```

Narrow by design — one method. Returning the public URL is not part of the interface; the public URL is constructed from `CloudflareR2Options.PublicBaseUrl + "/" + key` by callers who need it (e.g. publishers). This matches how other Infrastructure clients expose only the essential operation.

### 7. R2 client choice — `AWSSDK.S3`

**Use `AWSSDK.S3` (official AWS .NET SDK).** Cloudflare R2 is S3-compatible and Cloudflare's own documentation recommends the AWS SDK. This avoids pulling in any Cloudflare-specific client.

Add `AWSSDK.S3` package reference to `Infrastructure.csproj`. No other AWS packages are needed.

Implementation class: `Infrastructure/Storage/CloudflareR2Storage.cs`. Construction uses the standard R2 endpoint format:

```
https://{AccountId}.r2.cloudflarestorage.com
```

`AmazonS3Client` is instantiated with `BasicAWSCredentials(AccessKeyId, SecretAccessKey)` and `AmazonS3Config { ServiceURL = endpoint, ForcePathStyle = true }`. Region is set to `"auto"` (required by R2).

`CloudflareR2Storage` is registered as **scoped** (matching other Infrastructure clients) and constructs its own `AmazonS3Client` in the DI factory lambda using values from `CloudflareR2Options`, identical to how `ClaudeContentGenerator` is wired in `InfrastructureServiceExtensions.AddAiServices`.

### 8. Options class (`Infrastructure/Configuration/CloudflareR2Options.cs`)

```csharp
public class CloudflareR2Options
{
    public const string SectionName = "CloudflareR2";
    public string AccountId { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public int MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024; // 50 MB safety cap
    public int DownloadTimeoutSeconds { get; set; } = 30;
}
```

- `PublicBaseUrl` is the bucket's public hostname (e.g. the `r2.dev` URL or a custom domain) used by publishers to build renderable URLs.
- `MaxFileSizeBytes` and `DownloadTimeoutSeconds` are safety caps enforced in `MediaIngestionService`; both have sensible defaults so they are optional in config.

Registered via `services.Configure<CloudflareR2Options>(configuration.GetSection(CloudflareR2Options.SectionName))` in a new `AddStorage(configuration)` method in `InfrastructureServiceExtensions`, chained into `AddInfrastructure`.

### 9. Ingestion service (`Infrastructure/Services/MediaIngestionService.cs`)

Interface in `Core/Interfaces/Services/IMediaIngestionService.cs`:

```csharp
public interface IMediaIngestionService
{
    Task IngestForArticleAsync(
        Guid articleId,
        IReadOnlyList<MediaReference> references,
        CancellationToken cancellationToken = default);
}
```

The implementation:

1. Early-return if `references` is empty.
2. De-duplicate `references` by URL inside the batch (per-article dedup requirement).
3. For each reference, inside an inner `try/catch`:
   - Skip if `ExistsByArticleAndUrlAsync` returns true.
   - Download the bytes via an injected `IHttpClientFactory` (named client `"MediaDownloader"` registered with the configured timeout), streaming to a `MemoryStream`. Enforce `MaxFileSizeBytes` — abort if exceeded.
   - Resolve `ContentType` from the HTTP `Content-Type` response header, falling back to the reference's declared content type, then to a best-effort mapping from file extension. Reject types that are not `image/*` or `video/*`.
   - Generate `r2Key = $"articles/{articleId}/{Guid.NewGuid()}{extension}"`.
   - Call `IMediaStorage.UploadAsync(key, stream, contentType, cancellationToken)`.
   - Call `IMediaFileRepository.AddAsync(new MediaFile { ... CreatedAt = DateTimeOffset.UtcNow })`.
   - Log success at `Information`.
   - On any exception: log at `Warning` with the URL and reason, continue with the next reference. **Never rethrow.**
4. The outer method itself also has a top-level try/catch that logs and swallows anything unexpected — so the caller (`SourceFetcherWorker`) can treat `IngestForArticleAsync` as infallible.

Registered in `InfrastructureServiceExtensions.AddServices` as `AddScoped<IMediaIngestionService, MediaIngestionService>()`.

### 10. Parser changes (`Infrastructure/Parsers/RssParser.cs`)

Extract media references from `CodeHollow.FeedReader`'s feed item:
- RSS 2.0 `enclosure` elements (`item.SpecificItem` as `Rss20FeedItem` → `Enclosure`).
- Atom `link rel="enclosure"` (same path, `AtomFeedItem`).
- `media:content` / `media:thumbnail` via the `item.Element.Descendants(...)` XML fallback when the typed property is null.

For each extracted URL, classify as `MediaKind.Image` or `MediaKind.Video` based on the declared content type (fallback: file extension). Skip anything that is neither image nor video.

Populate `article.MediaReferences` before returning the list.

### 11. Worker integration (`Worker/Workers/SourceFetcherWorker.cs`)

- No changes to the constructor (no new singleton dependencies — `IMediaIngestionService` is scoped and resolved inside `ProcessAsync`).
- In `ProcessAsync`, add `var mediaIngestionService = scope.ServiceProvider.GetRequiredService<IMediaIngestionService>();`.
- Pass `mediaIngestionService` into `ProcessSourceAsync`.
- Inside `ProcessSourceAsync`, **after** the successful `await articleRepository.AddAsync(article, cancellationToken)` call (and only if the article was saved, i.e. not skipped by dedup), wrap an additional call:

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

The outer article loop's existing behaviour (saved++ / skipped++, title dedup) is unchanged. Media ingestion runs **inside** the per-source batch but **after** each article save, matching the "best-effort, log and continue" requirement.

### 12. Configuration additions

`Api/appsettings.Development.json` and `Worker/appsettings.Development.json` gain:

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

Dev placeholder values allow the app to start without real credentials. `MediaIngestionService` must tolerate an `AmazonS3Exception` at upload time (logged as warning, article still saved), so an invalid dev secret does not crash ingestion.

## Consequences

### Positive

- Layering preserved: `Worker/` never touches `DbContext` or S3 clients; `Core/` gains only a domain model, a repository interface, a storage interface, and an ingestion service interface.
- `IMediaStorage` is pluggable — tests inject a fake, production uses `CloudflareR2Storage`. No coupling between the ingestion logic and any AWS type outside `Infrastructure/Storage/`.
- Per-article unique `(ArticleId, OriginalUrl)` index gives DB-level idempotency, so a re-run of the fetcher on a partial failure will not create duplicate media rows.
- No schema change on `Article` — purely additive migration.
- Follows every existing convention: primary-constructor repo, `ExecuteUpdateAsync`/`Add+SaveChanges` split, string-stored enum, `snake_case` table name, `DateTimeOffset.UtcNow` used directly.
- Article ingestion success is fully decoupled from R2 availability: the outer `try/catch` in `SourceFetcherWorker` plus the inner per-reference `try/catch` in `MediaIngestionService` means no media-related exception can propagate up to abort the feed loop.

### Negative / risks

- **New external dependency surface.** `AWSSDK.S3` is a large package. Cloudflare R2 occasionally diverges from S3 behaviour (multipart upload edge cases, missing `PutObjectAcl`); initial implementation must avoid those APIs and stick to single-part `PutObjectAsync`.
- **Worker duration increases** proportionally to media count per article. For large feeds with many enclosures, per-source processing time grows. Mitigation: `DownloadTimeoutSeconds` cap (default 30s per file) and `MaxFileSizeBytes` cap (default 50 MB). If this becomes a bottleneck, a future ADR can move ingestion to a separate worker (Option 3) — the interfaces defined here make that migration straightforward (just switch the caller).
- **Transient `MediaReferences` property on the `Article` domain model.** A non-persisted field on the domain model is a small purity cost. It is explicitly ignored at the mapping layer and documented in `ArticleConfiguration` via `builder.Ignore`. Alternative (a parser-result wrapper class) was rejected because it would require changing `ISourceParser.ParseAsync` signature, affecting `TelegramParser` as well, which is out of scope for this feature.
- **R2 credentials in appsettings.** The Development file uses placeholder values; real credentials go into user secrets / environment variables in production. Document this in the implementation notes.
- **No cross-worker coordination.** If the fetcher crashes mid-ingestion for one article, that article will have a partial media set. Because there is no pending/processing state for media, there is no auto-recovery. Acceptable per the "best-effort" requirement; future ADR can revisit if completeness becomes important.

### Files affected

**New files:**
- `Core/DomainModels/MediaFile.cs`
- `Core/DomainModels/MediaReference.cs`
- `Core/Interfaces/Repositories/IMediaFileRepository.cs`
- `Core/Interfaces/Services/IMediaIngestionService.cs`
- `Core/Interfaces/Storage/IMediaStorage.cs`
- `Infrastructure/Configuration/CloudflareR2Options.cs`
- `Infrastructure/Persistence/Entity/MediaFileEntity.cs`
- `Infrastructure/Persistence/Configurations/MediaFileConfiguration.cs`
- `Infrastructure/Persistence/Mappers/MediaFileMapper.cs`
- `Infrastructure/Persistence/Repositories/MediaFileRepository.cs`
- `Infrastructure/Persistence/Migrations/<timestamp>_AddMediaFiles.cs` (generated)
- `Infrastructure/Storage/CloudflareR2Storage.cs`
- `Infrastructure/Services/MediaIngestionService.cs`

**Modified files:**
- `Core/DomainModels/Article.cs` — add transient `List<MediaReference> MediaReferences` property.
- `Infrastructure/Persistence/DataBase/NewsParserDbContext.cs` — add `DbSet<MediaFileEntity> MediaFiles`.
- `Infrastructure/Persistence/Configurations/ArticleConfiguration.cs` — `builder.Ignore(a => a.MediaReferences)` if EF tries to discover it (only needed if the type is traversable; otherwise no change).
- `Infrastructure/Persistence/Mappers/ArticleMapper.cs` — ensure `ToDomain` / `ToEntity` do **not** touch `MediaReferences` (no change to mapper body, just verification).
- `Infrastructure/Parsers/RssParser.cs` — extract enclosures and populate `MediaReferences`.
- `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` — register `CloudflareR2Options`, `IMediaStorage` → `CloudflareR2Storage`, `IMediaFileRepository` → `MediaFileRepository`, `IMediaIngestionService` → `MediaIngestionService`; add named `HttpClient` `"MediaDownloader"` with configured timeout.
- `Infrastructure/Infrastructure.csproj` — add `AWSSDK.S3` PackageReference.
- `Worker/Workers/SourceFetcherWorker.cs` — resolve `IMediaIngestionService` in `ProcessAsync`, call `IngestForArticleAsync` after each successful save inside a try/catch.
- `Api/appsettings.Development.json` — add `CloudflareR2` section with placeholder values.
- `Worker/appsettings.Development.json` — add `CloudflareR2` section with placeholder values.

## Implementation Notes

### Order of changes (strict — each step builds on the previous)

1. Add `CloudflareR2Options` and the placeholder sections in both `appsettings.Development.json` files. This locks the configuration contract first.
2. Add `AWSSDK.S3` PackageReference to `Infrastructure.csproj`.
3. Add `Core/DomainModels/MediaFile.cs` (+ `MediaKind` enum) and `Core/DomainModels/MediaReference.cs`.
4. Add `Core/Interfaces/Storage/IMediaStorage.cs`, `Core/Interfaces/Repositories/IMediaFileRepository.cs`, `Core/Interfaces/Services/IMediaIngestionService.cs`.
5. Add `MediaFileEntity`, `MediaFileConfiguration`, `MediaFileMapper`, register `DbSet` in `NewsParserDbContext`.
6. Generate EF migration `AddMediaFiles`. Verify the generated SQL matches the column list above before proceeding.
7. Implement `MediaFileRepository` and register in DI.
8. Implement `CloudflareR2Storage` and register in DI (new `AddStorage` helper in `InfrastructureServiceExtensions`, chained into `AddInfrastructure`).
9. Implement `MediaIngestionService` and register in DI. Register the named `"MediaDownloader"` `HttpClient` in the same place with a timeout from `CloudflareR2Options.DownloadTimeoutSeconds`.
10. Add the transient `MediaReferences` property to `Article` and verify mappers ignore it.
11. Extend `RssParser` to extract enclosures and populate `MediaReferences`.
12. Wire `IMediaIngestionService` into `SourceFetcherWorker.ProcessSourceAsync` with the post-save try/catch.
13. Add tests (delegated to `test-writer` — see §Testing below).

### Skills to follow (MUST be read by `feature-planner` and `implementer`)

- **`code-conventions`** (`.claude/skills/code-conventions/SKILL.md`) — layer boundaries (worker must not touch DbContext), worker architecture (singletons only in ctor, scoped resolved via `IServiceScopeFactory`), Options pattern (`SectionName` const, defaults), enum storage as strings, `DateTimeOffset.UtcNow` directly.
- **`ef-core-conventions`** (`.claude/skills/ef-core-conventions/SKILL.md`) — primary-constructor repo style for the new repository, `Add + SaveChangesAsync` for inserts, `ExistsByXxxAsync` naming, `CancellationToken cancellationToken = default` last parameter, `.ToString()` on enum values in queries.
- **`mappers`** (`.claude/skills/mappers/SKILL.md`) — static `MediaFileMapper`, `ToDomain` / `ToEntity` pair, expression body, no I/O.
- **`test-writer`** (`.claude/skills/testing/SKILL.md`) — for the tests.

### Testing (delegated to `test-writer`)

- `MediaFileRepositoryTests` — EF Core InMemory, covers `AddAsync`, `GetByArticleIdAsync`, `ExistsByArticleAndUrlAsync`.
- `MediaIngestionServiceTests` — mocked `IMediaStorage`, `IMediaFileRepository`, `IHttpClientFactory` (with `HttpMessageHandler` stub).
  Cases: empty references early return; successful upload path; HTTP 404 swallowed; file exceeds `MaxFileSizeBytes` rejected; existing `(articleId, url)` skipped; upload throwing `AmazonS3Exception` caught; unsupported `ContentType` rejected; every branch logs but never throws.
- `RssParserTests` — extend the existing test to cover enclosure extraction for `<enclosure>`, `media:content`, and `media:thumbnail` inputs.
- `SourceFetcherWorkerTests` (if present, otherwise integration-style) — verify that when `IMediaIngestionService.IngestForArticleAsync` throws, the article loop continues and `saved` count is unaffected. If no such test exists today, introduce a minimal one scoped to this behaviour only.
- `CloudflareR2Storage` is **not** unit-tested (it is a thin adapter over `IAmazonS3`). Rely on `MediaIngestionServiceTests` with a mocked `IMediaStorage` for the business logic, and a manual smoke test against the dev R2 bucket before shipping.

### Recommended next step

Pass this ADR to **feature-planner** to produce the atomic tasklist in `docs/tasks/active/media-files-cloudflare-r2.md`.
