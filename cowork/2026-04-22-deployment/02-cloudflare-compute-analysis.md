# 02 — Cloudflare Compute Analysis

**Research question:** Can Cloudflare host the .NET backend too? The user is already using Cloudflare for Pages, DNS, and R2.

## TL;DR

No — not for a running .NET backend, at least not comfortably. Cloudflare is excellent for the *edge* layer of NewsParser but is structurally a poor fit for both the API and the 24/7 Worker.

## Cloudflare's compute products, evaluated

### Cloudflare Workers — JavaScript only, request-scoped

**Runtime:** V8 isolates. JavaScript, TypeScript, WebAssembly. **No .NET CLR.** No ASP.NET Core host, no `dotnet` process.

- Community workarounds exist (compile F# to JS via Fable), but that means rewriting the backend, not running existing .NET 10 code.
- Workers are request-scoped: 128 MB memory per isolate, up to 5 min CPU time on paid plan.
- Long-running *duration* is possible (wall-clock while client holds connection), but that's a request handler, not a continuous background process.
- Cloudflare's own recommendation for long tasks: Workers + Queues + Workflows — still JavaScript-based.

**Verdict:** Fundamentally cannot run ASP.NET Core or a .NET `BackgroundService`. Not an option for the API or the Worker.

### Cloudflare Containers — runs Docker, but designed for bursts

Launched June 2025. Runs any `linux/amd64` Docker image, including .NET. Fronted by a Worker that routes requests to the container.

- **Pricing:** billed per 10 ms of active runtime. Included allowance on the $5/mo Workers Paid plan.
- **Lifecycle:** ephemeral by design. Containers start on request, handle traffic, and sleep after a timeout (`sleepAfter`).
- **Cold start:** 2–3 s in beta.
- Cloudflare docs explicitly state: *"not ideal for true always-on workloads"* and *"better suited for bursty traffic patterns rather than continuous background processing."*

**Verdict for the Worker:** The NewsParser Worker is continuous background processing by design — it wakes every N seconds (not on HTTP request), polls the DB, calls AI services, publishes. That's exactly what Cloudflare Containers is not optimized for. You'd have to:

1. Pay $5/mo Workers Paid minimum.
2. Pay per-10-ms for keeping a container awake 24/7 — defeating the per-request billing model, ending up more expensive than Railway or Fly.io flat-rate machines.
3. Either hack `renewActivityTimeout()` to prevent sleep, or re-architect the Worker as Cron Triggers + Queues in JavaScript.

**Verdict for the API:** Could work in principle (API is request-driven), but splitting API → Cloudflare and Worker → somewhere else gives you two deployment targets, two billing surfaces, cross-provider networking, for no clear gain.

### Durable Objects / Pages Functions

Also V8 isolates under the hood. Same "no .NET" constraint.

## What Cloudflare *is* great for in this architecture

Keep Cloudflare for what it's genuinely best at:

- **Cloudflare Pages** — React SPA. Free, unlimited bandwidth, EU edge.
- **Cloudflare DNS + CDN + WAF** — free DDoS protection and edge security in front of the API wherever it's hosted.
- **Cloudflare R2** — S3-compatible object storage, already in use for `CloudflareR2` settings in NewsParser.
- **Cloudflare Tunnel (`cloudflared`)** — run the daemon on your API VM; the API becomes reachable at `api.yourdomain.com` without opening inbound ports, without a static IP, without running Let's Encrypt yourself. **This is the piece that makes the Oracle Cloud VM plan elegant.**

## Recommended architecture (Cloudflare-friendly)

```
React SPA         → Cloudflare Pages (free)
DNS + TLS + WAF   → Cloudflare (free, already yours)
Object storage    → Cloudflare R2 (existing)
API + Worker      → Oracle Cloud Always Free VM (or Hetzner ~€5/mo)
                    reachable via Cloudflare Tunnel (free)
Postgres          → existing infra
```

This gives you Cloudflare handling everything it's excellent at, while the .NET processes run on a real Linux host that can actually execute `dotnet Worker.dll` under `systemd`. You don't expose ports, you don't manage TLS, you don't pay for request-driven compute kept awake artificially.

## Sources

- [Cloudflare Workers — overview](https://workers.cloudflare.com/)
- [Cloudflare Workers — limits (2026)](https://developers.cloudflare.com/workers/platform/limits/)
- [Run Workers for up to 5 minutes of CPU-time (changelog)](https://developers.cloudflare.com/changelog/post/2025-03-25-higher-cpu-limits/)
- [Cloudflare Containers — Overview](https://developers.cloudflare.com/containers/)
- [Cloudflare Containers — Pricing](https://developers.cloudflare.com/containers/pricing/)
- [Cloudflare Containers — everything you need to know (Sliplane)](https://sliplane.io/blog/cloudflare-released-containers-everything-you-need-to-know)
- [Containers are coming to Cloudflare Workers (June 2025 announcement)](https://blog.cloudflare.com/cloudflare-containers-coming-2025/)
- [Top Cloudflare Containers alternatives in 2026 — Northflank](https://northflank.com/blog/top-cloudflare-containers-alternatives)
- [Blazor on Cloudflare Pages](https://developers.cloudflare.com/pages/framework-guides/deploy-a-blazor-site/)
