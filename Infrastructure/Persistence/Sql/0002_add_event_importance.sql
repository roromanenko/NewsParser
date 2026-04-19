ALTER TABLE events ADD COLUMN IF NOT EXISTS "ImportanceTier"         TEXT             NULL;
ALTER TABLE events ADD COLUMN IF NOT EXISTS "ImportanceBaseScore"    DOUBLE PRECISION NULL;
ALTER TABLE events ADD COLUMN IF NOT EXISTS "ImportanceCalculatedAt" TIMESTAMPTZ      NULL;

CREATE INDEX IF NOT EXISTS "IX_events_ImportanceTier" ON events ("ImportanceTier");
