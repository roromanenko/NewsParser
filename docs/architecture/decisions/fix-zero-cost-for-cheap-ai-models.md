# Fix Zero Cost for Cheap AI Models in AI Request Logs

## Status
Proposed

## Context

The AI Operations dashboard (ADR `ai-request-logging-and-cost-tracking.md`,
ADR `0020-ai-operations-dashboard.md`, ADR `0021-ai-operations-read-api.md`) records
every Anthropic / Gemini call with a USD cost computed by
`Infrastructure/AI/Telemetry/AiCostCalculator.cs` from token counts and per-model
pricing in `appsettings.json`.

Operators report that **Gemini Flash rows are constantly logged with `CostUsd = 0`**.
The user described it as: "as if all requests were free." The KPI strip then shows
TOTAL COST `$0.00`, the breakdown panel shows `$0.0000`, and any time-series chart
is flat at zero for Gemini.

A walk through the calculation pipeline (Worker config → calculator → DB → API → UI)
finds the following facts.

### Fact 1 — Configured Worker model name does not match any pricing key

`Worker/appsettings.Development.json` (verified):

```json
"Gemini": {
  "AnalyzerModel": "gemini-2.5-flash",
  "EmbeddingModel": "gemini-embedding-2-preview",
  ...
}
```

`Worker/appsettings.json` and `Api/appsettings.json` `ModelPricing.Gemini` (verified):

```json
"Gemini": {
  "gemini-2.0-flash":     { "InputPerMillion": 0.10, "OutputPerMillion": 0.40 },
  "gemini-embedding-001": { "InputPerMillion": 0.15, "OutputPerMillion": 0.00 }
}
```

`Infrastructure/AI/Telemetry/AiCostCalculator.cs:24-28`:

```csharp
if (!priceTable.TryGetValue(model, out var price))
{
    logger.LogWarning("Missing pricing for {Provider} {Model}", provider, model);
    return 0m;
}
```

`AnalyzerModel` (used by `GeminiArticleAnalyzer.cs:38` as `_model` and passed verbatim
to `AiRequestLogEntry.Model` at `GeminiArticleAnalyzer.cs:105`) is `gemini-2.5-flash`,
but the pricing dictionary only has `gemini-2.0-flash`. The dictionary lookup misses,
the calculator returns `0m`, and the row is persisted with `CostUsd = 0`.

The same mismatch exists for embeddings: the embedding model is configured as
`gemini-embedding-2-preview` but the pricing key is `gemini-embedding-001`.

This is the **primary root cause**. It is silent in production because the
"Missing pricing" warning is at `LogWarning` level and easy to overlook in a noisy
worker log.

### Fact 2 — Storage and types are NOT the bug

- DB column: `"CostUsd" NUMERIC(18, 8)` (`Infrastructure/Persistence/Sql/0005_add_ai_request_log.sql:13`).
  10 integer digits + 8 fractional digits — this comfortably represents fractions of a
  cent. A Gemini Flash call of (500 in, 200 out) at $0.10 / $0.40 per million yields
  `(500 × 0.10 + 200 × 0.40) / 1_000_000 = 0.00013 USD`, well above the `1e-8`
  resolution.
- Calculator math (`AiCostCalculator.CalculateGemini`,
  `AiCostCalculator.CalculateAnthropic`) uses `decimal` end to end (`/ 1_000_000m`,
  multipliers stored as `decimal` per `ModelPricingOptions.cs:10-11`). No `double`
  intermediate, no premature rounding.
- Domain (`Core/DomainModels/AiRequestLog.cs:18`), entity
  (`Infrastructure/Persistence/Entity/AiRequestLogEntity.cs:18`), and DTO
  (`Api/Models/AiOperationsDtos.cs:43`) all type `CostUsd` as `decimal`.
- Dapper round-trips `decimal` ↔ Postgres `numeric` natively; no custom type handler
  is involved, no narrowing happens.

So if the calculator returned a non-zero value, it would survive intact through the
DB and the JSON wire. The bug is upstream, in the pricing dictionary lookup.

### Fact 3 — The UI display secondarily hides any sub-cent value

Even after the pricing keys are fixed, the KPI strip uses
`Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })`
(`UI/src/features/aiOperations/AiOpsKpiStrip.tsx:17`). With its default of two
fraction digits, **any total under half a cent renders as `$0.00`**. Per-row table
cells already show 6 fraction digits (`AiRequestsTable.tsx:24-26`,
`$${value.toFixed(6)}`) and the breakdown panel shows 4 (`AiOpsBreakdownPanel.tsx:12-14`),
so they are fine. Only the KPI cards round to two fractional digits. For early
adoption where cumulative cost over a 24-hour window may still be a few cents, this
is the difference between "the dashboard works" and "the dashboard still shows
zero".

### Fact 4 — Already-logged rows for the affected models are unrecoverable from the row itself

The row preserves `InputTokens`, `OutputTokens`, `CacheCreationInputTokens`,
`CacheReadInputTokens`, `Provider`, and `Model`. So a back-fill could recompute
`CostUsd` for a row by looking up `(Provider, Model)` against the corrected pricing
table. The numbers needed for the recomputation are exactly what the calculator
needs at write time. This is mechanically possible but cuts across two seams (config
+ DB) and requires either a one-off SQL script (with hard-coded prices) or a small
admin-only re-cost endpoint. The user described the goal as a bug fix, not a
redesign — back-fill is in scope only as far as it is safe and proportional.

### Recent ADRs that constrain this work

- `ai-request-logging-and-cost-tracking.md` — Decision D2: "If `priceTable` has no
  entry for `model`, return `0m` and the logger emits a `LogWarning` ... once per
  cycle." That contract is intentional. The fix is **not** to change calculator
  behaviour (silent zero on missing pricing is the right safety net) — the fix is
  to make sure the pricing dictionary actually contains every model the workers
  use, and to make the warning loud enough that the next missing entry surfaces
  immediately.
- ADR `0020-ai-operations-dashboard.md` D4 — "TOTAL COST — `$1,234.56` formatted
  with `Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })`". The
  KPI display format is from this ADR. Adjusting it for sub-cent visibility is a
  targeted refinement, not a contradiction — the dashboard ADR did not consider
  the case where total cost is below a cent.

## Options

### Option 1 — Fix the pricing dictionary only (configuration-only fix)

Add the actually-used model keys (`gemini-2.5-flash`, `gemini-embedding-2-preview`,
plus current Anthropic model id if any drift) to `ModelPricing.Gemini` and
`ModelPricing.Anthropic` in both `Api/appsettings.json` and `Worker/appsettings.json`,
with the correct per-million prices. Leave the calculator, the schema, the DTOs, and
the UI unchanged.

**Pros:**
- Minimal change. One JSON edit per environment.
- Restores the documented behaviour: every new call gets a non-zero cost.
- Zero code change, zero migration, zero rebuild risk.

**Cons:**
- KPI cards still display `$0.00` for small totals (under half a cent) because the
  formatter rounds to two decimals — operators will still see "the dashboard is
  broken" until enough volume accumulates.
- Already-logged rows for `gemini-2.5-flash` / `gemini-embedding-2-preview` keep
  `CostUsd = 0` forever. Anyone running a 30-day analysis of cost-by-model will see
  those rows as "free" — misleading.
- Nothing prevents the next configuration drift (e.g. when the worker is bumped to
  `gemini-3.0-flash` or Claude Haiku is bumped to a new dated id) from silently
  re-introducing the same bug.

### Option 2 — Fix pricing + raise visibility of missing-pricing warning + increase KPI display precision + best-effort backfill

Four small changes that together address every layer where the bug is observable:

1. **Pricing dictionary** — add `gemini-2.5-flash` and `gemini-embedding-2-preview`
   to `ModelPricing.Gemini` in `Api/appsettings.json` and `Worker/appsettings.json`
   with correct rates from Google's published pricing
   (`gemini-2.5-flash`: $0.30 input / $2.50 output per 1M tokens at the time of
   writing; `gemini-embedding-2-preview` follows the embedding-tier price set at
   the time of implementation — implementer to verify against
   <https://ai.google.dev/pricing> at the time of the change). Keep the existing
   `gemini-2.0-flash` and `gemini-embedding-001` keys for safety.
2. **Calculator already logs `LogWarning` on miss** — but to prevent future drift
   re-introducing this bug silently, add **a startup validation** in
   `Infrastructure/Extensions/InfrastructureServiceExtensions` (or a small
   `ModelPricingValidator` invoked from there) that compares the configured
   model ids in `AiOptions` (Anthropic.AnalyzerModel, Anthropic.GeneratorModel,
   Anthropic.ContentGeneratorModel, Anthropic.ClassifierModel,
   Anthropic.ContradictionDetectorModel, Anthropic.SummaryUpdaterModel,
   Anthropic.KeyFactsExtractorModel, Anthropic.TitleGeneratorModel,
   Gemini.AnalyzerModel, Gemini.EmbeddingModel) against the pricing dictionary
   and logs `LogError` (one line per missing model) at host start. The host
   continues to start (we do not want to block the worker on a config issue) but
   the error surfaces in startup logs and Aiven's log feed, where it cannot be
   missed.
3. **UI KPI display** — change the TOTAL COST formatter in
   `UI/src/features/aiOperations/AiOpsKpiStrip.tsx` so that values under one cent
   render with sub-cent precision rather than `$0.00`. Concretely: if
   `totalCostUsd >= 0.01`, format with `Intl.NumberFormat('en-US', { style:
   'currency', currency: 'USD' })` (current behaviour); otherwise if
   `totalCostUsd > 0`, render as `$${totalCostUsd.toFixed(6)}`; otherwise render
   `$0.00`. This means an operator who ran two test calls and accumulated
   $0.0003 sees `$0.000300` instead of `$0.00`, while normal multi-cent totals
   still get the conventional `$1,234.56` formatting.
4. **Best-effort backfill** — add a one-time DbUp migration
   (`Infrastructure/Persistence/Sql/0006_backfill_gemini_flash_cost.sql`) that
   recomputes `CostUsd` only for rows where `CostUsd = 0` AND
   `Provider = 'Gemini'` AND `Model IN ('gemini-2.5-flash',
   'gemini-embedding-2-preview')` using the per-million rates inlined as SQL
   numeric literals. The script is conservative: it only touches rows currently
   at zero, so it is idempotent and safe to re-run; it does not touch Anthropic
   rows or rows where the calculator already produced a non-zero value. Rows
   for unknown models (e.g. a future model name added then removed) stay at
   zero — that is correct, we do not invent prices we do not know.

**Pros:**
- Fixes the immediate user complaint (Gemini-flash zeros) for new and historical
  rows alike.
- Closes the visibility gap (KPI cards now show the truth, even sub-cent).
- Adds a guardrail (startup validation) so the next time someone bumps a model
  name without updating pricing, the build/deploy logs scream rather than
  silently logging zero.
- Each piece is small and independently revertable.

**Cons:**
- Touches four areas (config, infra startup, UI, SQL migration) — wider change
  set than Option 1.
- The backfill script hard-codes prices in SQL — those prices live in two places
  (the migration file and `appsettings.json`). Mitigation: the migration is a
  one-shot historical correction; once it has run, the duplication is dead and
  removed by being immutable (DbUp scripts are append-only; this one is for the
  one cohort of historical rows captured before the fix).

### Option 3 — Replace the dictionary lookup with a wildcard / family-prefix matcher

Make the calculator match `gemini-2.5-flash` against a `gemini-flash-*` or
`gemini-2.5-*` family pattern in the pricing dictionary, so that a model name the
worker invents at runtime still resolves to a price.

**Pros:**
- Robust against minor model-name version bumps.

**Cons:**
- Pricing actually does change between Gemini versions (2.0 Flash and 2.5 Flash
  have different $/Mtok rates) and between dated Anthropic model ids (Haiku 3.5
  vs Haiku 4 vs Haiku 4.5). A family-prefix matcher would happily charge
  Gemini 2.0 prices for Gemini 2.5 calls — silently wrong, which is worse than
  silently zero.
- Increases calculator complexity for a misfeature.

Rejected.

## Decision

**Option 2 — Pricing dictionary fix + startup validation + UI sub-cent display +
best-effort SQL backfill of historical zeros for the two affected Gemini models.**

Rationale: Option 1 is technically a valid one-line fix for the root cause, but
leaves the operator-visible symptoms (KPI shows `$0.00`, historical rows still
zero, next drift will silently zero things again) intact. The complaint is
operational ("the dashboard says everything was free") and the fix needs to
restore trust in the dashboard, not just in the calculator. Option 2's four parts
are each small enough to review in one sitting and address the four observable
seams (calculation, observability, UI, history) in proportion.

### Concrete change set

**D1. Pricing configuration (`Api/appsettings.json`, `Worker/appsettings.json`)**

Update the `ModelPricing.Gemini` dictionary to include the model ids actually
used by the workers, leaving the existing entries in place as fallbacks. Final
shape (use real per-million rates verified from
<https://ai.google.dev/pricing> at the time of implementation; the values below
are illustrative and must be replaced by the implementer):

```jsonc
"Gemini": {
  "gemini-2.0-flash":            { "InputPerMillion": 0.10, "OutputPerMillion": 0.40 },
  "gemini-2.5-flash":            { "InputPerMillion": 0.30, "OutputPerMillion": 2.50 },
  "gemini-embedding-001":        { "InputPerMillion": 0.15, "OutputPerMillion": 0.00 },
  "gemini-embedding-2-preview":  { "InputPerMillion": 0.15, "OutputPerMillion": 0.00 }
}
```

The Anthropic side uses dated model ids that match the configured ones today
(`claude-haiku-4-5-20251001`, `claude-sonnet-4-5`). The implementer must verify
both `Api/appsettings.json` and `Worker/appsettings.json` in tandem. No
`appsettings.Development.json` override is required unless the dev environment
uses different model ids.

**D2. Startup pricing validation (`Infrastructure/Extensions/InfrastructureServiceExtensions.cs`)**

Add a small validator class
`Infrastructure/Configuration/ModelPricingValidator.cs` (internal static helper,
no DI) with one method:

```csharp
internal static class ModelPricingValidator
{
    public static void ValidateOrLog(
        AiOptions ai,
        ModelPricingOptions pricing,
        ILogger logger)
    {
        // Collect every configured Anthropic / Gemini model id from AiOptions,
        // skip empty / null, look each up in the relevant pricing dictionary,
        // and emit one logger.LogError per missing model id.
    }
}
```

Invoked once during `AddAiServices` (or in `AddInfrastructure`, wherever the
options are already bound) by resolving `IOptions<AiOptions>` and
`IOptions<ModelPricingOptions>` at startup, **after** options are bound and
**before** the AI clients are built. The host continues to start regardless —
the validator only logs. Use `LogError` (not `LogWarning`) so the line shows up
in default production log levels. Do **not** throw — the worker starting in a
zero-cost-logging state is preferable to it failing to start, and the existing
per-call `LogWarning` in `AiCostCalculator` remains as a last-line indicator if
a model id is set at runtime.

This validator is a one-screen, side-effect-free piece of code; no test is
required beyond a smoke run that confirms the log line appears when a model is
removed from the dictionary.

**D3. UI KPI sub-cent display (`UI/src/features/aiOperations/AiOpsKpiStrip.tsx`)**

Replace the single `costFormatter.format(kpis.totalCostUsd)` call (line 49) with
a small helper that switches format based on magnitude:

```ts
function formatTotalCost(value: number): string {
  if (value === 0) return '$0.00'
  if (value < 0.01) return `$${value.toFixed(6)}`
  return costFormatter.format(value)
}
```

Used as `const totalCost = isLoading || !kpis ? dash : formatTotalCost(kpis.totalCostUsd)`.

Threshold rationale: `Intl.NumberFormat` with default `minimumFractionDigits: 2,
maximumFractionDigits: 2` rounds anything `[0, 0.005)` to `$0.00`. Using
`< 0.01` as the cutoff catches every value the formatter would show as `$0.00`
or `$0.01` (where `$0.01` is itself a roundup of `$0.005..0.0149`). For
sub-cent values we render six fraction digits, matching the per-row table format
already used in `AiRequestsTable.tsx:24-26` (consistent visual language across
the dashboard).

No other UI files change — `AiOpsBreakdownPanel.tsx` (`toFixed(4)`),
`AiRequestsTable.tsx` (`toFixed(6)`), `AiRequestDetailSlideOver.tsx`
(`toFixed(8)`), and `AiOpsCostTimeChart.tsx` (`toFixed(2)` for chart axis labels)
already display sub-cent values readably. The chart's `toFixed(2)` axis ticks
are acceptable: a chart axis at $0.00 is fine if the values are sub-cent — the
operator will read the actual numbers from the breakdown panel and table.

**D4. Best-effort SQL backfill (`Infrastructure/Persistence/Sql/0006_backfill_gemini_flash_cost.sql`)**

A single forward-only DbUp script. It updates only rows that meet **all** of the
following:

- `CostUsd = 0`
- `Provider = 'Gemini'`
- `Model` is one of the two affected ids (`gemini-2.5-flash`,
  `gemini-embedding-2-preview`)

The recomputation uses the same formula the calculator uses, with the per-million
rates inlined as `NUMERIC(18, 8)` literals to match the column precision. Shape:

```sql
UPDATE ai_request_log
SET "CostUsd" =
    ("InputTokens"::numeric  * <input_per_million>::numeric +
     "OutputTokens"::numeric * <output_per_million>::numeric) / 1000000::numeric
WHERE "CostUsd" = 0
  AND "Provider" = 'Gemini'
  AND "Model" = 'gemini-2.5-flash';

UPDATE ai_request_log
SET "CostUsd" =
    ("InputTokens"::numeric  * <input_per_million>::numeric +
     "OutputTokens"::numeric * <output_per_million>::numeric) / 1000000::numeric
WHERE "CostUsd" = 0
  AND "Provider" = 'Gemini'
  AND "Model" = 'gemini-embedding-2-preview';
```

No `cache_*` columns appear in the formula because Gemini does not produce them
(the writer always sets them to 0 — `GeminiArticleAnalyzer.ParseGeminiUsage`,
line 138). The literal rates **must** match the rates put into
`appsettings.json` in D1. Because Gemini does not use cache multipliers, this
formula is the full cost for those rows.

Anthropic rows are deliberately not touched. Rows for any future Gemini model
not in the WHERE list are deliberately not touched. Rows where the calculator
already returned a non-zero value are deliberately not touched.

The script is added as an embedded resource alongside the existing `0005_*.sql`
and is applied at startup by `DbUpMigrator.Migrate()`.

**D5. No DB schema change**

The `NUMERIC(18, 8)` column already has the precision needed for sub-cent
fractional values. A row with cost `0.00012345` USD round-trips losslessly. No
migration to widen or change the type is required.

**D6. No calculator change**

`AiCostCalculator.Calculate` correctly returns `0m` when pricing is missing,
correctly performs `decimal` arithmetic, correctly applies the cache
multipliers. The behaviour is exactly as ADR `ai-request-logging-and-cost-tracking.md`
specified. No change.

**D7. No DTO / mapper / API change**

`CostUsd` is `decimal` end to end (entity → domain → DTO). System.Text.Json
serializes a `decimal` as a JSON number with full precision (no scientific
notation, no truncation). The TypeScript generated client receives it as
`number`. JavaScript `number` is IEEE 754 double, which has ~15–17 significant
decimal digits — easily enough for sub-cent values like `0.00012345`. No change.

## Consequences

**Positive:**
- New Gemini Flash / Gemini embedding rows record correct USD cost from the next
  worker cycle onward.
- Historical zero rows for the two affected Gemini models are recomputed and no
  longer mislead the cost dashboard.
- The KPI strip stops showing `$0.00` for early-adoption sub-cent totals; what
  the operator sees matches what the data says.
- Future model-name drift triggers a startup-time `LogError`, not a silent
  per-call `LogWarning` lost in normal operation.
- Calculator behaviour, schema, and the API surface are all unchanged — no risk
  to other AI clients, no migration of types, no client-codegen regeneration.

**Negative / risks:**
- The illustrative pricing rates in this ADR (`$0.30` / `$2.50` per 1M for
  Gemini 2.5 Flash) **must** be verified by the implementer against
  Google's published pricing page at the time of the change. Wrong rates
  produce wrong costs — a different bug, not zero. The ADR explicitly defers
  exact numbers to implementation.
- The backfill SQL inlines those same rates. If they are entered wrong in the
  migration, the historical rows are wrong; rolling back means writing
  another forward-only migration that resets the affected rows to zero and
  re-running the corrected backfill. Mitigation: implementer manually verifies
  the per-million rates against Google's pricing page **once** and uses the
  same numbers in both `appsettings.json` and the migration file.
- Adding the startup validator means an extra few microseconds of work at host
  start. Negligible.
- The `formatTotalCost` helper introduces a magnitude-based formatting branch
  that the rest of the dashboard does not use. The cost is one extra function
  in one file; the benefit is honest display.

**Files affected:**

- **Modified:**
  - `Api/appsettings.json` — add `gemini-2.5-flash` and
    `gemini-embedding-2-preview` keys to `ModelPricing.Gemini` with verified
    per-million rates.
  - `Worker/appsettings.json` — same edit, in lockstep with the API config.
  - `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` — invoke the
    new `ModelPricingValidator.ValidateOrLog` once during AI-service registration.
  - `UI/src/features/aiOperations/AiOpsKpiStrip.tsx` — replace the one-line cost
    formatter call with the magnitude-aware `formatTotalCost` helper.
- **New:**
  - `Infrastructure/Configuration/ModelPricingValidator.cs` — internal static
    class with `ValidateOrLog(AiOptions, ModelPricingOptions, ILogger)`.
  - `Infrastructure/Persistence/Sql/0006_backfill_gemini_flash_cost.sql` —
    forward-only DbUp script, conservative WHERE clause, two UPDATEs.
- **Untouched (deliberately):**
  - `Infrastructure/AI/Telemetry/AiCostCalculator.cs` — calculator math is
    correct; the per-call `LogWarning` on missing pricing remains as a
    last-line guard.
  - `Infrastructure/AI/Telemetry/AiRequestLogger.cs` — log-and-swallow contract
    unchanged.
  - `Infrastructure/Persistence/Sql/0005_add_ai_request_log.sql` — column
    precision is sufficient.
  - `Core/DomainModels/AiRequestLog.cs`, `Infrastructure/Persistence/Entity/AiRequestLogEntity.cs`,
    `Infrastructure/Persistence/Mappers/AiRequestLogMapper.cs`,
    `Api/Models/AiOperationsDtos.cs`, `Api/Mappers/AiOperationsMapper.cs` — all
    use `decimal` correctly; no narrowing happens.
  - `UI/src/api/generated/*` — never edited by hand; no schema change means no
    regeneration is needed.
  - All AI clients (`GeminiArticleAnalyzer.cs` etc.) — no change.

## Implementation Notes

**For `feature-planner` — skills to follow during implementation:**

- `.claude/skills/code-conventions/SKILL.md` — `ModelPricingValidator` is a
  small internal helper in `Infrastructure/Configuration/`; primary-constructor
  syntax is not needed because it is a static class. Options are read via
  `IOptions<T>` at host start, not directly from configuration.
- `.claude/skills/dapper-conventions/SKILL.md` — only relevant for the
  migration script naming (`####_<verb>_<area>.sql`, embedded resource,
  forward-only). The script does not change schema, only data.
- `.claude/skills/api-conventions/SKILL.md` — no API surface change in this
  bug fix; no new controller, no new DTO, no new validator. The skill applies
  only as a sanity check that nothing in `Api/` needs to change (it does not).
- `.claude/skills/clean-code/SKILL.md` — the new `formatTotalCost` helper goes
  at module level in `AiOpsKpiStrip.tsx`, sits next to the existing `formatPercent`
  helper, no magic numbers (the `0.01` cutoff is a small named const if useful).
  The startup validator stays under 20 lines; if it grows, extract per-provider
  helpers, do not let one method handle both providers and the iteration.
- `.claude/skills/mappers/SKILL.md` — not directly applicable; no mapper
  changes.
- ADR `ai-request-logging-and-cost-tracking.md` — D2 contract preserved
  ("missing pricing returns `0m` and emits a `LogWarning`"); we do **not**
  weaken it. The new startup validator is additive observability, not a
  replacement.
- ADR `0020-ai-operations-dashboard.md` — the KPI display tweak is a refinement
  of D4 ("TOTAL COST formatted with `Intl.NumberFormat`"). The shape stays the
  same for normal totals; only the sub-cent fall-through is added.

**Order of work (each step independently verifiable):**

1. **Verify and update the pricing config.** Look up the current published per-million
   rates for `gemini-2.5-flash` and `gemini-embedding-2-preview` at
   <https://ai.google.dev/pricing>. Update `Api/appsettings.json` and
   `Worker/appsettings.json` in tandem. Restart the worker; confirm next Gemini
   call writes a non-zero `CostUsd` to the DB (one quick smoke query against
   `ai_request_log`).
2. **Add the startup validator.** Create
   `Infrastructure/Configuration/ModelPricingValidator.cs` and invoke it from
   `InfrastructureServiceExtensions.AddAiServices` (or wherever the AI clients
   are wired). Temporarily remove a model id from `Anthropic.AnalyzerModel` in
   `appsettings.Development.json`, restart the worker, confirm a `LogError`
   line appears at startup. Restore the value.
3. **Update the KPI strip formatter.** Add `formatTotalCost` next to
   `formatPercent` in `AiOpsKpiStrip.tsx`; replace the one consumer site;
   build the UI (`npm run build` from `UI/`) to confirm no type errors.
4. **Write the backfill migration.** Add
   `Infrastructure/Persistence/Sql/0006_backfill_gemini_flash_cost.sql` with the
   verified rates. Mark it as embedded resource in the `.csproj` if the project
   convention requires explicit `<EmbeddedResource>` items (it does — verify
   against the existing `0005_*.sql` entry). Restart the worker; confirm the
   migration runs once (DbUp logs); spot-check the DB that affected historical
   rows now have a non-zero `CostUsd`.
5. **End-to-end smoke.** Open the AI Operations dashboard as Admin. Confirm the
   KPI strip shows a non-`$0.00` total. Confirm the breakdown panel shows
   non-zero rows for `gemini-2.5-flash`. Confirm the time-series chart shows
   Gemini cost above the X axis.

**Out of scope (do not expand):**

- No change to the calculator's missing-pricing behaviour (still returns `0m`,
  still logs `LogWarning`).
- No new options class for "default pricing" or fallbacks. If a model is missing
  from the dictionary it stays at zero; the new startup validator surfaces that.
- No retention or archival of `ai_request_log` (separate ADR if ever needed).
- No reformat of per-row table cost cells; six fraction digits there is fine.
- No reformat of the chart Y axis; two decimal places is fine for chart ticks.
- No mid-flight migration of historical Anthropic rows. Anthropic rows are not
  affected by this bug.
- No "re-cost everything" admin endpoint. The one-shot SQL migration is enough
  for the historical correction; the next time a model id changes, the
  startup validator catches it before historical zeros accumulate.
- No support for hot-reload of `ModelPricing` via `IOptionsMonitor`. Redeploy
  on price change is acceptable, same as the writer ADR's stance.
