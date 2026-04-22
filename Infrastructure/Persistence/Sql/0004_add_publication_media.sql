-- 1. Add ownership discriminator (defaults 'Article' for existing rows)
ALTER TABLE media_files
    ADD COLUMN IF NOT EXISTS "OwnerKind"         TEXT NOT NULL DEFAULT 'Article',
    ADD COLUMN IF NOT EXISTS "PublicationId"     UUID NULL,
    ADD COLUMN IF NOT EXISTS "UploadedByUserId"  UUID NULL;

-- 2. Relax ArticleId to nullable so publication-owned rows are allowed
ALTER TABLE media_files ALTER COLUMN "ArticleId" DROP NOT NULL;

-- 3. FK to publications with cascade delete (cleans up rows when a publication is deleted).
--    FK to users is SET NULL (editor account removal must not lose the file).
ALTER TABLE media_files
    ADD CONSTRAINT "FK_media_files_publications_PublicationId"
        FOREIGN KEY ("PublicationId") REFERENCES publications ("Id") ON DELETE CASCADE,
    ADD CONSTRAINT "FK_media_files_users_UploadedByUserId"
        FOREIGN KEY ("UploadedByUserId") REFERENCES users ("Id") ON DELETE SET NULL;

-- 4. Invariant: exactly one owner, and the discriminator matches
ALTER TABLE media_files
    ADD CONSTRAINT "CK_media_files_owner_exclusive"
        CHECK (
            ("OwnerKind" = 'Article'     AND "ArticleId"     IS NOT NULL AND "PublicationId" IS NULL)
         OR ("OwnerKind" = 'Publication' AND "PublicationId" IS NOT NULL AND "ArticleId"     IS NULL)
        );

-- 5. Replace the existing unique (ArticleId, OriginalUrl) index with a partial one
--    so publication-owned rows (where OriginalUrl is empty) do not collide.
DROP INDEX IF EXISTS "IX_media_files_ArticleId_OriginalUrl";
CREATE UNIQUE INDEX IF NOT EXISTS "IX_media_files_ArticleId_OriginalUrl"
    ON media_files ("ArticleId", "OriginalUrl")
    WHERE "ArticleId" IS NOT NULL;

-- 6. Index for fast "list custom media for this publication" lookups
CREATE INDEX IF NOT EXISTS "IX_media_files_PublicationId"
    ON media_files ("PublicationId")
    WHERE "PublicationId" IS NOT NULL;
