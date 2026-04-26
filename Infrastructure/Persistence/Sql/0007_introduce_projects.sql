-- 1. Create the projects table
CREATE TABLE IF NOT EXISTS projects (
    "Id"                 UUID        NOT NULL,
    "Name"               TEXT        NOT NULL,
    "Slug"               TEXT        NOT NULL,
    "AnalyzerPromptText" TEXT        NOT NULL,
    "Categories"         TEXT[]      NOT NULL DEFAULT '{}',
    "OutputLanguage"     TEXT        NOT NULL DEFAULT 'uk',
    "OutputLanguageName" TEXT        NOT NULL DEFAULT 'Ukrainian',
    "IsActive"           BOOLEAN     NOT NULL DEFAULT TRUE,
    "CreatedAt"          TIMESTAMPTZ NOT NULL,
    CONSTRAINT "PK_projects" PRIMARY KEY ("Id")
);

-- 2. Unique index on Slug
CREATE UNIQUE INDEX IF NOT EXISTS "IX_projects_Slug" ON projects ("Slug");

-- 3. Insert the Default project (idempotent)
INSERT INTO projects (
    "Id", "Name", "Slug", "AnalyzerPromptText",
    "Categories", "OutputLanguage", "OutputLanguageName", "IsActive", "CreatedAt"
)
VALUES (
    '00000000-0000-0000-0000-000000000001',
    'Default',
    'default',
    $PROMPT$You are a professional news article analyzer. Your task is to analyze the provided news article and extract structured metadata.

INSTRUCTIONS:
- Return ONLY a valid JSON object. No markdown, no code blocks, no explanations, no extra text.
- If the article is too short, low quality, or not a news article — still return valid JSON with best-effort values.
- Detect the language from the article content, not from the source URL.
CATEGORY must be exactly one of:
{CATEGORIES}

SENTIMENT must be exactly one of:
Positive, Negative, Neutral

LANGUAGE must be exactly one of:
en, uk, ru, de, fr, pl, es, it

TAGS: between 3 and 7 relevant keywords, lowercase, in the same language as the article.

SUMMARY: Summary must be 2-3 sentences in {OUTPUT_LANGUAGE}, regardless of the article language.

Return exactly this JSON structure:
{"category": "string", "tags": ["string"], "sentiment": "Positive|Negative|Neutral", "language": "en|uk|ru|...", "summary": "string"}$PROMPT$,
    ARRAY['Politics','Economics','Technology','Sports','Culture','Science','War','Society','Health','Environment'],
    'uk',
    'Ukrainian',
    TRUE,
    now()
)
ON CONFLICT ("Id") DO NOTHING;

-- 4a. Backfill sources
ALTER TABLE sources
    ADD COLUMN IF NOT EXISTS "ProjectId" UUID NOT NULL
    DEFAULT '00000000-0000-0000-0000-000000000001';

ALTER TABLE sources ALTER COLUMN "ProjectId" DROP DEFAULT;

ALTER TABLE sources
    ADD CONSTRAINT "FK_sources_projects_ProjectId"
    FOREIGN KEY ("ProjectId") REFERENCES projects ("Id") ON DELETE RESTRICT;

-- 4b. Backfill articles
ALTER TABLE articles
    ADD COLUMN IF NOT EXISTS "ProjectId" UUID NOT NULL
    DEFAULT '00000000-0000-0000-0000-000000000001';

ALTER TABLE articles ALTER COLUMN "ProjectId" DROP DEFAULT;

ALTER TABLE articles
    ADD CONSTRAINT "FK_articles_projects_ProjectId"
    FOREIGN KEY ("ProjectId") REFERENCES projects ("Id") ON DELETE RESTRICT;

-- 4c. Backfill events
ALTER TABLE events
    ADD COLUMN IF NOT EXISTS "ProjectId" UUID NOT NULL
    DEFAULT '00000000-0000-0000-0000-000000000001';

ALTER TABLE events ALTER COLUMN "ProjectId" DROP DEFAULT;

ALTER TABLE events
    ADD CONSTRAINT "FK_events_projects_ProjectId"
    FOREIGN KEY ("ProjectId") REFERENCES projects ("Id") ON DELETE RESTRICT;

-- 4d. Backfill publish_targets
ALTER TABLE publish_targets
    ADD COLUMN IF NOT EXISTS "ProjectId" UUID NOT NULL
    DEFAULT '00000000-0000-0000-0000-000000000001';

ALTER TABLE publish_targets ALTER COLUMN "ProjectId" DROP DEFAULT;

ALTER TABLE publish_targets
    ADD CONSTRAINT "FK_publish_targets_projects_ProjectId"
    FOREIGN KEY ("ProjectId") REFERENCES projects ("Id") ON DELETE RESTRICT;

-- 4e. Backfill publications
ALTER TABLE publications
    ADD COLUMN IF NOT EXISTS "ProjectId" UUID NOT NULL
    DEFAULT '00000000-0000-0000-0000-000000000001';

ALTER TABLE publications ALTER COLUMN "ProjectId" DROP DEFAULT;

ALTER TABLE publications
    ADD CONSTRAINT "FK_publications_projects_ProjectId"
    FOREIGN KEY ("ProjectId") REFERENCES projects ("Id") ON DELETE RESTRICT;

-- 5. Drop the old global unique index on sources URL
DROP INDEX IF EXISTS "IX_sources_Url";

-- 6. Per-project unique index on source URL
CREATE UNIQUE INDEX IF NOT EXISTS "IX_sources_ProjectId_Url" ON sources ("ProjectId", "Url");

-- 7. Index for articles scoped paged queries
CREATE INDEX IF NOT EXISTS "IX_articles_ProjectId_Status_ProcessedAt"
    ON articles ("ProjectId", "Status", "ProcessedAt" DESC);

-- 8. Index for events scoped paged queries
CREATE INDEX IF NOT EXISTS "IX_events_ProjectId_Status_LastUpdatedAt"
    ON events ("ProjectId", "Status", "LastUpdatedAt" DESC);

-- 9. Index for publications scoped paged queries
CREATE INDEX IF NOT EXISTS "IX_publications_ProjectId_Status_CreatedAt"
    ON publications ("ProjectId", "Status", "CreatedAt" DESC);
