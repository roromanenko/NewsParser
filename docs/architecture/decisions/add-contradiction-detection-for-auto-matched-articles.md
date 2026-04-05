# Add Contradiction Detection for Auto-Matched Articles

## Context

In `Worker/Workers/ArticleAnalysisWorker.cs`, the `ClassifyIntoEventAsync` method has three paths:

1. **No similar events** (similarity below `AutoNewEventThreshold`) -- creates a new event, role = Initiator. No contradiction check needed (no existing event to contradict).
2. **Grey zone** (similarity between `AutoNewEventThreshold` and `AutoSameEventThreshold`) -- calls `IEventClassifier.ClassifyAsync`, which returns `EventClassificationResult` containing `Contradictions`, `IsSignificantUpdate`, and `NewFacts`. Contradictions are saved, role is set to `Contradiction` if any are found.
3. **Auto-match** (similarity >= `AutoSameEventThreshold`) -- skips the classifier entirely, sets role = `Update`, and updates the event embedding. **No contradiction detection occurs.**

Path 3 is the bug. An article that strongly matches an event (high vector similarity) can still contradict facts in that event. For example, "Country X confirms deal" auto-matching an event about "Country X denies deal" would have high similarity but opposite factual claims. Currently this goes through as a silent `Update`.

The classifier (`ClaudeEventClassifier`) performs contradiction detection as part of a larger classification task that also determines `IsNewEvent`, `MatchedEventId`, `IsSignificantUpdate`, and `NewFacts`. For auto-matched articles, the classification decision is already known (it belongs to the matched event), and the significance/new-facts analysis is explicitly not wanted for auto-matched articles per the feature requirements. Only contradiction detection is needed.

## Options

### Option 1 -- Extract a standalone IContradictionDetector interface and AI service

Create a new `IContradictionDetector` interface in `Core/Interfaces/AI/` with a method like `DetectAsync(Article article, Event targetEvent, CancellationToken)` returning `List<ContradictionInput>`. Implement it as a separate AI service (`ClaudeContradictionDetector`) with its own focused prompt. Call it for both auto-match and grey-zone paths.

**Pros:** Single Responsibility -- the contradiction detector does exactly one thing. Prompt can be optimized for contradiction detection only (smaller, cheaper, faster). Easy to test in isolation. Clean separation from classification logic.
**Cons:** New interface, new AI service class, new prompt file, new DI registration -- more files to create. The grey-zone path still uses the classifier which also detects contradictions, creating potential duplication of contradiction results (classifier finds contradictions AND detector finds contradictions for the same grey-zone article).

### Option 2 -- Reuse IEventClassifier for auto-matched articles, discarding non-contradiction fields

Call `IEventClassifier.ClassifyAsync` for auto-matched articles too, but only use the `Contradictions` field from the result. Ignore `IsNewEvent`, `MatchedEventId`, `IsSignificantUpdate`, `NewFacts` for auto-matched articles since those decisions are already made.

**Pros:** No new interfaces or AI services. Reuses existing prompt and parsing logic. Minimal code change -- just move the classifier call and contradiction-saving logic to also run in the auto-match branch.
**Cons:** Wastes AI tokens asking the classifier to determine classification and new-fact significance when those answers will be discarded. The classifier prompt is designed for grey-zone ambiguity ("decide which event this belongs to") which is conceptually wrong for an auto-matched article. Couples contradiction detection to the classifier's prompt structure.

### Option 3 -- Extract contradiction logic into a private method, call classifier for both paths

Keep `IEventClassifier` as-is but extract the contradiction-checking and saving logic from the grey-zone branch into a reusable private method in `ArticleAnalysisWorker`. For auto-matched articles, call the classifier with a single-element candidate list (just the matched event), then use only the contradiction results.

**Pros:** Minimal structural change -- just method extraction within the worker. No new interfaces. No new prompt files.
**Cons:** Same token waste as Option 2. The classifier still does unnecessary classification work. The auto-match path now makes an AI call it previously skipped, adding latency. No clean separation of concerns.

## Decision

**Option 1 -- Extract a standalone `IContradictionDetector` interface and AI service.**

Rationale:

1. **Single Responsibility.** Contradiction detection is a distinct analytical task from event classification. The classifier answers "which event does this article belong to?" while the detector answers "does this article contradict known facts in a given event?" These are different questions requiring different prompts.

2. **Cost efficiency.** A focused contradiction detection prompt is smaller and cheaper than the full classifier prompt. Auto-matched articles (high similarity) are likely the most common path -- they should not pay the cost of full classification.

3. **No duplication problem.** For the grey-zone path, the classifier already returns contradictions. The worker can check the classifier's `Contradictions` result first and only call the standalone detector if needed -- or simply let the classifier handle contradictions for grey-zone articles as it does today. The detector is only called for the auto-match path. This keeps the two paths cleanly separated.

4. **Follows existing patterns.** The codebase already has focused AI interfaces: `IArticleAnalyzer` (analysis only), `IKeyFactsExtractor` (key facts only), `IEventClassifier` (classification only), `IEventSummaryUpdater` (summary updates only). Adding `IContradictionDetector` is consistent with this pattern.

### Detailed design

**New interface** -- `Core/Interfaces/AI/IContradictionDetector.cs`:
```
IContradictionDetector
  Task<List<ContradictionInput>> DetectAsync(Article article, Event targetEvent, CancellationToken)
```

Returns `List<ContradictionInput>` -- the same type already used by `EventClassificationResult.Contradictions`, so `SaveContradictionsAsync` can be reused without changes.

**New AI service** -- `Infrastructure/AI/ClaudeContradictionDetector.cs`:
- Constructor: `(string apiKey, string model, string prompt)` -- same pattern as `ClaudeEventClassifier`.
- Prompt receives the article (title, summary, key facts) and the target event (title, summary, known facts from `EventUpdates`).
- Response is a JSON array of `ContradictionInput` objects (or empty array if no contradictions).

**New prompt file** -- `Infrastructure/Prompts/contradiction_detector.txt`:
- Focused prompt: given an article and an event's known facts, identify factual contradictions.
- Much simpler than the classifier prompt (no classification decision, no new-fact significance analysis).

**PromptsOptions update** -- `Infrastructure/Configuration/PromptsOptions.cs`:
- Add `ContradictionDetectorPath` and `ContradictionDetector` properties.

**DI registration** -- `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`:
- Register `IContradictionDetector` as `ClaudeContradictionDetector` (same pattern as other AI services).

**Worker changes** -- `Worker/Workers/ArticleAnalysisWorker.cs`:

The `AnalysisContext` record gains `IContradictionDetector ContradictionDetector`.

In `ClassifyIntoEventAsync`, the auto-match branch (line 191-199) changes from:
```
targetEvent = topEvent;
role = ArticleRole.Update;
await UpdateEventEmbeddingAsync(...);
```
to:
```
targetEvent = topEvent;
var contradictions = await ctx.ContradictionDetector.DetectAsync(article, targetEvent, cancellationToken);
role = contradictions.Count > 0 ? ArticleRole.Contradiction : ArticleRole.Update;
if (contradictions.Count > 0)
    await SaveContradictionsFromDetectorAsync(contradictions, targetEvent, article, ctx.EventRepository, cancellationToken);
await UpdateEventEmbeddingAsync(...);
```

A new private method `SaveContradictionsFromDetectorAsync` is needed (or adapt `SaveContradictionsAsync` to accept `List<ContradictionInput>` directly instead of `EventClassificationResult`). Looking at the current `SaveContradictionsAsync`, it takes `EventClassificationResult` only to read `.Contradictions` from it. Refactoring it to accept `List<ContradictionInput>` directly would make it reusable for both paths. The grey-zone path would then call `SaveContradictionsAsync(result.Contradictions, targetEvent, article, ...)` instead of `SaveContradictionsAsync(result, targetEvent, article, ...)`.

**Grey-zone path** -- no changes to contradiction logic. The classifier already detects contradictions for grey-zone articles, and that continues to work as-is.

## Implementation Notes

### For Feature-Planner

**Order of changes:**

1. Create `Core/Interfaces/AI/IContradictionDetector.cs` (new interface)
2. Create `Infrastructure/Prompts/contradiction_detector.txt` (new prompt)
3. Add `ContradictionDetectorPath` and `ContradictionDetector` to `Infrastructure/Configuration/PromptsOptions.cs`
4. Create `Infrastructure/AI/ClaudeContradictionDetector.cs` (new AI service, follows `ClaudeEventClassifier` pattern)
5. Register `IContradictionDetector` in `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`
6. Refactor `SaveContradictionsAsync` in `ArticleAnalysisWorker.cs` to accept `List<ContradictionInput>` instead of `EventClassificationResult`
7. Add `IContradictionDetector` to the `AnalysisContext` record in `ArticleAnalysisWorker.cs`
8. Update the auto-match branch in `ClassifyIntoEventAsync` to call contradiction detection and save results
9. Add `ContradictionDetector` configuration to `Worker/appsettings.Development.json` (if section-based) or verify the existing `Prompts` section covers the new path
10. Write tests for `ClaudeContradictionDetector` (unit test with mocked Anthropic response)
11. Write/update tests for `ArticleAnalysisWorker` auto-match path to verify contradiction detection is called

**Key files affected:**
- `Core/Interfaces/AI/IContradictionDetector.cs` (NEW)
- `Infrastructure/AI/ClaudeContradictionDetector.cs` (NEW)
- `Infrastructure/Prompts/contradiction_detector.txt` (NEW)
- `Infrastructure/Configuration/PromptsOptions.cs` (MODIFY -- add prompt path)
- `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` (MODIFY -- add DI registration)
- `Worker/Workers/ArticleAnalysisWorker.cs` (MODIFY -- add detector to context, update auto-match branch, refactor SaveContradictionsAsync signature)
- `Worker/appsettings.Development.json` (MODIFY -- add prompt path if needed)

### Skills to Follow

- `.claude/skills/code-conventions/SKILL.md` -- interface placement in `Core/Interfaces/AI/`, worker architecture (AnalysisContext pattern, scope resolution), Options pattern for prompt paths
- `.claude/skills/clean-code/SKILL.md` -- method extraction (SaveContradictionsAsync refactor), naming conventions for the new detector
- `.claude/skills/testing/SKILL.md` -- test patterns for AI services and worker paths
