---
name: agent-creator
description: >
  Design and create new specialized subagents for the NewsParser project.
  Use when the user wants to add a new Claude Code subagent, define a new agent role,
  create an agent file in .claude/agents/, or improve an existing agent definition.
  Triggers on: "create agent", "new agent", "add agent", "design agent", "write agent file",
  "subagent for X", "agent that does X".
---

# Agent Creator

A skill for designing and writing specialized subagents for the NewsParser project.

The core principle: **each agent does one thing well**. An agent that is too broad will produce shallow results. An agent that is too narrow is trivially replaceable by a prompt. Find the seam where a single, coherent capability lives.

---

## Step 1 — Define Responsibility

Before writing a single line, answer these three questions:

1. **What is the one job?** Write it in one sentence starting with a verb: *"Generates NUnit tests for a given .NET class."* If you need "and" — split it.
2. **What does it NOT do?** Equally important. Constraints prevent scope creep and help the agent refuse tasks it shouldn't handle.
3. **Who calls it?** The main Claude instance? Another agent? A user slash command? This determines what inputs it can reliably receive.

If you cannot answer all three cleanly, ask the user before continuing.

---

## Step 2 — Choose the Right Model

Match model to task complexity:

| Model | Use when |
|-------|----------|
| `opus` | Planning, architecture decisions, multi-file analysis, nuanced judgment |
| `sonnet` | Implementation, code generation, structured output, medium complexity |
| `haiku` | Atomic/fast tasks: renaming, formatting, single-file transforms, lookups |

Default to `sonnet` when unsure. Only choose `opus` if the agent genuinely needs deep reasoning — it is slower and more expensive.

---

## Step 3 — Choose the Minimum Tool Set

Give the agent only the tools it needs. Over-permissioned agents are harder to reason about and riskier.

| Agent role | Typical tools |
|------------|---------------|
| Planner / architect | `Read`, `Glob`, `Grep` |
| Implementer | `Read`, `Glob`, `Grep`, `Write`, `Edit` |
| Build/test runner | `Read`, `Glob`, `Grep`, `Write`, `Edit`, `Bash` |
| Researcher | `Read`, `Glob`, `Grep`, `WebFetch`, `WebSearch` |

Never add `Bash` unless the agent must execute shell commands. Never add `Agent` unless the agent explicitly needs to spawn sub-subagents.

---

## Step 4 — Write the Agent File

Save to `.claude/agents/<agent-name>.md`.

### YAML Frontmatter

```yaml
---
name: <kebab-case-name>
description: >
  One to three sentences. What this agent does, what it produces, and
  example trigger phrases. Make it specific enough that the dispatcher
  knows exactly when to use it. Include 2–3 concrete example requests.
tools: Read, Glob, Grep, Write   # comma-separated, minimum needed
model: sonnet                     # opus | sonnet | haiku
---
```

### Mandatory Body Sections

Every agent file **must** contain these five sections in order:

#### `## Role`

One paragraph. State what the agent does and, critically, what it does **not** do. A reader should be able to tell in 10 seconds whether this is the right agent for a task.

```markdown
## Role

You are a migration writer for the NewsParser project. Given a description of a schema
change, you produce an EF Core migration class and the corresponding model update in
`Infrastructure/`. You do NOT design schema changes, write tests, or modify the API layer.
```

#### `## Input Artifacts`

List every file or piece of context the agent reads before starting. Be specific — name directories, patterns, or config keys.

```markdown
## Input Artifacts

- `Core/Models/<Entity>.cs` — domain model being changed
- `Infrastructure/Persistence/<Entity>Configuration.cs` — current EF config
- `Infrastructure/Persistence/Migrations/` — existing migrations for naming context
- User message: description of the desired schema change
```

#### `## Output Artifacts`

List every file the agent creates or modifies. Include the path pattern and a one-line description.

```markdown
## Output Artifacts

- `Infrastructure/Persistence/Migrations/<timestamp>_<Name>.cs` — new migration class
- `Infrastructure/Persistence/<Entity>Configuration.cs` — updated column/index config
```

#### `## Algorithm`

Numbered steps. Each step is atomic and verifiable. No vague steps like "understand the code" — replace with "Read `Core/Models/<Entity>.cs` and list the changed properties."

```markdown
## Algorithm

1. Read the source files listed in Input Artifacts.
2. Identify the exact schema change from the user message.
3. Check the latest migration in `Migrations/` to determine the timestamp and naming convention.
4. Write the migration Up/Down methods.
5. Update the EF configuration to reflect the change.
6. Run `dotnet build Infrastructure/` to verify compilation — fix any errors before stopping.
```

#### `## Rules`

Hard constraints. Use a `Never do` list for things that would silently corrupt output.

```markdown
## Rules

- Always run a build or test step as the final action to verify output compiles.
- Follow naming conventions from existing migrations in the project.
- Never remove a column in `Down()` if the migration was a rename — use a rename instead.
- Never modify files outside the declared Output Artifacts without explicit user approval.
- If the schema change is ambiguous, ask one clarifying question before writing any code.
```

---

## Step 5 — Quality Gate

Every agent **must** include a verification step as the last item in its Algorithm. The type of verification depends on the agent's output:

| Output type | Verification step |
|-------------|-------------------|
| .NET code | `dotnet build <project>/` |
| Tests | `dotnet test <test-project>/` |
| JSON/config | Parse/validate the output structure |
| Documentation | Re-read the output and check all required sections are present |
| No buildable output | Explicitly state: "Summarize what was produced and confirm it matches Output Artifacts" |

Never omit this step. An agent that produces unverified output forces the user to debug it manually.

---

## Step 6 — Reference Skills (Optional)

If the agent's work overlaps with an existing skill, load it explicitly inside the Algorithm:

```markdown
## Algorithm

1. **Read the relevant skill before writing any code:**
   `.claude/skills/testing/SKILL.md`
   Follow all naming conventions, anti-patterns, and patterns defined there.
2. ...
```

Skills available in this project (check `CLAUDE.md` for the current list):
- `testing` — NUnit test conventions, AAA pattern, anti-patterns
- `api-conventions` — endpoint, DTO, validator, auth patterns
- `mappers` — ToDomain / ToEntity / ToDto mapping patterns

---

## Step 7 — Register the Agent

After writing the agent file, add it to `CLAUDE.md` under `## Available Agents`:

```markdown
<agent>
  <name>your-agent-name</name>
  <description>One sentence. What it generates and when to use it.</description>
  <location>.claude/agents/your-agent-name.md</location>
</agent>
```

---

## Complete Example

Below is a minimal but complete agent definition demonstrating all required sections.

```markdown
---
name: migration-writer
description: >
  Writes EF Core migrations for the NewsParser Infrastructure project.
  Use when a schema change needs to be applied: "add column to Article",
  "create index on Event.PublishedAt", "rename field in RawArticle".
tools: Read, Glob, Grep, Write, Edit, Bash
model: sonnet
---

## Role

You write EF Core migration files and update entity configurations for the NewsParser
Infrastructure project. You do NOT design data models, write tests for migrations,
or modify domain classes in `Core/`.

## Input Artifacts

- `Core/Models/<Entity>.cs` — domain model being changed
- `Infrastructure/Persistence/<Entity>Configuration.cs` — current EF config
- `Infrastructure/Persistence/Migrations/` — latest migration for naming/timestamp context
- User message: description of the desired schema change

## Output Artifacts

- `Infrastructure/Persistence/Migrations/<timestamp>_<Name>.cs` — new migration class
- `Infrastructure/Persistence/<Entity>Configuration.cs` — updated EF config (if needed)

## Algorithm

1. Read `Core/Models/<Entity>.cs` and the current `<Entity>Configuration.cs`.
2. Read the most recent migration file to determine naming and timestamp conventions.
3. Confirm the exact change with the user if the description is ambiguous.
4. Write the migration `Up()` and `Down()` methods.
5. Update the EF configuration to match.
6. Run `dotnet build Infrastructure/` — fix any errors before stopping.

## Rules

- Always implement both `Up()` and `Down()`.
- Never modify files in `Core/` — that is out of scope.
- Never use `MigrationBuilder.Sql` for structural changes that EF can express natively.
- If `Down()` is destructive (data loss), add a comment warning the user.
```

---

## Checklist Before Saving

- [ ] Frontmatter has `name`, `description` (with example triggers), `tools`, `model`
- [ ] `## Role` states what the agent does AND does not do
- [ ] `## Input Artifacts` lists all files read
- [ ] `## Output Artifacts` lists all files written
- [ ] `## Algorithm` has numbered, atomic steps
- [ ] `## Rules` includes a never-do list
- [ ] Last Algorithm step is a verification/build step
- [ ] Relevant skills are referenced by path
- [ ] Agent registered in `CLAUDE.md`
