# Refactoring: Move Non-LLM Deduplication into SourceFetcherWorker

**Goal:** Filter URL and title duplicates before an article is persisted, so workers with LLMs never see simple duplicates.

---

## Context

- **ExternalId dedup** already exists in `SourceFetcherWorker` via `ExistsAsync(sourceId, externalId)` — keep it.
- **Title fuzzy dedup** (FuzzySharp) currently lives in `ArticleAnalysisWorker.ProcessRawArticleAsync` (Step 1) — move it.
- **Cross-source URL dedup** does not exist yet — add it.

---

## Steps

### Step 1 — Add `ExistsByUrlAsync` to `IRawArticleRepository`

**Files:** `Core/Interfaces/Repositories/IRawArticleRepository.cs`, `Infrastructure/Persistence/Repositories/RawArticleRepository.cs`

```csharp
// Interface
Task<bool> ExistsByUrlAsync(string originalUrl, CancellationToken cancellationToken = default);

// Implementation
public async Task<bool> ExistsByUrlAsync(string originalUrl, CancellationToken cancellationToken = default)
{
    return await _context.RawArticles
        .AnyAsync(r => r.OriginalUrl == originalUrl
                    && r.Status != RawArticleStatus.Rejected.ToString(),
                  cancellationToken);
}
```

**Acceptance criteria:**
- [ ] Interface compiles.
- [ ] Returns `true` for non-rejected articles with matching `OriginalUrl`, `false` otherwise.
- [ ] No migration needed.

---

### Step 2 — Add `GetRecentTitlesForDeduplicationAsync` (pre-save variant) to `IRawArticleRepository`

The existing `GetRecentTitlesAsync(Guid currentId, ...)` is a post-save API (excludes the article itself by ID). A pre-save variant omits that exclusion.

**Files:** `Core/Interfaces/Repositories/IRawArticleRepository.cs`, `Infrastructure/Persistence/Repositories/RawArticleRepository.cs`

```csharp
// Interface
Task<List<string>> GetRecentTitlesForDeduplicationAsync(int windowHours, CancellationToken cancellationToken = default);

// Implementation
public async Task<List<string>> GetRecentTitlesForDeduplicationAsync(
    int windowHours, CancellationToken cancellationToken = default)
{
    var since = DateTimeOffset.UtcNow.AddHours(-windowHours);
    return await _context.RawArticles
        .Where(r => r.PublishedAt >= since
                 && r.Status != RawArticleStatus.Rejected.ToString())
        .Select(r => r.Title)
        .ToListAsync(cancellationToken);
}
```

**Acceptance criteria:**
- [ ] Interface compiles.
- [ ] Returns titles of all non-rejected articles within the window, without any `currentId` exclusion.
- [ ] Old `GetRecentTitlesAsync(Guid currentId, ...)` is kept untouched until Step 5.

---

### Step 3a — Add FuzzySharp PackageReference to Worker project (if missing)

**File:** `Worker/Worker.csproj`

Check: `grep -i fuzzysharp Worker/Worker.csproj`. If absent, add:
```xml
<PackageReference Include="FuzzySharp" Version="2.0.2" />
```

**Acceptance criteria:**
- [ ] `dotnet build Worker/Worker.csproj` succeeds.

---

### Step 3b — Inject `ValidationOptions` and add dedup checks in `SourceFetcherWorker`

**File:** `Worker/Workers/SourceFetcherWorker.cs`

1. Add constructor parameter `IOptions<ValidationOptions> validationOptions` and store as `_validationOptions`.
2. In `ProcessSourceAsync`, fetch recent titles **once before the article loop** (not per article — avoids N+1):

```csharp
private async Task ProcessSourceAsync(
    Source source,
    ISourceParser parser,
    IRawArticleRepository rawArticleRepository,
    IRawArticleValidator validator,
    CancellationToken cancellationToken)
{
    var rawArticles = await parser.ParseAsync(source, cancellationToken);
    _logger.LogInformation("Parsed {Count} articles from {SourceName}", rawArticles.Count, source.Name);

    // Fetch once per source — reused for every article in the batch
    var recentTitles = await rawArticleRepository.GetRecentTitlesForDeduplicationAsync(
        _validationOptions.TitleDeduplicationWindowHours, cancellationToken);

    var saved = 0;
    var skipped = 0;

    foreach (var rawArticle in rawArticles)
    {
        if (string.IsNullOrEmpty(rawArticle.ExternalId)) continue;

        var (isValid, reason) = validator.Validate(rawArticle);
        if (!isValid)
        {
            _logger.LogDebug("Skipping '{Title}' from {SourceName}: {Reason}", rawArticle.Title, source.Name, reason);
            skipped++;
            continue;
        }

        // ExternalId dedup (existing)
        var exists = await rawArticleRepository.ExistsAsync(source.Id, rawArticle.ExternalId, cancellationToken);
        if (exists) continue;

        // URL deduplication (cross-source, new)
        var urlExists = await rawArticleRepository.ExistsByUrlAsync(rawArticle.OriginalUrl, cancellationToken);
        if (urlExists)
        {
            _logger.LogDebug("Skipping '{Title}' — URL already exists: {Url}", rawArticle.Title, rawArticle.OriginalUrl);
            skipped++;
            continue;
        }

        // Title fuzzy deduplication (new)
        if (recentTitles.Count > 0)
        {
            var bestScore = recentTitles
                .Select(t => FuzzySharp.Fuzz.TokenSetRatio(rawArticle.Title, t))
                .Max();

            if (bestScore >= _validationOptions.TitleSimilarityThreshold)
            {
                _logger.LogDebug("Skipping '{Title}' — title duplicate (score {Score})", rawArticle.Title, bestScore);
                skipped++;
                continue;
            }
        }

        await rawArticleRepository.AddAsync(rawArticle, cancellationToken);
        recentTitles.Add(rawArticle.Title); // intra-batch dedup: subsequent articles in this source see this title
        saved++;
    }

    _logger.LogInformation("Saved {Saved} new articles from {SourceName}, skipped {Skipped}",
        saved, source.Name, skipped);
}
```

Key decisions:
- `GetRecentTitlesForDeduplicationAsync` is called **once per source**, not once per article.
- After `AddAsync`, the new title is appended to `recentTitles` in memory — this gives intra-batch dedup (if the same source emits two near-identical articles in one parse, only the first is saved) without any extra DB queries.
- `ExistsByUrlAsync` remains per-article (needs the article's specific URL; no cheap way to batch).

**Acceptance criteria:**
- [ ] Worker compiles.
- [ ] `GetRecentTitlesForDeduplicationAsync` is called exactly once per `ProcessSourceAsync` invocation.
- [ ] Duplicate URL from a different source is skipped before `AddAsync`.
- [ ] Title fuzzy match above threshold is skipped before `AddAsync`.
- [ ] A second article with a similar title in the **same batch** is also skipped (intra-batch dedup).
- [ ] Both skip cases increment `skipped` and log at Debug.

---

### Step 4 — Remove title dedup block from `ArticleAnalysisWorker`

**File:** `Worker/Workers/ArticleAnalysisWorker.cs`

Remove:
- The entire "Step 1 — быстрая дедупликация по заголовку" block (the `GetRecentTitlesAsync` call, FuzzySharp scoring, and the early-return `Rejected` update).
- `IOptions<ValidationOptions>` constructor injection and `_validationOptions` field (only if `ValidationOptions` is not used elsewhere in this class — verify first).

Keep: `IRawArticleRepository` is still needed for `UpdateEmbeddingAsync` and `HasSimilarAsync`.

**Acceptance criteria:**
- [ ] `ArticleAnalysisWorker` no longer references `FuzzySharp`, `GetRecentTitlesAsync`, `TitleSimilarityThreshold`, or `TitleDeduplicationWindowHours`.
- [ ] `dotnet build Worker/Worker.csproj` succeeds.
- [ ] Semantic dedup (embedding + `HasSimilarAsync`) is untouched.

---

### Step 5 — Remove old `GetRecentTitlesAsync(Guid, int, CancellationToken)` from repository

**Files:** `Core/Interfaces/Repositories/IRawArticleRepository.cs`, `Infrastructure/Persistence/Repositories/RawArticleRepository.cs`

**Prerequisite:** No callers remain after Step 4. Verify with `grep -rn "GetRecentTitlesAsync"`.

**Acceptance criteria:**
- [ ] `grep -rn "GetRecentTitlesAsync"` returns zero hits in non-test source files.
- [ ] Solution builds without error.

---

### Step 6 — Unit tests for `SourceFetcherWorker` dedup logic

**File:** `Tests/Worker.Tests/Workers/SourceFetcherWorkerTests.cs`

| # | Scenario | Expected |
|---|---|---|
| 1 | ExternalId already exists | `AddAsync` never called |
| 2 | URL already exists (cross-source) | `AddAsync` never called |
| 3 | Title fuzzy match ≥ threshold | `AddAsync` never called |
| 4 | Title fuzzy match < threshold | `AddAsync` called once |
| 5 | No recent titles | `AddAsync` called once |
| 6 | Invalid article (validator returns false) | `AddAsync` never called |
| 7 | Empty ExternalId | `ExistsAsync` never called |

**Acceptance criteria:**
- [ ] All 7 tests pass.

---

### Step 7 — Unit tests for new repository methods

**File:** `Tests/Infrastructure.Tests/Persistence/Repositories/RawArticleRepositoryTests.cs`

| # | Method | Scenario | Expected |
|---|---|---|
| 1 | `ExistsByUrlAsync` | URL exists, Pending | `true` |
| 2 | `ExistsByUrlAsync` | URL exists, Rejected | `false` |
| 3 | `ExistsByUrlAsync` | URL absent | `false` |
| 4 | `GetRecentTitlesForDeduplicationAsync` | 2 in window, 1 outside | 2 titles returned |
| 5 | `GetRecentTitlesForDeduplicationAsync` | All rejected | empty list |

**Acceptance criteria:**
- [ ] All 5 tests pass using EF Core InMemory or SQLite.

---

### Step 8 — Full build verification

```bash
dotnet build NewsParser.slnx
```

**Acceptance criteria:**
- [ ] Zero build errors.
- [ ] `grep -rn "TitleDeduplicationWindowHours\|TitleSimilarityThreshold" Worker/Workers/ArticleAnalysisWorker.cs` returns empty.

---

## File Change Summary

| File | Type | Change |
|---|---|---|
| `Core/Interfaces/Repositories/IRawArticleRepository.cs` | Modify | Add `ExistsByUrlAsync`; add `GetRecentTitlesForDeduplicationAsync`; remove old `GetRecentTitlesAsync(Guid,...)` |
| `Infrastructure/Persistence/Repositories/RawArticleRepository.cs` | Modify | Implement two new methods; remove old method |
| `Worker/Worker.csproj` | Modify | Add `FuzzySharp` PackageReference if absent |
| `Worker/Workers/SourceFetcherWorker.cs` | Modify | Inject `ValidationOptions`; add URL + title-fuzzy checks before `AddAsync` |
| `Worker/Workers/ArticleAnalysisWorker.cs` | Modify | Remove title-dedup block; remove `ValidationOptions` injection |
| `Tests/Worker.Tests/Workers/SourceFetcherWorkerTests.cs` | Create | 7 unit tests |
| `Tests/Infrastructure.Tests/Persistence/Repositories/RawArticleRepositoryTests.cs` | Create | 5 unit tests |

---

## Design Notes

- **Why a new `GetRecentTitlesForDeduplicationAsync`?** The old method takes `currentId` because the article is already in the DB. Pre-save, there is no ID yet; a new method with the correct signature avoids a semantic hack (`Guid.Empty`).
- **`ExistsByUrlAsync` excludes Rejected** to mirror title-dedup behavior. A rejected article could legitimately reappear later.
- **Cross-source title dedup is intentional** — the goal is to stop semantically identical stories from multiple feeds from all hitting the LLM pipeline.
- **Performance:** `GetRecentTitlesForDeduplicationAsync` loads titles into memory. Acceptable for current volumes; `pg_trgm` is a future option if the window grows.
