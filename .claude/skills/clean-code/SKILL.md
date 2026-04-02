---
name: clean-code
description: Clean Code principles grounded in the NewsParser codebase — concrete violations and good examples named explicitly. Use when reviewing code quality, refactoring, or identifying smells. Triggers on: "code review", "refactor", "clean code", "SOLID", "is this good code", "too many parameters", "method too long", "magic number", "dead code", "naming", "comments", "guard clause".
---

# Clean Code Principles — NewsParser

These rules are grounded in the actual codebase — violations and good examples are named explicitly.

---

## Method Length

**Target: ≤ 20 lines per method. Extract a private method when a block has a distinct purpose.**

`ProcessSourceAsync` in `SourceFetcherWorker.cs` is the reference for acceptable length. It is ~55 lines, but each line is part of one coherent operation: iterate articles, gate on each validation rule, persist. The guard clauses keep it flat and readable top-to-bottom.

`ProcessRawArticleAsync` in `ArticleAnalysisWorker.cs` is the counterexample. It does five distinct things in sequence:
1. Update status to `Analyzing`
2. Call Claude for analysis
3. Generate an embedding
4. Check for semantic duplicates
5. Persist the new `Article`
6. Handle retry logic in the `catch`

Each numbered step is a candidate for its own private method:
```csharp
// BETTER — extracted phases
var result    = await AnalyzeWithClaudeAsync(rawArticle, analyzer, cancellationToken);
var embedding = await GenerateEmbeddingAsync(result, embeddingService, cancellationToken);
if (await IsDuplicateAsync(rawArticle, embedding, cancellationToken)) ...
await PersistArticleAsync(rawArticle, result, embedding, cancellationToken);
```

A method name that ends in `Async` and still needs numbered comments inside it is a method doing too much.

---

## Naming

**Methods must read like sentences. No abbreviations in identifiers.**

Good names from the codebase:
- `GetPendingForAnalysisAsync` — caller knows exactly what comes back
- `UpdateRejectionAsync` — past tense for completed state transitions
- `ToListItemDto` — reads like a conversion, not a getter
- `ExistsByUrlAsync` — reads like a predicate
- `IncrementRetryAsync` — atomic verb + noun

Bad patterns to refuse:
- `evt` for a variable of type `Event` (`ArticlesController.cs:47`) — write `relatedEvent`
- Single-letter loop variables for anything other than an index: `p` in `.ToDictionary(p => p.SourceType)` should be `parser`
- Abbreviated method names like `Proc`, `Init`, `Val` — spell them out

The standard: if you have to read the type signature to understand a variable, the name is wrong.

---

## Function Arguments

**Maximum 4 parameters. Introduce a parameter object at 5+.**

`ProcessRawArticleAsync` in `ArticleAnalysisWorker.cs` takes 6 parameters:
```csharp
private async Task ProcessRawArticleAsync(
    RawArticle rawArticle,
    IArticleAnalyzer analyzer,
    IGeminiEmbeddingService embeddingService,
    IArticleRepository articleRepository,
    IRawArticleRepository rawArticleRepository,
    CancellationToken cancellationToken)
```

This is a sign the method is doing too much. The fix is usually method extraction: if you split the method into phases (see Method Length above), each extracted method needs fewer arguments because it only handles one thing.

`ProcessSourceAsync` in `SourceFetcherWorker.cs` has 5 parameters, which is borderline. The pattern is acceptable here only because all five are genuinely needed and there is no natural grouping — but it is the limit.

Controllers stay clean because dependencies go into the primary constructor, not method signatures:
```csharp
public class ArticlesController(
    IArticleRepository articleRepository,
    IArticleApprovalService approvalService,
    IEventRepository eventRepository) : BaseController
```

---

## Comments

**A comment that says *what* the code does is a code smell. A comment that says *why* is acceptable.**

Bad — the Russian step comments in `ArticleAnalysisWorker.cs`:
```csharp
// Шаг 1 — анализ через Claude
var result = await analyzer.AnalyzeAsync(rawArticle, cancellationToken);

// Шаг 3 — генерация embedding для summary
var embedding = await embeddingService.GenerateEmbeddingAsync(result.Summary, cancellationToken);
```

These comments exist because the method does too many things. If each step is extracted into `AnalyzeWithClaudeAsync`, `GenerateEmbeddingAsync`, etc., the call site becomes self-documenting and the comments disappear. Note also that the step numbers jump from 1 to 3 (step 2 was deleted but the numbers were not updated) — proof that comments rot while code does not.

Good — the intent comment in `ProcessSourceAsync`:
```csharp
// Fetch once per source — reused for every article in the batch
var recentTitles = await rawArticleRepository.GetRecentTitlesForDeduplicationAsync(...);
```

This explains a non-obvious performance decision: the titles list is fetched outside the loop intentionally. Without the comment a reader might "fix" it by moving the call inside the loop.

Rule: before writing a comment, ask if a better name or extraction removes the need for it. Write the comment only if the answer is no.

---

## Early Returns and Guard Clauses

**Validate first, return early, keep the happy path at the bottom and un-nested.**

Both workers use this correctly. From `ProcessSourceAsync`:
```csharp
if (string.IsNullOrEmpty(rawArticle.ExternalId)) continue;

var (isValid, reason) = validator.Validate(rawArticle);
if (!isValid) { skipped++; continue; }

var exists = await rawArticleRepository.ExistsAsync(...);
if (exists) continue;

var urlExists = await rawArticleRepository.ExistsByUrlAsync(...);
if (urlExists) { skipped++; continue; }

// happy path — only reached if all guards passed
await rawArticleRepository.AddAsync(rawArticle, cancellationToken);
```

From `ArticlesController.cs` — same pattern with return instead of continue:
```csharp
if (request.PublishTargetIds is null || request.PublishTargetIds.Count == 0)
    return BadRequest("At least one publish target must be specified");

if (UserId is null)
    return Unauthorized();

// happy path
var article = await approvalService.ApproveAsync(...);
return Ok(article.ToListItemDto());
```

**Never nest the happy path inside an `else` block when you can guard-and-return at the top.**

---

## Magic Strings and Numbers

**Zero tolerance. Every literal constant that affects behavior belongs in an Options class or a named `const`.**

Good examples already in the codebase:
```csharp
_validationOptions.TitleSimilarityThreshold   // not 85
_options.BatchSize                            // not 10
_options.IntervalSeconds                      // not 600
_aiOptions.Gemini.DeduplicationThreshold      // not 0.9
```

Current violation in `ArticlesController.cs`:
```csharp
if (page < 1) page = 1;
if (pageSize is < 1 or > 100) pageSize = 20;
```

The `100` and `20` are magic numbers. They belong in a `PaginationOptions` or `ValidationOptions` class:
```csharp
// CORRECT
if (pageSize is < 1 or > _options.MaxPageSize) pageSize = _options.DefaultPageSize;
```

The rule also covers strings: hardcoded role names (`"Editor"`, `"Admin"`) must use `nameof(UserRole.Editor)` — already done in `ArticlesController.cs`:
```csharp
[Authorize(Roles = nameof(UserRole.Editor) + "," + nameof(UserRole.Admin))]
```

---

## Dead Code

**No commented-out code. No unused parameters. No orphaned step numbers.**

Commented-out code is a lie — it suggests the code might be needed again, but it is never restored and only confuses readers. Delete it; git history is the backup.

Unused parameters on private methods signal either a refactor left half-done or a method that no longer matches its callers. Both must be resolved, not suppressed with `_` discards or `#pragma` suppressions.

The skipped step numbers in `ArticleAnalysisWorker.cs` (1, 3, 4, 5, 6 — no step 2) are the concrete cost of leaving dead comments in place: they mislead without adding information.
