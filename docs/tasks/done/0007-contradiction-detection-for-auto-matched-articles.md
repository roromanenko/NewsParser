# Contradiction Detection for Auto-Matched Articles

## Goal

Run contradiction detection for articles that auto-match an event (similarity >= `AutoSameEventThreshold`),
so factually opposing articles cannot silently pass through as `ArticleRole.Update`.

## Affected Layers

- Core
- Infrastructure
- Worker

## ADR Reference

`docs/architecture/decisions/add-contradiction-detection-for-auto-matched-articles.md`

---

## Tasks

---

### Core

- [x] **Create `Core/Interfaces/AI/IContradictionDetector.cs`** — new interface with a single method:
      `Task<List<ContradictionInput>> DetectAsync(Article article, Event targetEvent, CancellationToken cancellationToken = default);`
      where `ContradictionInput` is the existing type from `Core/DomainModels/AI/EventClassificationResult.cs`.
      _Acceptance: file compiles with no EF or infrastructure references; interface is in the `Core.Interfaces.AI` namespace_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Infrastructure

- [x] **Create `Infrastructure/Prompts/contradiction_detector.txt`** — focused prompt for contradiction detection.
      The prompt receives an article (title, summary, key facts) and a target event (title, summary, known facts
      from `EventUpdates`). It must return a JSON array of `ContradictionInput` objects — each with `articleIds`
      (array of GUIDs) and `description` (string) — or an empty JSON array if no contradictions are found.
      The prompt must not ask for classification decisions, new-fact significance, or any field other than contradictions.
      _Acceptance: file exists at the path; the JSON schema described in the prompt matches `ContradictionInput`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Configuration/PromptsOptions.cs`** — add:
      `public string ContradictionDetectorPath { get; set; } = "Prompts/contradiction_detector.txt";`
      and `public string ContradictionDetector => ReadPrompt(ContradictionDetectorPath);`
      following the existing `EventClassifierPath` / `EventClassifier` pattern.
      _Acceptance: file compiles; `PromptsOptions` exposes both the path property and the computed `ContradictionDetector` string_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Configuration/AiOptions.cs`** — add
      `public string ContradictionDetectorModel { get; set; } = "claude-haiku-4-5-20251001";`
      to `AnthropicOptions`, following the pattern of `ClassifierModel`.
      _Acceptance: file compiles; `AnthropicOptions.ContradictionDetectorModel` is readable by `InfrastructureServiceExtensions`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Infrastructure/AI/ClaudeContradictionDetector.cs`** — AI service implementing `IContradictionDetector`.
      Constructor signature: `(string apiKey, string model, string prompt)` — identical pattern to `ClaudeEventClassifier`.
      `DetectAsync` builds a user prompt containing the article fields (id, title, summary, key facts)
      and the target event fields (id, title, summary, `EventUpdates` count and their fact summaries),
      calls the Anthropic API, parses the response as `List<ContradictionInput>` (strip markdown fences,
      deserialize with `PropertyNameCaseInsensitive = true`), and returns the list.
      _Acceptance: file compiles; class is in the `Infrastructure.AI` namespace; implements `IContradictionDetector`;
      returns an empty list (not throws) when the model returns `[]`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — register `IContradictionDetector`
      in `AddAiServices` immediately after the `IEventClassifier` registration:
      ```
      services.AddScoped<IContradictionDetector>(_ => new ClaudeContradictionDetector(
          aiOptions.Anthropic.ApiKey,
          aiOptions.Anthropic.ContradictionDetectorModel,
          promptsOptions.ContradictionDetector
      ));
      ```
      _Acceptance: file compiles; `IContradictionDetector` resolves from DI in the Worker host_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Worker

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs` — add `IContradictionDetector` to `AnalysisContext`**
      Add `IContradictionDetector ContradictionDetector` as a new parameter to the `AnalysisContext` record
      (after `IKeyFactsExtractor KeyFactsExtractor`).
      Resolve it from `scope.ServiceProvider` in `ProcessAsync` alongside the other services.
      _Acceptance: file compiles; worker starts without DI exception in Development_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs` — refactor `SaveContradictionsAsync` signature**
      Change the first parameter from `EventClassificationResult result` to `List<ContradictionInput> contradictions`,
      replacing `result.Contradictions` references with the new parameter directly.
      Update the single existing call site in the grey-zone path from
      `SaveContradictionsAsync(result, ...)` to `SaveContradictionsAsync(result.Contradictions, ...)`.
      _Acceptance: file compiles; the grey-zone path still calls `SaveContradictionsAsync` correctly;
      the method no longer references `EventClassificationResult`_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs` — update auto-match branch in `ClassifyIntoEventAsync`**
      In the `topSimilarity >= _options.AutoSameEventThreshold` branch, after setting `targetEvent = topEvent`,
      add contradiction detection before `UpdateEventEmbeddingAsync`:
      ```
      var contradictions = await ctx.ContradictionDetector.DetectAsync(article, targetEvent, cancellationToken);
      role = contradictions.Count > 0 ? ArticleRole.Contradiction : ArticleRole.Update;
      if (contradictions.Count > 0)
          await SaveContradictionsAsync(contradictions, targetEvent, article, ctx.EventRepository, cancellationToken);
      ```
      _Acceptance: auto-matched articles with contradictions are assigned `ArticleRole.Contradiction`;
      `SaveContradictionsAsync` is called with the detector's output; `UpdateEventEmbeddingAsync` is still called_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Worker/appsettings.Development.json`** — add `ContradictionDetectorPath` to the `Prompts` section
      and `ContradictionDetectorModel` to the `Ai.Anthropic` section:
      ```json
      "Prompts": {
        ...
        "ContradictionDetectorPath": "Prompts/contradiction_detector.txt"
      },
      "Ai": {
        "Anthropic": {
          ...
          "ContradictionDetectorModel": "claude-haiku-4-5-20251001"
        }
      }
      ```
      _Acceptance: Worker host binds `PromptsOptions.ContradictionDetectorPath` without errors on startup_

- [ ] **Write tests for `ClaudeContradictionDetector` in `Tests/Infrastructure.Tests/AI/ClaudeContradictionDetectorTests.cs`** _Delegated to test-writer agent_
      Cover via reflection on the private prompt-building / parse helpers (same pattern as `ClaudeContentGeneratorTests`):
      - Returned list contains one `ContradictionInput` when JSON has one entry with correct fields
      - Returns empty list when model returns `[]`
      - Strips markdown fences before deserializing
      _Acceptance: all tests pass; no live HTTP calls; no `any`-equivalent untyped casts_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Write tests for the auto-match contradiction path in `Tests/Worker.Tests/Workers/ArticleAnalysisWorkerAutoMatchContradictionTests.cs`** _Delegated to test-writer agent_
      New `[TestFixture]` class mirroring the setup in `ArticleAnalysisWorkerDeduplicationTests`:
      - Add `Mock<IContradictionDetector>` alongside existing mocks
      - Register it in the DI scope wiring (in `WireUpScopeFactory`)
      - Test: when `FindSimilarEventsAsync` returns one event with similarity >= `AutoSameEventThreshold`
        and `IContradictionDetector.DetectAsync` returns one `ContradictionInput`,
        then `AssignArticleToEventAsync` is called with `ArticleRole.Contradiction`
        and `AddContradictionAsync` is called once.
      - Test: when `IContradictionDetector.DetectAsync` returns empty list,
        then `AssignArticleToEventAsync` is called with `ArticleRole.Update`
        and `AddContradictionAsync` is not called.
      - Test: `IContradictionDetector.DetectAsync` is called with the matched event's id,
        not with any other event.
      _Acceptance: all three tests pass; mocks verify exact call arguments_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

## Open Questions

- None. The ADR fully specifies the design.
