# Event Classification and Article Role Assignment

## Goal

Fix the event classification pipeline so that AI services receive complete event context
(articles with key facts, event updates), prompts include explicit update-vs-contradiction
guidance, and the auto-match path records significant new facts as `EventUpdate` entries.

## Affected Layers

- Core
- Infrastructure
- Worker

## ADR Reference

`docs/architecture/decisions/event-classification-and-article-role-assignment.md`

---

## Tasks

---

### Phase 1 — Repository and data flow

#### Core

- [x] **Modify `Core/Interfaces/Repositories/IEventRepository.cs`** — add the method signature:
      `Task<Event?> GetWithContextAsync(Guid id, CancellationToken cancellationToken = default);`
      Insert it after `GetDetailAsync` to keep purpose-specific query methods grouped.
      _Acceptance: file compiles; no implementation references; the method appears in the interface_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

#### Infrastructure

- [x] **Modify `Infrastructure/Persistence/Repositories/EventRepository.cs`** — implement `GetWithContextAsync`:
      Query `_context.Events` using `FirstOrDefaultAsync(e => e.Id == id, cancellationToken)` with:
      - `Include(e => e.Articles).ThenInclude(a => a.KeyFacts)` — NOT available as a navigation on `ArticleEntity`; instead `Include(e => e.Articles)` suffices because `KeyFacts` is a primitive `List<string>` column on `ArticleEntity`, not a navigation property.
      - `Include(e => e.EventUpdates)`
      - Do NOT include `Contradictions`.
      Map with `entity?.ToDomain()` and return.
      _Acceptance: method satisfies `IEventRepository`; returns `null` for unknown id; returned `Event.Articles` are populated with their `KeyFacts`; `Event.EventUpdates` is populated; `Event.Contradictions` is empty_
      _Skill: .claude/skills/ef-core-conventions/SKILL.md_

- [ ] **Write repository tests in `Tests/Infrastructure.Tests/Repositories/EventRepositoryGetWithContextTests.cs`**
      Cover:
      - Returns `null` when event id does not exist
      - Returns event with `Articles` populated (including `KeyFacts`) and `EventUpdates` populated
      - Returns event where `Contradictions` is empty even when contradictions exist in the database
      _Acceptance: all three tests pass; uses EF Core InMemory or Sqlite provider (same pattern as existing repo tests); no live DB calls_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 2 — Prompt improvements

#### Infrastructure — event_classifier prompt and classifier

- [x] **Rewrite `Infrastructure/AI/Prompts/event_classifier.txt`** — replace the entire file content:
      Keep the existing JSON output schema and SIGNIFICANT UPDATE criteria unchanged.
      Add a new `UPDATE VS. CONTRADICTION VS. NEW EVENT` section before `CLASSIFICATION RULES` with:
      - Uncertainty in early reporting resolved by later reporting = Update, NOT contradiction, NOT new event
      - Conflicting claims from different sources covering the same timeframe = Contradiction
      - Completely different incident in the same region or topic = New event
      Add an `ANTI-PATTERNS` section with:
      - "Do NOT classify an article as a new event just because it uses different wording or has less detail."
      - "Do NOT classify an article as a new event because early reporting was uncertain and later reporting provides concrete numbers."
      - "Two articles about the same real-world incident MUST be in the same event, even if one source had incomplete information."
      Update the `CANDIDATE EVENTS` context format description to show articles with their key facts
      (the format is described in the prompt instructions, consumed by `ClaudeEventClassifier`).
      _Acceptance: file exists; contains the three anti-pattern lines verbatim; contains the update-vs-contradiction-vs-new-event guidance; JSON schema block is preserved intact_

- [x] **Modify `Infrastructure/AI/ClaudeEventClassifier.cs`** — update the `candidatesText` string builder in `ClassifyAsync` to include article context per candidate event:
      For each candidate event, after `Known Facts Count:`, add:
      ```
      Known Facts:
        [1] {factSummary1}
        ...
      Articles in this event:
        - {article.Title} | Key facts: {string.Join("; ", article.KeyFacts)}
        ...
      ```
      Replace the existing `Known Facts Count: {e.EventUpdates.Count}` line with the full facts enumeration and article list.
      _Acceptance: file compiles; when `candidateEvents` contains events with populated `Articles` and `EventUpdates`, the user prompt includes article titles, key facts, and fact summaries_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [ ] **Write tests for `ClaudeEventClassifier` prompt construction in `Tests/Infrastructure.Tests/AI/ClaudeEventClassifierTests.cs`** (new file)
      Use reflection to invoke the private prompt-building logic or extract it to an `internal` method, following the pattern in `ClaudeContradictionDetectorTests.cs`.
      Cover:
      - Candidate with `EventUpdates` and `Articles` produces a prompt containing the fact summaries and article key facts
      - Candidate with empty `Articles` and empty `EventUpdates` produces a prompt without crashing
      - `No candidate events found.` is emitted when `candidateEvents` is empty
      _Acceptance: all tests pass; no live HTTP calls_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

#### Infrastructure — contradiction_detector prompt and detector

- [x] **Rewrite `Infrastructure/AI/Prompts/contradiction_detector.txt`** — keep the existing detection criteria and JSON output schema unchanged.
      Add a new `UPDATE VS. CONTRADICTION` section before `CONTRADICTION criteria` with:
      - "Differences in level of detail are NOT contradictions. 'Casualties being clarified' followed by '7 people injured' is an update, not a contradiction."
      - "Contradictions require mutually exclusive claims from sources covering the same timeframe."
      Update the `TARGET EVENT` description in the prompt to note that articles with key facts will be provided.
      _Acceptance: file exists; the two update-vs-contradiction guidance lines are present; the existing `CONTRADICTION criteria` and output format are preserved_

- [x] **Modify `Infrastructure/AI/ClaudeContradictionDetector.cs`** — update `BuildUserPrompt` to include articles from the target event after the `Known Facts` block:
      ```
      Articles in this event ({targetEvent.Articles.Count}):
        - [{article.Id}] {article.Title} | Key facts: {string.Join("; ", article.KeyFacts)}
        ...
      ```
      When `targetEvent.Articles` is empty, emit `No articles recorded.` in place of the list.
      _Acceptance: file compiles; when `targetEvent.Articles` is populated, the returned prompt string includes article ids, titles, and key facts; existing `Known Facts` block is unchanged_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [ ] **Write tests for `ClaudeContradictionDetector.BuildUserPrompt` article context in `Tests/Infrastructure.Tests/AI/ClaudeContradictionDetectorTests.cs`** (modify existing file)
      Add new test cases:
      - When `targetEvent.Articles` contains articles with `KeyFacts`, the prompt includes each article's title and key facts
      - When `targetEvent.Articles` is empty, the prompt contains `No articles recorded.`
      _Acceptance: new test cases pass; existing tests remain green_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 3 — Worker integration

#### Worker — configuration

- [x] **Modify `Worker/Configuration/ArticleProcessingOptions.cs`** — add:
      `public bool AnalyzeAutoMatchUpdates { get; set; } = true;`
      Place it after `DeduplicationWindowHours` at the end of the class.
      _Acceptance: file compiles; property has default `true`; bound by `IOptions<ArticleProcessingOptions>` without error_
      _Skill: .claude/skills/code-conventions/SKILL.md_

#### Worker — ClassifyIntoEventAsync enrichment and auto-match update analysis

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — in the auto-match branch (`topSimilarity >= _options.AutoSameEventThreshold`), replace `targetEvent = topEvent` with a call to load the enriched event:
      ```csharp
      var enrichedEvent = await ctx.EventRepository.GetWithContextAsync(topEvent.Id, cancellationToken);
      targetEvent = enrichedEvent ?? topEvent;
      ```
      Pass `targetEvent` (which is now the enriched version) to `ContradictionDetector.DetectAsync` and `UpdateEventEmbeddingAsync`.
      _Acceptance: file compiles; auto-match path uses enriched event (with articles and event updates populated) for contradiction detection and embedding update; falls back to `topEvent` when `GetWithContextAsync` returns null_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — in the auto-match branch, after the existing contradiction detection block, add the update significance analysis gated by `_options.AnalyzeAutoMatchUpdates`:
      Before calling `ClassifyAsync`, apply a pre-filter string check: only proceed if at least one item in `article.KeyFacts` is NOT already contained in `targetEvent.Summary` (case-insensitive substring check). If every key fact is already present in the summary, skip the classifier call entirely.
      ```csharp
      if (_options.AnalyzeAutoMatchUpdates)
      {
          var hasNewKeyFact = article.KeyFacts.Any(fact =>
              !targetEvent.Summary.Contains(fact, StringComparison.OrdinalIgnoreCase));

          if (hasNewKeyFact)
          {
              var updateResult = await ctx.Classifier.ClassifyAsync(article, [targetEvent], cancellationToken);
              if (updateResult is { IsSignificantUpdate: true, NewFacts.Count: > 0 })
                  await TrySaveEventUpdateAsync(targetEvent, article, updateResult.NewFacts, ctx.EventRepository, cancellationToken);
          }
      }
      ```
      Place this block after `SaveContradictionsAsync` and before `UpdateEventEmbeddingAsync`.
      _Acceptance: file compiles; when `AnalyzeAutoMatchUpdates` is `true` and at least one key fact is absent from the event summary, `ClassifyAsync` is called; when all key facts are already present in the summary, `ClassifyAsync` is NOT called even if `AnalyzeAutoMatchUpdates` is `true`; when `AnalyzeAutoMatchUpdates` is `false`, no classifier call is made on the auto-match path_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/Workers/ArticleAnalysisWorker.cs`** — in the grey-zone branch, replace the `candidates` list construction with enriched event loading:
      ```csharp
      var enrichedCandidates = new List<Event>();
      foreach (var (evt, _) in similarEvents)
      {
          var enriched = await ctx.EventRepository.GetWithContextAsync(evt.Id, cancellationToken);
          if (enriched != null) enrichedCandidates.Add(enriched);
      }
      var candidates = enrichedCandidates.Count > 0 ? enrichedCandidates : similarEvents.Select(x => x.Event).ToList();
      ```
      Replace the existing `var candidates = similarEvents.Select(x => x.Event).ToList();` line.
      Pass `candidates` (now enriched) to `ctx.Classifier.ClassifyAsync`.
      When looking up the matched event (`candidates.First(e => e.Id == result.MatchedEventId)`), use the enriched `candidates` list so the matched event also carries full context.
      _Acceptance: file compiles; grey-zone path passes enriched events to the classifier; falls back to lightweight events if all `GetWithContextAsync` calls return null_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Write tests for the auto-match update-analysis path in `Tests/Worker.Tests/Workers/ArticleAnalysisWorkerAutoMatchContradictionTests.cs`** (modify existing file)
      Add to the existing `[TestFixture]` class:
      - Setup: mock `IEventRepository.GetWithContextAsync` to return a populated event (articles + event updates); set `AnalyzeAutoMatchUpdates = true`
      - Test: when classifier returns `IsSignificantUpdate = true` and `NewFacts` has items, `AddEventUpdateAsync` is called once
      - Test: when `AnalyzeAutoMatchUpdates = false`, `IEventClassifier.ClassifyAsync` is NOT called on the auto-match path
      - Test: when classifier returns `IsSignificantUpdate = false`, `AddEventUpdateAsync` is NOT called
      - Test: `ContradictionDetector.DetectAsync` is called with the enriched event (verify by checking the event passed has non-empty `Articles`)
      - Test: when all `article.KeyFacts` are already contained in `targetEvent.Summary` (case-insensitive), `IEventClassifier.ClassifyAsync` is NOT called even when `AnalyzeAutoMatchUpdates` is `true`
      - Test: when at least one `article.KeyFact` is absent from `targetEvent.Summary`, `IEventClassifier.ClassifyAsync` IS called
      _Acceptance: all new tests pass; existing tests in the file remain green_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Write tests for the grey-zone enriched-candidates path in `Tests/Worker.Tests/Workers/ArticleAnalysisWorkerGreyZoneEnrichedTests.cs`** (new file)
      New `[TestFixture]` class following the pattern in `ArticleAnalysisWorkerAutoMatchContradictionTests`:
      - Setup: `FindSimilarEventsAsync` returns one event with similarity in grey zone; `GetWithContextAsync` returns an enriched version of that event
      - Test: `IEventClassifier.ClassifyAsync` is called with the enriched candidate (verify by checking the candidate passed has non-empty `Articles`)
      - Test: when `GetWithContextAsync` returns `null` for all candidates, the classifier is still called (with the lightweight fallback candidates)
      _Acceptance: both tests pass; no live DB or HTTP calls_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 4 — Configuration and verification

- [x] **Modify `Worker/appsettings.Development.json`** — add `AnalyzeAutoMatchUpdates` to the `ArticleProcessing` section:
      ```json
      "ArticleProcessing": {
        ...
        "AnalyzeAutoMatchUpdates": true
      }
      ```
      _Acceptance: Worker host starts without binding errors; `ArticleProcessingOptions.AnalyzeAutoMatchUpdates` is `true` at runtime_

- [ ] **End-to-end verification** — manually run the Worker against a live RSS feed and process at least 2 real articles that cover the same real-world incident (e.g., two sources reporting the same news story):
      - Confirm no runtime exceptions are thrown during classification
      - Query the database (or inspect structured logs) to verify both articles land in the same event
      - Verify that the article role (`Update` vs. `Contradiction`) is assigned correctly based on whether the second article contradicts or extends the first
      - Verify that when a significant update is detected on the auto-match path, an `EventUpdate` row is created for that event
      _Acceptance: no runtime exceptions; at least 2 articles classified into events; event assignments are visible in logs or a DB query on the `Articles` table; `EventUpdates` table shows at least one new row when a significant update is processed_

---

## Open Questions

- None. The ADR fully specifies all design decisions including the fallback strategy, the configuration flag, and the ordering of operations within the auto-match branch.
