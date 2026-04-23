---
description: Consolidate all review findings into a single prioritized action plan
---

Read every file matching `docs/reviews/*-findings.md` and produce
`docs/reviews/ACTION_PLAN.md` with three sections:

## Critical (fix now)
Things that are active bugs, exploitable security issues, data corruption
risks, or reliability hazards.

## Warnings (fix this sprint)
Real problems that will bite under load or over time but are not actively
on fire.

## Improvements (backlog)
Hygiene, modernization, cleanups.

## Per-item format

Each item must include:
- **Source:** which findings file and section it came from
- **File:** `path/to/File.cs:123`
- **Problem:** one sentence
- **Proposed fix:** one or two sentences
- **Effort:** S / M / L
- **Category:** security / data / architecture / razor / frontend / dependencies

## Rules

1. Deduplicate. If the same file:line shows up in multiple findings files
   (for example, SQL injection flagged by both security and data reviewers),
   merge into one item and list both sources.
2. Within each section, order by estimated impact first, then by effort
   (high-impact / low-effort at the top).
3. Do not invent findings. Only consolidate what is in the existing files.
4. If a findings file is missing, note it at the top of the action plan
   under "Coverage gaps" and list which review passes have not been run.
5. At the end, print:
   - Total counts per section
   - The top 5 items overall, ranked by (impact / effort)
   - Any category that is suspiciously empty (may indicate a review pass
     that should be rerun with broader scope)

Do not modify any source files.
