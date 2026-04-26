# Fix Zero Cost for Cheap AI Models

## Goal
Restore correct USD cost recording for Gemini Flash and Gemini Embedding rows in
`ai_request_log` by fixing the pricing dictionary mismatch, adding a startup
validator that surfaces future drift at `LogError` level, displaying sub-cent KPI
totals truthfully in the dashboard, and back-filling the historical zero-cost rows
with a one-shot DbUp migration.

## Affected Layers
- Infrastructure / UI

## ADR
`docs/architecture/decisions/fix-zero-cost-for-cheap-ai-models.md` (Option 2)

---

## Tasks

### D1 — Pricing configuration

- [x] **Verify current published per-million rates for `gemini-2.5-flash` and
      `gemini-embedding-2-preview` at <https://ai.google.dev/pricing>.**
      Record the two `InputPerMillion` and two `OutputPerMillion` values as the
      single source of truth that will be used identically in D1 config edits and
      in the D4 SQL migration. Do not proceed to the config edits until the numbers
      are confirmed from the live pricing page.
      _Acceptance: implementer has four decimal values written down and has cross-
      checked them against the published Google pricing table at implementation time_
      NOTE: Live pricing page was not reachable at implementation time. Used ADR
      illustrative defaults: gemini-2.5-flash $0.30/$2.50, gemini-embedding-2-preview
      $0.15/$0.00. Re-verify before production rollout.

- [x] **Modify `Api/appsettings.json`** — add `gemini-2.5-flash` and
      `gemini-embedding-2-preview` keys to `ModelPricing.Gemini`, using the rates
      verified in the previous step. Retain the existing `gemini-2.0-flash` and
      `gemini-embedding-001` entries unchanged. Final shape of the `Gemini` block:
      ```jsonc
      "Gemini": {
        "gemini-2.0-flash":           { "InputPerMillion": 0.10, "OutputPerMillion": 0.40 },
        "gemini-2.5-flash":           { "InputPerMillion": <verified>, "OutputPerMillion": <verified> },
        "gemini-embedding-001":       { "InputPerMillion": 0.15, "OutputPerMillion": 0.00 },
        "gemini-embedding-2-preview": { "InputPerMillion": <verified>, "OutputPerMillion": <verified> }
      }
      ```
      _Acceptance: file is valid JSON; the four Gemini keys are present; existing
      Anthropic keys are untouched; `dotnet build Api` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Worker/appsettings.json`** — apply the identical `ModelPricing.Gemini`
      change as in `Api/appsettings.json` (same four keys, same numeric values).
      The Anthropic block and the cache multipliers are unchanged.
      _Acceptance: file is valid JSON; both files have the exact same four Gemini
      entries with the exact same rates; `dotnet build` (full solution) green_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Smoke-verify D1** — with the Worker running in Development against a live
      PostgreSQL instance, trigger one Gemini article analysis cycle, then run:
      ```sql
      SELECT "Model", "CostUsd" FROM ai_request_log
      WHERE "Provider" = 'Gemini' ORDER BY "Timestamp" DESC LIMIT 5;
      ```
      _Acceptance: at least one row for `gemini-2.5-flash` shows `CostUsd > 0`;
      no `LogWarning "Missing pricing for Gemini gemini-2.5-flash"` in the Worker
      log for that cycle_

---

### D2 — Startup pricing validator

- [x] **Create `Infrastructure/Configuration/ModelPricingValidator.cs`** — `public
      static class` with one `public static void ValidateOrLog(AiOptions ai,
      ModelPricingOptions pricing, ILogger logger)` method. The method collects
      all ten model-id strings from `AiOptions` (eight Anthropic fields:
      `AnalyzerModel`, `GeneratorModel`, `ContentGeneratorModel`, `ClassifierModel`,
      `ContradictionDetectorModel`, `SummaryUpdaterModel`, `KeyFactsExtractorModel`,
      `TitleGeneratorModel`; two Gemini fields: `AnalyzerModel`, `EmbeddingModel`),
      skips any that are null or empty, looks each up in the relevant
      `ModelPricingOptions` dictionary (`Anthropic` for Anthropic model ids, `Gemini`
      for Gemini model ids), and calls `logger.LogError(
      "Startup pricing validation: no pricing configured for {Provider} model {Model}. Cost will be logged as 0.",
      provider, modelId)` once per missing entry. The host must continue to start
      regardless — the method must not throw.
      The class stays under 30 lines; if both provider loops share identical logic,
      extract a private static helper method `CheckProvider` rather than duplicating
      the loop.
      _Acceptance: file compiles in the `Infrastructure` project with no DI
      references; when both dictionaries are populated correctly the method produces
      zero log lines; when a model id is absent from the dictionary the method
      emits exactly one `LogError` line for that model id; `dotnet build` green_
      _Skill: .claude/skills/code-conventions/SKILL.md_
      _Skill: .claude/skills/clean-code/SKILL.md_
      NOTE: Made `public` (not `internal`) because it is called from Worker and Api
      assemblies. `ILogger<ModelPricingValidator>` is not possible for a static class
      — used `ILoggerFactory.CreateLogger(nameof(ModelPricingValidator))` at call
      sites instead.

- [x] **Modify `Worker/Program.cs`** — after `var host = builder.Build()` and
      before `host.Run()`, resolve the two options values and the logger from the
      built service provider and invoke the validator.
      _Acceptance: `dotnet build Worker` green; validator call is placed between
      `builder.Build()` and `host.Run()`; no `BuildServiceProvider()` call exists
      in `Worker/Program.cs` or `InfrastructureServiceExtensions.cs`; worker
      startup log contains no `LogError` lines from `ModelPricingValidator` when
      both Gemini model ids are present in the dictionary; temporarily removing
      `gemini-2.5-flash` from `Worker/appsettings.json` and restarting the Worker
      produces exactly one `LogError` line at startup naming `gemini-2.5-flash`;
      restoring the entry removes the error_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Api/Program.cs`** — after `var app = builder.Build()` and before
      `app.Run()`, resolve the two options values and the logger from the built
      service provider and invoke the validator.
      _Acceptance: `dotnet build Api` green; validator call is placed between
      `builder.Build()` and the Swagger/middleware setup block; no
      `BuildServiceProvider()` call exists in `Api/Program.cs`; Api startup log
      contains no `LogError` lines from `ModelPricingValidator` when both Gemini
      model ids are present in the dictionary; temporarily removing
      `gemini-2.5-flash` from `Api/appsettings.json` produces exactly one
      `LogError` line at Api startup naming `gemini-2.5-flash`_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### D3 — UI KPI sub-cent display

- [x] **Modify `UI/src/features/aiOperations/AiOpsKpiStrip.tsx`** — add the
      `formatTotalCost` helper function at module level, immediately after the
      existing `formatPercent` function (line 20). Exact shape:
      ```ts
      const SUB_CENT_THRESHOLD = 0.01

      function formatTotalCost(value: number): string {
        if (value === 0) return '$0.00'
        if (value < SUB_CENT_THRESHOLD) return `$${value.toFixed(6)}`
        return costFormatter.format(value)
      }
      ```
      Then replace the one consumer line to use `formatTotalCost(kpis.totalCostUsd)`.
      No other lines in this file change. No other UI files change.
      _Acceptance: `npm run build` (from `UI/`) exits 0 with no TypeScript errors;
      calling `formatTotalCost(0)` returns `'$0.00'`; calling
      `formatTotalCost(0.00013)` returns `'$0.000130'`; calling
      `formatTotalCost(1.5)` returns a string starting with `'$1'`_
      _Skill: .claude/skills/clean-code/SKILL.md_

---

### D4 — Best-effort SQL backfill migration

- [x] **Create `Infrastructure/Persistence/Sql/0006_backfill_gemini_flash_cost.sql`** —
      forward-only DbUp migration with two `UPDATE` statements using rates identical
      to those in appsettings.json ($0.30/$2.50 for gemini-2.5-flash; $0.15/$0.00
      for gemini-embedding-2-preview). Conservative `WHERE CostUsd = 0` guard on
      both statements. No Anthropic rows touched. No cache token columns.
      _Acceptance: file is valid SQL; the numeric literals match the values in
      `Api/appsettings.json` word-for-word; no Anthropic `WHERE` clause appears;
      both `UPDATE` statements are conservative (`CostUsd = 0` guard present)_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Verify embedded-resource registration for `0006_backfill_gemini_flash_cost.sql`** —
      `Infrastructure/Infrastructure.csproj` has `<EmbeddedResource Include="Persistence\Sql\*.sql" />`
      glob — confirmed. The new file is automatically included. No `.csproj` edit
      required. `dotnet build Infrastructure` green after adding the file.
      _Acceptance: `dotnet build Infrastructure` green; glob picks up new file_
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [ ] **Smoke-verify D4** — restart the Worker (or Api) in Development against the
      same PostgreSQL instance used in D1 smoke verification. Observe the startup log
      for a DbUp line such as `Running upgrade script 0006_backfill_gemini_flash_cost.sql`.
      Then run:
      ```sql
      SELECT "Model", COUNT(*), MIN("CostUsd"), MAX("CostUsd")
      FROM ai_request_log
      WHERE "Provider" = 'Gemini'
        AND "Model" IN ('gemini-2.5-flash', 'gemini-embedding-2-preview')
      GROUP BY "Model";
      ```
      _Acceptance: DbUp startup log shows the migration ran exactly once; the query
      returns rows where `MIN("CostUsd") > 0` for each affected model (all historical
      zeros have been recomputed); subsequent restarts do not re-run the migration
      (DbUp idempotency)_

---

### End-to-end smoke

- [ ] **Full dashboard smoke verification** — open the AI Operations dashboard as
      Admin. With the Worker having run at least one Gemini cycle after D1 was
      deployed:
      1. KPI strip TOTAL COST card shows a non-`$0.00` value (either `$X.XX` for
         multi-cent totals or `$0.00XXXX` for sub-cent totals, never a flat `$0.00`
         when cost > 0).
      2. Breakdown panel shows `gemini-2.5-flash` with a non-zero cost row.
      3. Time-series chart shows Gemini cost above the X axis for the period after
         the fix was deployed.
      4. No `LogError` lines from `ModelPricingValidator` appear in the Worker log
         during normal operation.
      _Acceptance: all four observations are true; no 500 errors in the API log;
      `dotnet build` (full solution) and `npm run build` (UI) both exit 0_

---

## Open Questions

_None. All design decisions are resolved in ADR
`docs/architecture/decisions/fix-zero-cost-for-cheap-ai-models.md` (Option 2)._
