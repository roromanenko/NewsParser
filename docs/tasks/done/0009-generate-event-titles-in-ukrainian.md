# Generate Event Titles in Ukrainian

## Goal

Generate a Ukrainian-language title for every event at creation time and regenerate it
whenever the event receives a significant update, so that event titles always match the
language and content of the event summary.

## Affected Layers

- Core
- Infrastructure
- Worker
- (Tests)

## ADR Reference

`docs/architecture/decisions/generate-event-titles-in-ukrainian.md`

---

## Tasks

### Core

- [x] **Create `Core/Interfaces/AI/IEventTitleGenerator.cs`** — new interface with a single
      method:
      ```csharp
      Task<string> GenerateTitleAsync(
          string eventSummary,
          List<string> articleTitles,
          CancellationToken cancellationToken = default);
      ```
      Place the file in `Core/Interfaces/AI/` alongside `IEventSummaryUpdater.cs`.
      _Acceptance: file compiles; no references to Infrastructure or EF Core; interface lives
      in namespace `Core.Interfaces.AI`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Core/Interfaces/Repositories/IEventRepository.cs`** — rename the method
      `UpdateSummaryAndEmbeddingAsync` to `UpdateSummaryTitleAndEmbeddingAsync` and add a
      `string title` parameter as the second argument (before `summary`):
      ```csharp
      Task UpdateSummaryTitleAndEmbeddingAsync(
          Guid id,
          string title,
          string summary,
          float[] embedding,
          CancellationToken cancellationToken = default);
      ```
      Remove the old signature entirely.
      _Acceptance: file compiles; the old method name is gone; the new signature appears once
      in the interface_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

---

### Infrastructure

- [x] **Create `Infrastructure/AI/HaikuEventTitleGenerator.cs`** — implement
      `IEventTitleGenerator` following the same constructor pattern as
      `HaikuKeyFactsExtractor` (`apiKey` and `model` string parameters; no DI; constructs
      `AnthropicClient` internally).
      System prompt instructs the model to:
      - Return a concise Ukrainian-language news headline.
      - Base it on the provided event summary and article titles.
      - Maximum 15 words.
      - No quotes, no trailing punctuation, no "Breaking:" prefixes.
      - Be factual and neutral.
      User message: event summary followed by the list of article titles.
      On success, return the trimmed string response.
      On any exception other than `OperationCanceledException`, return `string.Empty` so the
      caller can apply its own fallback.
      _Acceptance: file compiles; class is in namespace `Infrastructure.AI`; implements
      `IEventTitleGenerator`; `OperationCanceledException` is NOT caught_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/EventRepository.cs`** — rename the
      method `UpdateSummaryAndEmbeddingAsync` to `UpdateSummaryTitleAndEmbeddingAsync` and
      add `.SetProperty(e => e.Title, title)` to the existing `ExecuteUpdateAsync` chain.
      Add the `string title` parameter as the second parameter (before `summary`).
      _Acceptance: file compiles; the old method name does not exist; the `ExecuteUpdateAsync`
      call sets `Title`, `Summary`, `Embedding`, `ArticleCount + 1`, and `LastUpdatedAt`_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Configuration/AiOptions.cs`** — add a
      `TitleGeneratorModel` property to `AnthropicOptions`:
      ```csharp
      public string TitleGeneratorModel { get; set; } = "claude-haiku-4-5-20251001";
      ```
      Place it after `KeyFactsExtractorModel`.
      _Acceptance: file compiles; `AnthropicOptions` has the new property with the haiku
      default; existing properties are unchanged_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — in
      `AddAiServices`, register `IEventTitleGenerator` as a scoped service:
      ```csharp
      services.AddScoped<IEventTitleGenerator>(_ => new HaikuEventTitleGenerator(
          aiOptions.Anthropic.ApiKey,
          aiOptions.Anthropic.TitleGeneratorModel
      ));
      ```
      Place the registration after the `IKeyFactsExtractor` registration to keep cheap-model
      services grouped together.
      _Acceptance: project builds; `IEventTitleGenerator` resolves from the DI container
      without exception; existing service registrations are unchanged_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Services/EventService.cs`** — add `IEventTitleGenerator` to
      the primary constructor parameters. In `MergeAsync`, after the merged summary is
      produced and before calling the repository update, generate a Ukrainian title:
      ```csharp
      var mergedTitle = await titleGenerator.GenerateTitleAsync(
          mergedSummary,
          [source.Title, target.Title],
          cancellationToken);
      var finalTitle = string.IsNullOrWhiteSpace(mergedTitle) ? target.Title : mergedTitle;
      ```
      Replace the call to `UpdateSummaryAndEmbeddingAsync` with
      `UpdateSummaryTitleAndEmbeddingAsync`, passing `finalTitle` as the title argument.
      The entire try/catch block already suppresses AI failures — no additional error handling
      is needed.
      _Acceptance: file compiles; `EventService` constructor has `IEventTitleGenerator`;
      `MergeAsync` passes a title to the repository method; existing `ReclassifyArticleAsync`
      is unchanged_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Worker

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — add `IEventTitleGenerator` to
      the `AnalysisContext` record as the last field:
      ```csharp
      private record AnalysisContext(
          IArticleRepository ArticleRepository,
          IEventRepository EventRepository,
          IArticleAnalyzer Analyzer,
          IGeminiEmbeddingService EmbeddingService,
          IEventClassifier Classifier,
          IEventSummaryUpdater SummaryUpdater,
          IKeyFactsExtractor KeyFactsExtractor,
          IContradictionDetector ContradictionDetector,
          IEventTitleGenerator TitleGenerator);
      ```
      In `ProcessAsync`, resolve the service and add it to the `AnalysisContext`
      constructor call:
      ```csharp
      TitleGenerator: scope.ServiceProvider.GetRequiredService<IEventTitleGenerator>()
      ```
      _Acceptance: file compiles; `AnalysisContext` has the new field; `ProcessAsync` resolves
      `IEventTitleGenerator` from the scope_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — change `CreateNewEventAsync`
      from a `static` method to an instance method (it will need `_logger` for fallback
      logging) and add `IEventTitleGenerator titleGenerator` and `CancellationToken` as
      parameters. Before constructing the `Event`, generate the Ukrainian title:
      ```csharp
      string title;
      try
      {
          var generated = await titleGenerator.GenerateTitleAsync(
              article.Summary ?? string.Empty,
              [article.Title],
              cancellationToken);
          title = string.IsNullOrWhiteSpace(generated) ? article.Title : generated;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
          _logger.LogWarning(ex, "Title generation failed for new event, using article title");
          title = article.Title;
      }
      var newEvent = new Event { Title = title, ... };
      ```
      Update all call sites of `CreateNewEventAsync` inside the same file to pass
      `ctx.TitleGenerator` and remove the leading `static` keyword from the method
      signature.
      _Acceptance: file compiles; `CreateNewEventAsync` is no longer `static`; both call sites
      (`ClassifyIntoEventAsync` and `HandleGreyZoneAsync`) pass `ctx.TitleGenerator`; the
      generated title is used when non-empty, article title is used as fallback_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — in
      `UpdateEventEmbeddingAsync`, after `updatedSummary` is produced by `SummaryUpdater`,
      generate a new Ukrainian title and pass it to the renamed repository method.
      Add title generation before the repository call:
      ```csharp
      string updatedTitle;
      try
      {
          var articleTitles = evt.Articles.Select(a => a.Title).ToList();
          var generated = await ctx.TitleGenerator.GenerateTitleAsync(
              updatedSummary, articleTitles, cancellationToken);
          updatedTitle = string.IsNullOrWhiteSpace(generated) ? evt.Title : generated;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
          _logger.LogWarning(ex, "Title regeneration failed for event {EventId}, keeping existing title", evt.Id);
          updatedTitle = evt.Title;
      }
      await ctx.EventRepository.UpdateSummaryTitleAndEmbeddingAsync(
          evt.Id, updatedTitle, updatedSummary, updatedEmbedding, cancellationToken);
      ```
      When `newFacts` is empty (no summary update occurred), still call the renamed method
      using `evt.Title` as the title so the signature is satisfied.
      _Acceptance: file compiles; `UpdateSummaryAndEmbeddingAsync` is no longer called
      anywhere in the file; the renamed method is called with a non-null title in all branches;
      `OperationCanceledException` propagates; other exceptions use the existing event title as
      fallback_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Tests

- [ ] **Create `Tests/Infrastructure.Tests/AI/HaikuEventTitleGeneratorTests.cs`** _Delegated to test-writer agent_ — unit
      tests for `HaikuEventTitleGenerator` following the same reflection-based pattern as
      `HaikuKeyFactsExtractorTests.cs`.
      Cover:
      - `GenerateTitleAsync` with an already-cancelled token throws
        `OperationCanceledException` (not swallowed)
      - When the client returns a non-empty trimmed string, `GenerateTitleAsync` returns
        it unchanged (use reflection to test the internal parsing/trimming helper if one is
        extracted, or test the cancellation path as an integration boundary)
      - `HaikuEventTitleGenerator` can be constructed with arbitrary `apiKey` and `model`
        strings without throwing
      _Acceptance: all tests pass; no live HTTP or Anthropic API calls_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Worker.Tests/Workers/ArticleAnalysisWorkerAutoMatchContradictionTests.cs`** _Delegated to test-writer agent_
      — add `Mock<IEventTitleGenerator> _titleGeneratorMock` to the fixture and register it
      in `SetUp`. Update the DI scope stub to resolve `IEventTitleGenerator` from the mock.
      Configure the mock to return a non-empty title by default
      (`"Тестовий заголовок події"`).
      Also update the mock for `IEventRepository.UpdateSummaryTitleAndEmbeddingAsync` (the
      renamed method) so existing tests that verify repository calls still compile and pass.
      Add two new test cases:
      - When `UpdateEventEmbeddingAsync` is called on the auto-match path, `TitleGenerator.GenerateTitleAsync`
        is called once with the updated summary and a non-null article titles list.
      - When `TitleGenerator.GenerateTitleAsync` returns an empty string, the existing event
        title is used as the fallback (verify via the title argument passed to
        `UpdateSummaryTitleAndEmbeddingAsync`).
      _Acceptance: all existing tests remain green; two new tests pass; no compilation errors_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Create `Tests/Worker.Tests/Workers/ArticleAnalysisWorkerCreateEventTitleTests.cs`** _Delegated to test-writer agent_
      — new `[TestFixture]` class following the same scaffold as
      `ArticleAnalysisWorkerAutoMatchContradictionTests`. Set up the worker so that
      `FindSimilarEventsAsync` returns an empty list (forcing the new-event path).
      Cover:
      - When `IEventTitleGenerator.GenerateTitleAsync` returns a non-empty Ukrainian string,
        the event created via `IEventRepository.CreateAsync` has that string as its `Title`.
      - When `IEventTitleGenerator.GenerateTitleAsync` returns an empty string, the event
        created via `CreateAsync` has `article.Title` as its `Title` (fallback).
      - When `IEventTitleGenerator.GenerateTitleAsync` throws a non-cancellation exception,
        the event is still created with `article.Title` as its `Title` (exception is caught
        and logged).
      _Acceptance: all three tests pass; no live service calls; `IEventRepository.CreateAsync`
      mock verifies the `Title` property of the created event_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

## Open Questions

- None. The ADR fully specifies the interface signature, implementation pattern, model
  configuration key, fallback strategy, and call sites.
