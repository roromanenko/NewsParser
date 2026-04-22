# NewsParser — Claude Cowork Instructions

This document orients a coworking Claude instance on how to contribute to the NewsParser project.

## Start Here

- @CLAUDE.md — primary project instructions: solution structure, architecture, workflow rules, available skills and agents. Read this first.
- @docs/system.md — high-level system overview and domain glossary.
- @UI/CLAUDE.md — frontend-specific conventions (React 19 + TypeScript).

## Key Source Locations

| Area | Path |
|---|---|
| REST API | @Api/ |
| Domain models & interfaces | @Core/ |
| Dapper repos, AI services, parsers | @Infrastructure/ |
| Background workers | @Worker/ |
| React SPA | @UI/src/ |
| NUnit tests | @Tests/ |

## Planning & Decision Artifacts

- @docs/architecture/decisions/ — Architecture Decision Records (ADRs). Read the latest before touching a feature area.
- @docs/tasks/active/ — atomic tasklists for in-progress features.
- @docs/tasks/done/ — completed tasklists for historical context.
- @docs/planning/ — freeform planning notes.

## Skills & Agents

Skills (invoke via `/skill-name`) and agents live in:

- @.claude/skills/ — coding conventions, Dapper, API, mappers, clean code, testing.
- @.claude/agents/ — feature-planner, architect, implementer, reviewer, test-writer.

Before writing any code consult the relevant skill. Before starting a feature run the `feature-planner` agent.

## Workflow

1. Read @CLAUDE.md and the relevant ADR in @docs/architecture/decisions/.
2. For a new feature: run `feature-planner` → produces a tasklist in @docs/tasks/active/.
3. For architecture decisions: run `architect` → produces an ADR in @docs/architecture/decisions/.
4. Implement using the `implementer` agent or directly with skill guidance.
5. After implementation: run `reviewer` agent before committing.

## Output Policy

**All files and artifacts produced during a cowork session must be saved inside `/cowork/`.**

This includes scratch notes, draft documents, research outputs, intermediate plans, generated code snippets not yet placed in the main tree, and any other session artifacts. This keeps the main source tree clean and makes it easy to review what was produced before promoting work into the codebase.

Suggested layout:

```
cowork/
  COWORK.md          ← this file
  <date>-<topic>/    ← one folder per session or topic
    notes.md
    draft-*.md
    *.cs  (staging area for code before placement)
```
