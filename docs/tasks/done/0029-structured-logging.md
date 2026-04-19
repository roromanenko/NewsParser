# Structured Logging Conventions Across the NewsParser Solution

## Goal
Introduce a consistent, production-ready structured logging convention across every layer of the
solution using only `Microsoft.Extensions.Logging` — no external sinks — fixing two latent bugs
along the way and making every worker cycle, HTTP request, and AI call fully observable.

## Affected Layers
- Infrastructure / Api / Worker

---

## Tasks

### Step A — Logging skill (reference document)

- [x] **Create `.claude/skills/logging-conventions/SKILL.md`** — document the full log-level
      matrix (D1), message-template conventions (D2), and `BeginScope` correlation patterns (D3)
      from the ADR, so every subsequent task and future contributor has one binding reference.
      Sections required: Level Matrix table, Naming Rules for placeholders, Exception logging
      rule, PII/secrets policy, Worker scope pattern (cycle + item), API scope pattern.
      _Acceptance: file exists; references `_logger.BeginScope` and `Dictionary<string, object>`
      patterns verbatim from ADR D3; contains the forbidden-patterns list from D2
      (no `$""` interpolation, no `string.Format`, no `ex.Message` in template); no code changes
      to any `.cs` file in this step._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step B — Configuration

- [x] **Modify `Api/appsettings.json`** — replace the existing `Logging` block with the
      namespace-level overrides and console scope config from ADR D5:
      `Default=Information`, `Microsoft.AspNetCore=Warning`,
      `Microsoft.AspNetCore.Hosting.Diagnostics=Information`, `Api=Information`,
      `Infrastructure=Information`; add `Console.IncludeScopes=true`,
      `Console.FormatterName=simple`.
      _Acceptance: JSON is valid; existing non-Logging keys (`AllowedHosts`) are untouched;
      `dotnet build` passes._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Api/appsettings.Development.json`** — add the `Logging` section from ADR D5:
      `Default=Information`, `Microsoft.AspNetCore=Information`,
      `Infrastructure.AI=Debug`, `Api.Middleware.RequestLoggingMiddleware=Information`.
      All existing keys (`ConnectionStrings`, `Jwt`, `Ai`, `EventImportance`, `ArticleScraper`,
      `CloudflareR2`) must remain unchanged.
      _Acceptance: JSON is valid; existing keys are untouched; `dotnet build` passes._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/appsettings.json`** — replace the existing `Logging` block with the
      namespace-level overrides from ADR D5: `Default=Information`,
      `Microsoft.Hosting.Lifetime=Information`, `Worker.Workers=Information`,
      `Infrastructure=Information`; add `Console.IncludeScopes=true`,
      `Console.FormatterName=simple`.
      _Acceptance: JSON is valid; `dotnet build` passes._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/appsettings.Development.json`** — add the `Logging` section from ADR D5:
      `Default=Information`, `Worker.Workers.SourceFetcherWorker=Debug`,
      `Infrastructure.AI=Debug`, `Infrastructure.Parsers=Debug`.
      All existing keys (`ConnectionStrings`, `RssFetcher`, `Ai`, `EventImportance`,
      `ArticleProcessing`, `PublishingWorker`, `Validation`, `Telegram`, `ArticleScraper`,
      `CloudflareR2`) must remain unchanged.
      _Acceptance: JSON is valid; existing keys are untouched; `dotnet build` passes._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step C — API middleware

- [x] **Create `Api/Middleware/RequestLoggingMiddleware.cs`** — a primary-constructor middleware
      that:
      1. Reads or generates a `X-Correlation-Id` (GUID string) from the incoming request header
         and writes it back in the response header.
      2. Opens a `_logger.BeginScope` with `{ CorrelationId, Method, Path, UserId? }` (UserId
         read from `context.User` if authenticated).
      3. After `next(context)`, logs one `Information` line:
         `"HTTP {Method} {Path} responded {StatusCode} in {DurationMs}ms"`.
      4. Skips the log line (but still propagates the correlation id scope) for paths starting
         with `/swagger`.
      5. Does not log request or response bodies.
      _Acceptance: file compiles; no `$""` interpolation; `UserId` is omitted from scope dict when
      the user is not authenticated; the middleware is not yet wired — that is the next task._
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Program.cs`** — register `RequestLoggingMiddleware` immediately after the
      existing `app.UseMiddleware<ExceptionMiddleware>()` line and before `app.UseHttpsRedirection()`.
      _Acceptance: application starts; `dotnet build` passes; middleware order is
      `ExceptionMiddleware → RequestLoggingMiddleware → UseHttpsRedirection → UseCors →
      UseAuthentication → UseAuthorization → MapControllers`._
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Middleware/ExceptionMiddleware.cs`** — split the single `catch` block so that
      exceptions that map to 4xx status codes (`KeyNotFoundException`, `InvalidOperationException`,
      `UnauthorizedAccessException`, `ArgumentException`) are logged at `Information` with the
      mapped status code, and only the `_ => InternalServerError` branch logs at `Error` with the
      exception object. Message template: `"Request {Method} {Path} mapped to {StatusCode}"` for
      the 4xx branch; existing `LogError` template `"Unhandled exception for {Method} {Path}"` for
      the 5xx branch.
      _Acceptance: `dotnet build` passes; a `KeyNotFoundException` thrown by a controller no longer
      produces an `Error` log line; only the 5xx branch passes the exception to `LogError`._
      _Skill: .claude/skills/api-conventions/SKILL.md_

---

### Step D — AI client logging + GeminiEmbeddingService bug fix

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — update every
      factory lambda inside `AddAiServices` that currently uses `_ =>` to use `sp =>` and
      additionally resolve `sp.GetRequiredService<ILogger<T>>()` passing it to the new
      constructor parameter. Affected registrations: `GeminiArticleAnalyzer`,
      `ClaudeEventSummaryUpdater`, `ClaudeContentGenerator`, `HaikuKeyFactsExtractor`,
      `ClaudeEventClassifier`, `ClaudeContradictionDetector`, `GeminiEmbeddingService`,
      `ClaudeArticleAnalyzer`. `HaikuEventTitleGenerator` already uses `sp =>` and already
      resolves its logger — no change needed.
      _Acceptance: every AI client lambda passes a logger; no `_ =>` lambdas remain in
      `AddAiServices`; `dotnet build` passes; the app starts without DI resolution errors.
      This task must be completed BEFORE any AI client constructor is changed so that the DI
      factory is ready to supply the logger the moment each new parameter is added._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/GeminiEmbeddingService.cs`** — (a) delete line 22
      (`Console.WriteLine($"[DEBUG] Embedding URL: {url}")`); (b) add `ILogger<GeminiEmbeddingService>`
      constructor parameter and field; (c) build a sanitized URL for logging by stripping the
      `?key=...` query parameter before any log call; (d) add `_logger.LogDebug` before the HTTP
      call: `"Calling {Provider} {Model} with {PromptChars} chars", "Gemini", _model,
      text.Length`; (e) add `_logger.LogDebug` after success:
      `"{Provider} {Model} succeeded in {DurationMs}ms", "Gemini", _model, sw.ElapsedMilliseconds`.
      Use `System.Diagnostics.Stopwatch` for timing.
      _Acceptance: `Console.WriteLine` is gone; the logged URL does not contain `?key=`;
      `ILogger<GeminiEmbeddingService>` is the only new constructor parameter;
      `dotnet build` passes._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/ClaudeArticleAnalyzer.cs`** — add `ILogger<ClaudeArticleAnalyzer>`
      constructor parameter; add `LogDebug` before and after the `GetClaudeMessageAsync` call
      using the standard pair: `"Calling {Provider} {Model} with {PromptChars} chars"` /
      `"{Provider} {Model} succeeded in {DurationMs}ms"` (`Provider="Anthropic"`). Wrap
      `ParseAnalysisResult` call in a `try/catch` that logs
      `LogWarning(ex, "{Provider} {Model} returned unparseable response", ...)` and rethrows.
      _Acceptance: class compiles; no string interpolation in log calls; existing `ParseAnalysisResult`
      throw paths are unchanged; `dotnet build` passes._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/GeminiArticleAnalyzer.cs`** — same pattern as
      `ClaudeArticleAnalyzer`: inject `ILogger<GeminiArticleAnalyzer>`, add Debug pair around
      the HTTP call, add Warning on parse failure.
      _Acceptance: identical to `ClaudeArticleAnalyzer` acceptance criteria; `dotnet build` passes._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/ClaudeEventClassifier.cs`** — inject
      `ILogger<ClaudeEventClassifier>`; add Debug pair around the network call; Warning on parse
      failure.
      _Acceptance: `dotnet build` passes; no string interpolation._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/ClaudeContradictionDetector.cs`** — inject
      `ILogger<ClaudeContradictionDetector>`; add Debug pair; Warning on parse failure.
      _Acceptance: `dotnet build` passes._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/ClaudeEventSummaryUpdater.cs`** — inject
      `ILogger<ClaudeEventSummaryUpdater>`; add Debug pair; Warning on parse failure.
      _Acceptance: `dotnet build` passes._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/HaikuKeyFactsExtractor.cs`** — inject
      `ILogger<HaikuKeyFactsExtractor>`; add Debug pair; Warning on parse failure.
      _Acceptance: `dotnet build` passes._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/ClaudeContentGenerator.cs`** — inject
      `ILogger<ClaudeContentGenerator>`; add Debug pair; Warning on parse failure.
      _Acceptance: `dotnet build` passes._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/HaikuEventTitleGenerator.cs`** — extend the existing logger
      usage to add the standard Debug pair around `GetClaudeMessageAsync` (before-call and
      after-success lines). The existing `catch` block that logs Warning and returns empty string
      already conforms; keep it unchanged.
      _Acceptance: two new `LogDebug` calls added; existing `LogWarning` catch is untouched;
      `dotnet build` passes._
      _Skill: .claude/skills/clean-code/SKILL.md_

---

### Step E — Infrastructure service logging

- [x] **Modify `Infrastructure/Services/SourceService.cs`** — add `ILogger<SourceService>` as a
      primary-constructor parameter; add `LogInformation` after the repository call in each
      state-changing method: `CreateAsync` ("Source {SourceId} created: {SourceName}"),
      `UpdateAsync` ("Source {SourceId} updated"), `DeleteAsync` ("Source {SourceId} deleted").
      No logging on read-only methods.
      _Acceptance: `dotnet build` passes; no string interpolation; reads are unchanged._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Services/UserService.cs`** — add `ILogger<UserService>` primary-
      constructor parameter; add `LogInformation` after each state-changing repository call:
      `CreateUserAsync` ("User {UserId} created with role {Role}"),
      `UpdateEditorAsync` ("User {UserId} updated"),
      `DeleteEditorAsync` ("User {UserId} deleted"). Do NOT log the password, password hash, or
      email in these calls.
      _Acceptance: `dotnet build` passes; no PII beyond UserId appears in log templates._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Services/JwtService.cs`** — add `ILogger<JwtService>` primary-
      constructor parameter; add one `LogInformation` at the end of `GenerateToken`:
      `"JWT issued for user {UserId} role {Role}", user.Id, user.Role`.
      _Acceptance: `dotnet build` passes; the JWT value itself is never logged._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Services/PublicationService.cs`** — add `ILogger<PublicationService>`
      primary-constructor parameter; add `LogInformation` after the repository call in each
      state-changing method, including `{EditorId}` and `{NewStatus}` placeholders on every
      state-transition call per ADR D4 (`{ PublicationId, EditorId, NewStatus }`):
      `CreateForEventAsync` ("Publication {PublicationId} created for event {EventId} by editor
      {EditorId}"), `UpdateContentAsync` ("Publication {PublicationId} content updated by editor
      {EditorId}"), `ApproveAsync` ("Publication {PublicationId} status set to {NewStatus} by
      editor {EditorId}"), `RejectAsync` ("Publication {PublicationId} status set to {NewStatus}
      by editor {EditorId}"), `SendAsync` ("Publication {PublicationId} status set to {NewStatus}
      by editor {EditorId}"). Read-only methods are not logged.
      _Acceptance: `dotnet build` passes; all five state-changing methods log at Information;
      every log call includes `{PublicationId}`, `{EditorId}`, and `{NewStatus}` placeholders;
      no log call omits any of the three required placeholders._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Services/PublishTargetService.cs`** — add
      `ILogger<PublishTargetService>` primary-constructor parameter; add `LogInformation` on
      `CreateAsync` ("PublishTarget {PublishTargetId} created: {Name}"), `UpdateAsync`
      ("PublishTarget {PublishTargetId} updated"), `DeleteAsync`
      ("PublishTarget {PublishTargetId} deleted").
      _Acceptance: `dotnet build` passes; read-only methods unchanged._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Services/EventService.cs`** — the existing `ILogger<EventService>`
      injection and Warning on AI-enrichment failure are already present. Add `LogInformation`
      at the end of the commit block in `MergeAsync` ("Event {TargetEventId} merged from
      {SourceEventId}") and at the end of `ReclassifyArticleAsync` ("Article {ArticleId}
      reclassified to event {EventId}"). Do not alter existing Warning or any other existing
      log call.
      _Acceptance: `dotnet build` passes; new lines use named placeholders; existing lines are
      unchanged._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Services/TelegramClientService.cs`** — promote the log call for
      the `LoginUserIfNeeded` startup failure from `LogError` to `LogCritical`. The message
      template and exception argument must remain unchanged; only the log level changes.
      _Acceptance: `dotnet build` passes; the `LoginUserIfNeeded` failure path calls `LogCritical`
      (not `LogError`); no other log calls in the file are modified._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Publishers/TelegramPublisher.cs`** — inject
      `ILogger<TelegramPublisher>` as a constructor parameter if not already present; add one
      `LogDebug` call immediately before the Telegram `sendMessage` request:
      `"Sending {MessageType} to {ChatId}", messageType, chatId` (use the actual variable names
      for message type and chat id as they appear in the method). No other log calls are added
      or modified.
      _Acceptance: `dotnet build` passes; `ILogger<TelegramPublisher>` is injected; the `LogDebug`
      line fires before every `sendMessage` call; no string interpolation in the log call._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step F — Parser logging

- [x] **Modify `Infrastructure/Parsers/HtmlArticleContentScraper.cs`** — add
      `ILogger<HtmlArticleContentScraper>` as a primary-constructor parameter (alongside
      existing `IHttpClientFactory` and `IOptions<ArticleScraperOptions>`); replace the two
      silent `catch` blocks (`TaskCanceledException` and `HttpRequestException`) with
      `LogDebug` calls: `"Scrape skipped for {Url}: {Reason}", url, ex.GetType().Name`;
      add `LogDebug` on the non-2xx branch: `"Scrape returned {StatusCode} for {Url}",
      (int)response.StatusCode, url`; add `LogWarning` when `ReadBodyUpToLimitAsync` returns
      null due to size limit: `"Scrape body exceeded size limit for {Url}", url`.
      _Acceptance: the two formerly-silent catches now produce Debug output; `dotnet build`
      passes; no string interpolation._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Parsers/TelegramParser.cs`** — add `ILogger<TelegramParser>` as
      a primary-constructor parameter (alongside existing `ITelegramChannelReader`); add one
      `LogInformation` line at the end of `ParseAsync` before the return:
      `"Parsed {AlbumCount} albums and {SingleCount} singles from {Username}",
      albums.Count, singles.Count, username`.
      _Acceptance: `dotnet build` passes; the log line fires once per channel per parse cycle;
      no string interpolation._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step G — Worker scopes + ArticleAnalysisWorker bug fix

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — two changes in one file:
      1. **Bug fix (line ~209):** replace the message template `"Article {Id} auto-matched to
         event {EventId} (similarity: {S})"` with
         `"Article {ArticleId} auto-matched to event {EventId} (similarity: {Similarity})"` and
         pass `article.Id`, `topEvent.Id`, and the actual similarity score variable
         (not `topEvent.Id` twice). The placeholder `{ArticleId}` must be consistent with all
         other log lines in this file that reference the same article.
      2. **Scopes:** wrap the body of `ProcessAsync` in a cycle scope
         `_logger.BeginScope(new Dictionary<string, object> { ["Worker"] = nameof(ArticleAnalysisWorker), ["CycleId"] = Guid.NewGuid() })`;
         wrap the per-article processing call in an item scope
         `_logger.BeginScope(new Dictionary<string, object> { ["ArticleId"] = article.Id })`.
      Do NOT delete, restate, or change the level of any existing log line other than the
      `(similarity: {S})` placeholder fix.
      _Acceptance: `dotnet build` passes; `topEvent.Id` appears exactly once in the fixed log
      call (as `{EventId}`); the similarity score variable is the third argument; both scope
      dictionaries use `Dictionary<string, object>` literals._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Worker/Workers/SourceFetcherWorker.cs`** — add scopes only; do not touch
      existing log lines or their levels:
      1. Wrap the body of `ProcessAsync` in a cycle scope
         `{ ["Worker"] = nameof(SourceFetcherWorker), ["CycleId"] = Guid.NewGuid() }`.
      2. Wrap the `foreach (var (sourceType, parser))` body in a source-type scope
         `{ ["SourceType"] = sourceType.ToString() }`.
      3. Wrap the inner per-source loop body in a source scope
         `{ ["SourceId"] = source.Id, ["SourceName"] = source.Name }`.
      4. Add `LogDebug` at the entry of `ProcessSourceAsync`:
         `"Begin parse for {SourceName}", source.Name`.
      _Acceptance: `dotnet build` passes; no existing log line is modified; `Dictionary<string, object>`
      literals used for all scopes._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/Workers/PublishingWorker.cs`** — add scopes only; do not touch existing
      log lines:
      1. Wrap `ProcessBatchAsync` body in a cycle scope
         `{ ["Worker"] = nameof(PublishingWorker), ["CycleId"] = Guid.NewGuid() }`.
      2. Wrap the per-publication processing call in an item scope
         `{ ["PublicationId"] = publication.Id, ["PublishTargetName"] = publication.PublishTarget?.Name, ["Platform"] = publication.PublishTarget?.Platform.ToString() }`.
      3. Add `LogDebug` at the entry of the media-resolution section inside
         `ResolveAndPublishAsync` (or equivalent method): `"Resolving {Count} media files",
         mediaFiles.Count`.
      _Acceptance: `dotnet build` passes; no existing log line is modified._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/Workers/PublicationGenerationWorker.cs`** — add scopes only; do not touch
      existing log lines:
      1. Wrap `ProcessBatchAsync` body in a cycle scope
         `{ ["Worker"] = nameof(PublicationGenerationWorker), ["CycleId"] = Guid.NewGuid() }`.
      2. Wrap the per-publication processing call in an item scope
         `{ ["PublicationId"] = publication.Id, ["EventId"] = publication.EventId }`.
      _Acceptance: `dotnet build` passes; no existing log line is modified._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Step H — Auth controller logging

- [x] **Modify `Api/Controllers/AuthController.cs`** — add `ILogger<AuthController>` as a
      primary-constructor parameter; add the following log calls:
      - After `if (user is null) return Unauthorized(...)`: `LogWarning("Failed login attempt
        for {Email}", request.Email)`.
      - Before `return Ok(...)` in `Login`: `LogInformation("User {Email} logged in",
        user.Email)`.
      - Before `return CreatedAtAction(...)` in `Register`: `LogInformation("User {UserId}
        registered with role {Role}", user.Id, user.Role)`.
      Never log the password, token string, or password hash.
      _Acceptance: `dotnet build` passes; failed login logs Warning before returning 401;
      successful login logs Information; registration logs Information with UserId + Role but
      not email (email is already visible from the Warning on failures and from the service
      layer); no string interpolation in any log call._
      _Skill: .claude/skills/api-conventions/SKILL.md_

---

## Open Questions

None — the ADR fully specifies every file, level, placeholder name, scope key, and out-of-scope
boundary. The implementation order (A → H) is required to keep the build green at each step.
