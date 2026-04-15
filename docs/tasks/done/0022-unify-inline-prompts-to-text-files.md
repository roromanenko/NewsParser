# Unify Inline Prompts to Text Files

## Goal

Move the two remaining inline AI system prompts (in `HaikuKeyFactsExtractor` and
`HaikuEventTitleGenerator`) into `Infrastructure/AI/Prompts/*.txt` files and wire them
through `PromptsOptions`, so all eight AI services consume prompts through the same
uniform mechanism.

## Affected Layers

- Infrastructure (prompt files, `PromptsOptions`, two AI service classes, DI extensions)
- Tests (Infrastructure.Tests — two test fixtures)

## Tasks

### Infrastructure — New Prompt Files

- [x] **Create `Infrastructure/AI/Prompts/haiku_key_facts.txt`**

  Transcribe the inline system prompt currently found at
  `Infrastructure/AI/HaikuKeyFactsExtractor.cs` lines 26–30 verbatim into this file.
  Replace the interpolated `{_targetLanguageName}` fragment with the literal token
  `{OUTPUT_LANGUAGE}`.

  Current inline text for reference (implementer must read the source file and copy
  faithfully — do not rewrite semantics):

  > "You are a factual extraction assistant. Extract 3 to 7 short, discrete factual
  > statements from the article. Each fact must be a single sentence.
  > Respond with a JSON object: {"facts": ["fact1", "fact2", ...]}.
  > Respond in {OUTPUT_LANGUAGE}, regardless of the input language."

  _Acceptance: file exists under `Infrastructure/AI/Prompts/`; contains the
  `{OUTPUT_LANGUAGE}` placeholder; contains no C# interpolation syntax; wording is
  identical to the source except for the placeholder substitution._
  _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Create `Infrastructure/AI/Prompts/haiku_event_title.txt`**

  Transcribe the inline system prompt currently built at
  `Infrastructure/AI/HaikuEventTitleGenerator.cs` lines 23–27 verbatim into this file.
  Replace the interpolated `{targetLanguageName}-language` fragment so the sentence reads
  `{OUTPUT_LANGUAGE}-language` (or equivalent natural phrasing using the placeholder).

  Current inline text for reference (implementer must read the source file and copy
  faithfully — do not rewrite semantics):

  > "You are a news headline writer. Generate a concise {OUTPUT_LANGUAGE}-language news
  > headline. Rules: maximum 15 words; no quotes; no trailing punctuation; no prefixes
  > like "Breaking:"; factual and neutral. Respond with the headline text only — no extra
  > formatting."

  _Acceptance: file exists under `Infrastructure/AI/Prompts/`; contains the
  `{OUTPUT_LANGUAGE}` placeholder; wording is identical to the source except for the
  placeholder substitution._
  _Skill: .claude/skills/code-conventions/SKILL.md_

### Infrastructure — PromptsOptions

- [x] **Modify `Infrastructure/Configuration/PromptsOptions.cs`** — add two path
  constants and two computed properties following the same pattern as the existing
  `AnalyzerPath` / `Analyzer` pair:

  - `public string HaikuKeyFactsPath { get; set; } = "Prompts/haiku_key_facts.txt";`
  - `public string HaikuEventTitlePath { get; set; } = "Prompts/haiku_event_title.txt";`
  - `public string HaikuKeyFacts => ReadPrompt(HaikuKeyFactsPath);`
  - `public string HaikuEventTitle => ReadPrompt(HaikuEventTitlePath);`

  No other lines in the file change.

  _Acceptance: file compiles; calling `HaikuKeyFacts` returns the content of
  `haiku_key_facts.txt` with `{OUTPUT_LANGUAGE}` replaced by the configured language
  name (the existing `ReadPrompt` already performs this substitution)._
  _Skill: .claude/skills/code-conventions/SKILL.md_

### Infrastructure — AI Service Classes

- [x] **Modify `Infrastructure/AI/HaikuKeyFactsExtractor.cs`** — change the constructor
  signature and remove the language-interpolation logic:

  1. Rename the parameter `string targetLanguageName` → `string systemPrompt`.
  2. Replace the `private readonly string _targetLanguageName;` field with
     `private readonly string _systemPrompt;`.
  3. Store the parameter directly: `_systemPrompt = systemPrompt;`.
  4. In `ExtractAsync`, replace the multi-line local `var systemPrompt = ...;`
     interpolation block with `var systemPrompt = _systemPrompt;` (or inline `_systemPrompt`
     directly into the `SystemMessage` constructor — whichever is cleaner).
  5. Remove any remaining reference to `_targetLanguageName`.

  The `ParseFacts` private static method and all other logic remain unchanged.

  _Acceptance: file compiles with no reference to `_targetLanguageName` or
  `targetLanguageName`; the system prompt passed to `AnthropicClient` is the value of
  `_systemPrompt` with no further string interpolation._
  _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/HaikuEventTitleGenerator.cs`** — change the constructor
  signature and remove the prompt-building logic:

  1. Rename the parameter `string targetLanguageName` → `string systemPrompt`.
  2. Replace the multi-statement `_systemPrompt = $"..."` interpolation block with direct
     assignment: `_systemPrompt = systemPrompt;`.
  3. Remove any remaining reference to `targetLanguageName`.

  The `_client`, `_model`, `_logger`, `GenerateTitleAsync`, and `BuildUserMessage` members
  remain unchanged.

  _Acceptance: file compiles with no reference to `targetLanguageName`; the
  `_systemPrompt` field holds exactly the value passed into the constructor._
  _Skill: .claude/skills/clean-code/SKILL.md_

### Infrastructure — DI Wiring

- [x] **Modify `Infrastructure/Extensions/InfrastructureServiceExtensions.cs`** — update
  the two `AddAiServices` registrations that currently pass
  `aiOptions.Normalization.TargetLanguageName`:

  1. `IKeyFactsExtractor` registration (lines 117–121): replace the third argument
     `aiOptions.Normalization.TargetLanguageName` with `promptsOptions.HaikuKeyFacts`.
  2. `IEventTitleGenerator` registration (lines 123–128): replace the third argument
     `aiOptions.Normalization.TargetLanguageName` with `promptsOptions.HaikuEventTitle`.

  No other lines in `AddAiServices` change. Verify that `promptsOptions` is already
  constructed before these two registrations (it is, at line 91 — no ordering change
  needed).

  _Acceptance: file compiles; the two Haiku constructors no longer receive a raw language
  name; they receive the fully substituted prompt string from `PromptsOptions`._
  _Skill: .claude/skills/code-conventions/SKILL.md_

### Tests — HaikuKeyFactsExtractor

- [x] **Modify `Tests/Infrastructure.Tests/AI/HaikuKeyFactsExtractorTests.cs`** — update
  the two tests and the reflection target that are broken by the constructor change:

  1. Remove the `TargetLanguageNameField` reflection field that targets
     `_targetLanguageName` (it no longer exists on the class).
  2. Rewrite `Constructor_WhenTargetLanguageNameIsEnglish_StoresEnglishAndNotUkrainian`:
     - Pass a test `systemPrompt` string that contains the word "English" but not
       "Ukrainian" directly as the third constructor argument (no reflection needed —
       the injected value is the prompt itself).
     - Reflect on `_systemPrompt` instead, OR assert that constructing with a
       known-language-containing prompt string does not throw and stores the value.
     - **Implementer note:** evaluate whether this test is still meaningful at the
       class level now that substitution happens in `PromptsOptions.ReadPrompt`. If the
       test only duplicates what a `PromptsOptions` test would cover, leave a comment
       explaining that and keep the test as a smoke-test that `_systemPrompt` holds the
       passed-in value (assert `_systemPrompt` contains the passed-in text via reflection
       on the new field name).
  3. Update `ExtractAsync_WhenCancellationTokenAlreadyCancelled_ThrowsOperationCanceledException`:
     - Change the constructor call from `targetLanguageName: "Ukrainian"` to
       `systemPrompt: "You are a factual extraction assistant."` (or any non-empty string).
  4. All other tests (`ParseFacts_*`) do not touch the constructor — no change required.

  _Acceptance: all non-`[Explicit]` tests compile and pass; no reflection targets a field
  that no longer exists._
  _Agent: test-writer_
  _Skill: .claude/skills/testing/SKILL.md_

- [x] **Modify `Tests/Infrastructure.Tests/AI/HaikuEventTitleGeneratorTests.cs`** — update
  the three tests broken by the constructor signature change:

  1. `Constructor_WithArbitraryApiKeyAndModel_DoesNotThrow`: rename parameter label
     `targetLanguageName:` → `systemPrompt:` (keep the value `"Ukrainian"` or use a
     realistic prompt fragment).
  2. `Constructor_WithEmptyStrings_DoesNotThrow`: rename parameter label
     `targetLanguageName:` → `systemPrompt:`.
  3. `Constructor_WhenTargetLanguageNameIsEnglish_BuildsSystemPromptWithEnglishAndWithoutUkrainian`:
     - This test previously verified that `targetLanguageName` was interpolated into the
       prompt. After the change the class no longer interpolates — the passed-in string
       IS the prompt.
     - Rewrite the test to pass a `systemPrompt` that contains "English" but not
       "Ukrainian"; assert that `_systemPrompt` (reflected) contains "English" and does
       not contain "Ukrainian". The reflection target field name (`_systemPrompt`) is
       unchanged, so `SystemPromptField` still works.
     - **Implementer note:** same evaluation as for `HaikuKeyFactsExtractor` — consider
       adding a comment that the language-substitution boundary has moved to
       `PromptsOptions`; this test becomes a pass-through smoke-test.
  4. `GenerateTitleAsync_WhenCancellationTokenAlreadyCancelled_ThrowsOperationCanceledException`:
     rename parameter label `targetLanguageName:` → `systemPrompt:`.

  _Acceptance: all non-`[Explicit]` tests compile and pass; `SystemPromptField`
  reflection still resolves (field name is unchanged)._
  _Agent: test-writer_
  _Skill: .claude/skills/testing/SKILL.md_

### Verification

- [x] **Run `dotnet build`** from the solution root — confirm zero errors and zero
  warnings introduced by this change.

  _Acceptance: build exits with code 0._

- [x] **Run `dotnet test Tests/Infrastructure.Tests/`** — confirm all non-`[Explicit]`
  tests pass.

  _Acceptance: test run exits with code 0; no test regressions._

## Open Questions

- None. The ADR follow-up section fully specifies the scope and approach.
