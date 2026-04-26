-- Backfill CostUsd for Gemini rows logged before pricing was configured.
-- Idempotent:
--   * Wrapped in a transaction so partial application cannot occur.
--   * WHERE CostUsd = 0 guard ensures already-recomputed rows are skipped on re-run.
--   * Token guard (InputTokens > 0 OR OutputTokens > 0) skips rows whose recomputed
--     cost would still be 0, so re-running is a strict no-op once applied.
BEGIN;

-- Rates: $0.30 input / $2.50 output per 1M tokens (matches ModelPricing.Gemini in appsettings.json).
UPDATE ai_request_log
SET "CostUsd" =
    ("InputTokens"::numeric  * 0.30::numeric +
     "OutputTokens"::numeric * 2.50::numeric) / 1000000::numeric
WHERE "CostUsd" = 0
  AND "Provider" = 'Gemini'
  AND "Model"    = 'gemini-2.5-flash'
  AND ("InputTokens" > 0 OR "OutputTokens" > 0);

-- Rates: $0.15 input per 1M tokens (embedding has no output pricing).
UPDATE ai_request_log
SET "CostUsd" =
    ("InputTokens"::numeric * 0.15::numeric) / 1000000::numeric
WHERE "CostUsd" = 0
  AND "Provider" = 'Gemini'
  AND "Model"    = 'gemini-embedding-2-preview'
  AND "InputTokens" > 0;

COMMIT;
