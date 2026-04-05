# Event Classification and Article Role Assignment

## Status

Proposed

## Context

The event classification pipeline in `ArticleAnalysisWorker.ClassifyIntoEventAsync` determines whether each article describes a new event or belongs to an existing one, and assigns a role (`Initiator`, `Update`, or `Contradiction`) to each article. The current implementation has structural gaps that cause incorrect classifications, particularly around the distinction between updates and contradictions, and the handling of incomplete early reporting.

### Problem 1: Classifiers and contradiction detectors receive incomplete event data

`FindSimilarEventsAsync` in `EventRepository.cs` (line 44-70) performs a vector similarity search but does NOT `Include` any navigation properties -- no `Articles`, no `EventUpdates`, no `Contradictions`. The query projects only the event entity and similarity score. When `.ToDomain()` maps these results, all navigation collections default to `[]`.

This means:
- `ClaudeEventClassifier.ClassifyAsync` receives candidate events where `EventUpdates.Count` is always 0, so the prompt always shows "Known Facts Count: 0" regardless of how many facts are actually recorded.
- `ClaudeContradictionDetector.DetectAsync` receives a `targetEvent` where `EventUpdates` is always empty, so the prompt always shows "No known facts recorded" -- making it impossible to detect contradictions against known facts.
- Neither the classifier nor the contradiction detector sees the articles already assigned to the event, their key facts, or their summaries.

The AI models are being asked to classify and detect contradictions without the data they need to do so correctly.

### Problem 2: The classifier prompt lacks guidance on update vs. contradiction semantics

The `event_classifier.txt` prompt instructs the model to detect contradictions ("sources directly state opposing facts") and significant updates, but provides no guidance on the core distinction the feature requires:

- Uncertainty in early reporting resolved by later reporting (e.g., "casualties being clarified" then "7 people injured") is an **update**, not a contradiction and not a new event.
- Genuinely conflicting claims from different sources in the same timeframe (e.g., Source A says "3 injured", Source B says "7 injured") may be a **contradiction**.
- Articles about a completely different incident in the same region should be a **new event**.

Without explicit guidance, the model may split same-incident articles into separate events when early reports are vague, or flag normal reporting evolution as contradictions.

### Problem 3: The contradiction detector prompt lacks article-level context

The `contradiction_detector.txt` prompt receives only the event summary and event updates (which, due to Problem 1, are always empty). It does not receive the key facts or summaries of individual articles already in the event. This means it cannot compare the new article's claims against what specific sources have reported.

### Problem 4: The auto-match path does not update the event summary with significant new facts

When an article auto-matches an event (similarity >= `AutoSameEventThreshold`), the `UpdateEventEmbeddingAsync` method is called. This method updates the event summary only if `newFact` (which is `article.Summary`) is non-empty. However, unlike the grey-zone path, the auto-match path does not:
- Determine whether the article contains genuinely new facts (no `IsSignificantUpdate` / `NewFacts` analysis)
- Create an `EventUpdate` record for significant new information

The auto-match path always calls `UpdateEventEmbeddingAsync` with `article.Summary` as the single "new fact", causing the summary updater to incorporate the entire article summary every time -- even when it adds no new information. Meanwhile, significant new facts from auto-matched articles are never recorded as `EventUpdate` entries.

### Relationship to existing ADRs

- `worker-pipeline-refactoring.md` established the three-phase pipeline (enrichment -> embedding -> classification) and the auto-match/grey-zone/new-event decision tree. This ADR fixes data flow gaps in that design.
- `event-based-publication-with-key-facts.md` added `KeyFacts` to articles and `IKeyFactsExtractor`. This ADR leverages key facts to provide richer context to classifiers and contradiction detectors.
- `add-contradiction-detection-for-auto-matched-articles.md` added `IContradictionDetector` for the auto-match path. This ADR fixes the fact that the detector receives empty event data, and extends the auto-match path to also perform update significance analysis.

## Options

### Option 1 -- Enrich FindSimilarEventsAsync with includes, improve prompts, add update analysis to auto-match

Modify the `FindSimilarEventsAsync` query to include `Articles` (with their `KeyFacts`) and `EventUpdates` so that AI services receive complete event context. Rewrite both the classifier and contradiction detector prompts to include explicit update-vs-contradiction guidance. Add a lightweight "update significance" check to the auto-match path using either the existing `IEventClassifier` (with only one candidate) or a new focused interface.

**Pros:** Fixes all four problems. Uses existing interfaces. Prompts receive the data they need.
**Cons:** Including `Articles` and `EventUpdates` in the similarity search query increases query payload. The auto-match update analysis adds an AI call to the fast path.

### Option 2 -- Separate repository method for enriched event loading, lazy-load context only when needed

Keep `FindSimilarEventsAsync` lightweight (no includes). After the similarity search identifies candidates, load full event context via a separate `GetByIdAsync` or `GetDetailAsync` call only for the events that will be sent to AI. This avoids loading full context for events that fall below the threshold.

**Pros:** Similarity query stays fast. Context is loaded only when needed. Follows the existing pattern where `GetDetailAsync` includes full navigation properties.
**Cons:** Extra database round-trip for each candidate event. For the auto-match path (typically 1 event), this is one extra query. For the grey-zone path with multiple candidates, this is N extra queries.

### Option 3 -- New repository method with configurable includes, prompt rewriting only

Add a new `FindSimilarEventsWithContextAsync` method that includes `Articles` and `EventUpdates` in the query. Use it instead of the current method. Do not add update analysis to the auto-match path (accept that auto-matched articles only get contradiction detection, not update significance analysis).

**Pros:** Single query with context. Simpler than Option 1 (no auto-match update analysis).
**Cons:** Does not fix Problem 4 (auto-match path still doesn't track significant updates). Loads full context for all similar events even if most are below the grey-zone threshold.

## Decision

**Option 2 -- Separate repository method for enriched event loading, with prompt improvements and auto-match update analysis.**

This option is the best balance of correctness and efficiency. The similarity search stays lightweight (vector math only), and full event context is loaded only for the 1-3 events that actually need AI analysis.

### Detailed Design

#### 1. New repository method: GetWithContextAsync

**`Core/Interfaces/Repositories/IEventRepository.cs`** -- add:
```
Task<Event?> GetWithContextAsync(Guid id, CancellationToken cancellationToken = default);
```

**`Infrastructure/Persistence/Repositories/EventRepository.cs`** -- implement:
- Include `Articles` (to get their `KeyFacts`, `Summary`, `Title`)
- Include `EventUpdates` (to get known facts)
- Do NOT include `Contradictions` (not needed for classification or contradiction detection)

This method is similar to `GetDetailAsync` but with a different include set -- `GetDetailAsync` includes contradictions with their articles (for the UI detail view), while `GetWithContextAsync` includes articles with their data (for AI context). They serve different purposes and keeping them separate follows the existing pattern of purpose-specific include sets (per `.claude/skills/ef-core-conventions/SKILL.md` section 4).

#### 2. Enrich event context in ClassifyIntoEventAsync

After `FindSimilarEventsAsync` returns candidates with similarity scores, and before passing events to any AI service (classifier, contradiction detector, or summary updater), load full context via `GetWithContextAsync`.

In the **auto-match path** (similarity >= `AutoSameEventThreshold`):
```
var enrichedEvent = await ctx.EventRepository.GetWithContextAsync(topEvent.Id, cancellationToken);
// Use enrichedEvent for contradiction detection and summary update
```

In the **grey-zone path** (similarity between thresholds):
```
var enrichedCandidates = new List<Event>();
foreach (var (evt, _) in similarEvents)
{
    var enriched = await ctx.EventRepository.GetWithContextAsync(evt.Id, cancellationToken);
    if (enriched != null) enrichedCandidates.Add(enriched);
}
// Use enrichedCandidates for classification
```

This is typically 1 query for auto-match and 1-3 queries for grey-zone -- acceptable overhead for correct classification.

#### 3. Improve the event classifier prompt

**`Infrastructure/AI/Prompts/event_classifier.txt`** -- rewrite to include:

a) Explicit update-vs-contradiction-vs-new-event guidance matching the feature requirements:
- Uncertainty resolved by later reporting = Update (NOT contradiction, NOT new event)
- Conflicting claims from different sources in the same timeframe = Contradiction
- Completely different incident in the same region = New event

b) Article-level context in the candidate events section. Update `ClaudeEventClassifier.cs` to include each candidate event's articles with their key facts and summaries in the user prompt:
```
CANDIDATE EVENT [1]:
Id: {e.Id}
Title: {e.Title}
Summary: {e.Summary}
Last Updated: {e.LastUpdatedAt}
Known Facts:
  [1] {factSummary1}
  [2] {factSummary2}
Articles in this event:
  - {article1.Title} | Key facts: {string.Join("; ", article1.KeyFacts)}
  - {article2.Title} | Key facts: {string.Join("; ", article2.KeyFacts)}
```

c) Explicit anti-patterns from the feature requirements:
- "Do NOT classify an article as a new event just because it uses different wording or has less detail than the existing event."
- "Do NOT classify an article as a new event just because early reporting was uncertain and later reporting provides concrete numbers."
- "Two articles about the same real-world incident MUST be in the same event, even if one source had incomplete information."

#### 4. Improve the contradiction detector prompt

**`Infrastructure/AI/Prompts/contradiction_detector.txt`** -- update to include the same update-vs-contradiction guidance. Update `ClaudeContradictionDetector.BuildUserPrompt` to include articles in the target event with their key facts:

```
TARGET EVENT:
Id: {targetEvent.Id}
Title: {targetEvent.Title}
Summary: {targetEvent.Summary}
Known Facts ({targetEvent.EventUpdates.Count}):
  [1] {factSummary1}
Articles in this event ({targetEvent.Articles.Count}):
  - [{article1.Id}] {article1.Title} | Key facts: {string.Join("; ", article1.KeyFacts)}
```

Add explicit guidance:
- "Differences in level of detail are NOT contradictions. 'Casualties being clarified' followed by '7 people injured' is an update, not a contradiction."
- "Contradictions require mutually exclusive claims from sources covering the same timeframe."

#### 5. Add update significance analysis to the auto-match path

Currently, the auto-match path skips `IsSignificantUpdate` / `NewFacts` analysis entirely. When an auto-matched article contains genuinely new facts, they should be recorded as `EventUpdate` entries (same as the grey-zone path does).

**Approach:** Reuse `IEventClassifier.ClassifyAsync` for auto-matched articles but with a single candidate. The classifier will return `IsNewEvent = false`, `MatchedEventId = the event's ID`, and crucially, `IsSignificantUpdate` and `NewFacts`.

This is NOT the same as Option 2 from the contradiction detection ADR (which rejected reusing the classifier for auto-match). The key difference: the contradiction detection ADR was concerned about token waste when the only goal was contradiction detection. Here, we need the classifier's full output -- update significance AND new facts -- which only the classifier provides. The contradiction detector (`IContradictionDetector`) handles contradictions separately. The classifier call handles the update analysis.

However, calling the full classifier for every auto-matched article adds cost. To mitigate:
- Only call the classifier when the article has key facts that are not obviously contained in the event summary (a simple string check).
- Add a configuration flag `AnalyzeAutoMatchUpdates` to `ArticleProcessingOptions` (default `true`), allowing this to be disabled if cost is a concern.

**Updated auto-match flow:**
```
1. Load enriched event via GetWithContextAsync
2. Run IContradictionDetector.DetectAsync -> contradictions
3. If AnalyzeAutoMatchUpdates is true:
   a. Run IEventClassifier.ClassifyAsync with single-element candidate list
   b. Use NewFacts and IsSignificantUpdate from result
   c. If significant update: save EventUpdate via TrySaveEventUpdateAsync
4. Determine role: Contradiction (if contradictions found) > Update (default)
5. Update event embedding and summary
```

#### 6. Configuration addition

**`Worker/Configuration/ArticleProcessingOptions.cs`** -- add:
```
public bool AnalyzeAutoMatchUpdates { get; set; } = true;
```

#### 7. No domain model changes

The `ArticleRole` enum (`Initiator`, `Update`, `Contradiction`) is already correct for the feature requirements. No new enum values are needed. The `EventClassificationResult` model already carries all needed fields. No migration is needed.

## Consequences

### Positive

1. **Correct classification data** -- AI models receive complete event context (articles with key facts, event updates), enabling accurate same-event detection and contradiction analysis.
2. **Fewer false new events** -- the improved prompt explicitly prevents splitting same-incident articles into different events due to wording differences or incomplete early reporting.
3. **Accurate contradiction detection** -- the contradiction detector can now compare against actual known facts and article key facts, not just an empty event shell.
4. **Significant updates tracked for auto-matched articles** -- new facts from high-similarity articles are now recorded as `EventUpdate` entries, improving event summary quality.
5. **No schema changes** -- all changes are in query includes, prompt text, and worker logic.

### Negative / Risks

1. **Additional database queries** -- `GetWithContextAsync` adds 1-3 extra queries per classification cycle. Acceptable for the current single-deployment architecture.
2. **Additional AI call for auto-match path** -- when `AnalyzeAutoMatchUpdates` is enabled, the classifier is called for auto-matched articles (adding latency and cost). Mitigated by the configuration flag.
3. **Larger AI prompts** -- including article key facts and event updates in prompts increases token usage. The increase is proportional to the number of articles in an event (typically 2-10).
4. **Prompt engineering risk** -- the improved prompts may need iterative tuning to achieve the desired classification accuracy. The feature requirements describe nuanced distinctions (update vs. contradiction vs. new event) that depend on prompt quality.

### Files Affected

**Core:**
- `Core/Interfaces/Repositories/IEventRepository.cs` -- add `GetWithContextAsync`

**Infrastructure/Persistence:**
- `Infrastructure/Persistence/Repositories/EventRepository.cs` -- implement `GetWithContextAsync` with `Include(Articles)` and `Include(EventUpdates)`

**Infrastructure/AI:**
- `Infrastructure/AI/ClaudeEventClassifier.cs` -- update user prompt to include article key facts, summaries, and event updates from candidate events
- `Infrastructure/AI/ClaudeContradictionDetector.cs` -- update `BuildUserPrompt` to include article key facts and event updates from the target event

**Infrastructure/AI/Prompts:**
- `Infrastructure/AI/Prompts/event_classifier.txt` -- rewrite with update-vs-contradiction guidance, anti-patterns, and enriched context instructions
- `Infrastructure/AI/Prompts/contradiction_detector.txt` -- add update-vs-contradiction guidance, enriched article context

**Worker:**
- `Worker/Workers/ArticleAnalysisWorker.cs` -- load enriched events via `GetWithContextAsync` before passing to AI services; add classifier call to auto-match path for update analysis; refactor `ClassifyIntoEventAsync` to use enriched events throughout
- `Worker/Configuration/ArticleProcessingOptions.cs` -- add `AnalyzeAutoMatchUpdates` option

## Implementation Notes

### For Feature-Planner

This change should be sequenced in three phases, each independently deployable:

**Phase 1 -- Repository and data flow (non-breaking):**
1. Add `GetWithContextAsync` to `IEventRepository` interface
2. Implement `GetWithContextAsync` in `EventRepository` with `Include(e => e.Articles)` and `Include(e => e.EventUpdates)`
3. Write repository tests verifying the includes load correctly

**Phase 2 -- Prompt improvements (non-breaking, incremental quality improvement):**
4. Rewrite `Infrastructure/AI/Prompts/event_classifier.txt` with update-vs-contradiction semantics, anti-patterns, and enriched context format
5. Update `ClaudeEventClassifier.cs` to build user prompt with article key facts, summaries, and event updates from candidate events
6. Rewrite `Infrastructure/AI/Prompts/contradiction_detector.txt` with update-vs-contradiction guidance
7. Update `ClaudeContradictionDetector.BuildUserPrompt` to include article key facts from the target event
8. Write/update tests for `ClaudeEventClassifier` and `ClaudeContradictionDetector` prompt construction

**Phase 3 -- Worker integration (behavioral change):**
9. Add `AnalyzeAutoMatchUpdates` to `ArticleProcessingOptions`
10. Update `ClassifyIntoEventAsync` in `ArticleAnalysisWorker`:
    a. After similarity search, load enriched events via `GetWithContextAsync` for candidates that will go to AI
    b. Pass enriched events to classifier and contradiction detector instead of the lightweight similarity results
    c. In auto-match path: add classifier call for update analysis (gated by `AnalyzeAutoMatchUpdates`)
    d. In auto-match path: save `EventUpdate` when significant update is detected
11. Write/update worker tests for both auto-match and grey-zone paths with enriched event data

**Phase 4 -- Configuration and deployment:**
12. Add `AnalyzeAutoMatchUpdates` setting to `Worker/appsettings.Development.json` under `ArticleProcessing` section
13. End-to-end verification: process test articles through the pipeline and verify correct event assignment and role classification

### Skills to Follow

- `.claude/skills/ef-core-conventions/SKILL.md` -- include patterns (section 4) for `GetWithContextAsync`, method naming (section 3)
- `.claude/skills/code-conventions/SKILL.md` -- worker architecture (scope resolution, AnalysisContext pattern), Options pattern for `AnalyzeAutoMatchUpdates`
- `.claude/skills/clean-code/SKILL.md` -- method length limits for the updated `ClassifyIntoEventAsync` (likely needs extraction of auto-match and grey-zone logic into separate private methods), naming conventions
- `.claude/skills/testing/SKILL.md` -- test patterns for repository includes, AI service prompt construction, worker classification paths
