# CLAUDE.md

## Solution Structure

This is a .NET 10 + React 19 monorepo for an AI-powered news curation and publishing platform. The solution (`NewsParser.slnx`) contains four projects:

- **`Api/`** — ASP.NET Core REST API (HTTPS port 7054, HTTP port 5172 for Swagger gen)
- **`Core/`** — Domain models and repository/service interfaces (no dependencies)
- **`Infrastructure/`** — EF Core (PostgreSQL + pgvector), AI services, parsers, publishers
- **`Worker/`** — .NET Generic Host with background workers
- **`UI/`** — React 19 + TypeScript SPA (see `UI/CLAUDE.md` for frontend-specific guidance)

## Architecture

### Data Flow
```
RSS Sources
  → RssFetcherWorker → RawArticles
  → ArticleAnalysisWorker (Claude/Gemini) → Articles (enriched)
  → Editor approves via UI
  → EventClassificationWorker → Events (grouped articles)
  → PublicationWorker → Telegram (or other platforms)
```

### Key Configuration

- `Api/appsettings.Development.json` — DB connection string, JWT secret/issuer/audience
- Options classes: `AiOptions` (Anthropic/Gemini API keys, model names), `PromptsOptions` (prompt file paths), `TelegramOptions`, `RssFetcherOptions`, `ArticleProcessingOptions`, `EventClassificationOptions`, `ValidationOptions`

### Database

PostgreSQL with the `pgvector` extension. EF Core migrations are in `Infrastructure/Persistence/Migrations/`. The `pgvector` column is used on `Event` for semantic similarity when classifying articles into events. `FuzzySharp` is used for string-based deduplication.

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
    <name>ef-core-conventions</name>
    <description>NewsParser EF Core repository conventions for Infrastructure/Persistence/Repositories/. Use when adding a new repository class, adding a method to an existing repository, writing a query with Include/ThenInclude, using pgvector, or writing an update/delete operation. Triggers on: "add repository", "new repository", "add method to repository", "EF Core query", "pgvector query", "ExecuteUpdateAsync", "repository pattern", "GetPendingFor".</description>
    <location>.claude/skills/ef-core-conventions/SKILL.md</location>
  </skill>
</available_skills>

## Available Agents

<available_agents>
  <agent>
    <name>test-writer</name>
    <description>Generates enterprise-grade NUnit tests for NewsParser. Use for: Core domain, EF Core repositories, services, API endpoints, and background workers.</description>
    <location>.claude/agents/test-writer.md</location>
  </agent>
  <agent>
    <name>feature-planner</name>
    <description>Given a feature description, explores the codebase and produces an atomic tasklist saved to docs/tasks/active/&lt;feature-name&gt;.md. Call before starting any implementation. Does NOT write code or modify source files.</description>
    <location>.claude/agents/feature-planner.md</location>
  </agent>
</available_agents>