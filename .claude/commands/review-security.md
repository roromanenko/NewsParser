---
description: Run the security review pass over a specified scope
argument-hint: [folder-or-file]
---

Use the **security-reviewer** agent to review the following scope: $ARGUMENTS

If no scope was provided, ask me which folder or files to review before
starting. Suggest sensible defaults based on `docs/reviews/PROJECT_MAP.md`, for
example: `Controllers/`, `Areas/*/Controllers/`, `App_Start/`, `Web.config`,
`appsettings.json`.

Requirements:
1. Read `CLAUDE.md` and `docs/reviews/PROJECT_MAP.md` first.
2. Append findings to `docs/reviews/security-findings.md` in the format defined
   in the agent's instructions. Do not overwrite existing findings.
3. When done, print a one-paragraph summary: total findings by severity,
   and the top 3 issues I should look at first.
4. Do not modify any source code.
