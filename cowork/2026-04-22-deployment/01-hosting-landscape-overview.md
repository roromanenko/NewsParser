# 01 — Hosting Landscape Overview (First Pass)

**Research question:** What are the free or cheapest viable production hosts for NewsParser's compute (.NET 10 API + .NET 10 Worker + React 19 SPA), given Postgres + R2 are already deployed and users are in Europe?

## Frontend — React 19 SPA

Clear winner: **Cloudflare Pages.** Unlimited bandwidth, unlimited requests, 500 builds/month, 100 sites, EU edge, commercial use allowed, no surprise bills (over-limit builds queue rather than charge).

Runners-up evaluated:
- **Vercel Hobby** — comparable DX, but **prohibits commercial use** on the free plan. Pro is $20/seat/mo. Out.
- **Netlify** — moved to credit-based pricing (300 credits/mo). More restrictive than Cloudflare Pages for comparable workloads.
- **Render (static)** — works, but less generous than Cloudflare Pages on bandwidth.

Verdict: **Cloudflare Pages**, no serious competition for a React SPA in 2026.

## Backend — .NET 10 API + .NET 10 Worker (24/7 constraint, original framing)

The 24/7 worker requirement eliminates most free tiers. Summary:

| Option | Free tier for 24/7 worker? | EU region | Notes |
|---|---|---|---|
| **Oracle Cloud Always Free** | ✅ Yes — 4 OCPU / 24 GB RAM Ampere A1 forever | ✅ Frankfurt, Amsterdam, Stockholm, Milan, Madrid, Paris | Ampere A1 capacity contested in popular regions; .NET 10 ARM64 support is excellent |
| **Railway** | ❌ $5 trial credit one-time; then usage-based (~$5+/mo minimum) | ✅ Amsterdam | Best DX; separate services for API + Worker included |
| **Render** | ❌ Free web service sleeps; Background Worker starts at $7/mo | ✅ Frankfurt | ~$14/mo for API + Worker — most expensive PaaS for this shape |
| **Fly.io** | ❌ No real free tier since 2024 (2h trial only) | ✅ Multiple EU | ~$2/mo per small machine; pay-as-you-go |
| **Heroku** | ❌ No free tier since 2022 | ✅ EU | Eco dyno sleeps; Basic is $7/dyno × 2 services |
| **Northflank** | ⚠️ Persistent free tier exists but tight | ✅ | Container-based; worth evaluating for tight free workloads |
| **Azure / AWS free** | ❌ 12-month trial, then expires | ✅ | Not a long-term free tier |

Verdict for the "stay-free, accept sysadmin" path: **Oracle Cloud Always Free Ampere A1 VM.** Runs both .NET services on one box via `systemd`, reached through **Cloudflare Tunnel** (no inbound ports, no TLS management, free).

## Truly free recommended architecture (first pass)

```
React SPA         → Cloudflare Pages (free)
DNS + TLS + WAF   → Cloudflare (free)
API + Worker      → Oracle Cloud Always Free VM (free forever)
                    connected via Cloudflare Tunnel (free)
Postgres + R2     → existing infra (out of scope)
```

Total compute cost: **€0/month.**

## Known caveats from this pass

1. Oracle Ampere A1 capacity is reportedly hard to get in popular EU regions (Frankfurt worst). Recommended region order: Madrid → Stockholm → Milan → Paris → Amsterdam → Frankfurt. Worst case, scripted retries work.
2. Running both services on a single VM means one-host blast radius. For v1, acceptable; revisit if the app grows.
3. Oracle Always Free requires a credit card for identity verification; Oracle is known to occasionally reclaim idle resources — keep both services actively running and set up health checks.

## What the user asked next

"Can Cloudflare host the backend too? Most of my infrastructure is already there." → see `02-cloudflare-compute-analysis.md`.

## Sources

- [Oracle Cloud Free Tier — Always Free Resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm)
- [Oracle Cloud Free VPS Review 2026](https://space-node.net/blog/oracle-vps-free-tier-review-2026)
- [Hosting & PaaS Free Tier Comparison 2026](https://agentdeals.dev/hosting-free-tier-comparison-2026)
- [Cloudflare Pages vs Netlify vs Vercel — Static Hosting 2026](https://danubedata.ro/blog/cloudflare-pages-vs-netlify-vs-vercel-static-hosting-2026)
- [Railway vs Render 2026](https://thesoftwarescout.com/railway-vs-render-2026-best-platform-for-deploying-apps/)
- [Fly.io vs Railway 2026](https://thesoftwarescout.com/fly-io-vs-railway-2026-which-developer-platform-should-you-deploy-on/)
- [7 Best Render alternatives — Northflank](https://northflank.com/blog/render-alternatives)
