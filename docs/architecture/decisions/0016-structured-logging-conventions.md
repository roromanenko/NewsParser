# 0016 — Structured Logging Conventions Across All Layers

## Status
Proposed

## Context

Logging across the NewsParser solution is inconsistent and incomplete. Some workers and services log diligently, others not at all. There is no agreed-upon convention for which level to use where, no correlation across a single processing cycle, and no `ILoggerMessage`-style discipline. The result is operational logs that are simultaneously **noisy where they should be quiet** (per-article DEBUG noise written at INFO) and **silent where they should speak** (no AI call timing, no DB latency, no per-HTTP-request logging in the API).

### Concrete state of the codebase today

**Workers (all four files use `ILogger` correctly with message templates):**
- `Worker/Workers/SourceFetcherWorker.cs` — INFO for "Found N sources", "Parsed N articles", "Saved N skipped N"; DEBUG for skip reasons; WARNING on media ingestion failure; ERROR with `ex` on per-source failure. **Good template.**
- `Worker/Workers/ArticleAnalysisWorker.cs` — INFO per article ("Analyzing article {Id}: {Title}", "Successfully processed article {Id}"), INFO for grey-zone / auto-match decisions, WARNING for partial failures (key facts, title, importance), ERROR for hard failures, also WARNING when retry budget is exceeded. Message templates use named placeholders consistently. The `_logger.LogInformation("Article {Id} auto-matched ... (similarity: {S})", article.Id, topEvent.Id, topEvent.Id)` line has a **bug** — passes `topEvent.Id` twice instead of similarity.
- `Worker/Workers/PublishingWorker.cs` — INFO for "Found N", "Publishing {Id}", "Successfully published"; WARNING for skipped media files / missing publisher / missing parent message id; ERROR with `ex` on publish failure. **Good template.**
- `Worker/Workers/PublicationGenerationWorker.cs` — INFO for "Found N", "Generating content", "Successfully generated"; WARNING for missing event; ERROR on generation failure. **Good template.**

**Infrastructure services:**
- `Infrastructure/Services/MediaIngestionService.cs` — uses ILogger correctly (WARNING for download failures, INFO for successful ingestion).
- `Infrastructure/Services/HttpMediaContentDownloader.cs` — WARNING for non-2xx, oversize, unsupported MIME. Good.
- `Infrastructure/Services/TelegramMediaContentDownloader.cs` — WARNING on errors. Good.
- `Infrastructure/Services/TelegramClientService.cs` — INFO on connect, WARNING on resolve/download failure, ERROR on init failure. Good.
- `Infrastructure/Services/EventService.cs` — WARNING when the AI enrichment after merge fails (the merge itself succeeded). Good.
- `Infrastructure/Services/EventImportanceScorer.cs` — WARNING on unknown AI label. Good.
- `Infrastructure/Publishers/TelegramPublisher.cs` — INFO on success. **No WARNING/ERROR** — failures are thrown as `InvalidOperationException` and logged by `PublishingWorker`. Acceptable but should at least DEBUG before the request.
- `Infrastructure/Parsers/RssParser.cs` — WARNING on per-article scrape failure. Good.

**Infrastructure services with NO logging at all** (gaps):
- `Infrastructure/Services/SourceService.cs`, `UserService.cs`, `JwtService.cs`, `PublishTargetService.cs`, `PublicationService.cs` — pure CRUD with state transitions. **Need INFO on state-changing operations** (publication approved/rejected/sent, source created/updated, user created, editor updated/deleted) and DEBUG on reads. Token issuance in JwtService should be INFO (audit trail).
- `Infrastructure/Parsers/HtmlArticleContentScraper.cs` — silently swallows `TaskCanceledException` and `HttpRequestException`. **Need DEBUG/WARNING** so failures are observable.
- `Infrastructure/Parsers/TelegramParser.cs` — no logging. Should DEBUG on entry, INFO on per-channel result.
- `Infrastructure/AI/ClaudeArticleAnalyzer.cs`, `GeminiArticleAnalyzer.cs`, `GeminiEmbeddingService.cs`, `ClaudeEventClassifier.cs`, `ClaudeContradictionDetector.cs`, `ClaudeEventSummaryUpdater.cs`, `HaikuKeyFactsExtractor.cs`, `ClaudeContentGenerator.cs` — **none of the AI services log anything**. AI calls are the slowest, most expensive, and most failure-prone parts of the system. They need DEBUG before the call (model + prompt size), DEBUG/INFO on success (tokens, duration), WARNING on parse failures, ERROR on transport failures.
- All `Infrastructure/Persistence/Repositories/*` — **no logging**. This is correct policy and we should keep it that way (see Decision below).

**Api layer:**
- `Api/Middleware/ExceptionMiddleware.cs` — logs unhandled exceptions at ERROR. **Good but incomplete:** it does not log the request body or correlation id, and it does not distinguish 4xx-mappable exceptions (KeyNotFoundException, InvalidOperationException, ArgumentException) from true 5xx. Today every mapped exception is logged as ERROR even when it is a 404, which inflates error rates.
- `Api/Controllers/*` — **no controllers log anything**. There is no per-request log line and no per-action audit log.
- `Api/Controllers/AuthController.cs` — does not log login successes/failures (security audit gap).

**Configuration:**
- `Api/appsettings.json` — `"Default": "Information"`, `"Microsoft.AspNetCore": "Warning"`. Reasonable but missing namespace overrides and missing Worker overrides.
- `Worker/appsettings.json` — only `"Default": "Information"`, `"Microsoft.Hosting.Lifetime": "Information"`. Missing per-worker / per-namespace overrides.

**Anomalies that must be removed:**
- `Infrastructure/AI/GeminiEmbeddingService.cs:22` — `Console.WriteLine($"[DEBUG] Embedding URL: {url}");` — debug print left in production code, leaks the API key in the URL into stdout.
- `Worker/Workers/ArticleAnalysisWorker.cs:209-210` — message template says "(similarity: {S})" but the parameter passed is `topEvent.Id` not the similarity score.

**Skills landscape:** `.claude/skills/` does not yet contain a `logging-conventions` skill. Currently the `clean-code` and `code-conventions` skills contain no logging guidance. This ADR will dictate that one be authored alongside implementation, so the convention persists for future contributors.

---

## Options

### Option 1 — Minimal cleanup only
Fix the obvious anomalies (`Console.WriteLine`, the duplicated parameter), add AI-call logging, and stop. Leave existing patterns alone. Do not add per-request logging to the API, do not add logging to services missing it, do not add cycle correlation to workers.

**Pros:** Smallest diff. No architectural change. No new helpers.
**Cons:** Does not solve the structural problems: still no per-request observability in the API, still impossible to correlate "all log lines for one fetch cycle" or "all log lines for one article's lifetime", still inconsistent — half of services log, half do not. Operators will continue to fly blind on production incidents.

### Option 2 — Convention + targeted additions, no new infrastructure
Adopt an explicit logging convention (level table, message-template rules, scope rules) and apply it across the codebase. Add the missing logs in services and AI clients. Add lightweight per-cycle and per-item `ILogger.BeginScope` correlation in workers. Add per-request scope logging in the API via a small middleware. Categorise namespaces in `appsettings.json` so noisy categories can be tuned without code changes. **No external sinks, no Serilog, no OpenTelemetry — pure `Microsoft.Extensions.Logging`.**

**Pros:** Solves the structural problems. Stays inside the framework — zero new dependencies. The convention is small enough to live in one skill file. `BeginScope` works with the default console logger and any future structured sink (Seq, Loki, Datadog) without code changes.
**Cons:** Touches many files (every service/AI client/controller), needs a new skill file authored, needs operators to become familiar with the level matrix.

### Option 3 — Adopt Serilog + structured sinks now
Replace `Microsoft.Extensions.Logging` defaults with Serilog, add JSON console sink, add file sink with rotation, push enrichers for thread id / process id / machine name, plan for Seq/Elastic sinks later.

**Pros:** Best long-term observability. JSON-shaped logs are queryable. Enrichers add metadata for free.
**Cons:** Out of scope: the user explicitly stated **"No external log sink configuration needed — focus on using Microsoft.Extensions.Logging correctly."** Adopting Serilog now would contradict that. Also adds a new dependency and a new bootstrap path that has to be aligned across `Api/Program.cs` and `Worker/Program.cs`.

---

## Decision

**Option 2 — Convention + targeted additions, no new infrastructure.**

The work is split into five concrete sub-decisions.

### D1. Log-level matrix (the canonical reference)

The level for any new log line is selected from this matrix. **Deviation requires explicit justification in code review.**

| Level | Meaning | When to use | Concrete examples in NewsParser |
|---|---|---|---|
| `Trace` | Per-element diagnostic noise; off in all environments by default | Hot loops, per-row data | **Not used.** We have no current need; reserve for future. |
| `Debug` | Developer-visible detail; off in production by default | Inputs/outputs of a unit of work, skip-decisions in loops, AI request prep | RSS scrape skip reason, AI prompt size, per-item dedup decision in `SourceFetcherWorker`, "About to call Telegram sendMessage" before the request |
| `Information` | Operationally meaningful events; on in production | Lifecycle (start/stop), successful state transitions, batch summaries, request line | Worker cycle start with batch size, "Found N publications", "Successfully published", "Article approved by editor X", per-HTTP-request line, JWT issued |
| `Warning` | Recoverable degradation, retried failure, missing optional data | Transient failure that we recovered from, skipped item with a reason that operators care about, AI returned empty but we have a fallback | Title generator returned empty, scrape failed but RSS data kept, oversized media skipped, retry attempted, importance scoring failed but summary update succeeded |
| `Error` | Operation failed, work was lost or rejected | Per-item failure inside a batch (the batch continues), 5xx response from external API, unhandled exception in a controller, retry budget exhausted | "Failed to process article {Id}", "Failed to publish {Id}", unhandled controller exception, AI call failed after retries |
| `Critical` | Process cannot continue; immediate human attention | Database unreachable on startup, Telegram client failed to authenticate at boot, secret/config missing | DbUp migration failure on startup, JWT secret missing, Telegram client `LoginUserIfNeeded` failure on startup (currently logged as `Error` — promote to `Critical`) |

**Rules:**
- A failure that causes the batch loop to skip one item is **Error** for the item, not for the batch (the worker keeps running). The outer `ProcessAsync` does not log a separate Error — the per-item Error is enough.
- A failure that takes down the worker process is **Critical**.
- 4xx HTTP outcomes mapped from `KeyNotFoundException` / `InvalidOperationException` / `ArgumentException` in `ExceptionMiddleware` are **not** Errors — they are expected business outcomes. Log them at `Information` (or `Debug` if too noisy) with the mapped status code; reserve `Error` for the `_ => InternalServerError` branch.

### D2. Message template conventions (structured, not interpolated)

**Mandatory:** every log call uses message templates with named placeholders. **Never** use `$""` interpolation, `string.Format`, or `string.Concat` inside a log call.

```csharp
// CORRECT
_logger.LogInformation("Saved {Saved} new articles from {SourceName}, skipped {Skipped}",
    saved, source.Name, skipped);

// FORBIDDEN — kills structured logging, prevents querying by SourceName
_logger.LogInformation($"Saved {saved} new articles from {source.Name}, skipped {skipped}");
```

**Naming rules for placeholders:**
- PascalCase, descriptive: `{ArticleId}`, `{EventId}`, `{SourceName}`, `{Url}`, `{StatusCode}`, `{DurationMs}`
- Never single-letter (existing `(similarity: {S})` is wrong — must be `{Similarity}`)
- Stable across log lines — the same concept always uses the same key (`{ArticleId}` everywhere, never `{Id}` for an article in one place and `{ArticleId}` in another)

**Exception logging:** always pass the exception as the first argument, never concatenate `ex.Message` into the template.
```csharp
// CORRECT
_logger.LogError(ex, "Failed to publish {PublicationId} to {Target}", publication.Id, target.Name);

// FORBIDDEN — loses the stack trace
_logger.LogError("Failed to publish {Id}: {Error}", publication.Id, ex.Message);
```

**No PII / no secrets in templates:**
- Never log a JWT, API key, password, or email body. URL parameters that contain `?key=...` (Gemini) or `bot{token}` (Telegram) **must not** be logged. The current `Console.WriteLine($"[DEBUG] Embedding URL: {url}")` violates this and will be deleted.
- AI prompts may be logged at `Debug` only as **size** (`{PromptChars}`), not as content, because prompts include article bodies which may carry copyrighted text or PII.
- User emails in `AuthController` are operationally useful — log them at INFO on login success/failure, but never log the password or hash.

### D3. Scopes for correlation (no external libs)

Use `ILogger.BeginScope` with a state dictionary at exactly two points to give every log line correlation context for free:

**Workers — one scope per cycle, one nested per item.**
```csharp
using var cycleScope = _logger.BeginScope(new Dictionary<string, object>
{
    ["Worker"] = nameof(SourceFetcherWorker),
    ["CycleId"] = Guid.NewGuid()
});
// ... ProcessAsync body ...

foreach (var article in articles)
{
    using var itemScope = _logger.BeginScope(new Dictionary<string, object>
    {
        ["ArticleId"] = article.Id
    });
    await ProcessArticleAsync(article, ...);
}
```

This means every line emitted while processing one article carries `Worker=ArticleAnalysisWorker, CycleId=..., ArticleId=...` automatically — no need to pass these through method signatures or restate them in every template. The default console formatter prints scope state when `IncludeScopes=true` (set in `appsettings.json`).

**API — one scope per HTTP request.**
A new tiny middleware `RequestLoggingMiddleware` (`Api/Middleware/RequestLoggingMiddleware.cs`) registered immediately after `ExceptionMiddleware`:
- Generates or reads `X-Correlation-Id` from the incoming request, writes it back in the response.
- Opens a scope `{ CorrelationId, Method, Path, UserId? }` so every downstream log line carries it.
- Logs one `Information` line on completion: `"HTTP {Method} {Path} responded {StatusCode} in {DurationMs}ms"`.
- Skips logging for `/swagger/*` to avoid noise.

**No correlation framework, no `Activity` wiring, no OpenTelemetry SDK** — `BeginScope` plus a request-id middleware is enough for the scope of this ADR.

### D4. Where to add logging that is currently missing (per-file plan)

This list is the implementation surface for the feature-planner. It is exhaustive — if a file is not listed, no changes are required.

**Workers (`Worker/Workers/`):**
- `SourceFetcherWorker.cs` — wrap `ProcessAsync` in cycle scope; wrap inner `foreach (var (sourceType, parser))` body in `{ SourceType }` scope; wrap inner per-source loop in `{ SourceId, SourceName }` scope. Existing log lines stay (their levels are already correct). Add `Debug` on entry to `ProcessSourceAsync` ("Begin parse for {SourceName}").
- `ArticleAnalysisWorker.cs` — same cycle/item scope pattern keyed on `{ArticleId}`. **Fix the bug** at line 209-210 (`{S}` placeholder): replace with `{Similarity}` and pass the actual `topSimilarity` value. Promote the line "No similar events for article {Id}, creating new event" — already INFO, fine. Demote `Debug` decisions are already `LogDebug`. Keep current levels on success/failure.
- `PublishingWorker.cs` — cycle scope keyed on `CycleId`; item scope keyed on `{ PublicationId, PublishTargetName, Platform }`. Add `Debug` on entry to `ResolveAndPublishAsync` ("Resolving {Count} media files"). Existing levels are correct.
- `PublicationGenerationWorker.cs` — cycle scope; item scope keyed on `{ PublicationId, EventId }`. Existing levels are correct.

**Infrastructure AI clients (`Infrastructure/AI/`):** every client gets `ILogger<T>` injected and emits a uniform pair of log lines around the network call.
- Before the call: `_logger.LogDebug("Calling {Provider} {Model} with {PromptChars} chars", "Anthropic", _model, userPrompt.Length);`
- After success: `_logger.LogDebug("{Provider} {Model} succeeded in {DurationMs}ms", "Anthropic", _model, sw.ElapsedMilliseconds);`
- On parse failure: `_logger.LogWarning(ex, "{Provider} {Model} returned unparseable response", "Anthropic", _model);` then rethrow.
- On HTTP/transport failure: do **not** catch — let the exception propagate to the worker which logs ERROR. (The worker already has the `{ArticleId}` scope, so its ERROR has more useful correlation than the AI client could add.)
- Files affected: `ClaudeArticleAnalyzer.cs`, `GeminiArticleAnalyzer.cs`, `GeminiEmbeddingService.cs`, `ClaudeEventClassifier.cs`, `ClaudeContradictionDetector.cs`, `ClaudeEventSummaryUpdater.cs`, `HaikuKeyFactsExtractor.cs`, `ClaudeContentGenerator.cs`. `HaikuEventTitleGenerator.cs` already has `ILogger` — extend it to the same pattern.
- `GeminiEmbeddingService.cs` — **delete the `Console.WriteLine` on line 22.** Replace with the Debug call described above and **strip the API key from the URL before logging** (`?key=...`).
- DI: `InfrastructureServiceExtensions.AddAiServices` currently constructs each AI client with `new XxxAnalyzer(apiKey, model, prompt, httpClient)`. The factory lambdas need to additionally resolve `sp.GetRequiredService<ILogger<T>>()` and pass it to the constructor (same pattern as `HaikuEventTitleGenerator` already does).

**Infrastructure parsers (`Infrastructure/Parsers/`):**
- `HtmlArticleContentScraper.cs` — inject `ILogger<HtmlArticleContentScraper>`. The currently silent catches of `TaskCanceledException` and `HttpRequestException` log `Debug` ("Scrape skipped for {Url}: {Reason}"); non-2xx response logs `Debug`; oversized body logs `Warning`.
- `TelegramParser.cs` — inject `ILogger<TelegramParser>`. INFO once per channel ("Parsed {AlbumCount} albums and {SingleCount} singles from {Username}").
- `RssParser.cs` — already correct, no change.

**Infrastructure services (`Infrastructure/Services/`):**
- `SourceService.cs` — INFO on `CreateAsync`/`UpdateAsync`/`DeleteAsync` ("Source {SourceId} created/updated/deleted by …"). Inject `ILogger<SourceService>`.
- `UserService.cs` — INFO on `CreateUserAsync`/`UpdateEditorAsync`/`DeleteEditorAsync`. Do **not** log password or password hash.
- `JwtService.cs` — INFO on token issuance: "JWT issued for user {UserId} role {Role}". This is the audit trail for authentication.
- `PublicationService.cs` — INFO on every state-changing method: `CreateForEventAsync`, `UpdateContentAsync`, `ApproveAsync`, `RejectAsync`, `SendAsync`. Each carries `{ PublicationId, EditorId, NewStatus }`.
- `PublishTargetService.cs` — INFO on creation/update/deletion of publish targets.
- `EventService.cs` — already logs the AI-enrichment-after-merge warning. Add INFO on successful `MergeAsync` and `ReclassifyArticleAsync`.
- All other services already covered.

**API layer:**
- New `Api/Middleware/RequestLoggingMiddleware.cs` (see D3).
- `Api/Program.cs` — register the middleware **after** `ExceptionMiddleware`, **before** `UseCors`.
- `Api/Middleware/ExceptionMiddleware.cs` — change to log mapped 4xx exceptions (`KeyNotFoundException`, `InvalidOperationException`, `UnauthorizedAccessException`, `ArgumentException`) at **Information** with the mapped status code, and only the `_ => InternalServerError` branch at **Error** with the exception. This stops 404s from inflating error rates.
- `Api/Controllers/AuthController.cs` — INFO on successful login ("User {Email} logged in"), **Warning** on failed login ("Failed login attempt for {Email}"), INFO on registration. Inject `ILogger<AuthController>`.
- All other controllers — **no logging added.** Per-request logging is in the middleware; service-layer state changes are logged in services. Adding logs to controllers would duplicate both. The only exception is auth controller (above) because the security audit needs the email visible at the boundary.

**Repositories (`Infrastructure/Persistence/Repositories/*.cs`):**
- **No logging added.** Repositories are thin Dapper wrappers; their inputs and outputs are already visible from the service that called them (via the per-item scope) and from EF Core / Dapper command logging if it is ever turned on. Adding logging to every method would create noise without information. This matches the existing convention (no repository today logs anything) — the ADR confirms it as policy.
- Slow-query observability, when needed in the future, will be added once at the `IDbConnectionFactory` layer (a future ADR), not by sprinkling logs into 60+ repository methods.

### D5. Configuration changes

**`Api/appsettings.json`** — add namespace-level overrides and enable scope rendering:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.AspNetCore.Hosting.Diagnostics": "Information",
      "Api": "Information",
      "Infrastructure": "Information"
    },
    "Console": {
      "IncludeScopes": true,
      "FormatterName": "simple"
    }
  }
}
```

**`Api/appsettings.Development.json`** — flip `Infrastructure.AI` to `Debug` so the AI request/response telemetry shows up locally:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Information",
      "Infrastructure.AI": "Debug",
      "Api.Middleware.RequestLoggingMiddleware": "Information"
    }
  }
}
```

**`Worker/appsettings.json`** — same per-namespace pattern, with worker-specific overrides:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information",
      "Worker.Workers": "Information",
      "Infrastructure": "Information"
    },
    "Console": {
      "IncludeScopes": true,
      "FormatterName": "simple"
    }
  }
}
```

**`Worker/appsettings.Development.json`** — Debug for noisy diagnostic categories:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Worker.Workers.SourceFetcherWorker": "Debug",
      "Infrastructure.AI": "Debug",
      "Infrastructure.Parsers": "Debug"
    }
  }
}
```

No code changes are needed to enable scope rendering — `IncludeScopes=true` does it for the default console logger.

---

## Consequences

**Positive:**
- Every log line in the system can be traced back to (a) the worker cycle that produced it or (b) the HTTP request that produced it — with zero parameter-passing.
- Operators can dial individual namespaces up to Debug in production without touching code (e.g. `Infrastructure.AI=Debug` to inspect a misbehaving AI client).
- AI calls — currently a black hole — become observable at the call-site level (provider, model, prompt size, duration).
- 4xx outcomes stop polluting the error rate.
- The logging convention becomes the law of the codebase via a new skill, so future contributors do not regress to `string.Format` / silent catches.
- Two latent bugs (`Console.WriteLine` leaking the Gemini API key, the duplicated `topEvent.Id` parameter) are fixed as part of the work.

**Negative / risks:**
- ~25 files modified — a sizeable PR that has to land in one go to avoid half-conventions in the codebase. The feature-planner must split it into atomic steps that build cleanly at each stage.
- `BeginScope` allocates a dictionary per cycle and per item. Negligible at our throughput (one cycle every 600s in `SourceFetcherWorker`, batch sizes ≤ tens) but worth knowing. Use a `Dictionary<string, object>` literal — the runtime caches it well enough.
- The Debug logs in AI clients will produce a lot of output if someone forgets to keep `Infrastructure.AI=Information` in production `appsettings.json`. The default in `appsettings.json` (Production) keeps it at `Information`; only `Development` enables `Debug`.
- The new `RequestLoggingMiddleware` adds one log line per HTTP request. At our scale this is fine; for a high-RPS rewrite it would need sampling.

**Files affected:**
- New: `Api/Middleware/RequestLoggingMiddleware.cs`, `.claude/skills/logging-conventions/SKILL.md`.
- Modified: `Api/Program.cs`, `Api/Middleware/ExceptionMiddleware.cs`, `Api/Controllers/AuthController.cs`, `Api/appsettings.json`, `Api/appsettings.Development.json`, `Worker/appsettings.json`, `Worker/appsettings.Development.json`, all four files in `Worker/Workers/`, `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`, all eight AI client files in `Infrastructure/AI/`, `Infrastructure/Parsers/HtmlArticleContentScraper.cs`, `Infrastructure/Parsers/TelegramParser.cs`, `Infrastructure/Services/SourceService.cs`, `Infrastructure/Services/UserService.cs`, `Infrastructure/Services/JwtService.cs`, `Infrastructure/Services/PublicationService.cs`, `Infrastructure/Services/PublishTargetService.cs`, `Infrastructure/Services/EventService.cs`.
- Untouched: every file under `Core/`, every repository under `Infrastructure/Persistence/`, every controller other than `AuthController`, every mapper, every DTO, every Options class.

---

## Implementation Notes

**For `feature-planner`:**

1. **Order of work matters** — keep the build green at every step:
   - Step A: Author `.claude/skills/logging-conventions/SKILL.md` from the D1, D2, D3 sections of this ADR (so reviewers and future contributors have a reference).
   - Step B: Configuration updates (`appsettings*.json` in both Api and Worker). No code change, no risk.
   - Step C: Add `RequestLoggingMiddleware` and wire it in `Api/Program.cs`. Adjust `ExceptionMiddleware` to demote 4xx-mapped exceptions to Information.
   - Step D: AI client logging — extend each constructor to accept `ILogger<T>` and add the Debug-before / Debug-after / Warning-on-parse pattern. Update the DI factories in `InfrastructureServiceExtensions.AddAiServices` to inject the logger. Delete the `Console.WriteLine` in `GeminiEmbeddingService` and ensure the logged URL has the API key stripped.
   - Step E: Service logging — `SourceService`, `UserService`, `JwtService`, `PublicationService`, `PublishTargetService`, `EventService` get `ILogger<T>` injected and INFO logs on state-changing methods.
   - Step F: Parser logging — `HtmlArticleContentScraper` and `TelegramParser` get `ILogger<T>` and the missing logs.
   - Step G: Worker scopes — add cycle and item `BeginScope` to all four workers. Fix the `{S}` → `{Similarity}` bug in `ArticleAnalysisWorker` while in the file. **Do not delete or restate any existing log lines except the `(similarity: {S})` line**, since their levels and templates already conform.
   - Step H: `AuthController` login/registration logging.

2. **Skills to follow during implementation:**
   - `.claude/skills/code-conventions/SKILL.md` — for primary-constructor vs. traditional-constructor decisions when adding `ILogger<T>` to existing classes (workers must keep traditional constructors; services use primary constructors).
   - `.claude/skills/clean-code/SKILL.md` — for naming of new methods (no abbreviations in scope keys, no `evt`-style names) and for making sure no log call uses string interpolation.
   - `.claude/skills/api-conventions/SKILL.md` — for placement of the new middleware (after `ExceptionMiddleware`, before `UseCors`) and for the audit log format in `AuthController`.
   - The new `.claude/skills/logging-conventions/SKILL.md` (Step A above) once authored is the binding reference for every other step.

3. **Testing:** This work has no behavioral change to test against business logic. Verification is:
   - Build succeeds at each step.
   - All existing tests pass — no test should depend on a specific log line.
   - Manual smoke test: start the API, hit one endpoint, confirm the request log line and the correlation id appear; start the Worker, confirm cycle and per-item scope keys appear in console output.

4. **Out of scope (do not expand the work):**
   - No external sinks (Serilog, Seq, Elastic, OpenTelemetry, Application Insights). The user explicitly excluded these.
   - No structured request body logging — only method, path, status, duration.
   - No logging in repositories (D4 explicitly forbids it).
   - No `Activity`/`ActivitySource` instrumentation — that is a separate distributed-tracing decision for a future ADR.
   - No log-driven metrics (counters, histograms) — also a future decision.
