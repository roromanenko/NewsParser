---
description: Audit NuGet and npm dependencies for outdated and vulnerable packages
allowed-tools: Read, Grep, Glob, Bash(dotnet list package:*), Bash(npm outdated:*), Bash(npm audit:*)
---

Use the **dependency-auditor** agent to audit this solution's dependencies.

## Pre-gathered context

- Outdated NuGet packages:
  !`dotnet list package --outdated 2>&1 | head -200 || echo "dotnet CLI unavailable or no PackageReference projects"`

- Vulnerable NuGet packages:
  !`dotnet list package --vulnerable --include-transitive 2>&1 | head -200 || echo "dotnet CLI unavailable"`

- Outdated npm packages:
  !`npm outdated 2>&1 | head -200 || echo "no package.json or npm unavailable"`

- npm audit:
  !`npm audit --omit=dev 2>&1 | head -200 || echo "no package.json or npm audit unavailable"`

## Task

Requirements:
1. Read `CLAUDE.md` and `docs/reviews/PROJECT_MAP.md` first.
2. For any `packages.config`-style projects not covered by `dotnet list`,
   read the XML manifests directly and assess versions.
3. Append findings to `docs/reviews/dependencies-findings.md` in the format
   defined in the agent's instructions. Do not overwrite existing findings.
4. When done, print a summary: counts by severity + the top 5 upgrades
   ranked by risk-reduction per unit of effort.
5. Do not modify any manifest files.
