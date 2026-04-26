# CLAUDE.md

## Solution Structure

This is a .NET 10 + React 19 monorepo for an AI-powered news curation and publishing platform. The solution (`NewsParser.slnx`) contains four projects:

- **`Api/`** — ASP.NET Core REST API (HTTPS port 7054, HTTP port 5172 for Swagger gen)
- **`Core/`** — Domain models and repository/service interfaces (no dependencies)
- **`Infrastructure/`** — Dapper (PostgreSQL + pgvector), AI services, parsers, publishers
- **`Worker/`** — .NET Generic Host with background workers
- **`UI/`** — React 19 + TypeScript SPA (see `UI/CLAUDE.md` for frontend-specific guidance)

## Architecture

### Data Flow
```
RSS Sources
  → SourceFetcherWorker → Articles (Pending, with media)
  → ArticleAnalysisWorker (Gemini/Claude)
      → Article enriched (category, tags, sentiment, summary, embedding)
      → Event created or matched (semantic similarity via pgvector)
  → Editor creates Publication via UI
  → PublicationGenerationWorker (Claude) → Publication content generated
  → PublishingWorker → Telegram (or other platforms)
```

### Key Configuration

- `Api/appsettings.Development.json` — DB connection string, JWT secret/issuer/audience
- Options classes: `AiOptions` (Anthropic/Gemini API keys, model names), `PromptsOptions` (prompt file paths), `TelegramOptions`, `RssFetcherOptions`, `ArticleProcessingOptions`, `EventClassificationOptions`, `ValidationOptions`

### Database

PostgreSQL with the `pgvector` extension. Schema migrations are managed by **DbUp** (`dbup-postgresql`); forward-only SQL scripts live in `Infrastructure/Persistence/Sql/*.sql` as embedded resources and are applied at startup by `DbUpMigrator.Migrate()`. The `pgvector` column is used on `Event` for semantic similarity when classifying articles into events. `FuzzySharp` is used for string-based deduplication. Data access uses **Dapper** with `IDbConnectionFactory` / `IUnitOfWork` — see `.claude/skills/dapper-conventions/SKILL.md`.

### Frontend

See `UI/CLAUDE.md`.

## Workflow Rules

- **Plan before code:** For any task touching 3+ files or introducing a new feature, always start in Plan Mode. Explore and ask questions before making changes.
- **Tasklist for complex tasks:** Break approved plans into atomic, verifiable steps saved to `docs/tasks/active/<feature-name>.md` before implementation. Each step must have a clear acceptance criterion.
- **Verify your work:** After implementing, always run the relevant tests or build commands to confirm the result before considering the task done.

## Available Skills

<available_skills>
  <skill>
    <name>skill-creator</name>
    <description>Create new skills, modify and improve existing skills, and measure skill performance. Use when users want to create a skill from scratch, edit, or optimize an existing skill, run evals to test a skill, benchmark skill performance with variance analysis, or optimize a skill's description for better triggering accuracy.</description>
    <location>.claude/skills/skill-creator/SKILL.md</location>
  </skill>
  <skill>
    <name>test-writer</name>
    <description>Use this skill when writing, reviewing, or generating tests for .NET projects using NUnit, Moq, and FluentAssertions. Triggers include: creating unit tests for domain logic, repository tests with EF Core InMemory, API endpoint tests with WebApplicationFactory, service tests with mocked dependencies, parameterized tests with TestCase, or any request to follow project testing conventions (AAA pattern, naming convention, anti-patterns checklist).</description>
    <location>.claude/skills/testing/SKILL.md</location>
  </skill>
  <skill>
    <name>api-conventions</name>
    <description>NewsParser API conventions for ASP.NET Core (.NET 10). Use when adding a new controller, endpoint, DTO, validator, or modifying auth/error-handling in the Api/ project. Triggers on: "add endpoint", "new controller", "create API route", "add DTO", "add validator", "API conventions", "REST endpoint", "HTTP status", "auth middleware", "FluentValidation".</description>
    <location>.claude/skills/api-conventions/SKILL.md</location>
  </skill>
  <skill>
    <name>mappers</name>
    <description>NewsParser mapping conventions. Use when adding a new mapper class, adding a ToDomain/ToEntity/ToDto method, extracting inline DTO construction from a controller, or mapping between Entity↔Domain (Infrastructure) or Domain→DTO (Api). Triggers on: "add mapper", "create mapper", "extract mapping", "ToDto", "ToDomain", "ToEntity", "map to DTO", "inline mapping".</description>
    <location>.claude/skills/mappers/SKILL.md</location>
  </skill>
  <skill>
    <name>agent-creator</name>
    <description>Design and create new specialized subagents for the NewsParser project. Use when adding a new agent file to .claude/agents/, defining agent responsibility, choosing tools/model, or structuring the mandatory sections (Role, Input Artifacts, Output Artifacts, Algorithm, Rules). Triggers on: "create agent", "new agent", "add agent", "design agent", "write agent file", "subagent for X".</description>
    <location>.claude/skills/agent-creator/SKILL.md</location>
  </skill>
  <skill>
    <name>code-conventions</name>
    <description>NewsParser project-specific conventions for structure and placement. Use when adding a new class to any layer, asking where something belongs, or checking naming patterns. Triggers on: "where does X go", "what layer", "naming convention", "how do workers work", "how do services work", "how should I structure", "is this the right pattern", "how do repositories work", "Options pattern", "how do mappers work".</description>
    <location>.claude/skills/code-conventions/SKILL.md</location>
  </skill>
  <skill>
    <name>clean-code</name>
    <description>Clean Code principles grounded in the NewsParser codebase — concrete violations and good examples named explicitly. Use when reviewing code quality, refactoring, or identifying smells. Triggers on: "code review", "refactor", "clean code", "SOLID", "is this good code", "too many parameters", "method too long", "magic number", "dead code", "naming", "comments", "guard clause".</description>
    <location>.claude/skills/clean-code/SKILL.md</location>
  </skill>
  <skill>
    <name>dapper-conventions</name>
    <description>NewsParser Dapper repository conventions for Infrastructure/Persistence/Repositories/. Use when adding a new repository class, adding a method to an existing repository, writing a query with multi-table stitching, using pgvector, or writing an update/delete operation. Triggers on: "add repository", "new repository", "add method to repository", "Dapper query", "pgvector query", "ExecuteAsync", "repository pattern", "add GetPendingFor", "write a query", "IDbConnectionFactory", "IUnitOfWork".</description>
    <location>.claude/skills/dapper-conventions/SKILL.md</location>
  </skill>
</available_skills>

## Available Agents

Agents fall into two groups: **feature-cycle** agents (plan → implement → review) and **audit-cycle** agents (specialist reviewers invoked per scope).

### Feature-cycle agents

<available_agents>
  <agent>
    <name>architect</name>
    <description>Two modes: (1) Design — given a task description, saves an ADR to docs/architecture/decisions/; (2) Review Tasklist — given a path to docs/tasks/active/X.md, verifies the tasklist against its ADR and outputs APPROVED or ISSUES FOUND. Does NOT write code or create tasklists.</description>
    <location>.claude/agents/architect.md</location>
  </agent>
  <agent>
    <name>feature-planner</name>
    <description>Given a feature description, explores the codebase and produces an atomic tasklist saved to docs/tasks/active/&lt;feature-name&gt;.md. Call before starting any implementation. Does NOT write code or modify source files.</description>
    <location>.claude/agents/feature-planner.md</location>
  </agent>
  <agent>
    <name>implementer</name>
    <description>Implements an approved tasklist from docs/tasks/active/, writing production .NET code across Core, Infrastructure, Api, and Worker. Delegates tests to test-writer. Call after a tasklist exists and is approved.</description>
    <location>.claude/agents/implementer.md</location>
  </agent>
  <agent>
    <name>test-writer</name>
    <description>Generates NUnit + Moq + FluentAssertions tests for NewsParser. Use for: Core domain, Dapper repositories, services, API endpoints, and background workers.</description>
    <location>.claude/agents/test-writer.md</location>
  </agent>
  <agent>
    <name>reviewer</name>
    <description>Reviews implemented code for quality and convention violations. Produces a BLOCKER / WARNING / SUGGESTION report and a "Ready to commit" / "Committable with warnings" / "Do not commit" verdict. Call after implementation, before committing. Does NOT write or modify code.</description>
    <location>.claude/agents/reviewer.md</location>
  </agent>
</available_agents>

### Audit-cycle agents (specialist reviewers)

Each writes findings to `reviews/<area>-findings.md` and is wired to a matching slash command (see below).

<available_agents>
  <agent>
    <name>csharp-architecture-reviewer</name>
    <description>Reviews C# code quality, async correctness, DI patterns, exception handling, and layer organization across Api/, Core/, Infrastructure/, and Worker/. Writes to reviews/architecture-findings.md.</description>
    <location>.claude/agents/csharp-architecture-reviewer.md</location>
  </agent>
  <agent>
    <name>data-access-reviewer</name>
    <description>Reviews Dapper + PostgreSQL + pgvector code: connection lifecycle, IUnitOfWork transactions, SQL safety, pgvector/JSONB/text[] type-handler binding, DbUp migrations. Writes to reviews/data-access-findings.md.</description>
    <location>.claude/agents/data-access-reviewer.md</location>
  </agent>
  <agent>
    <name>react-ui-reviewer</name>
    <description>Reviews the React 19 + TypeScript SPA: hook hygiene, TanStack React Query cache/invalidation, Zustand store usage, React Hook Form + Zod, React Router v7 guards, Tailwind v4 + CVA styling. Skips UI/src/api/generated/. Writes to reviews/frontend-findings.md.</description>
    <location>.claude/agents/react-ui-reviewer.md</location>
  </agent>
  <agent>
    <name>security-reviewer</name>
    <description>Reviews ASP.NET Core code for OWASP Top 10, auth/authorization gaps, and .NET-specific pitfalls. Writes to reviews/security-findings.md.</description>
    <location>.claude/agents/security-reviewer.md</location>
  </agent>
  <agent>
    <name>dependency-auditor</name>
    <description>Audits NuGet (.csproj PackageReference) and npm (UI/package.json) dependencies for outdated versions, known vulnerabilities, and abandoned packages. Can run dotnet and npm CLI. Writes to reviews/dependencies-findings.md.</description>
    <location>.claude/agents/dependency-auditor.md</location>
  </agent>
</available_agents>

## Available Slash Commands

Slash commands drive the two workflows above.

### Feature workflow
- `/ship <feature description>` — orchestrator: architect → planner → architect review → implementer → reviewer, with gates between each step.

### Audit workflow
Run `/map-project` once, then any review command, then `/triage-findings` to consolidate.

- `/map-project` — generates `reviews/PROJECT_MAP.md` (solution layout, packages, entry points, data access, auth, frontend, tests). Run first.
- `/review-architecture [scope]` — C# architecture + code quality pass via `csharp-architecture-reviewer`.
- `/review-data [scope]` — Dapper + PostgreSQL + pgvector pass via `data-access-reviewer`. Suggested scopes: `Infrastructure/Persistence/Repositories/`, `Infrastructure/Persistence/Sql/`, services that call `IUnitOfWork.BeginAsync`.
- `/review-frontend [scope]` — React + TypeScript pass via `react-ui-reviewer`. Suggested scopes: `UI/src/features/`, `UI/src/store/`, `UI/src/router/`. Skips `UI/src/api/generated/`.
- `/review-security [scope]` — OWASP + .NET auth pass via `security-reviewer`.
- `/review-dependencies` — NuGet + npm audit via `dependency-auditor`.
- `/triage-findings` — reads every `reviews/*-findings.md`, deduplicates, and produces `reviews/ACTION_PLAN.md` with Critical / Warnings / Improvements sections.