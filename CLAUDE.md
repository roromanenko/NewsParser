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

## Available Skills

<available_skills>
  <skill>
    <name>skill-creator</name>
    <description>Create new skills, modify and improve existing skills, and measure skill performance. Use when users want to create a skill from scratch, edit, or optimize an existing skill, run evals to test a skill, benchmark skill performance with variance analysis, or optimize a skill's description for better triggering accuracy.</description>
    <location>.claude/skills/skill-creator/SKILL.md</location>
  </skill>
</available_skills>
