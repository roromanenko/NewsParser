# 2026-04-24 — AI Request Logging and Cost Tracking

## Status
Proposed

## Context

The NewsParser pipeline makes heavy use of two AI providers:

- **Anthropic Claude** via `Anthropic.SDK` — used by eight services in `Infrastructure/AI/` (`ClaudeArticleAnalyzer`, `ClaudeEventClassifier`, `ClaudeEventSummaryUpdater`, `ClaudeContentGenerator`, `ClaudeContradictionDetector`, `HaikuKeyFactsExtractor`, `HaikuEventTitleGenerator`).
- **Google Gemini** via raw `HttpClient` — used by two services (`GeminiArticleAnalyzer`, `GeminiEmbeddingService`).

These services are consumed from `ArticleAnalysisWorker` (the biggest spender — it calls the analyzer, classifier, summary updater, key-facts extractor, title generator, contradiction detector, and embedding service for every pending article) and from `PublicationGenerationWorker` via `IContentGenerator`.

Today **there is no visibility at all** into token consumption or USD cost:
- The SDK / HTTP response does carry token counts (Anthropic returns `usage.input_tokens`, `usage.output_tokens`, `usage.cache_creation_input_tokens`, `usage.cache_read_input_tokens`; Gemini returns `usageMetadata.promptTokenCount`, `candidatesTokenCount`, `totalTokenCount`), but nothing reads those fields.
- The only per-call observability added by ADR 0016 is `LogDebug` lines with `{PromptChars}` and `{DurationMs}` — no tokens, no cost.
- It is impossible to answer questions like: "How much did we spend on Haiku last week?", "Which worker dominates our Anthropic bill?", "Is prompt caching actually kicking in?", "How much does one article cost end-to-end?"

The goal of this ADR is to add **persistent, per-call cost tracking** so operators can optimise AI spending: identify the most expensive call-sites, enable and verify Anthropic prompt caching savings, and correlate cost to specific articles / workers.

### Constraints baked into the task

1. Every call must be logged to the database after completion, with real USD cost computed from token counts × model pricing.
2. Anthropic cache accounting is required: `cache_creation_input_tokens × 1.25 × input_price`, `cache_read_input_tokens × 0.1 × input_price`.
3. **Logging must never break the business flow** — if the log write fails, the worker continues and the caller receives the AI result normally.
4. Pricing lives in `appsettings.json` as a `ModelPricing` options class so adjustments do not require a deploy.
5. No UI for now; this is a data-collection feature.

### Shape of the existing code, as written today

- All ten AI client constructors follow the same rough template: they accept `apiKey`, `model`, `prompt`, `ILogger<T>`, and (for Gemini) an `HttpClient`. They are registered as `Scoped` in `InfrastructureServiceExtensions.AddAiServices` via factory lambdas.
- Token-usage fields are **not currently read** from either response. Anthropic's `MessageResponse.Usage` is populated by the SDK; Gemini's `usageMetadata` is a sibling of `candidates` in the response JSON and is not parsed today.
- No shared base class and no cross-cutting pipeline exists — every AI client is a hand-written `public class` with its own try/catch, its own JSON cleanup, its own `Stopwatch`, its own logging pattern.
- Workers (`ArticleAnalysisWorker`, `PublicationGenerationWorker`) call AI services through their `Core.Interfaces.AI` interfaces (`IArticleAnalyzer`, `IEventClassifier`, …). These interfaces return domain objects (`ArticleAnalysisResult`, `EventClassificationResult`, `string`, `float[]`, …) — they carry **no token or usage data today**.
- Article IDs are known in the worker; some AI calls (e.g. `IContentGenerator`, `IEventTitleGenerator`) have **no** `article_id` — they operate on events.
- Database access follows strict Dapper conventions (`.claude/skills/dapper-conventions/SKILL.md`): `IDbConnectionFactory`, `IUnitOfWork`, SQL constants in `*Sql.cs`, PascalCase quoted columns, snake_case table names, enums stored as strings, `DateTimeOffset` for timestamps.

### Recent ADRs that constrain this work

- **ADR 0016 (Structured Logging Conventions)** — every AI client already has `ILogger<T>` and emits standard Debug lines (`Calling {Provider} {Model}…` / `succeeded in {DurationMs}ms`). Our new logging layer must **not** duplicate or replace these; it lives alongside them.
- **Dapper migration ADR** — repositories are `internal`, use `IDbConnectionFactory` / `IUnitOfWork`, and have SQL constants in `Infrastructure/Persistence/Repositories/Sql/`.
- Workers use traditional constructors and only inject singletons (`IServiceScopeFactory`, `ILogger<T>`, `IOptions<T>`). Scoped services are resolved via `_scopeFactory.CreateScope()`.

---

## Options

### Option 1 — Edit every AI client directly

Open each of the ten AI client files, read the usage fields from the SDK / response, build a log record inline, and call an `IAiRequestLogger` before returning. Interfaces (`IArticleAnalyzer`, `IEventClassifier`, …) stay unchanged; the log write is internal to each client.

**Pros:**
- Simple, no new abstractions.
- Each client has direct, type-specific access to the provider's usage shape (`MessageResponse.Usage` vs. Gemini's `usageMetadata`) — no need for a generic interface to flatten the two shapes.
- Zero change to worker code.

**Cons:**
- Ten files must be modified, and every future AI client must remember to emit the log (easy to forget).
- The "try to log but never break the business flow" error-handling pattern gets duplicated ten times. Drift is inevitable.
- Couples the AI clients to `IAiRequestLogger` — they gain a second responsibility (call the AI and persist cost metadata) on top of calling the AI.

### Option 2 — Decorator per interface

Create ten decorator classes (`LoggingArticleAnalyzer`, `LoggingEventClassifier`, …), each wrapping its inner implementation, registered via DI with the decorator pattern.

**Pros:**
- Classical cross-cutting concern solution.
- Inner clients stay pristine.

**Cons:**
- **Doesn't actually work for this problem.** The decorator only sees the domain return type (e.g. `ArticleAnalysisResult`), not the provider's `Usage` object. To compute cost we need the raw token counts from `MessageResponse.Usage` / Gemini's `usageMetadata`, which live inside the inner client. A decorator wrapping `IArticleAnalyzer.AnalyzeAsync(article) -> ArticleAnalysisResult` has no hook into the SDK response object.
- We would either have to pollute every Core domain result object with `TokenUsage` properties (leaking provider concerns into Core) or add a side-channel (e.g. a scoped `IAiUsageCollector`) that the inner client writes to and the decorator reads. Both are worse than Option 3.
- Ten decorator files for ten interfaces is a lot of surface area for a feature whose essence is one cross-cutting record.

### Option 3 — Thin telemetry helper that each client invokes after the SDK call

Introduce one internal service in `Infrastructure/AI/Telemetry/`:

```
IAiRequestLogger
├─ LogAsync(AiRequestLogEntry entry, CancellationToken ct)   // fire-and-forget internally
```

and one pure cost calculator:

```
IAiCostCalculator
├─ Calculate(AiUsage usage, ModelPricing pricing) -> decimal  // returns cost_usd
```

Each AI client, after it receives the provider response, reads the usage fields from the provider-specific object, builds an `AiRequestLogEntry` (a record), and calls `IAiRequestLogger.LogAsync`. The logger internally wraps the repository call in a try/catch (log-and-swallow) so a DB failure never escapes into the worker.

Provider-specific data extraction stays where it belongs (inside each client), but the cost calculation, DB call, and error-isolation rules live **once** in the telemetry layer.

**Pros:**
- One place to change the error-isolation policy, one place to change the cost formula, one place to change the SQL write.
- Each client touches telemetry in only 2–3 lines at the end of its method: "read tokens from response, call `LogAsync(entry)`". Easy to read, easy to grep, and a future AI client has a clear template.
- No leaking of provider-specific shapes into Core domain objects.
- `IAiCostCalculator` is a pure, side-effect-free service that is trivially testable in isolation.

**Cons:**
- Each AI client must be edited (same count as Option 1), but the footprint per file is much smaller and mechanical.
- The abstraction is "helper-service-called-explicitly" rather than "middleware-that-wraps-transparently" — slightly less elegant than a decorator but aligned with what the decorator option cannot deliver.

### Error isolation: log-and-swallow vs. fire-and-forget-with-queue

Two sub-options for how `IAiRequestLogger.LogAsync` isolates itself from the caller:

**A. Synchronous log-and-swallow** — `LogAsync` awaits the repository write inside a try/catch that logs a `LogWarning` on failure and returns. Latency of the AI call is increased by one INSERT round-trip to the DB (~a few ms on Aiven).

**B. Fire-and-forget with in-process queue** — `LogAsync` enqueues the entry to a `Channel<AiRequestLogEntry>`; a singleton `AiRequestLogWriter` background service drains the channel and writes batches. No latency added to the AI call.

**Decision on this sub-choice: A (synchronous log-and-swallow).**

- The AI call already takes hundreds of milliseconds to several seconds; adding ~5 ms for an INSERT is invisible.
- The batch sizes in our workers are small (tens of articles per cycle). There is no scale problem to solve.
- A fire-and-forget queue introduces real risks: entries lost on worker shutdown, complexity around service lifetime, and hidden coupling to a singleton writer. Not worth it for a feature whose whole purpose is reliable cost data.
- Option B can be added later as a pure infrastructure change to `AiRequestLogger` if real latency problems ever materialise — the interface stays the same.

---

## Decision

**Option 3 — Thin telemetry helper invoked by each AI client, with synchronous log-and-swallow.**

Concretely:

### D1. Domain layer (`Core/`)

**New domain model** — `Core/DomainModels/AiRequestLog.cs`:
```csharp
public class AiRequestLog
{
    public Guid Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Worker { get; init; } = string.Empty;      // e.g. "ArticleAnalysisWorker", "PublicationGenerationWorker", "Unknown"
    public string Provider { get; init; } = string.Empty;    // "Anthropic" | "Gemini"
    public string Operation { get; init; } = string.Empty;   // e.g. "Analyze", "Classify", "EmbedContent", "GenerateContent"
    public string Model { get; init; } = string.Empty;       // full model id

    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheCreationInputTokens { get; init; }       // Anthropic only; 0 for Gemini
    public int CacheReadInputTokens { get; init; }           // Anthropic only; 0 for Gemini
    public int TotalTokens { get; init; }                    // convenience sum

    public decimal CostUsd { get; init; }                    // computed; never null (0 if pricing missing)
    public int LatencyMs { get; init; }
    public AiRequestStatus Status { get; init; }             // Success | Error
    public string? ErrorMessage { get; init; }               // null on Success; first 500 chars on Error

    public Guid CorrelationId { get; init; }                 // per-cycle / per-request; set by caller
    public Guid? ArticleId { get; init; }                    // nullable — not all calls have one
}

public enum AiRequestStatus
{
    Success,
    Error
}
```

- Timestamps are `DateTimeOffset` per convention.
- Enums stored as strings.
- `CostUsd` is `decimal` (not `double`) because it is money — matches the guidance implicit in the codebase for anything resembling currency.
- `CorrelationId` is a `Guid` because both workers already generate a `CycleId` GUID (see `ArticleAnalysisWorker.ProcessAsync` in ADR 0016) and the API middleware emits an `X-Correlation-Id` GUID. Using the same type keeps querying uniform.

**New repository interface** — `Core/Interfaces/Repositories/IAiRequestLogRepository.cs`:
```csharp
public interface IAiRequestLogRepository
{
    Task AddAsync(AiRequestLog entry, CancellationToken cancellationToken = default);
}
```

Only `AddAsync` in the first pass. Read / aggregation queries are out of scope; they will be added when the UI/dashboard work begins in a later feature.

**New service interfaces** — `Core/Interfaces/AI/IAiCostCalculator.cs` and `Core/Interfaces/AI/IAiRequestLogger.cs`:
```csharp
public interface IAiCostCalculator
{
    decimal Calculate(AiUsage usage, string provider, string model);
}

public interface IAiRequestLogger
{
    Task LogAsync(AiRequestLogEntry entry, CancellationToken cancellationToken = default);
}
```

- `AiUsage` is a plain record in `Core/DomainModels/AI/AiUsage.cs`: `(int InputTokens, int OutputTokens, int CacheCreationInputTokens, int CacheReadInputTokens)`. It is the provider-agnostic input to the cost calculator.
- `AiRequestLogEntry` is a record in `Core/DomainModels/AI/AiRequestLogEntry.cs` carrying the inputs the logger needs (provider, operation, model, usage, latencyMs, status, errorMessage, correlationId, articleId, worker). The logger builds the final `AiRequestLog` (adds Id, Timestamp, computed CostUsd, TotalTokens).

Placing these interfaces under `Core/Interfaces/AI/` matches the existing group (`IArticleAnalyzer`, `IEventClassifier`, etc. all live there).

### D2. Infrastructure layer (`Infrastructure/`)

**New Options class** — `Infrastructure/Configuration/ModelPricingOptions.cs`:
```csharp
public class ModelPricingOptions
{
    public const string SectionName = "ModelPricing";

    public Dictionary<string, ModelPrice> Anthropic { get; set; } = new();
    public Dictionary<string, ModelPrice> Gemini { get; set; } = new();

    public double AnthropicCacheWriteMultiplier { get; set; } = 1.25;   // input_price × 1.25
    public double AnthropicCacheReadMultiplier { get; set; } = 0.1;     // input_price × 0.1
}

public class ModelPrice
{
    // Dollars per 1,000,000 tokens. Use decimal for money.
    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }
}
```

Dictionary keyed by full model id (e.g. `"claude-haiku-4-5-20251001"`, `"gemini-2.0-flash"`, `"gemini-embedding-001"`). Registered via `services.Configure<ModelPricingOptions>(configuration.GetSection(ModelPricingOptions.SectionName))` in `InfrastructureServiceExtensions`.

**New cost calculator** — `Infrastructure/AI/Telemetry/AiCostCalculator.cs`:
```csharp
internal class AiCostCalculator(IOptions<ModelPricingOptions> options) : IAiCostCalculator
{
    private readonly ModelPricingOptions _options = options.Value;

    public decimal Calculate(AiUsage usage, string provider, string model) { ... }
}
```

Formula (decimal math only):
- `priceTable = provider == "Anthropic" ? _options.Anthropic : _options.Gemini;`
- If `priceTable` has no entry for `model`, return `0m` and the logger emits a `LogWarning("Missing pricing for {Provider} {Model}", ...)` once per cycle (throttled by caching the last-warned model in the logger; Options reload swaps the dict).
- For Gemini: `cost = (input × inputPrice + output × outputPrice) / 1_000_000`.
- For Anthropic: the `input_tokens` field in the SDK response is already the **non-cached** input portion; `cache_creation_input_tokens` and `cache_read_input_tokens` are separate. So:
  ```
  cost = (
      input            * inputPrice                                       +
      output           * outputPrice                                      +
      cacheCreation    * inputPrice * AnthropicCacheWriteMultiplier       +
      cacheRead        * inputPrice * AnthropicCacheReadMultiplier
  ) / 1_000_000
  ```

**New AI request logger** — `Infrastructure/AI/Telemetry/AiRequestLogger.cs`:
```csharp
internal class AiRequestLogger(
    IAiCostCalculator calculator,
    IAiRequestLogRepository repository,
    ILogger<AiRequestLogger> logger) : IAiRequestLogger
{
    public async Task LogAsync(AiRequestLogEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            var cost = calculator.Calculate(entry.Usage, entry.Provider, entry.Model);

            var row = new AiRequestLog
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ...
                CostUsd = cost,
                ...
            };

            await repository.AddAsync(row, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Failed to persist AI request log for {Provider} {Model} ({Operation})",
                entry.Provider, entry.Model, entry.Operation);
        }
    }
}
```

`OperationCanceledException` is allowed to propagate (host shutdown / worker-cancel behaviour). All other exceptions are swallowed with a warning — this is the single enforcement of "logging must not break business flow".

**New repository** — `Infrastructure/Persistence/Repositories/AiRequestLogRepository.cs` (internal Dapper repository per `.claude/skills/dapper-conventions/SKILL.md`).

**New entity** — `Infrastructure/Persistence/Entity/AiRequestLogEntity.cs` (flat class, PascalCase properties matching the SQL columns).

**New mapper** — `Infrastructure/Persistence/Mappers/AiRequestLogMapper.cs` with `ToDomain(this AiRequestLogEntity)` and `ToEntity(this AiRequestLog)`. Enum stored/parsed as string per convention.

**New SQL constants** — `Infrastructure/Persistence/Repositories/Sql/AiRequestLogSql.cs` with `Insert` constant only.

**Schema** — `Infrastructure/Persistence/Sql/0005_add_ai_request_log.sql`:
```sql
CREATE TABLE IF NOT EXISTS ai_request_log (
    "Id"                        UUID             NOT NULL,
    "Timestamp"                 TIMESTAMPTZ      NOT NULL,
    "Worker"                    TEXT             NOT NULL DEFAULT '',
    "Provider"                  TEXT             NOT NULL DEFAULT '',
    "Operation"                 TEXT             NOT NULL DEFAULT '',
    "Model"                     TEXT             NOT NULL DEFAULT '',
    "InputTokens"               INTEGER          NOT NULL DEFAULT 0,
    "OutputTokens"              INTEGER          NOT NULL DEFAULT 0,
    "CacheCreationInputTokens"  INTEGER          NOT NULL DEFAULT 0,
    "CacheReadInputTokens"      INTEGER          NOT NULL DEFAULT 0,
    "TotalTokens"               INTEGER          NOT NULL DEFAULT 0,
    "CostUsd"                   NUMERIC(18, 8)   NOT NULL DEFAULT 0,
    "LatencyMs"                 INTEGER          NOT NULL DEFAULT 0,
    "Status"                    TEXT             NOT NULL DEFAULT 'Success',
    "ErrorMessage"              TEXT             NULL,
    "CorrelationId"             UUID             NOT NULL,
    "ArticleId"                 UUID             NULL,
    CONSTRAINT "PK_ai_request_log" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_ai_request_log_Timestamp"     ON ai_request_log ("Timestamp");
CREATE INDEX IF NOT EXISTS "IX_ai_request_log_Provider"      ON ai_request_log ("Provider");
CREATE INDEX IF NOT EXISTS "IX_ai_request_log_Worker"        ON ai_request_log ("Worker");
CREATE INDEX IF NOT EXISTS "IX_ai_request_log_Model"         ON ai_request_log ("Model");
CREATE INDEX IF NOT EXISTS "IX_ai_request_log_ArticleId"     ON ai_request_log ("ArticleId");
CREATE INDEX IF NOT EXISTS "IX_ai_request_log_CorrelationId" ON ai_request_log ("CorrelationId");
```

Column-name / type rationale:
- PascalCase quoted, snake_case table name — matches every other table.
- `NUMERIC(18, 8)` for `CostUsd`: 10 integer digits and 8 fractional digits cover even the smallest per-call cost fractions (sub-cent) without losing precision. `DOUBLE PRECISION` is wrong for money.
- `ArticleId` is nullable because `IContentGenerator`, `IEventTitleGenerator`, `IEventSummaryUpdater` operate on events, not articles. No FK is declared — `ai_request_log` is an append-only audit trail that must survive `articles` deletes. If an article is removed for any reason the log row stays.
- `ErrorMessage` is nullable and truncated to 500 chars by the logger (long Claude stack traces otherwise bloat the table).
- Indexes cover the expected query axes ("cost by day", "cost by provider", "cost by worker", "cost by model", "all calls for one article", "all calls for one cycle"). No composite indexes in the first migration — add later if a specific query dominates.
- `Status` stored as string per enum convention.
- `"TotalTokens"` is denormalised for fast `SUM()`ing; the logger computes it on insert.

### D3. Wiring in AI clients (ten files)

Each AI client follows this uniform pattern after the provider call:

```csharp
// Anthropic example — after the SDK call
var sw = Stopwatch.StartNew();
MessageResponse? response = null;
Exception? failure = null;
try
{
    response = await client.Messages.GetClaudeMessageAsync(request, cancellationToken);
}
catch (OperationCanceledException) { throw; }
catch (Exception ex) { failure = ex; }
sw.Stop();

var usage = response is null
    ? new AiUsage(0, 0, 0, 0)
    : new AiUsage(
        response.Usage.InputTokens,
        response.Usage.OutputTokens,
        response.Usage.CacheCreationInputTokens,
        response.Usage.CacheReadInputTokens);

await aiRequestLogger.LogAsync(new AiRequestLogEntry(
    Provider: "Anthropic",
    Operation: nameof(AnalyzeAsync),
    Model: _model,
    Usage: usage,
    LatencyMs: (int)sw.ElapsedMilliseconds,
    Status: failure is null ? AiRequestStatus.Success : AiRequestStatus.Error,
    ErrorMessage: failure?.Message is { } m ? m[..Math.Min(m.Length, 500)] : null,
    CorrelationId: AiCallContext.CurrentCorrelationId,
    ArticleId: AiCallContext.CurrentArticleId,
    Worker: AiCallContext.CurrentWorker),
    cancellationToken);

if (failure is not null) throw failure;

// ... continue to parse response as today ...
```

For Gemini, parse `usageMetadata` from the response JSON (`promptTokenCount`, `candidatesTokenCount`; `cache_*` are always 0) and feed the same `AiUsage` shape.

**Correlation / Article ID / Worker discovery:**

The AI clients don't know which worker called them or which article is being processed. Three options considered:

1. Add parameters to every `IArticleAnalyzer.AnalyzeAsync` / `IEventClassifier.ClassifyAsync` / etc. — **rejected**, cascades through Core interfaces and ripples everywhere.
2. Use `System.Diagnostics.Activity` and its tags — possible but requires enabling an `ActivitySource` pipeline we don't have.
3. **Accepted — `AsyncLocal<T>` ambient context.** A new `Infrastructure/AI/Telemetry/AiCallContext.cs` exposes `CurrentCorrelationId`, `CurrentArticleId`, `CurrentWorker` via `AsyncLocal<Guid?>` / `AsyncLocal<string?>`. Workers already create their cycle scope (per ADR 0016); we extend those `using var` blocks with a parallel `using var _ = AiCallContext.Push(correlationId, articleId, workerName)` that sets the async-local values and pops them on dispose.

`AsyncLocal` matches the grain we already use for logger scopes (`ILogger.BeginScope`), flows correctly through `await`, and requires **zero** signature changes to Core interfaces. Two workers (`ArticleAnalysisWorker`, `PublicationGenerationWorker`) call `AiCallContext.Push` around each item; the API middleware does the same around each request (the API currently makes no AI calls, but a future `re-generate` endpoint would).

**DI changes in `InfrastructureServiceExtensions`:**
- Register `services.AddScoped<IAiRequestLogRepository, AiRequestLogRepository>();`
- Register `services.AddScoped<IAiCostCalculator, AiCostCalculator>();`
- Register `services.AddScoped<IAiRequestLogger, AiRequestLogger>();`
- Every AI client factory lambda in `AddAiServices` now additionally resolves `sp.GetRequiredService<IAiRequestLogger>()` and passes it to the constructor. Every AI client constructor gains an `IAiRequestLogger` parameter.

### D4. Worker changes (two files)

Only two workers call AI services:
- `Worker/Workers/ArticleAnalysisWorker.cs` — push `AiCallContext` with `{ CorrelationId = cycleId, ArticleId = article.Id, Worker = nameof(ArticleAnalysisWorker) }` around each `ProcessArticleAsync` call. Add one `using var _ = AiCallContext.Push(...)` statement at the top of the per-item `try`; no other logic changes.
- `Worker/Workers/PublicationGenerationWorker.cs` — push `AiCallContext` with `{ CorrelationId = cycleId, ArticleId = null, Worker = nameof(PublicationGenerationWorker) }` around each publication-generation call.

No existing logic in these workers changes. No retry behaviour changes. No batch sizes change.

### D5. appsettings.json

In both `Api/appsettings.json` and `Worker/appsettings.json`, add:
```json
"ModelPricing": {
  "AnthropicCacheWriteMultiplier": 1.25,
  "AnthropicCacheReadMultiplier": 0.1,
  "Anthropic": {
    "claude-haiku-4-5-20251001": { "InputPerMillion": 1.00, "OutputPerMillion": 5.00 },
    "claude-sonnet-4-5":         { "InputPerMillion": 3.00, "OutputPerMillion": 15.00 }
  },
  "Gemini": {
    "gemini-2.0-flash":      { "InputPerMillion": 0.10, "OutputPerMillion": 0.40 },
    "gemini-embedding-001":  { "InputPerMillion": 0.15, "OutputPerMillion": 0.00 }
  }
}
```

Real pricing numbers are set by the implementer at the time of the feature; the numbers above are indicative. Embedding models have no output tokens, so `OutputPerMillion` is `0`.

### D6. Testing strategy

**Unit tests** (`Tests/Infrastructure.Tests/AI/Telemetry/AiCostCalculatorTests.cs`):
- Gemini case: `Calculate({input=1_000_000, output=500_000, cacheCreation=0, cacheRead=0}, "Gemini", "gemini-2.0-flash")` with price 0.10/0.40 → `0.30m`.
- Anthropic no-cache case.
- Anthropic cache-write case: `cache_creation × 1.25 × inputPrice`.
- Anthropic cache-read case: `cache_read × 0.1 × inputPrice`.
- Anthropic mixed case: all four fields populated, verify the formula sums correctly.
- Unknown model → returns `0m`, does not throw.
- Unknown provider → returns `0m`, does not throw.
- Zero-token edge case: all-zero usage returns `0m`.
- Boundary: one-token input returns a value with the expected precision (≥ 8 decimal places).

**Repository tests** (`Tests/Infrastructure.Tests/Repositories/AiRequestLogRepositoryContractTests.cs`):
- Mock-based contract tests on `IAiRequestLogRepository` (mirrors `PublicationRepositoryInterfaceContractTests`) covering the `AddAsync(entry, ct)` signature, cancellation propagation, and rejection of null input.
- Live-DB integration test is out of scope — the other repositories with FOR UPDATE SKIP LOCKED do not have live-DB tests either.

**Logger tests** (`Tests/Infrastructure.Tests/AI/Telemetry/AiRequestLoggerTests.cs`):
- When the repository succeeds, `LogAsync` completes without throwing.
- When the repository throws, `LogAsync` logs a warning and does **not** rethrow (the "never break business flow" contract).
- `OperationCanceledException` from the repository is rethrown (cancellation must propagate).
- `CostUsd` equals the calculator's return value.
- `ErrorMessage` is truncated to 500 characters when the entry carries a longer message.

**Context tests** (`Tests/Infrastructure.Tests/AI/Telemetry/AiCallContextTests.cs`):
- `Push` sets all three ambient values; `Dispose` restores the previous (null) values.
- Nested `Push`es stack correctly (inner values visible inside, outer values restored on dispose).
- Async flow: the ambient values cross `await` boundaries.

**No AI-client tests added for logging.** The existing AI-client tests (`HaikuKeyFactsExtractorTests`, `ClaudeContradictionDetectorTests`, …) mock the SDK poorly (most test parse logic via reflection) — adding logger-invocation assertions there would widen the surface without adding value. The telemetry layer itself is thoroughly tested in isolation.

---

## Consequences

**Positive:**
- Every AI call is persisted with real USD cost, token breakdown (including Anthropic cache write/read), worker, model, article id, and correlation id.
- Operators can answer every cost-optimisation question the feature was commissioned to answer (cost by provider, by worker, by model, by article, by cycle, Anthropic cache effectiveness).
- The cost formula and error-isolation policy each live in **one** file, not scattered across ten AI clients.
- Adding a new AI client in the future costs exactly "3 lines at the end of the method" plus `IAiRequestLogger` in the constructor.
- Pricing changes (Anthropic quarterly adjustments, Gemini new tier) are a config-only change — no redeploy needed if `IOptionsMonitor` is used (optional; a redeploy is also acceptable).

**Negative / risks:**
- Writes to `ai_request_log` add ~5 ms per AI call (one INSERT). Given current AI-call latencies (100ms–several seconds) this is invisible, but the negative result is a strictly larger DB write volume. `ai_request_log` will grow rapidly — a future ADR may add a retention / archival policy (out of scope here).
- If the `ModelPricing` section is missing an entry for a newly adopted model, cost will silently record as `0`. Mitigation: the calculator emits a `LogWarning` when it falls back to `0`, which operators will notice.
- `AsyncLocal` has a small overhead per set/unset and should not be used in hot loops; the current call patterns (one push per worker item, one push per HTTP request) are far from that regime.
- The log write failing (e.g. DB down) will produce one `LogWarning` per failed call. A flood of these would be noisy; the logger can add short-term de-duplication in a future change if this becomes a problem in practice.

**Files affected:**

- **New files:**
  - `Core/DomainModels/AiRequestLog.cs` (with `AiRequestStatus` enum co-located)
  - `Core/DomainModels/AI/AiUsage.cs`
  - `Core/DomainModels/AI/AiRequestLogEntry.cs`
  - `Core/Interfaces/AI/IAiCostCalculator.cs`
  - `Core/Interfaces/AI/IAiRequestLogger.cs`
  - `Core/Interfaces/Repositories/IAiRequestLogRepository.cs`
  - `Infrastructure/Configuration/ModelPricingOptions.cs` (contains `ModelPrice` co-located)
  - `Infrastructure/AI/Telemetry/AiCostCalculator.cs`
  - `Infrastructure/AI/Telemetry/AiRequestLogger.cs`
  - `Infrastructure/AI/Telemetry/AiCallContext.cs`
  - `Infrastructure/Persistence/Entity/AiRequestLogEntity.cs`
  - `Infrastructure/Persistence/Mappers/AiRequestLogMapper.cs`
  - `Infrastructure/Persistence/Repositories/AiRequestLogRepository.cs`
  - `Infrastructure/Persistence/Repositories/Sql/AiRequestLogSql.cs`
  - `Infrastructure/Persistence/Sql/0005_add_ai_request_log.sql`
  - `Tests/Infrastructure.Tests/AI/Telemetry/AiCostCalculatorTests.cs`
  - `Tests/Infrastructure.Tests/AI/Telemetry/AiRequestLoggerTests.cs`
  - `Tests/Infrastructure.Tests/AI/Telemetry/AiCallContextTests.cs`
  - `Tests/Infrastructure.Tests/Repositories/AiRequestLogRepositoryContractTests.cs`

- **Modified files:**
  - `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` — register the new options / services / repository; update the ten AI-client factory lambdas to pass `IAiRequestLogger`.
  - All ten AI-client files in `Infrastructure/AI/`: `ClaudeArticleAnalyzer.cs`, `ClaudeEventClassifier.cs`, `ClaudeEventSummaryUpdater.cs`, `ClaudeContentGenerator.cs`, `ClaudeContradictionDetector.cs`, `HaikuKeyFactsExtractor.cs`, `HaikuEventTitleGenerator.cs`, `GeminiArticleAnalyzer.cs`, `GeminiEmbeddingService.cs` — constructor parameter added, telemetry block added after each provider call.
  - `Worker/Workers/ArticleAnalysisWorker.cs` — add `AiCallContext.Push` inside the per-article `try`.
  - `Worker/Workers/PublicationGenerationWorker.cs` — add `AiCallContext.Push` around the AI call.
  - `Api/appsettings.json`, `Worker/appsettings.json` — add `ModelPricing` section.
  - `Api/appsettings.Development.json`, `Worker/appsettings.Development.json` — optionally override pricing for local dev.

- **Untouched:** every file under `Core/DomainModels/` other than the new ones; every existing repository; every existing mapper; every controller; every DTO; every other Options class; every validator; every parser; every publisher.

---

## Implementation Notes

**For `feature-planner` — skills to follow during implementation:**

- `.claude/skills/code-conventions/SKILL.md` — layer placement (interfaces in Core, implementations in Infrastructure, workers only modify scope context — no business logic change), primary-constructor syntax for the new services, Options pattern with `SectionName`, enums stored as strings.
- `.claude/skills/dapper-conventions/SKILL.md` — `internal` repository, primary constructor with `IDbConnectionFactory` + `IUnitOfWork`, SQL constant in `*Sql.cs`, `CommandDefinition` with `cancellationToken`, PascalCase quoted columns, snake_case table name, `DateTimeOffset.UtcNow` for timestamps, **no logging inside the repository** (policy from ADR 0016 D4).
- `.claude/skills/mappers/SKILL.md` — `Infrastructure/Persistence/Mappers/AiRequestLogMapper.cs` follows the `Entity ↔ Domain` extension-method pattern; `ToDomain` parses the enum with `Enum.Parse<AiRequestStatus>`, `ToEntity` writes `.ToString()`.
- `.claude/skills/clean-code/SKILL.md` — no magic numbers (cache multipliers come from `ModelPricingOptions`, not literals in `AiCostCalculator`), method length ≤ 20 lines (the per-client telemetry block must stay short — if it grows, extract into a helper on the telemetry layer, not inside the client), no inline DTO construction in workers.
- `.claude/skills/testing/SKILL.md` — AAA pattern, NUnit + Moq + FluentAssertions, parameterized `[TestCase]` for the four cache cases in the calculator, no live DB access for the unit tests.
- ADR 0016 (Structured Logging Conventions) — the existing `LogDebug` lines around every AI call stay; the telemetry logger's `LogWarning` on failure uses the same naming conventions (`{Provider} {Model} {Operation}`).

**Order of work (each step leaves the build green):**

1. **Schema first.** Add `0005_add_ai_request_log.sql` to `Infrastructure/Persistence/Sql/` as embedded resource. Verify `DbUpMigrator.Migrate` applies it on startup.
2. **Core layer.** Add `AiRequestLog`, `AiRequestStatus`, `AiUsage`, `AiRequestLogEntry`, `IAiCostCalculator`, `IAiRequestLogger`, `IAiRequestLogRepository`. Nothing consumes them yet — project still builds.
3. **Infrastructure persistence.** Add `AiRequestLogEntity`, `AiRequestLogMapper`, `AiRequestLogSql`, `AiRequestLogRepository`. Register in `InfrastructureServiceExtensions.AddRepositories`.
4. **Telemetry layer.** Add `ModelPricingOptions`, `AiCostCalculator`, `AiRequestLogger`, `AiCallContext`. Register the options and the scoped services in `InfrastructureServiceExtensions`.
5. **Calculator + logger tests.** Write the unit tests from D6 — these can run now even though no AI client uses the logger yet.
6. **AI-client integration.** Add `IAiRequestLogger` to every AI-client constructor; insert the telemetry block after each provider call. Update the factory lambdas in `AddAiServices`. Deleting the existing `Stopwatch` blocks is **not** required — they feed the ADR 0016 Debug lines; both can coexist, or the implementer can consolidate them (out of scope for this ADR to mandate either way, but simpler to keep both).
7. **Worker integration.** Add `AiCallContext.Push` to `ArticleAnalysisWorker.ProcessArticleAsync` and `PublicationGenerationWorker`'s equivalent per-item method.
8. **appsettings.** Add the `ModelPricing` section to `Api/appsettings.json` and `Worker/appsettings.json`.
9. **Smoke test.** Start the Worker against a dev database; confirm rows appear in `ai_request_log` after one article-analysis cycle with plausible `CostUsd` values.

**Out of scope (do not expand):**
- No UI / API read endpoints for the log. "That's a later task" per the user.
- No aggregation queries (`SUM(CostUsd) GROUP BY ...`) — when they are added they belong in a new method on `IAiRequestLogRepository`, covered by a future ADR.
- No retention / archival / partitioning of `ai_request_log`.
- No `IOptionsMonitor` hot-reload of pricing unless trivially achievable — `IOptions<T>` with redeploy is acceptable for the first version.
- No fire-and-forget batched writer (explicitly rejected in Options analysis above).
- No distributed tracing / `ActivitySource` / OpenTelemetry.
- No changes to existing AI interfaces in `Core/Interfaces/AI/` — they keep their current signatures.
