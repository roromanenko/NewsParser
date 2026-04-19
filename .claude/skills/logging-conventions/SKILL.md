---
name: logging-conventions
description: NewsParser structured logging conventions using Microsoft.Extensions.Logging. Use when adding a new log call, choosing a log level, adding BeginScope correlation, configuring appsettings Logging section, or checking whether a log call is correct. Triggers on: "add log", "log level", "which level", "BeginScope", "correlation", "scope", "ILogger", "log template", "message template", "PII in logs", "structured logging".
---

# Logging Conventions — NewsParser

This is the binding reference for every log call in the solution. Follow these rules exactly when adding or reviewing logging code.

---

## D1. Log-Level Matrix

| Level | Meaning | When to use | Examples in NewsParser |
|---|---|---|---|
| `Trace` | Per-element noise; disabled everywhere | Hot loops, per-row data | **Not used** — reserved for future |
| `Debug` | Developer detail; off in production by default | AI call inputs/outputs, skip-decision in loops, parse-before-request | AI prompt size before network call, RSS scrape skip reason, "Begin parse for {SourceName}", "Resolving {Count} media files" |
| `Information` | Operationally meaningful; on in production | Lifecycle (start/stop), successful state transitions, batch summaries, per-HTTP-request line | "Found {Count} articles", "JWT issued for user {UserId}", "HTTP {Method} {Path} responded {StatusCode} in {DurationMs}ms", "Source {SourceId} created: {SourceName}" |
| `Warning` | Recoverable degradation; we kept going | Transient failure with fallback, skipped item with reason, AI returned empty but we have fallback | Title generator returned empty, scrape failed but RSS data kept, media file oversized, key-facts extraction failed but summary succeeded |
| `Error` | Operation failed; work was lost for this item | Per-item failure inside a batch (batch keeps running), unhandled exception in a controller | "Failed to process article {Id}", "Failed to publish {Id}" |
| `Critical` | Process cannot continue; immediate human attention | DB unreachable at startup, Telegram `LoginUserIfNeeded` failure, JWT secret missing | `TelegramClientService.InitializeAsync` failure |

**Level selection rules:**
- A failure that causes the batch loop to skip **one item** → `Error` for the item only; the outer `ProcessAsync` does not emit a second `Error`.
- A failure that terminates the worker process → `Critical`.
- 4xx HTTP outcomes mapped from `KeyNotFoundException` / `InvalidOperationException` / `ArgumentException` in `ExceptionMiddleware` → `Information` (they are expected business outcomes, not errors). Only the `_ => InternalServerError` branch → `Error`.

---

## D2. Message Template Conventions

### Mandatory: named placeholders, never string interpolation

```csharp
// CORRECT
_logger.LogInformation("Saved {Saved} new articles from {SourceName}, skipped {Skipped}",
    saved, source.Name, skipped);

// FORBIDDEN — kills structured logging; {SourceName} cannot be queried
_logger.LogInformation($"Saved {saved} new articles from {source.Name}, skipped {skipped}");
```

### Naming rules for placeholders

- PascalCase, descriptive: `{ArticleId}`, `{EventId}`, `{SourceName}`, `{Url}`, `{StatusCode}`, `{DurationMs}`
- Never single-letter: `{S}` is wrong — use `{Similarity}`
- Stable across the codebase — the same concept always uses the same key:
  - Articles → `{ArticleId}` everywhere (not `{Id}` in one place and `{ArticleId}` in another)
  - Events → `{EventId}`
  - Publications → `{PublicationId}`
  - Sources → `{SourceId}`, `{SourceName}`

### Exception logging rule

Always pass the exception as the **first argument** to the log call. Never concatenate `ex.Message` into the template.

```csharp
// CORRECT — preserves full stack trace, appears in structured sinks
_logger.LogError(ex, "Failed to publish {PublicationId} to {Target}", publication.Id, target.Name);

// FORBIDDEN — loses the stack trace; {Error} carries only the message string
_logger.LogError("Failed to publish {Id}: {Error}", publication.Id, ex.Message);
```

### Forbidden patterns

| Pattern | Why it is forbidden |
|---|---|
| `$""` interpolation in log calls | Defeats structured logging — the value is concatenated before the call |
| `string.Format(...)` in log calls | Same as interpolation |
| `ex.Message` in the template | Loses stack trace; use the exception overload |
| Single-letter placeholders (`{S}`, `{N}`) | Not queryable; misleading |
| Logging JWT values, API keys, passwords, password hashes | Security — never log secrets |
| Logging `?key=...` query parameters (Gemini URL) | Leaks the API key into stdout |
| Logging full AI prompt content | May contain copyrighted text or PII; log only size (`{PromptChars}`) |
| `Console.WriteLine` for diagnostics | Use `_logger.LogDebug`; console writes bypass log filtering |

### PII / secrets policy

- Never log: JWT token, Anthropic API key, Gemini API key (strip `?key=...` from URLs), passwords, password hashes.
- User emails: allowed at `Information` for login success/failure in `AuthController` (security audit trail). Do not log email in service-layer calls.
- AI prompts: log only the **size** in characters (`{PromptChars}`), never the content.

---

## D3. Scopes for Correlation

Use `ILogger.BeginScope` with a `Dictionary<string, object>` state at two points to give every log line correlation context automatically.

### Worker pattern — one scope per cycle, one nested per item

```csharp
private async Task ProcessAsync(CancellationToken cancellationToken)
{
    using var cycleScope = _logger.BeginScope(new Dictionary<string, object>
    {
        ["Worker"] = nameof(ArticleAnalysisWorker),
        ["CycleId"] = Guid.NewGuid()
    });

    // ... fetch batch ...

    foreach (var article in articles)
    {
        using var itemScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ArticleId"] = article.Id
        });
        await ProcessArticleAsync(article, ...);
    }
}
```

Every log line emitted while processing one article automatically carries `Worker=ArticleAnalysisWorker, CycleId=..., ArticleId=...` — no need to pass these through method signatures or repeat them in every template.

### Per-worker scope keys

| Worker | Cycle scope keys | Item scope keys |
|---|---|---|
| `ArticleAnalysisWorker` | `Worker`, `CycleId` | `ArticleId` |
| `SourceFetcherWorker` | `Worker`, `CycleId` | `SourceType` (source-type loop), then `SourceId`, `SourceName` (per-source) |
| `PublishingWorker` | `Worker`, `CycleId` | `PublicationId`, `PublishTargetName`, `Platform` |
| `PublicationGenerationWorker` | `Worker`, `CycleId` | `PublicationId`, `EventId` |

### API pattern — one scope per HTTP request

`RequestLoggingMiddleware` opens a scope per request:

```csharp
using var scope = _logger.BeginScope(new Dictionary<string, object>
{
    ["CorrelationId"] = correlationId,
    ["Method"] = context.Request.Method,
    ["Path"] = context.Request.Path.Value ?? string.Empty,
    // UserId only when authenticated:
    ["UserId"] = userId
});
```

Every downstream log line carries `CorrelationId`, `Method`, `Path`, and optionally `UserId` without any changes to controllers or services.

**Always use `Dictionary<string, object>` literals** — not anonymous objects, not strongly-typed state classes. The default console logger reads `IDictionary<string, object>` for scope rendering.

---

## Standard AI Client Log Pair

Every AI client emits exactly this pair around the network call:

```csharp
var sw = System.Diagnostics.Stopwatch.StartNew();
_logger.LogDebug("Calling {Provider} {Model} with {PromptChars} chars",
    "Anthropic", _model, userPrompt.Length);

var response = await client.Messages.GetClaudeMessageAsync(request, cancellationToken);

sw.Stop();
_logger.LogDebug("{Provider} {Model} succeeded in {DurationMs}ms",
    "Anthropic", _model, sw.ElapsedMilliseconds);
```

On parse failure:
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "{Provider} {Model} returned unparseable response",
        "Anthropic", _model);
    throw;
}
```

On HTTP/transport failure: do **not** catch — let the exception propagate to the worker. The worker's `Error` log already has the `{ArticleId}` scope.

Provider values: `"Anthropic"` for all Claude/Haiku clients, `"Gemini"` for Gemini clients.

---

## Service-Layer Log Calls (state-changing only)

Log `Information` after the repository call in every **state-changing** method. Never log read-only methods.

```csharp
// CORRECT — SourceService.cs
await sourceRepository.CreateAsync(source, cancellationToken);
_logger.LogInformation("Source {SourceId} created: {SourceName}", source.Id, source.Name);

// CORRECT — PublicationService.cs (all three placeholders required)
_logger.LogInformation("Publication {PublicationId} status set to {NewStatus} by editor {EditorId}",
    publication.Id, PublicationStatus.Approved, editorId);
```

---

## appsettings.json Logging Sections (canonical)

### Api/appsettings.json (production defaults)

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

### Api/appsettings.Development.json

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

### Worker/appsettings.json (production defaults)

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

### Worker/appsettings.Development.json

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

---

## Constructor Injection Style for ILogger

- **Services** (primary-constructor style): add `ILogger<T>` as a primary-constructor parameter alongside existing parameters.
- **Workers** (traditional constructor): add `ILogger<T>` to the existing constructor parameter list and assign to a `private readonly` field.
- **Middleware** (primary-constructor style): `ILogger<T>` is a primary-constructor parameter.

Repository classes: **no `ILogger` injection** — repositories are thin Dapper wrappers; logging belongs at the service layer above them.
