# Fix Gemini AI Response JSON Parsing

## Context

`GeminiArticleAnalyzer` sends a prompt to Gemini and parses the response as JSON via `GeminiJsonHelper.ParseAnalysisResult`. Real-world Gemini responses are frequently malformed in ways the current parser cannot handle:

1. **Single-quoted JSON** instead of standard double-quoted JSON.
2. **Missing closing braces** -- the response is truncated.
3. **Inner single quotes within single-quoted strings** -- e.g., a single-quoted summary value contains `'пологовим туризмом'` where the inner `'` is not an escape but a quotation mark in the text. The current `NormalizeSingleQuotedJson` cannot distinguish a closing `'` from an inner `'`.
4. **Backslash-escaped single quotes** (`\'`) which are invalid in standard JSON.

The failing test `ParseAnalysisResult_RawInput_RepairsAndParses` demonstrates all four issues simultaneously.

The root cause has two layers:
- **Prompt weakness:** The prompt in `analyzer.txt` tells Gemini to use single quotes inside values but does not request Gemini's native JSON output mode (`responseMimeType: "application/json"`), which would eliminate most malformation at the source.
- **Parser weakness:** `NormalizeSingleQuotedJson` uses a simple state machine that treats every `'` as either a string opener or closer. It has no heuristic to determine whether a `'` is an inner character within an already-open string.

## Options

### Option 1 -- Enable Gemini JSON Mode + Improve Parser as Fallback

Enable Gemini's `responseMimeType: "application/json"` in the API request body via `generationConfig`. This instructs the model to return structurally valid JSON (double-quoted, balanced braces). Keep and improve `GeminiJsonHelper` as a defense-in-depth fallback for edge cases.

**Pros:**
- Eliminates single-quote JSON at the source in the vast majority of cases.
- Reduces the complexity burden on the parser -- it only needs to handle rare edge cases.
- Gemini JSON mode also enforces balanced braces and proper escaping.
- No additional API cost or latency.

**Cons:**
- Still need a fallback parser because no LLM output mode is 100% reliable.
- Minor change to `GeminiArticleAnalyzer` request body construction.

### Option 2 -- Parser-Only Fix (No Prompt/API Changes)

Redesign `NormalizeSingleQuotedJson` to use context-aware heuristics to distinguish closing quotes from inner quotes. No changes to the prompt or API request.

**Pros:**
- Contained to one file (`GeminiJsonHelper.cs`).

**Cons:**
- Fundamentally ambiguous: a `'` inside a single-quoted string cannot be reliably classified as inner vs. closing without domain-specific heuristics that will break on future inputs.
- Does not address the root cause -- Gemini keeps producing malformed JSON.
- The heuristics grow increasingly complex and fragile over time.

## Decision

**Option 1 -- Enable Gemini JSON Mode + Improve Parser as Fallback.** This is a defense-in-depth approach: fix the source (API request) and harden the fallback (parser).

### 1. Enable Gemini JSON Mode in `GeminiArticleAnalyzer`

Add `generationConfig` with `responseMimeType: "application/json"` to the request body sent to the Gemini API. This is a one-field addition to the anonymous object in `GeminiArticleAnalyzer.AnalyzeAsync`:

```
generationConfig = new { responseMimeType = "application/json" }
```

This tells Gemini to constrain its output to valid JSON, eliminating single-quote usage, unbalanced braces, and markdown fencing in the response.

### 2. Simplify the Prompt in `analyzer.txt`

Remove the instruction `NEVER use double quote characters (") inside any string value. Use single quotes (') instead.` -- this instruction is the reason Gemini returns single-quoted JSON and uses inner single quotes in text. With JSON mode enabled, the model will produce valid double-quoted JSON natively.

Keep the structural instructions (category values, sentiment values, language codes, tag count, JSON shape example) but ensure the example uses double quotes.

### 3. Redesign `NormalizeSingleQuotedJson` with Lookahead Heuristics

For the fallback path (when JSON mode fails or older cached responses are reprocessed), redesign the method using this strategy:

**Key insight:** When a `'` is encountered inside a single-quoted string, it is a **closing quote** only if the next non-whitespace character is a JSON structural token (`:`, `,`, `}`, `]`). Otherwise, it is an inner character and should be preserved as-is (escaped to `\"` in the double-quoted output, or left as a literal single quote character).

This is the same heuristic already used successfully in `RepairUnescapedQuotes` for double-quote disambiguation. Apply the identical pattern to single-quote disambiguation:

- When `inString == true` and `stringChar == '\''` and the current char is `'`:
  - Look ahead past whitespace to the next non-whitespace character.
  - If it is `:`, `,`, `}`, `]`, or end-of-input: this `'` closes the string. Emit `"`.
  - Otherwise: this `'` is an inner character. Emit the character without closing the string. Since the output string is double-quoted, no escaping is needed for a literal `'`.

### 4. Add Missing Brace Repair

After all quote normalization, before deserialization, count unmatched `{` and `}`. If there are more opening braces than closing braces, append the missing `}` characters. This handles truncated responses.

### 5. Pipeline Order in `ParseAnalysisResult`

The repair steps must execute in this order:
1. Strip markdown fences and leading non-JSON text (existing).
2. Replace `\'` with `'` (existing).
3. If single quotes are present, run `NormalizeSingleQuotedJson` (redesigned).
4. Repair missing closing braces.
5. Attempt `JsonSerializer.Deserialize`.
6. On `JsonException`, run `RepairUnescapedQuotes` and retry (existing fallback).
7. Validate required fields (existing).

## Implementation Notes

**Files changed:**
- `Infrastructure/AI/GeminiArticleAnalyzer.cs` -- add `generationConfig` to request body (one field).
- `Infrastructure/AI/Prompts/analyzer.txt` -- remove the single-quote instruction, keep everything else.
- `Infrastructure/AI/GeminiJsonHelper.cs` -- redesign `NormalizeSingleQuotedJson` with lookahead heuristic; add brace-balancing step to `ParseAnalysisResult`.
- `Tests/Infrastructure.Tests/AI/GeminiJsonHelperTests.cs` -- the existing failing test should pass; add test cases for inner single quotes with lookahead, missing closing braces, and mixed scenarios.

**Skills to follow:**
- `.claude/skills/code-conventions/SKILL.md` -- placement rules (all changes stay in `Infrastructure/AI/`), no magic numbers.
- `.claude/skills/clean-code/SKILL.md` -- method length (each repair step should be its own private/static method), naming (e.g., `RepairMissingBraces`, `IsJsonStructuralToken`), no dead code.
- `.claude/skills/testing/SKILL.md` -- test naming, AAA pattern, parameterized tests for the various malformation types.

**Key constraint:** The lookahead heuristic in `NormalizeSingleQuotedJson` must mirror the pattern already established in `RepairUnescapedQuotes` (lines 84-88 of the current `GeminiJsonHelper.cs`) for consistency. Do not introduce a regex-based approach or a third-party JSON repair library.
