# Architect Agent Prompt — Multi-Project Tenancy

Copy the block below into Claude Code and run it through the `architect` agent (Design mode).

---

```
Mode: Design

Task: Design an ADR for introducing a `Project` entity as the root tenancy boundary in
NewsParser. Today Source/Article/Event/PublishTarget/AiRequestLog all live in a single
global scope; we need a hard split by project.

The product owner has already made the following decisions. DO NOT re-litigate them in
the ADR — record them as Decisions:

1. Tenancy model: Project is a hard tenant. Source belongs to ONE project via FK
   ProjectId. If the same RSS feed is needed in two projects, TWO separate Source rows
   are created with the same URL. Duplicating fetches and AI analysis is accepted
   consciously in exchange for schema and code simplicity. (Alternatives — Source M:N
   Project, and a two-stage analysis pipeline — were considered and rejected.)

2. Project analyzer prompt and category storage: ENTIRELY IN THE DATABASE. Project
   stores:
   - AnalyzerPromptText (text) — the full system prompt used by the article analyzer
   - Categories (text[] OR a separate table — decide and justify) — the allowed
     category list
   No new files under Infrastructure/AI/Prompts/ for project prompts. The existing
   Infrastructure/AI/Prompts/analyzer.txt content must be migrated into the DB for
   the Default project.

3. API scoping: URL segment. All scoped endpoints live under
   `/api/projects/{projectId:guid}/...` (articles, events, sources, publications,
   publish-targets, ai-operations). Global endpoints (project CRUD, auth, health)
   stay unprefixed. Decide on the implementation: route prefix on controllers,
   action/endpoint filter for project existence/active validation, and where the
   current ProjectId lives so services and repositories can read it (a scoped DI
   `IProjectContext`?).

4. Authorization: every authenticated user sees every project. No UserProject M:N
   table is introduced. Project-level authorization is OUT OF SCOPE for this ADR.

5. Data migration: ONE Default project is created and ALL existing
   Source/Article/Event/PublishTarget/AiRequestLog rows are attached to it. ProjectId
   becomes NOT NULL after backfill. The text from
   Infrastructure/AI/Prompts/analyzer.txt is copied into Default.AnalyzerPromptText,
   and the currently hardcoded categories (Politics, Economics, Technology, Sports,
   Culture, Science, War, Society, Health, Environment) are copied into
   Default.Categories.

Design and document in the ADR:

A. The Project entity: full field list, types, nullability. At minimum: Id, Name,
   Slug (if needed), AnalyzerPromptText, Categories, IsActive, CreatedAt. Justify
   each field. Resolve the question of DefaultLanguage / OutputLanguage (currently
   used in analyzer.txt as the `{OUTPUT_LANGUAGE}` placeholder and discussed in
   the existing ADR `normalize-internal-ai-fields-to-configured-language`).

B. Changes to existing entities: which FKs ProjectId go where. Show the full list
   of affected tables together with the CASCADE strategy (what happens when a
   project is deleted).

C. PublishTarget per-project design: confirm the FK and explain how the existing
   `PublishTarget.SystemPrompt` is migrated (this prompt remains target-specific,
   not project-specific).

D. The DbUp migration script (`Infrastructure/Persistence/Sql/NNNN_introduce_projects.sql`):
   - CREATE TABLE projects
   - INSERT the Default project with a hardcoded GUID (for idempotency and tests)
   - ALTER TABLE on each affected table: add project_id with a DEFAULT, backfill,
     drop the default, add NOT NULL and FK
   - Indexes on (project_id, ...) for the critical query paths (articles by status,
     events by status/date, pgvector index keyed by (project_id, embedding))
   - Show examples of the key indexes

E. Pgvector similarity for Event classification: how the EventRepository query
   changes (kNN with a project_id predicate). Discuss whether a separate index is
   needed or whether a WHERE predicate on top of the existing ivfflat/hnsw index
   is sufficient.

F. API changes:
   - New ProjectsController (CRUD): `GET/POST/PUT/DELETE /api/projects`
   - All scoped controllers move under `/api/projects/{projectId}/`
   - Project validation mechanism: middleware vs action filter vs route constraint.
     Where the current ProjectId lives for downstream services (scoped DI
     `IProjectContext`?).
   - Behavior when the project is missing or inactive (404 vs 403 vs 400).

G. Worker changes:
   - SourceFetcherWorker: source.ProjectId is automatically inherited by Article
     on creation; nothing else changes.
   - ArticleAnalysisWorker: loads the Project by the article's ProjectId and uses
     Project.AnalyzerPromptText instead of the file. Categories are passed into
     the prompt as a placeholder. Discuss where {CATEGORIES} / {OUTPUT_LANGUAGE}
     placeholders are substituted.
   - PublicationGenerationWorker and PublishingWorker: project filtering on read,
     no fundamental logic changes.

H. UI changes (high-level only — no detailed wireframes; those go into a separate
   ADR if needed):
   - Project switcher in the shell layout
   - React Router: adding the `:projectId` segment, redirect logic when no project
     is selected
   - TanStack Query cache invalidation on project switch
   - OpenAPI client regeneration

I. Edge cases and risks:
   - Project deletion: cascade or soft-delete? How do we guard against accidental
     deletion when production targets are attached?
   - What happens when Source.ProjectId is changed (is it allowed at all?). What
     about existing Article/Event rows tied to that source?
   - Duplicate sources across projects: should the system warn when a Source is
     created with a URL that already exists in another project?
   - Testing: fixtures with multiple projects, isolation in integration tests.

J. Phasing recommendation: one PR or staged rollout (entity + migration → API →
   workers → UI)? What's the minimum viable scope for the first release.

Output artifact:
`docs/architecture/decisions/NNNN-multi-project-tenancy.md` (next free number after
0021). Structure should match the existing ADRs in this project (Context, Decision,
Consequences, plus area-specific sections).

IMPORTANT: do NOT write code. Do NOT create a tasklist. ADR only. The tasklist will
be created by the feature-planner agent in the next step.

Planning context and the variant comparison: cowork/2026-04-26-multi-project-tenancy/notes.md
```

---

## After the architect produces the ADR

1. Read the ADR and verify all 4 decisions are recorded and not re-litigated.
2. If anything is off, ask the architect to revise.
3. Once the ADR is approved, run `feature-planner` with the description:
   `Implement multi-project tenancy per docs/architecture/decisions/NNNN-multi-project-tenancy.md.`
   You will get a tasklist at `docs/tasks/active/multi-project-tenancy.md`.
4. After the tasklist is produced, run `architect` again in Review Tasklist mode to
   verify it against the ADR.
5. Then `implementer` → `reviewer` → commit.
