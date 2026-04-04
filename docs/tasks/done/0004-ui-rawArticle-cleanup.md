# UI RawArticle Cleanup

## Goal

Update the React UI to remove all references to `RawArticleDto` and `article.source`,
replacing them with `article.originalUrl` and `article.publishedAt` that are now carried
directly on `ArticleDetailDto`, and regenerate the OpenAPI TypeScript client so the
generated types match the refactored backend.

## Affected Layers

- UI

## Backend Phase Dependency

All tasks in this list depend on **Phase 6** of `0003-worker-pipeline-refactoring.md`
being complete — specifically the tasks that modify `Api/Models/ArticleDetailDto.cs`
(remove `RawArticleDto`), `Api/Mappers/ArticleMapper.cs`, and `Api/Controllers/ArticlesController.cs`.
The API must be running with those changes applied before `npm run generate-api` is executed.

---

## Tasks

### UI — OpenAPI Client Regeneration

- [x] **Regenerate `UI/src/api/generated/`** — with the backend running on port 5172,
      run `npm run generate-api` from the `UI/` directory to replace all files in
      `UI/src/api/generated/` with output derived from the updated OpenAPI spec.
      _Acceptance: `UI/src/api/generated/api.ts` no longer contains the `RawArticleDto`
      interface; `ArticleDetailDto` interface contains `'originalUrl'?: string | null` and
      `'publishedAt'?: string` at the top level instead of `'source'?: RawArticleDto`;
      `npm run build` exits 0 after regeneration (TypeScript compilation may fail — that
      is expected and is addressed in the next task)_
      _Skill: .claude/skills/code-conventions/SKILL.md_

### UI — ArticleDetailPage Component Update

- [x] **Modify `UI/src/features/articles/ArticleDetailPage.tsx`** — replace the "Original
      Source" sidebar section that reads `article.source` with a section that reads
      `article.originalUrl` and `article.publishedAt` directly from the article.
      Concretely: remove the `{article.source && (...)}` block (lines 126–145 in the
      current file); add an "Original Source" section that renders
      `article.publishedAt` via `formatDate` and an anchor pointing to `article.originalUrl`
      when `article.originalUrl` is non-null; the section should only render when at least
      one of the two fields is non-null.
      _Acceptance: the component renders without TypeScript errors; `npm run build` exits 0;
      no reference to `article.source` remains in the file; the "View original" link still
      appears when `originalUrl` is present; the published date still appears when
      `publishedAt` is present_
      _Skill: .claude/skills/code-conventions/SKILL.md_

### UI — Build Verification

- [x] **Verify `UI/` TypeScript build passes** — run `npm run build` from the `UI/`
      directory and confirm it exits 0 with no type errors.
      _Acceptance: `npm run build` exits 0; no errors referencing `RawArticleDto`,
      `article.source`, or missing properties on `ArticleDetailDto`_

## Open Questions

- None. The backend DTO shape (`originalUrl` and `publishedAt` on `ArticleDetailDto`) is
  already defined in the current `Api/Models/ArticleDetailDto.cs`; the only gap is the
  stale generated client and the one component that consumes `article.source`.
