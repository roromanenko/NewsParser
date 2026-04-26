# 03 — Paid-But-Cheap Comfort Options

**Research question:** If not fully free, what's the cheapest *comfortable* (low-sysadmin-overhead) solution for running .NET services?

## 2026 pricing heads-up

**Hetzner is raising prices ~30–35% from April 1, 2026.** The €3.79 CX22 / CAX11 quoted in older articles is now roughly €4.90–€5.10/month. Still cheap, just less so than before.

## The three serious contenders

### 1. Railway Hobby — $5/month flat. The comfort king.

- $5/month base includes $5 of resource credits.
- Both API and Worker count as **separate services on the same Hobby plan** — no per-service charge.
- Realistic usage estimate for two small idle-ish .NET services hitting external Postgres: ~$2–4/month in resources, fully absorbed by the credit.
- EU region: Amsterdam.
- DX: connect GitHub → Railway auto-detects .NET → builds from Dockerfile or Nixpacks → deploys on every `main` push. Separate services on one canvas, shared env vars UI, free TLS on auto-subdomains, one-click custom domains.
- **Zero sysadmin**: no VM, no `systemd`, no `nginx`, no `cloudflared`, no `ufw`.

This is "it just works" at this price tier.

### 2. Hetzner Cloud CAX11 (ARM) or CX22 (x86) — ~€5/month. Best resources-per-euro.

- 2 vCPU + 4 GB RAM + 40 GB NVMe + 20 TB traffic.
- EU-only (Germany, Finland for CAX11; more regions for CX22).
- Roughly 8× the resources of Railway's $5 tier.
- Catch: you still own a Linux VM. Everything in `deployment-plan.md` (systemd, `cloudflared`, deploy workflow) applies — just swap Oracle for Hetzner.
- If the app grows, no ceiling — run more stuff on the same box.

**Not the most comfortable, but the cheapest way to run *a lot* of .NET in EU.**

### 3. Fly.io — pay-as-you-go, ~$4–6/month. Middle ground.

- No base fee; pay per-second per machine.
- Small shared-CPU 256 MB machine ≈ $1.94/mo. API + Worker = two machines ≈ $4–6/mo minimum.
- Multiple EU regions: Frankfurt, Amsterdam, Warsaw, Paris, Madrid.
- DX: `fly deploy` from your laptop with a Dockerfile. No VM to own.
- Downsides vs Railway: less predictable pricing once you add a volume ($0.15/GB/mo even when stopped) or dedicated IPv4; more Docker-native config style.

## Options to skip at this price tier

| Option | Monthly cost | Why skip |
|---|---|---|
| **Render** | $14/mo ($7 web + $7 worker) | 2× Railway's price for no meaningful gain at this scale |
| **DigitalOcean App Platform** | $10/mo ($5 × 2 services) | 2× Railway, DX isn't noticeably better |
| **Azure Container Apps** | Variable | Flexible but unpredictable cost and tooling overhead — not comfort-oriented (outside the Jobs/Consumption model, see `04-trigger-based-serverless.md`) |
| **AWS Fargate** | Variable | Same issues as ACA for always-on workloads |
| **Heroku** | $14/mo ($7 basic × 2) | No free tier; expensive at this scale |

## Cost comparison summary

| Option | Monthly | Sysadmin | EU region | Notes |
|---|---|---|---|---|
| **Railway Hobby** | $5 | None | Amsterdam | Best DX, 2 services on one plan |
| **Hetzner CAX11** | €5 | Full Linux ownership | Germany/Finland | Best resources/€; same plan as Oracle free, different box |
| **Fly.io (small)** | ~$4–6 | Minimal (Docker only) | 5 EU regions | Pay-per-second flexibility |
| Render | $14 | None | Frankfurt | Overpriced for this shape |
| DO App Platform | $10 | None | Frankfurt/Amsterdam | Fine but beaten on price |

## Recommendation

**Railway Hobby + Cloudflare Pages = $5/mo total** is the pick for "cheap AND comfort."

Rationale:
- You don't need 4 GB of RAM for two idle .NET services against an external DB (what Hetzner gives you extra).
- The recurring-time savings from not owning a VM (no patching, no systemd, no tunnel, no firewall rules) is worth more than €0 price delta.
- The comfort delta between Railway and Hetzner is larger than the price delta.

If later you outgrow Railway's $5 credit (AI calls balloon, heavy log I/O), your options:
- Stay on Railway, pay overage (~$10–15/mo).
- Migrate to Hetzner for flat €5, eat the sysadmin cost.

Either is fine. Start with Railway.

## What the user asked next

"If the root cause is full-time workers, let's check solutions which could start background workers by trigger." → see `04-trigger-based-serverless.md` — this research flipped the recommendation again.

## Sources

- [Railway — Pricing Plans](https://railway.com/pricing)
- [Railway — Pricing docs](https://docs.railway.com/reference/pricing/plans)
- [Railway Pricing 2026 breakdown](https://thesoftwarescout.com/railway-pricing-2026-plans-costs-is-it-worth-it/)
- [Fly.io — Pricing](https://fly.io/pricing/)
- [Fly.io — Resource Pricing docs](https://fly.io/docs/about/pricing/)
- [Fly.io Pricing 2026 breakdown](https://costbench.com/software/developer-tools/flyio/)
- [Hetzner — Statement on April 1 2026 price adjustment](https://www.hetzner.com/pressroom/statement-price-adjustment/)
- [Hetzner Cloud Review 2026 — Better Stack](https://betterstack.com/community/guides/web-servers/hetzner-cloud-review/)
- [CAX11 by Hetzner Cloud — Spare Cores](https://sparecores.com/server/hcloud/cax11)
- [DigitalOcean App Platform — Pricing](https://www.digitalocean.com/pricing/app-platform)
- [Railway vs Render (2026) — Northflank](https://northflank.com/blog/railway-vs-render)
