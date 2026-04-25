CREATE TABLE IF NOT EXISTS ai_request_log (
    "Id"                        UUID             NOT NULL,
    "Timestamp"                 TIMESTAMPTZ      NOT NULL,
    "Worker"                    TEXT             NOT NULL DEFAULT '',
    "Provider"                  TEXT             NOT NULL DEFAULT '',
    "Operation"                 TEXT             NOT NULL DEFAULT '',
    "Model"                     TEXT             NOT NULL DEFAULT '',
    "InputTokens"               INTEGER          NOT NULL DEFAULT 0,
    "OutputTokens"              INTEGER          NOT NULL DEFAULT 0,
    "CacheCreationInputTokens"  INTEGER          NOT NULL DEFAULT 0,
    "CacheReadInputTokens"      INTEGER          NOT NULL DEFAULT 0,
    "TotalTokens"               INTEGER          NOT NULL DEFAULT 0,
    "CostUsd"                   NUMERIC(18, 8)   NOT NULL DEFAULT 0,
    "LatencyMs"                 INTEGER          NOT NULL DEFAULT 0,
    "Status"                    TEXT             NOT NULL DEFAULT 'Success',
    "ErrorMessage"              TEXT             NULL,
    "CorrelationId"             UUID             NOT NULL,
    "ArticleId"                 UUID             NULL,
    CONSTRAINT "PK_ai_request_log" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_ai_request_log_Timestamp"     ON ai_request_log ("Timestamp");
CREATE INDEX IF NOT EXISTS "IX_ai_request_log_Provider"      ON ai_request_log ("Provider");
CREATE INDEX IF NOT EXISTS "IX_ai_request_log_Worker"        ON ai_request_log ("Worker");
CREATE INDEX IF NOT EXISTS "IX_ai_request_log_Model"         ON ai_request_log ("Model");
CREATE INDEX IF NOT EXISTS "IX_ai_request_log_ArticleId"     ON ai_request_log ("ArticleId");
CREATE INDEX IF NOT EXISTS "IX_ai_request_log_CorrelationId" ON ai_request_log ("CorrelationId");
