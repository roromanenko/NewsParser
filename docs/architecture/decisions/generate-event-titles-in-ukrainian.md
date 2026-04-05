# Generate Event Titles in Ukrainian

## Context

Event titles are currently set once at creation time in `ArticleAnalysisWorker.CreateNewEventAsync` (line 332) by copying the article's title verbatim:

```csharp
Title = article.Title,
```

This has two problems:

1. **Language mismatch.** Articles come from sources in various languages (English, Russian, Ukrainian, etc.). The event title inherits whatever language the first article uses. Event summaries are already generated in Ukrainian (the `event_summary_updater.txt` prompt explicitly says "Write the summary in Ukrainian (uk)"), but titles are not.

2. **No title regeneration on update.** When new articles are added to an existing event (via auto-match or grey-zone classification), the event summary is updated through `IEventSummaryUpdater`, but the title stays as the original article's title forever. As the event evolves with new facts and perspectives, the title may no longer accurately represent the event.

The desired behavior:
- When a new event is created, generate a Ukrainian-language title using AI.
- When an existing event receives a significant update (new facts added), regenerate the title in Ukrainian to reflect the updated understanding.

### Relationship to existing ADRs

- `event-classification-and-article-role-assignment.md` established the auto-match/grey-zone/new-event flow and added update significance analysis. Title generation hooks into the same points where updates are detected.
- `event-based-publication-with-key-facts.md` established the `IEventSummaryUpdater` pattern for AI-driven event summary maintenance. Title generation follows a similar pattern.

## Options

### Option 1 -- Extend IEventSummaryUpdater to also return a title

Change `IEventSummaryUpdater.UpdateSummaryAsync` to return both a summary and a title. Update the prompt to produce `{ "updated_summary": "...", "updated_title": "..." }`. On event creation, call the summary updater with the initial article's summary as the "new fact" to get both a Ukrainian summary and title.

**Pros:** No new interface or AI service class. Single AI call produces both summary and title. Consistent -- title and summary always update together.
**Cons:** Changes the existing `IEventSummaryUpdater` interface signature (breaking change to all callers). Violates SRP -- the summary updater now also generates titles. On event creation there is no "existing summary" to update, so the prompt semantics are awkward.

### Option 2 -- Add title generation to the event classifier response

Extend `EventClassificationResult` to include a `Title` field. The classifier prompt already knows the article and candidate events; add an instruction to generate a Ukrainian title. For new events, use the returned title. For matched events, use it as the updated title.

**Pros:** No new AI call -- title comes "for free" with classification. Classifier has full context (article + candidate events) to generate a good title.
**Cons:** Increases classifier prompt complexity and output size. Mixes classification (analytical) with generation (creative) concerns in one prompt. The classifier is already a complex prompt; adding title generation may degrade classification quality. For the auto-match path that skips the full classifier (only runs when `AnalyzeAutoMatchUpdates` is true), there is no classifier call to piggyback on.

### Option 3 -- New IEventTitleGenerator interface with a dedicated prompt

Create a new `IEventTitleGenerator` interface in `Core/Interfaces/AI/` with a single method. Implement it as a lightweight AI call (cheap model) that takes the event summary and returns a Ukrainian title. Call it after event creation and after summary updates.

**Pros:** Clean SRP -- one interface, one responsibility. Can use a cheap/fast model since title generation is simple. Does not change any existing interfaces. Works for both creation and update paths. Prompt can be tuned independently.
**Cons:** Additional AI call per event creation and per significant update. New interface + implementation + prompt file to maintain.

## Decision

**Option 3 -- New IEventTitleGenerator interface with a dedicated prompt.**

This approach follows the established pattern in the codebase: each AI responsibility has its own interface (`IArticleAnalyzer`, `IKeyFactsExtractor`, `IEventClassifier`, `IEventSummaryUpdater`, `IContradictionDetector`). Adding `IEventTitleGenerator` is consistent with this pattern. The task is simple enough for a cheap model (e.g., Haiku), keeping cost low.

### Detailed Design

#### 1. New interface: IEventTitleGenerator

**`Core/Interfaces/AI/IEventTitleGenerator.cs`:**
```csharp
public interface IEventTitleGenerator
{
    Task<string> GenerateTitleAsync(
        string eventSummary,
        List<string> articleTitles,
        CancellationToken cancellationToken = default);
}
```

The method takes the event summary (which is already in Ukrainian) and the titles of articles in the event (for context). It returns a single Ukrainian-language title string.

#### 2. New implementation: HaikuEventTitleGenerator

**`Infrastructure/AI/HaikuEventTitleGenerator.cs`** -- follows the same constructor pattern as `HaikuKeyFactsExtractor`: takes `apiKey` and `model` parameters, no prompt file (inline system message, since the prompt is very short).

If the prompt grows beyond a few lines during implementation, extract it to `Infrastructure/AI/Prompts/event_title_generator.txt` and add `EventTitleGeneratorPath` to `PromptsOptions`. Start inline; extract if needed.

#### 3. New prompt

The system prompt instructs the model to:
- Generate a concise news headline in Ukrainian (uk).
- Base it on the provided event summary and article titles.
- Maximum 15 words.
- No quotes, no punctuation at the end, no "Breaking:" or similar prefixes.
- Must be factual and neutral.

#### 4. DI registration

**`Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** in `AddAiServices` -- add:
```csharp
services.AddScoped<IEventTitleGenerator>(_ => new HaikuEventTitleGenerator(
    aiOptions.Anthropic.ApiKey,
    aiOptions.Anthropic.TitleGeneratorModel
));
```

Add `TitleGeneratorModel` to the Anthropic section of `AiOptions` (or reuse the existing `KeyFactsExtractorModel` since both are cheap model tasks -- follow whatever the implementer decides based on cost).

#### 5. Hook into event creation

In `ArticleAnalysisWorker.CreateNewEventAsync`, after creating the event with a placeholder title (`article.Title`), generate the proper Ukrainian title and update it:

```
1. Create event with Title = article.Title (as fallback)
2. Generate Ukrainian title via IEventTitleGenerator
3. Update event title in DB via a new repository method
```

Alternatively, generate the title before creating the event and pass it directly. This is simpler and avoids an extra DB round-trip. The summary is already available (`article.Summary`), and the article titles list is just `[article.Title]` for a new event.

#### 6. Hook into event update (summary changes)

In `ArticleAnalysisWorker.UpdateEventEmbeddingAsync`, after the summary is updated via `IEventSummaryUpdater`, regenerate the title using the new summary. This requires:

- Adding a `title` parameter to `UpdateSummaryAndEmbeddingAsync` (rename to `UpdateSummaryTitleAndEmbeddingAsync`) OR adding a separate `UpdateTitleAsync` method to `IEventRepository`.

The cleaner approach is to extend `UpdateSummaryAndEmbeddingAsync` to also accept a title, since summary and title always update together. This avoids an extra DB round-trip.

#### 7. Repository changes

**`Core/Interfaces/Repositories/IEventRepository.cs`** -- modify:
```
Task UpdateSummaryTitleAndEmbeddingAsync(
    Guid id,
    string title,
    string summary,
    float[] embedding,
    CancellationToken cancellationToken = default);
```

**`Infrastructure/Persistence/Repositories/EventRepository.cs`** -- update the implementation to add `.SetProperty(e => e.Title, title)` to the existing `ExecuteUpdateAsync` chain.

Rename the old method rather than adding a new one, since every caller should now pass a title. Update all call sites (`ArticleAnalysisWorker.UpdateEventEmbeddingAsync`, `EventService.MergeAsync`).

#### 8. Worker changes

Add `IEventTitleGenerator` to the `AnalysisContext` record in `ArticleAnalysisWorker`. Resolve it from the scope in `ProcessAsync`.

**CreateNewEventAsync** -- generate title before creating the event:
```
var title = await titleGenerator.GenerateTitleAsync(article.Summary, [article.Title], ct);
var newEvent = new Event { Title = title, ... };
```

**UpdateEventEmbeddingAsync** -- after getting `updatedSummary` from the summary updater, generate a new title:
```
var updatedTitle = await titleGenerator.GenerateTitleAsync(updatedSummary, articleTitles, ct);
await eventRepository.UpdateSummaryTitleAndEmbeddingAsync(evt.Id, updatedTitle, updatedSummary, ...);
```

For the article titles parameter, collect titles from `evt.Articles` (which are loaded via `GetWithContextAsync` in the enriched event paths).

**Fallback on failure** -- wrap the title generation in a try/catch. If it fails, use the article title (for creation) or the existing event title (for update). Title generation failure should not block the pipeline.

#### 9. EventService.MergeAsync update

`EventService.MergeAsync` also calls `UpdateSummaryAndEmbeddingAsync`. After the rename, it must also generate a title for the merged event. Add `IEventTitleGenerator` to the `EventService` constructor and call it after summary update.

## Implementation Notes

### For Feature-Planner

Sequence the work in this order:

1. **Core interface** -- add `IEventTitleGenerator` to `Core/Interfaces/AI/`
2. **Infrastructure AI** -- implement `HaikuEventTitleGenerator` in `Infrastructure/AI/`
3. **Repository change** -- rename `UpdateSummaryAndEmbeddingAsync` to `UpdateSummaryTitleAndEmbeddingAsync`, add `title` parameter to interface and implementation
4. **DI registration** -- register `IEventTitleGenerator` in `InfrastructureServiceExtensions.AddAiServices`; add model config to `AiOptions`
5. **Worker integration** -- add `IEventTitleGenerator` to `AnalysisContext`, update `CreateNewEventAsync` and `UpdateEventEmbeddingAsync`
6. **EventService update** -- inject `IEventTitleGenerator` into `EventService`, update `MergeAsync`
7. **Tests** -- unit tests for `HaikuEventTitleGenerator`, worker tests for title generation on creation and update paths

### Skills to Follow

- `.claude/skills/code-conventions/SKILL.md` -- worker architecture (AnalysisContext record pattern), Options pattern for model config, interface organization in `Core/Interfaces/AI/`
- `.claude/skills/clean-code/SKILL.md` -- method length limits (title generation should be a separate private method in the worker, not inlined), naming conventions
- `.claude/skills/ef-core-conventions/SKILL.md` -- `ExecuteUpdateAsync` with `SetProperty` for the renamed repository method, method naming conventions
- `.claude/skills/mappers/SKILL.md` -- no mapper changes expected, but verify if `EventMapper.ToDomain` / `ToEntity` need `Title` handling adjustments (they should already map Title correctly)
