-- Baseline schema for NewsParser
-- Generated from EF Core configurations as of migration AddPublicationPipelineRedesign

CREATE EXTENSION IF NOT EXISTS vector;

-- ============================================================
-- TABLE: users
-- ============================================================
CREATE TABLE IF NOT EXISTS users (
    "Id"           UUID         NOT NULL,
    "FirstName"    TEXT         NOT NULL DEFAULT '',
    "LastName"     TEXT         NOT NULL DEFAULT '',
    "Email"        TEXT         NOT NULL DEFAULT '',
    "PasswordHash" TEXT         NOT NULL DEFAULT '',
    "Role"         TEXT         NOT NULL DEFAULT '',
    CONSTRAINT "PK_users" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_Email" ON users ("Email");

-- ============================================================
-- TABLE: sources
-- ============================================================
CREATE TABLE IF NOT EXISTS sources (
    "Id"            UUID             NOT NULL,
    "Name"          TEXT             NOT NULL DEFAULT '',
    "Url"           TEXT             NOT NULL DEFAULT '',
    "Type"          TEXT             NOT NULL DEFAULT '',
    "IsActive"      BOOLEAN          NOT NULL DEFAULT FALSE,
    "LastFetchedAt" TIMESTAMPTZ      NULL,
    CONSTRAINT "PK_sources" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_sources_IsActive" ON sources ("IsActive");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_sources_Url" ON sources ("Url");

-- ============================================================
-- TABLE: publish_targets
-- ============================================================
CREATE TABLE IF NOT EXISTS publish_targets (
    "Id"           UUID    NOT NULL,
    "Name"         TEXT    NOT NULL DEFAULT '',
    "Platform"     TEXT    NOT NULL DEFAULT '',
    "Identifier"   TEXT    NOT NULL DEFAULT '',
    "SystemPrompt" TEXT    NOT NULL DEFAULT '',
    "IsActive"     BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_publish_targets" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_publish_targets_IsActive"  ON publish_targets ("IsActive");
CREATE INDEX IF NOT EXISTS "IX_publish_targets_Platform"  ON publish_targets ("Platform");

-- ============================================================
-- TABLE: events
-- ============================================================
CREATE TABLE IF NOT EXISTS events (
    "Id"            UUID        NOT NULL,
    "Title"         TEXT        NOT NULL DEFAULT '',
    "Summary"       TEXT        NOT NULL DEFAULT '',
    "Status"        TEXT        NOT NULL DEFAULT '',
    "FirstSeenAt"   TIMESTAMPTZ NOT NULL,
    "LastUpdatedAt" TIMESTAMPTZ NOT NULL,
    "Embedding"     vector(768) NULL,
    "ArticleCount"  INTEGER     NOT NULL DEFAULT 0,
    CONSTRAINT "PK_events" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_events_Status"        ON events ("Status");
CREATE INDEX IF NOT EXISTS "IX_events_LastUpdatedAt" ON events ("LastUpdatedAt");
CREATE INDEX IF NOT EXISTS "IX_events_FirstSeenAt"   ON events ("FirstSeenAt");

-- ============================================================
-- TABLE: articles
-- ============================================================
CREATE TABLE IF NOT EXISTS articles (
    "Id"              UUID        NOT NULL,
    "OriginalContent" TEXT        NULL,
    "SourceId"        UUID        NULL,
    "OriginalUrl"     TEXT        NULL,
    "PublishedAt"     TIMESTAMPTZ NULL,
    "ExternalId"      TEXT        NULL,
    "Embedding"       vector(768) NULL,
    "Title"           TEXT        NOT NULL DEFAULT '',
    "Tags"            TEXT[]      NOT NULL DEFAULT '{}',
    "Category"        TEXT        NOT NULL DEFAULT '',
    "Sentiment"       TEXT        NOT NULL DEFAULT '',
    "ProcessedAt"     TIMESTAMPTZ NOT NULL,
    "Status"          TEXT        NOT NULL DEFAULT '',
    "ModelVersion"    TEXT        NOT NULL DEFAULT '',
    "Language"        TEXT        NOT NULL DEFAULT '',
    "Summary"         TEXT        NULL,
    "KeyFacts"        JSONB       NOT NULL DEFAULT '[]',
    "RejectionReason" TEXT        NULL,
    "RetryCount"      INTEGER     NOT NULL DEFAULT 0,
    "EventId"         UUID        NULL,
    "Role"            TEXT        NULL,
    "WasReclassified" BOOLEAN     NOT NULL DEFAULT FALSE,
    "AddedToEventAt"  TIMESTAMPTZ NULL,
    CONSTRAINT "PK_articles" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_articles_sources_SourceId"
        FOREIGN KEY ("SourceId") REFERENCES sources ("Id"),
    CONSTRAINT "FK_articles_events_EventId"
        FOREIGN KEY ("EventId") REFERENCES events ("Id") ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS "IX_articles_Status"      ON articles ("Status");
CREATE INDEX IF NOT EXISTS "IX_articles_ProcessedAt" ON articles ("ProcessedAt");
CREATE INDEX IF NOT EXISTS "IX_articles_EventId"     ON articles ("EventId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_articles_SourceId_ExternalId"
    ON articles ("SourceId", "ExternalId")
    WHERE "SourceId" IS NOT NULL AND "ExternalId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_articles_Embedding"
    ON articles USING hnsw ("Embedding" vector_cosine_ops);

-- ============================================================
-- TABLE: media_files
-- ============================================================
CREATE TABLE IF NOT EXISTS media_files (
    "Id"          UUID        NOT NULL,
    "ArticleId"   UUID        NOT NULL,
    "R2Key"       TEXT        NOT NULL DEFAULT '',
    "OriginalUrl" TEXT        NOT NULL DEFAULT '',
    "ContentType" TEXT        NOT NULL DEFAULT '',
    "SizeBytes"   BIGINT      NOT NULL DEFAULT 0,
    "Kind"        TEXT        NOT NULL DEFAULT '',
    "CreatedAt"   TIMESTAMPTZ NOT NULL,
    CONSTRAINT "PK_media_files" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_media_files_articles_ArticleId"
        FOREIGN KEY ("ArticleId") REFERENCES articles ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_media_files_ArticleId" ON media_files ("ArticleId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_media_files_ArticleId_OriginalUrl"
    ON media_files ("ArticleId", "OriginalUrl");

-- ============================================================
-- TABLE: publications
-- ============================================================
CREATE TABLE IF NOT EXISTS publications (
    "Id"                   UUID        NOT NULL,
    "ArticleId"            UUID        NOT NULL,
    "EditorId"             UUID        NULL,
    "PublishTargetId"      UUID        NOT NULL,
    "GeneratedContent"     TEXT        NOT NULL DEFAULT '',
    "Status"               TEXT        NOT NULL DEFAULT '',
    "CreatedAt"            TIMESTAMPTZ NOT NULL,
    "PublishedAt"          TIMESTAMPTZ NULL,
    "ApprovedAt"           TIMESTAMPTZ NULL,
    "EventId"              UUID        NULL,
    "ParentPublicationId"  UUID        NULL,
    "UpdateContext"        TEXT        NULL,
    "SelectedMediaFileIds" JSONB       NOT NULL DEFAULT '[]',
    "ReviewedByEditorId"   UUID        NULL,
    "RejectedAt"           TIMESTAMPTZ NULL,
    "RejectionReason"      TEXT        NULL,
    CONSTRAINT "PK_publications" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_publications_articles_ArticleId"
        FOREIGN KEY ("ArticleId") REFERENCES articles ("Id"),
    CONSTRAINT "FK_publications_users_EditorId"
        FOREIGN KEY ("EditorId") REFERENCES users ("Id"),
    CONSTRAINT "FK_publications_publish_targets_PublishTargetId"
        FOREIGN KEY ("PublishTargetId") REFERENCES publish_targets ("Id"),
    CONSTRAINT "FK_publications_events_EventId"
        FOREIGN KEY ("EventId") REFERENCES events ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_publications_publications_ParentPublicationId"
        FOREIGN KEY ("ParentPublicationId") REFERENCES publications ("Id") ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS "IX_publications_Status"              ON publications ("Status");
CREATE INDEX IF NOT EXISTS "IX_publications_PublishTargetId"     ON publications ("PublishTargetId");
CREATE INDEX IF NOT EXISTS "IX_publications_EventId"             ON publications ("EventId");
CREATE INDEX IF NOT EXISTS "IX_publications_ParentPublicationId" ON publications ("ParentPublicationId");

-- ============================================================
-- TABLE: publish_logs
-- ============================================================
CREATE TABLE IF NOT EXISTS publish_logs (
    "Id"                UUID        NOT NULL,
    "PublicationId"     UUID        NOT NULL,
    "Status"            TEXT        NOT NULL DEFAULT '',
    "ErrorMessage"      TEXT        NULL,
    "AttemptedAt"       TIMESTAMPTZ NOT NULL,
    "ExternalMessageId" TEXT        NULL,
    CONSTRAINT "PK_publish_logs" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_publish_logs_publications_PublicationId"
        FOREIGN KEY ("PublicationId") REFERENCES publications ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_publish_logs_PublicationId" ON publish_logs ("PublicationId");

-- ============================================================
-- TABLE: event_updates
-- ============================================================
CREATE TABLE IF NOT EXISTS event_updates (
    "Id"          UUID        NOT NULL,
    "EventId"     UUID        NOT NULL,
    "ArticleId"   UUID        NOT NULL,
    "FactSummary" TEXT        NOT NULL DEFAULT '',
    "IsPublished" BOOLEAN     NOT NULL DEFAULT FALSE,
    "CreatedAt"   TIMESTAMPTZ NOT NULL,
    CONSTRAINT "PK_event_updates" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_event_updates_events_EventId"
        FOREIGN KEY ("EventId") REFERENCES events ("Id"),
    CONSTRAINT "FK_event_updates_articles_ArticleId"
        FOREIGN KEY ("ArticleId") REFERENCES articles ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_event_updates_EventId"     ON event_updates ("EventId");
CREATE INDEX IF NOT EXISTS "IX_event_updates_IsPublished" ON event_updates ("IsPublished");

-- ============================================================
-- TABLE: contradictions
-- ============================================================
CREATE TABLE IF NOT EXISTS contradictions (
    "Id"          UUID        NOT NULL,
    "EventId"     UUID        NOT NULL,
    "Description" TEXT        NOT NULL DEFAULT '',
    "IsResolved"  BOOLEAN     NOT NULL DEFAULT FALSE,
    "CreatedAt"   TIMESTAMPTZ NOT NULL,
    CONSTRAINT "PK_contradictions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_contradictions_events_EventId"
        FOREIGN KEY ("EventId") REFERENCES events ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_contradictions_EventId"    ON contradictions ("EventId");
CREATE INDEX IF NOT EXISTS "IX_contradictions_IsResolved" ON contradictions ("IsResolved");

-- ============================================================
-- TABLE: contradiction_articles
-- ============================================================
CREATE TABLE IF NOT EXISTS contradiction_articles (
    "ContradictionId" UUID NOT NULL,
    "ArticleId"       UUID NOT NULL,
    CONSTRAINT "PK_contradiction_articles" PRIMARY KEY ("ContradictionId", "ArticleId"),
    CONSTRAINT "FK_contradiction_articles_contradictions_ContradictionId"
        FOREIGN KEY ("ContradictionId") REFERENCES contradictions ("Id"),
    CONSTRAINT "FK_contradiction_articles_articles_ArticleId"
        FOREIGN KEY ("ArticleId") REFERENCES articles ("Id") ON DELETE RESTRICT
);
