---
description: Run the data access review pass over a specified scope (Dapper + PostgreSQL + pgvector)
argument-hint: [folder-or-file]
---

Use the **data-access-reviewer** agent to review the following scope: $ARGUMENTS

If no scope was provided, ask me which folder or files to review before
starting. Suggest sensible defaults based on `docs/reviews/PROJECT_MAP.md`, for
example:
- `Infrastructure/Persistence/Repositories/` — Dapper repository classes
- `Infrastructure/Persistence/Repositories/Sql/` — SQL constant classes
- `Infrastructure/Persistence/Connection/` — `IDbConnectionFactory` and
  `IUnitOfWork` implementations
- `Infrastructure/Persistence/Sql/*.sql` — DbUp migration scripts
- `Infrastructure/Persistence/Entity/` and `Infrastructure/Persistence/Mappers/`
  — entity shapes and Entity↔Domain mappers
- Any service or worker that opens connections or composes multi-step writes
  via `IUnitOfWork.BeginAsync`

Requirements:
1. Read `CLAUDE.md`, `UI/CLAUDE.md` (if UI-adjacent code is in scope),
   `.claude/skills/dapper-conventions/SKILL.md`, and `docs/reviews/PROJECT_MAP.md`
   first.
2. Append findings to `docs/reviews/data-access-findings.md` in the format
   defined in the agent's instructions. Do not overwrite existing findings.
3. When done, print a one-paragraph summary: total findings by severity,
   and the top 3 issues I should look at first.
4. Do not modify any source code.
