# Deployment Research — 2026-04-22

Research session on production deployment options for NewsParser (.NET 10 API + .NET 10 Worker + React 19 SPA). Postgres with pgvector and Cloudflare R2 file storage are already deployed elsewhere — this research covers **compute only**.

## Constraints captured from the user

- Postgres + file storage already deployed (out of scope).
- Target: free tier first; cheapest-viable if not free.
- Users in Europe → prefer EU regions / latency.
- No existing cloud accounts or credits.
- Workers must run their scheduled work reliably (originally "24/7 always-on," revised after research — see `04-trigger-based-serverless.md`).

## Document index

| File | Content |
|---|---|
| `01-hosting-landscape-overview.md` | First-pass comparison of free and cheap hosts for the full stack — Oracle Cloud, Railway, Fly.io, Render, Cloudflare Pages, etc. |
| `02-cloudflare-compute-analysis.md` | Why Cloudflare Workers / Containers **can't** host the .NET backend despite the user being deep in the Cloudflare ecosystem. |
| `03-paid-comfort-options.md` | Paid-but-cheap PaaS ranking focused on developer comfort — Railway, Hetzner, Fly.io. 2026 Hetzner price increase flagged. |
| `04-trigger-based-serverless.md` | Architectural pivot: treat the workers as cron-triggered jobs instead of 24/7 processes. AWS Lambda now supports .NET 10 natively. All-in cost drops to $0/mo. |
| `deployment-plan.md` | **Executable** step-by-step plan for the Oracle Cloud Always Free VM + Cloudflare Pages path (pre-pivot). Currently the canonical concrete plan. |

## Final recommendation snapshot

After all four research passes, there are two good answers depending on what the user values:

**Option 1 — Zero-refactor, $0/mo:**
Oracle Cloud Always Free VM (API + Worker via systemd) + Cloudflare Tunnel + Cloudflare Pages (UI). Fully documented in `deployment-plan.md`. Biggest risk: Ampere A1 capacity in EU regions.

**Option 2 — Modest refactor, $0/mo, architecturally cleaner:**
AWS Lambda (.NET 10, native runtime since Jan 2026) + EventBridge Scheduler for each worker, Oracle Cloud VM (or Lambda) for the API, Cloudflare Pages for UI. Requires pulling `RunOnceAsync` out of each `BackgroundService` — see `04-trigger-based-serverless.md` for the sketch. No 24/7 process pressure; workers wake on schedule, process a batch, exit.

**Option 3 — Paid, maximum comfort, $5/mo:**
Railway Hobby (API + Worker as two services) + Cloudflare Pages (UI). Git-push deploys, no sysadmin. See `03-paid-comfort-options.md`.

## Shared across all options

- UI → **Cloudflare Pages** (unlimited bandwidth free tier, EU edge, commercial use allowed). Not seriously contested by any alternative for this specific use case.
- DNS + TLS + WAF → **Cloudflare** (free).
- Secrets → **GitHub Actions encrypted secrets**, injected at deploy time. Never commit, never bake into images.

## Next actions decided

- ☐ Convert `deployment-plan.md` into a formal ADR in `docs/architecture/decisions/` via the `architect` agent.
- ☐ Decide between Option 1 vs Option 2 before execution — Option 2 is the more durable architecture but needs the refactor.
- ☐ If Option 2 is chosen: draft `deployment-plan-lambda.md` with the refactor sketch, Lambda function handler, EventBridge rules, IAM policies, and GitHub Actions flow.
