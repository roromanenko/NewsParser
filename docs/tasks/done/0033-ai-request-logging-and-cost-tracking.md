# AI Request Logging and Cost Tracking

## Goal
Persist every Anthropic and Gemini API call to `ai_request_log` with real USD cost
calculated from token counts × model pricing, so operators can optimise AI spending
without breaking the business flow if the log write fails.

## Affected Layers
- Core / Infrastructure / Worker

## ADR
`docs/architecture/decisions/2026-04-24-ai-request-logging-and-cost-tracking.md`
(Option 3 — thin telemetry helper, synchronous log-and-swallow)

---

## Tasks

### Phase 1 — Schema

- [x] **Create `Infrastructure/Persistence/Sql/0005_add_ai_request_log.sql`** — DbUp forward-only
      migration that creates the `ai_request_log` table (UUID PK `"Id"`, `TIMESTAMPTZ "Timestamp"`,
      TEXT columns for `"Worker"`, `"Provider"`, `"Operation"`, `"Model"`, `"Status"`,
      nullable TEXT `"ErrorMessage"`, four INTEGER token columns, `NUMERIC(18,8) "CostUsd"`,
      INTEGER `"LatencyMs"`, UUID `"CorrelationId"`, nullable UUID `"ArticleId"`) and six indexes
      (`"Timestamp"`, `"Provider"`, `"Worker"`, `"Model"`, `"ArticleId"`, `"CorrelationId"`).
      Exact DDL is in ADR section D2.
      _Acceptance: file is an embedded resource; `DbUpMigrator.Migrate()` applies it at startup
      without error; `\d ai_request_log` in psql shows all columns and indexes_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_ (DbUp conventions)

---

### Phase 2 — Core domain models and interfaces

- [x] **Create `Core/DomainModels/AiRequestLog.cs`** — domain model with all properties listed
      in ADR D1 plus co-located `AiRequestStatus` enum (`Success`, `Error`).
      `CostUsd` is `decimal`; timestamps are `DateTimeOffset`; enums are plain C# enums.
      _Acceptance: file compiles; no Infrastructure or EF references; `AiRequestStatus` is in
      the same file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/DomainModels/AI/AiUsage.cs`** — positional record
      `(int InputTokens, int OutputTokens, int CacheCreationInputTokens, int CacheReadInputTokens)`.
      _Acceptance: file compiles; record is `public`; no dependencies beyond `System`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/DomainModels/AI/AiRequestLogEntry.cs`** — positional record carrying
      `Provider`, `Operation`, `Model`, `Usage` (`AiUsage`), `LatencyMs`, `Status`
      (`AiRequestStatus`), `ErrorMessage?`, `CorrelationId`, `ArticleId?`, `Worker`.
      _Acceptance: file compiles; uses the two models created above; no infrastructure references_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/Interfaces/AI/IAiCostCalculator.cs`** — single method
      `decimal Calculate(AiUsage usage, string provider, string model)`.
      _Acceptance: interface only; no implementation; compiles in Core project_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/Interfaces/AI/IAiRequestLogger.cs`** — single method
      `Task LogAsync(AiRequestLogEntry entry, CancellationToken cancellationToken = default)`.
      _Acceptance: interface only; no implementation; compiles in Core project_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Core/Interfaces/Repositories/IAiRequestLogRepository.cs`** — single method
      `Task AddAsync(AiRequestLog entry, CancellationToken cancellationToken = default)`.
      _Acceptance: interface only; no implementation; compiles in Core project_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 3 — Infrastructure persistence

- [x] **Create `Infrastructure/Persistence/Entity/AiRequestLogEntity.cs`** — flat class with
      PascalCase `{ get; init; }` or `{ get; set; }` properties matching every column in the
      SQL schema (all token ints, `decimal CostUsd`, string `Status`, nullable string
      `ErrorMessage`, `DateTimeOffset Timestamp`, etc.).
      _Acceptance: file compiles; no domain or EF references; all column names reachable via
      Dapper's default property mapping_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Create `Infrastructure/Persistence/Mappers/AiRequestLogMapper.cs`** — static class with
      extension methods `ToEntity(this AiRequestLog)` and `ToDomain(this AiRequestLogEntity)`.
      `Status` stored/parsed as string via `Enum.Parse<AiRequestStatus>` /
      `.ToString()` per project convention.
      _Acceptance: both methods compile; `ToDomain(ToEntity(log))` is a round-trip for all fields_
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Create `Infrastructure/Persistence/Repositories/Sql/AiRequestLogSql.cs`** — `internal static`
      class with a single `const string Insert` that inserts all columns into `ai_request_log`.
      Follow the pattern in `ArticleSql.cs`: named `@Param` placeholders, PascalCase quoted
      column names, snake_case table name.
      _Acceptance: SQL string compiles in the project; constant is `internal`_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Create `Infrastructure/Persistence/Repositories/AiRequestLogRepository.cs`** — `internal`
      class, primary constructor `(IDbConnectionFactory factory, IUnitOfWork uow)`, implements
      `IAiRequestLogRepository`. `AddAsync` opens a connection via `factory.CreateOpenAsync`,
      maps the domain to entity with `ToEntity()`, calls `conn.ExecuteAsync(new CommandDefinition(
      AiRequestLogSql.Insert, entity, cancellationToken: cancellationToken))`.
      `uow` is present for convention parity with all other repositories but no transaction is
      opened in this method (append-only write). No logging inside the repository (ADR 0016 policy).
      `AddAsync` must throw `ArgumentNullException` when `entry` is `null`.
      _Acceptance: satisfies `IAiRequestLogRepository`; no raw SQL strings in the class body;
      null `entry` throws `ArgumentNullException`; primary constructor includes both
      `IDbConnectionFactory` and `IUnitOfWork`_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — add
      `services.AddScoped<IAiRequestLogRepository, AiRequestLogRepository>()` inside
      the existing `AddRepositories` private method.
      _Acceptance: solution builds; `IAiRequestLogRepository` resolves from DI_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 4 — Telemetry layer

- [x] **Create `Infrastructure/Configuration/ModelPricingOptions.cs`** — `public` class with
      `const string SectionName = "ModelPricing"`, `Dictionary<string, ModelPrice> Anthropic`,
      `Dictionary<string, ModelPrice> Gemini`, `double AnthropicCacheWriteMultiplier = 1.25`,
      `double AnthropicCacheReadMultiplier = 0.1`; co-located `ModelPrice` class with
      `decimal InputPerMillion` and `decimal OutputPerMillion`. Follows the Options pattern
      used by `AiOptions`, `TelegramOptions`, etc. in the same folder.
      _Acceptance: class compiles; `SectionName` constant present; `ModelPrice` is in the same file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Infrastructure/AI/Telemetry/AiCallContext.cs`** — static class exposing
      `AsyncLocal`-backed `CurrentCorrelationId` (`Guid`), `CurrentArticleId` (`Guid?`), and
      `CurrentWorker` (`string`) properties, plus a `Push(Guid correlationId, Guid? articleId,
      string worker)` factory that returns an `IDisposable` restoring previous values on dispose.
      Nested pushes must stack correctly (save/restore, not clear).
      _Acceptance: `Push` sets all three values; `Dispose` restores prior values (verified by
      AiCallContextTests in Phase 6)_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Infrastructure/AI/Telemetry/AiCostCalculator.cs`** — `internal` class, primary
      constructor `(IOptions<ModelPricingOptions> options, ILogger<AiCostCalculator> logger)`,
      implements `IAiCostCalculator`.
      Implements the formula from ADR D2 exactly: Gemini uses straight input+output division;
      Anthropic adds cache-creation and cache-read terms using `AnthropicCacheWriteMultiplier`
      and `AnthropicCacheReadMultiplier` from options (no magic numbers in the class body).
      When the model is unknown for the given provider, emits
      `logger.LogWarning("Missing pricing for {Provider} {Model}", provider, model)` and
      returns `0m`. All arithmetic uses `decimal` (no `double` or `float`).
      _Acceptance: unit tests in Phase 5 pass; no division occurs when `ModelPricingOptions` has
      no entry for the model; `LogWarning` is emitted exactly once per unknown-model or
      unknown-provider call_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Create `Infrastructure/AI/Telemetry/AiRequestLogger.cs`** — `internal` class, primary
      constructor `(IAiCostCalculator calculator, IAiRequestLogRepository repository,
      ILogger<AiRequestLogger> logger)`, implements `IAiRequestLogger`. `LogAsync` calls
      `calculator.Calculate`, builds an `AiRequestLog` (sets `Id = Guid.NewGuid()`,
      `Timestamp = DateTimeOffset.UtcNow`, copies all `entry` fields, sets `TotalTokens` as
      sum, truncates `ErrorMessage` to 500 chars), calls `repository.AddAsync` inside a
      `try/catch` that swallows all exceptions except `OperationCanceledException` and emits a
      single `LogWarning` on failure.
      _Acceptance: logger unit tests in Phase 5 pass; `OperationCanceledException` propagates;
      all other exceptions are swallowed_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — add to
      `AddAiServices`: `services.Configure<ModelPricingOptions>(configuration.GetSection(
      ModelPricingOptions.SectionName))`, `services.AddScoped<IAiCostCalculator, AiCostCalculator>()`,
      `services.AddScoped<IAiRequestLogger, AiRequestLogger>()`.
      _Acceptance: solution builds; all three services resolve from DI; existing AI client
      registrations are not changed yet (that is Phase 6)_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 5 — Tests for telemetry layer

- [x] **Create `Tests/Infrastructure.Tests/AI/Telemetry/AiCostCalculatorTests.cs`** — NUnit
      + FluentAssertions unit tests. Required cases (use `[TestCase]` for the five formula
      variants): Gemini straight cost; Anthropic no-cache; Anthropic cache-write only;
      Anthropic cache-read only; Anthropic all four fields; unknown model returns `0m` without
      throwing; unknown provider returns `0m` without throwing; all-zero usage returns `0m`;
      one-token input has ≥ 8 decimal-place precision. Mock `IOptions<ModelPricingOptions>` with
      a fixed pricing dict. For the unknown-model and unknown-provider cases, assert that
      `LogWarning` was called on `Mock<ILogger<AiCostCalculator>>` using `Verify` on
      `LogWarning` with the matching provider and model arguments.
      _Acceptance: all test cases pass with `dotnet test`; no live DB or HTTP calls;
      `Mock<ILogger<AiCostCalculator>>.Verify` confirms the warning on unknown-model and
      unknown-provider paths_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Create `Tests/Infrastructure.Tests/AI/Telemetry/AiRequestLoggerTests.cs`** — NUnit unit
      tests: (1) repository succeeds → `LogAsync` completes without throwing; (2) repository
      throws → `LogAsync` logs a warning and does not rethrow; (3) repository throws
      `OperationCanceledException` → `LogAsync` rethrows; (4) `CostUsd` on the persisted row
      equals the calculator's return value; (5) `ErrorMessage` longer than 500 chars is
      truncated to 500. Use `Mock<IAiCostCalculator>`, `Mock<IAiRequestLogRepository>`,
      `Mock<ILogger<AiRequestLogger>>`.
      _Acceptance: all five tests pass; repository mock verifies `AddAsync` was called once on
      the success path_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Create `Tests/Infrastructure.Tests/AI/Telemetry/AiCallContextTests.cs`** — NUnit unit
      tests: (1) `Push` sets all three ambient values; (2) `Dispose` restores prior null/empty
      values; (3) nested `Push` — inner values visible inside, outer values restored on inner
      dispose; (4) ambient values survive an `await Task.Yield()` crossing (async flow). No
      mocks needed — tests the static `AiCallContext` class directly.
      _Acceptance: all four tests pass_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [x] **Create `Tests/Infrastructure.Tests/Repositories/AiRequestLogRepositoryContractTests.cs`**
      — mock-based contract tests on `IAiRequestLogRepository` following the pattern in
      `PublicationRepositoryInterfaceContractTests.cs`. Required cases: (1) `AddAsync` with
      a valid `AiRequestLog` completes without throwing; (2) cancellation token is forwarded
      (verify mock was called with a specific `CancellationToken`); (3) calling `AddAsync` twice
      records two distinct invocations; (4) `AddAsync(null!, CancellationToken.None)` throws
      `ArgumentNullException`. No live DB.
      _Acceptance: all four tests pass; no EF or Npgsql references in the test file_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 6 — Wire IAiRequestLogger into each AI client

- [x] **Modify `Infrastructure/AI/ClaudeArticleAnalyzer.cs`** — add `IAiRequestLogger _aiRequestLogger`
      field and constructor parameter. Wrap the `GetClaudeMessageAsync` call in the ADR D3 pattern:
      `Stopwatch` before, `try/catch` around the SDK call (re-throw `OperationCanceledException`,
      capture other exceptions in `failure`), `Stopwatch.Stop()`, build `AiUsage` from
      `response.Usage` (zeros if response is null), call `_aiRequestLogger.LogAsync(new
      AiRequestLogEntry(...))` with `Provider="Anthropic"`, `Operation=nameof(AnalyzeAsync)`,
      then rethrow `failure` if present. The existing `LogDebug` lines stay.
      _Acceptance: telemetry call compiles; existing `ParseAnalysisResult` logic is unchanged;
      `OperationCanceledException` still propagates_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/ClaudeEventClassifier.cs`** — same pattern as
      `ClaudeArticleAnalyzer`. Add `IAiRequestLogger` parameter; wrap `GetClaudeMessageAsync`;
      build `AiUsage` from `response.Usage`; call `LogAsync` with `Operation=nameof(ClassifyAsync)`.
      _Acceptance: same criteria as above; existing parse logic unchanged_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/ClaudeEventSummaryUpdater.cs`** — same pattern. Add
      `IAiRequestLogger` parameter; wrap `GetClaudeMessageAsync`; call `LogAsync` with
      `Operation=nameof(UpdateSummaryAsync)`.
      _Acceptance: same criteria; existing `ParseResult` logic unchanged_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/ClaudeContentGenerator.cs`** — same pattern. Note: this class
      already uses primary-constructor syntax. Add `IAiRequestLogger aiRequestLogger` to the
      primary constructor; wrap `GetClaudeMessageAsync`; call `LogAsync` with
      `Operation=nameof(GenerateForPlatformAsync)`.
      _Acceptance: primary-constructor syntax preserved; no regression in existing
      `ParseContent` logic_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/ClaudeContradictionDetector.cs`** — same pattern. `_client`
      is already a field (not local). Add `IAiRequestLogger` constructor parameter; wrap the
      `GetClaudeMessageAsync` call; call `LogAsync` with `Operation=nameof(DetectAsync)`.
      _Acceptance: tool-use response parsing (`ToolUseContent`) is unchanged; telemetry fires
      before `ParseResult`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/HaikuKeyFactsExtractor.cs`** — same pattern. Note: this class
      already has an outer `try/catch (ex is not OperationCanceledException)` that returns `[]`.
      The telemetry block must capture `failure` and call `LogAsync` before the outer catch
      returns. After `LogAsync`, re-throw into the outer catch so it still swallows the error
      and returns `[]`. `Operation=nameof(ExtractAsync)`.
      _Acceptance: the outer swallow-and-return-empty behaviour is preserved; the log is still
      written on failure_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/HaikuEventTitleGenerator.cs`** — same pattern. This class also
      has an outer `try/catch (ex is not OperationCanceledException)`. Same treatment as
      `HaikuKeyFactsExtractor`. `Operation=nameof(GenerateTitleAsync)`.
      _Acceptance: outer catch still swallows and returns empty string; log fires on success
      and failure paths_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/GeminiArticleAnalyzer.cs`** — same pattern but for Gemini.
      Wrap `_httpClient.PostAsync` + `EnsureSuccessStatusCode` in a try/catch. After the HTTP
      call succeeds, parse `usageMetadata` from the response JSON (`promptTokenCount` →
      `InputTokens`, `candidatesTokenCount` → `OutputTokens`; cache tokens are always 0 for
      Gemini). Build `AiUsage` and call `LogAsync` with `Provider="Gemini"`,
      `Operation=nameof(AnalyzeAsync)`.
      _Acceptance: existing `candidates[0].content.parts[0].text` extraction unchanged; usage
      metadata is parsed from the same `JsonDocument`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/GeminiEmbeddingService.cs`** — same Gemini pattern. Wrap
      `_httpClient.PostAsync`; parse `usageMetadata` from the response JSON; call `LogAsync`
      with `Provider="Gemini"`, `Operation=nameof(GenerateEmbeddingAsync)`. Note: embedding
      responses may not always include `usageMetadata` — use `TryGetProperty` and fall back to
      zeros.
      _Acceptance: existing `embedding.values` extraction unchanged; zeros are used when
      `usageMetadata` is absent_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — update all
      nine factory lambdas in `AddAiServices` (one per AI client registered) to resolve
      `sp.GetRequiredService<IAiRequestLogger>()` and pass it as the last constructor argument.
      Each lambda adds exactly one `sp.GetRequiredService<IAiRequestLogger>()` line.
      _Acceptance: solution builds; all nine AI interfaces resolve from DI; `ai_request_log`
      rows are written after each AI call (Worker/ArticleId/CorrelationId columns will be
      populated once Phase 7 lands)_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 7 — Worker context push

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — inside `ProcessArticleAsync`, at
      the top of the `try` block (before the `UpdateStatusAsync` call), add:
      `using var _ = AiCallContext.Push(cycleId, article.Id, nameof(ArticleAnalysisWorker));`
      where `cycleId` is the `Guid` already captured in `ProcessAsync`'s `BeginScope` dict.
      The `cycleId` must be extracted from the scope or regenerated consistently — extract it
      to a local variable in `ProcessAsync` before `BeginScope` so it is available to pass down.
      No other logic changes.
      _Acceptance: `ArticleId` and `Worker` columns are populated in `ai_request_log` rows
      produced by this worker; `CorrelationId` matches the cycle GUID in logs_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/Workers/PublicationGenerationWorker.cs`** — inside
      `GenerateContentForPublicationAsync`, at the top of the `try` block, add:
      `using var _ = AiCallContext.Push(cycleId, null, nameof(PublicationGenerationWorker));`
      where `cycleId` is extracted as a local variable in `ProcessBatchAsync` before `BeginScope`
      (same pattern as `ArticleAnalysisWorker`). `ArticleId` is null for publication-generation
      calls. No other logic changes.
      _Acceptance: `Worker` column is `"PublicationGenerationWorker"` and `ArticleId` is NULL
      in rows from this worker; running the Worker against a dev DB produces rows in
      `ai_request_log` with populated `Worker`, `ArticleId`, and `CorrelationId` columns after
      one complete article-analysis cycle_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 8 — Configuration

- [x] **Modify `Worker/appsettings.json`** — add `"ModelPricing"` section at the root level with
      `AnthropicCacheWriteMultiplier`, `AnthropicCacheReadMultiplier`, and entries under
      `"Anthropic"` and `"Gemini"` dictionaries keyed by full model ID as specified in ADR D5.
      Use the indicative pricing values from the ADR; the implementer should update to current
      published prices at implementation time.
      _Acceptance: `IOptions<ModelPricingOptions>` resolves non-empty dictionaries at Worker
      startup; no `InvalidOperationException` on bind_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Api/appsettings.json`** — add the same `"ModelPricing"` section. (The API does
      not currently make AI calls, but `InfrastructureServiceExtensions` is shared and the
      options class must bind successfully in both hosts.)
      _Acceptance: Api project starts without bind errors; `ModelPricingOptions` sections
      deserialise correctly_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

## Open Questions

_None. All design decisions are resolved in the ADR._
