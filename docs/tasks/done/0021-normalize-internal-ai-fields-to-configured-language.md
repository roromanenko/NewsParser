# Normalize Internal AI-Generated Fields to a Configured Language

## Goal
Replace all hardcoded "Ukrainian" language references in AI prompts and inline service
system prompts with a single `Ai.Normalization.TargetLanguageName` config value so that
changing one setting causes every internal AI-produced field (`Article.Summary`,
`Article.KeyFacts`, `Event.Title`, `Event.Summary`, `EventUpdate.FactSummary`,
`Contradiction.Description`) to be produced in the configured language.

## Affected Layers
- Infrastructure
- Worker (appsettings only)
- Api (appsettings only)

## Tasks

### Infrastructure — Configuration

- [x] **Modify `Infrastructure/Configuration/AiOptions.cs`** — add a new nested class
      `NormalizationOptions` with two string properties: `TargetLanguage` (default `"uk"`)
      and `TargetLanguageName` (default `"Ukrainian"`); add a `Normalization` property of
      type `NormalizationOptions` to `AiOptions`; remove `OutputLanguage` from
      `AnthropicOptions`.
      _Acceptance: `AiOptions.Normalization.TargetLanguageName` exists and compiles;
      `AnthropicOptions.OutputLanguage` no longer exists; no other property is altered;
      follows the same nested-class pattern as `GeminiOptions` and `AnthropicOptions`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Configuration/PromptsOptions.cs`** — convert `ReadPrompt`
      from a `private static string` method to a `private string` instance method;
      add a constructor parameter `string targetLanguageName` stored as a private field;
      after `File.ReadAllText`, apply
      `.Replace("{OUTPUT_LANGUAGE}", _targetLanguageName)` before returning; keep all
      existing computed properties (`Analyzer`, `Generator`, `Telegram`, `EventClassifier`,
      `EventSummaryUpdater`, `ContradictionDetector`) intact so call sites in
      `InfrastructureServiceExtensions` require no change beyond construction.
      _Acceptance: `PromptsOptions` compiles as an instance class; prompts that contain
      `{OUTPUT_LANGUAGE}` receive the substituted language name; prompts with no placeholder
      (e.g. `telegram.txt`) are returned unchanged; the class has no static state_
      _Skill: .claude/skills/code-conventions/SKILL.md_

### Infrastructure — Prompt Files

- [x] **Modify `Infrastructure/AI/Prompts/analyzer.txt`** — on line 19, replace
      `"in Ukrainian (uk)"` with `"in {OUTPUT_LANGUAGE}"`.
      _Acceptance: the file contains exactly one occurrence of `{OUTPUT_LANGUAGE}`;
      no literal "Ukrainian" remains anywhere in the file_

- [x] **Modify `Infrastructure/AI/Prompts/event_summary_updater.txt`** — on line 5,
      replace `"in Ukrainian (uk)"` with `"in {OUTPUT_LANGUAGE}"`.
      _Acceptance: the file contains exactly one occurrence of `{OUTPUT_LANGUAGE}`;
      no literal "Ukrainian" remains anywhere in the file_

- [x] **Modify `Infrastructure/AI/Prompts/event_classifier.txt`** — on line 7, replace
      `"in Ukrainian"` with `"in {OUTPUT_LANGUAGE}"`; add a new instruction rule (in the
      INSTRUCTIONS block) reading: `"Write all \"new_facts\" values in {OUTPUT_LANGUAGE},
      regardless of the article language."`.
      _Acceptance: the file contains exactly two occurrences of `{OUTPUT_LANGUAGE}` (one
      for contradiction descriptions, one for new_facts); no literal "Ukrainian" remains
      anywhere in the file_

- [x] **Modify `Infrastructure/AI/Prompts/contradiction_detector.txt`** — on line 5,
      replace `"in Ukrainian"` with `"in {OUTPUT_LANGUAGE}"`; on line 33, replace
      `"in Ukrainian"` with `"in {OUTPUT_LANGUAGE}"` (within the OUTPUT FORMAT section).
      _Acceptance: the file contains exactly two occurrences of `{OUTPUT_LANGUAGE}`; no
      literal "Ukrainian" remains anywhere in the file_

### Infrastructure — AI Services

- [x] **Modify `Infrastructure/AI/HaikuKeyFactsExtractor.cs`** — add `string
      targetLanguageName` as a third constructor parameter; store it in a private field;
      append `$"Respond in {targetLanguageName}, regardless of the input language."` to
      the system prompt string (this is the primary bug fix — key facts currently emit
      in the source article's language).
      _Acceptance: the constructor signature is `(string apiKey, string model, string
      targetLanguageName)`; the system prompt passed to Claude contains the configured
      language name, not the literal word "Ukrainian"; no magic string "Ukrainian" remains
      in this file_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/HaikuEventTitleGenerator.cs`** — replace the
      `private const string SystemPrompt` constant with a `private readonly string
      _systemPrompt` field; add `string targetLanguageName` as a new constructor parameter
      (before the existing `ILogger` parameter); set `_systemPrompt` by replacing the
      hardcoded `"Ukrainian-language"` phrase with the injected language name; update all
      internal usages of `SystemPrompt` to `_systemPrompt`.
      _Acceptance: the constructor signature is `(string apiKey, string model, string
      targetLanguageName, ILogger<HaikuEventTitleGenerator> logger)`; no literal
      "Ukrainian" remains in this file; the class compiles_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — in
      `AddAiServices`, after loading `aiOptions` (line 90), construct `PromptsOptions` with
      `new PromptsOptions(aiOptions.Normalization.TargetLanguageName)` instead of the
      current `configuration.GetSection(...).Get<PromptsOptions>()`; pass
      `aiOptions.Normalization.TargetLanguageName` as the third constructor argument to
      `HaikuKeyFactsExtractor` (after `apiKey` and `model`); pass the same value as the
      third constructor argument to `HaikuEventTitleGenerator` (after `apiKey` and `model`,
      before `ILogger`).
      _Acceptance: the solution compiles; `PromptsOptions` is no longer built from the
      config section directly; both Haiku constructors receive the language name string;
      no other DI registration is changed_
      _Skill: .claude/skills/code-conventions/SKILL.md_

### appsettings

- [x] **Modify `Worker/appsettings.Development.json`** — under the `"Ai"` key, add a
      `"Normalization"` object with `"TargetLanguage": "uk"` and
      `"TargetLanguageName": "Ukrainian"`; remove the `"OutputLanguage": "uk"` key from
      `"Ai"."Anthropic"`.
      _Acceptance: `jq '.Ai.Normalization'` on the file returns the new object;
      `jq '.Ai.Anthropic.OutputLanguage'` returns `null`; all other keys are unchanged_

- [x] **Modify `Api/appsettings.Development.json`** — add a top-level `"Ai"` section
      containing only `"Normalization": { "TargetLanguage": "uk", "TargetLanguageName":
      "Ukrainian" }` (the Api project does not run analysis but the schemas must stay
      aligned to prevent config drift).
      _Acceptance: the file parses as valid JSON; `jq '.Ai.Normalization'` returns the
      new object; no other key is added or modified_

### Tests

- [ ] **Add tests for `HaikuKeyFactsExtractor`** — in the appropriate test project,
      create `HaikuKeyFactsExtractorTests.cs`; write a test that constructs the extractor
      with `targetLanguageName = "English"` and asserts the system prompt sent to the AI
      client contains `"English"` and does NOT contain `"Ukrainian"`; follow the AAA
      pattern and the project naming convention per the testing skill.
      _Acceptance: test passes; no literal "Ukrainian" can survive in the prompt when
      a different language is configured_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_
      _Delegated to test-writer agent_

- [ ] **Add tests for `HaikuEventTitleGenerator`** — in the appropriate test project,
      create `HaikuEventTitleGeneratorTests.cs`; write a test that constructs the generator
      with `targetLanguageName = "English"` and asserts the system prompt contains
      `"English"` and does NOT contain `"Ukrainian"`.
      _Acceptance: test passes; language name in the system prompt reflects constructor
      injection, not a hardcoded constant_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_
      _Delegated to test-writer agent_

- [ ] **Add a test for `PromptsOptions` placeholder substitution** — write a test that
      constructs `PromptsOptions` with `targetLanguageName = "English"`, reads a prompt
      that contains `{OUTPUT_LANGUAGE}`, and asserts the returned string contains
      `"English"` with no remaining `{OUTPUT_LANGUAGE}` token.
      _Acceptance: test passes; substitution is confirmed to be applied at read time;
      a prompt without the placeholder is returned unchanged_
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_
      _Delegated to test-writer agent_

## Open Questions
- None — the ADR fully specifies the chosen option, all affected files, the config shape,
  the constructor signature changes, and the sequencing order.
