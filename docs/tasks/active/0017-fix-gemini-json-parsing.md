# Fix Gemini AI Response JSON Parsing

## Goal
Eliminate malformed-JSON failures from `GeminiArticleAnalyzer` by enabling Gemini's native JSON output mode and hardening the fallback repair pipeline in `GeminiJsonHelper`.

## Affected Layers
- Infrastructure

## Tasks

### Infrastructure

- [x] **Modify `Infrastructure/AI/GeminiArticleAnalyzer.cs`** — add `generationConfig = new { responseMimeType = "application/json" }` as a top-level property on the anonymous `requestBody` object serialized in `AnalyzeAsync`. The property must be placed at the same level as `contents`.
      _Acceptance: serialized request body contains `"generationConfig":{"responseMimeType":"application/json"}`; existing `contents` shape is unchanged; project builds._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/Prompts/analyzer.txt`** — remove the line `NEVER use double quote characters (") inside any string value. Use single quotes (') instead.` Verify the JSON example at the bottom of the file uses double quotes (it already does; no change needed there).
      _Acceptance: the word "single quotes" no longer appears anywhere in the file; all other instructions (CATEGORY values, SENTIMENT values, LANGUAGE codes, TAGS count, SUMMARY language, JSON structure example) are preserved verbatim._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/GeminiJsonHelper.cs` — add private static helper `IsJsonStructuralToken(char c)`** — returns `true` if `c` is one of `:`, `,`, `}`, `]`. Mirrors the inline check already used in `RepairUnescapedQuotes` (line 87).
      _Acceptance: method is `private static bool`; compiles; no duplicate logic with the existing inline check (the existing inline check in `RepairUnescapedQuotes` will be refactored to call this helper in the next task)._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/GeminiJsonHelper.cs` — refactor `RepairUnescapedQuotes` to call `IsJsonStructuralToken`** — replace the inline `json[j] is ':' or ',' or '}' or ']'` expression (line 87) with a call to `IsJsonStructuralToken(json[j])`.
      _Acceptance: existing `RepairUnescapedQuotes` tests still pass; no behavioural change._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/GeminiJsonHelper.cs` — redesign `NormalizeSingleQuotedJson`** — replace the current closing-quote detection logic with the lookahead heuristic: when `inString == true` and `stringChar == '\''` and the current character is `'`, look ahead past whitespace to the next non-whitespace character; if it satisfies `IsJsonStructuralToken` or is end-of-input, emit `"` and close the string; otherwise emit the `'` as a literal character (no escaping needed since the output string is double-quoted). The existing logic for escaping `"` characters found inside a single-quoted string (current lines 140-145) must be preserved. Escape sequences (`\\`) must still be passed through untouched.
      _Acceptance: all existing `NormalizeSingleQuotedJson_*` tests pass; the inner single quote in the `ParseAnalysisResult_RawInput_RepairsAndParses` test input (`'пологовим туризмом'`) is treated as a literal character, not a string terminator._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/GeminiJsonHelper.cs` — add `public static string RepairMissingBraces(string json)`** — count unmatched `{`/`}` characters in `json` (not inside strings; track string state with a boolean), then append the required number of `}` characters. If braces are balanced, return the string unchanged.
      _Acceptance: method is `public static string`; passing a string with one missing `}` appends exactly one `}`; passing a balanced string returns it unchanged; method compiles._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/AI/GeminiJsonHelper.cs` — insert `RepairMissingBraces` call into `ParseAnalysisResult` pipeline** — after the `NormalizeSingleQuotedJson` block (step 3 in the ADR) and before the `JsonSerializer.Deserialize` call (step 5), add: `json = RepairMissingBraces(json);`. The pipeline order must be exactly:
      1. Strip markdown fences + leading non-JSON text (existing).
      2. Replace `\'` with `'` (existing).
      3. If single quotes present → `NormalizeSingleQuotedJson` (existing guard, redesigned method).
      4. `RepairMissingBraces` (new call).
      5. `JsonSerializer.Deserialize` attempt (existing).
      6. On `JsonException` → `RepairUnescapedQuotes` + retry (existing).
      7. Validate required fields (existing).
      _Acceptance: `ParseAnalysisResult_RawInput_RepairsAndParses` passes end-to-end; existing happy-path tests are unaffected._
      _Skill: .claude/skills/clean-code/SKILL.md_

- [ ] **Modify `Tests/Infrastructure.Tests/AI/GeminiJsonHelperTests.cs` — add `RepairMissingBraces` unit tests** — add a `[TestFixture]` section with at least three `[Test]` methods covering: (a) balanced input returns unchanged string, (b) input missing one `}` appends exactly one `}`, (c) input missing two `}` appends exactly two `}`. Follow the AAA pattern and the project naming convention `MethodName_WhenCondition_ExpectedOutcome`.
      _Acceptance: all three new tests pass; no existing tests are modified._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Infrastructure.Tests/AI/GeminiJsonHelperTests.cs` — add `NormalizeSingleQuotedJson` inner-single-quote test** — add one `[Test]` named `NormalizeSingleQuotedJson_WhenValueContainsInnerSingleQuote_PreservesInnerQuote` that verifies a single-quoted string whose value contains a literal inner `'` (e.g., `{'key':'it's here'}`) is normalized to `{"key":"it's here"}` (inner `'` preserved as-is).
      _Acceptance: test passes with the redesigned `NormalizeSingleQuotedJson`._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

## Open Questions
- None
