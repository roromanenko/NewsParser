# 04 — Trigger-Based Serverless for the Worker (Architectural Pivot)

**Research question:** If the root cause blocking every free option is the 24/7 worker requirement, can we replace always-on workers with trigger-based scheduled execution and go back to truly free?

**Answer:** Yes, and **AWS now supports .NET 10 on Lambda natively** (announced January 2026) — which flips the cost picture.

## The key insight

NewsParser's workers are **already** interval-based polling. Look at `Worker/appsettings.Development.json`:

```
RssFetcher.IntervalSeconds                          = 60
ArticleProcessing.AnalyzerIntervalSeconds           = 60
ArticleProcessing.GeneratorIntervalSeconds          = 60
ArticleProcessing.PublicationGenerationIntervalSeconds = 60
ArticleProcessing.PublicationWorkerIntervalSeconds  = 30
PublishingWorker.IntervalSeconds                    = 30
```

Each worker wakes every N seconds, queries the DB for pending items, processes a batch (typically 10), and sleeps. That's semantically identical to *"run this on cron every N seconds."* The only reason we were paying for 24/7 compute was the `while (!ct.IsCancellationRequested) { ...; await Task.Delay(interval); }` loop inside each `BackgroundService`. Move scheduling out of the process and onto the platform and you can run each worker as a scheduled Docker/Lambda invocation.

## Four serious trigger-based options

### 1. AWS Lambda (.NET 10) + EventBridge Scheduler — $0/month — **Recommended**

**Big news (Jan 2026):** AWS added first-class .NET 10 support to Lambda, both as a managed runtime and as a container base image. .NET 10 NativeAOT on ARM64 Graviton3 gets **sub-50 ms cold starts** — not a concern at a 30–60 s cadence.

**Permanent free tier (not a 12-month trial):**
- Lambda: 1,000,000 requests/month + 400,000 GB-seconds/month compute, forever.
- EventBridge Scheduler: 14,000,000 free invocations/month.

**Workload math for NewsParser:**
- 6 workers × 1 tick/min × 43,200 min/mo = ~260,000 invocations/mo → ~2.5% of Lambda's free request allowance.
- ~2 sec avg execution × 256 MB = ~130,000 GB-seconds/mo → ~32% of Lambda's free compute allowance.
- EventBridge: 6 scheduled rules × ~43,200 invocations = ~260,000/mo → ~2% of the 14M free.

**All within the permanent free tier with >3× headroom.**

Downside: adds AWS to the stack. The AWS console is less friendly than Railway or Cloudflare, but the price and runtime support are unbeatable.

EU regions: `eu-west-1` (Ireland), `eu-central-1` (Frankfurt), `eu-south-2` (Madrid).

### 2. Google Cloud Run Jobs + Cloud Scheduler — $0/month

- Runs any Docker image (your existing Worker image works as-is).
- Free tier: 180k vCPU-seconds + 360k GiB-seconds + 2M requests per month.
- Google's own pricing calculator confirms: a job running hourly at 1 vCPU / 512 MiB for 60 sec = $0.
- **Gotcha:** Cloud Scheduler gives 3 free jobs per *billing account*, then $0.10/job/mo past that. For 6 workers, you either consolidate into ≤3 schedules or pay $0.30/mo. Not a real constraint.

EU regions: `europe-west1` (Belgium), `europe-west3` (Frankfurt), `europe-west4` (Netherlands), `europe-north1` (Finland), etc.

### 3. Azure Container Apps Jobs (Consumption) — $0/month

- Native cron schedule support (`*/1 * * * *` style).
- Runs any container image.
- Free tier: 180k vCPU-seconds + 360k GiB-seconds + 2M requests per **subscription** per month (same envelope as Cloud Run).
- Request charges don't apply to Jobs (they have no ingress).
- Best .NET-native DX: Azure tooling plays extremely well with .NET.
- `az containerapp job create --trigger-type Schedule --cron-expression ...`

EU regions: West Europe (Amsterdam), North Europe (Dublin), Sweden Central, France Central.

### 4. Cloudflare Containers + Cron-Triggered Worker — $5/month

Pattern: a tiny Cloudflare Worker runs on cron (Cloudflare Cron Triggers), invokes a Cloudflare Container that executes your .NET image for one tick, container scales to zero.

- This is literally what Cloudflare Containers were designed for — they're optimized for request/cron-triggered, not always-on.
- Stays entirely inside the Cloudflare ecosystem (already in use for Pages + R2 + DNS).
- Cost: $5/mo Workers Paid plan; included container allowance likely covers this workload.
- Not free, but maximum "one dashboard, one bill" comfort.

### Runner-up: GitHub Actions scheduled workflows

- 2,000 free minutes/mo on private repos.
- Minimum interval: 5 minutes.
- **Reliability caveat:** scheduled workflows are best-effort, can be delayed or skipped during GitHub load; multiple 2026 community threads report scheduled workflows not firing on private repos. Workable for dev/pre-production but *not* production-grade for a news pipeline.

## Cost comparison

| Option | Workers (6, trigger-based) | API (always-on) | UI | Total |
|---|---|---|---|---|
| **AWS Lambda + EventBridge** | $0 (within free tier) | OCI free VM or Lambda-ized | Cloudflare Pages | **$0/mo** |
| **GCP Cloud Run Jobs** | $0 | OCI free VM or Cloud Run | Cloudflare Pages | **$0/mo** |
| **Azure Container Apps Jobs** | $0 | OCI free VM or ACA | Cloudflare Pages | **$0/mo** |
| **Cloudflare Containers + Cron** | within Workers Paid | same | Cloudflare Pages | **$5/mo** |
| **GitHub Actions scheduled** | $0 (within 2k min) | OCI free VM | Cloudflare Pages | **$0/mo** (not production-grade) |

## Refactor required in the NewsParser codebase

Modest. Each `BackgroundService` in `Worker/` has an inner "do one pass" block wrapped in a `while` loop. You pull the inner block into a method, and keep the `BackgroundService` as a thin wrapper for local dev:

```csharp
public class RssFetcherOneShot
{
    // existing single-pass logic: fetch feeds, insert RawArticles, return.
    public async Task RunOnceAsync(CancellationToken ct)
    {
        // ...
    }
}

// Unchanged behavior locally — just delegates to the one-shot:
public class RssFetcherBackgroundService(RssFetcherOneShot inner, IOptions<RssFetcherOptions> opt)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await inner.RunOnceAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(opt.Value.IntervalSeconds), ct);
        }
    }
}
```

Add a CLI entry point in `Worker/Program.cs`:

```csharp
// Usage: dotnet Worker.dll tick rss-fetcher
//        dotnet Worker.dll tick analyzer
//        dotnet Worker.dll tick classifier
//        dotnet Worker.dll tick publisher
if (args is [ "tick", var workerName ])
{
    using var host = BuildHost(args);
    var worker = host.Services.GetRequiredKeyedService<IOneShotWorker>(workerName);
    await worker.RunOnceAsync(CancellationToken.None);
    return 0;
}
return await host.RunAsync();  // legacy mode: run all as BackgroundServices
```

For AWS Lambda specifically, use `Amazon.Lambda.RuntimeSupport` with a handler that reads the worker name from the event payload:

```csharp
public class Function
{
    private static readonly IHost Host = BuildHost();

    public static async Task Main() =>
        await LambdaBootstrapBuilder.Create<WorkerEvent>(HandleAsync).Build().RunAsync();

    public static async Task HandleAsync(WorkerEvent evt, ILambdaContext ctx)
    {
        var worker = Host.Services.GetRequiredKeyedService<IOneShotWorker>(evt.Worker);
        await worker.RunOnceAsync(CancellationToken.None);
    }
}

public record WorkerEvent(string Worker);
```

Each EventBridge Schedule rule targets the same Lambda with a different `{"Worker":"..."}` payload.

**Win:** local dev stays a single 24/7 process (existing `BackgroundService`s as thin wrappers). Production deploys each worker as a scheduled container. Best of both worlds.

## Recommendation

**AWS Lambda (.NET 10 container images) + EventBridge Scheduler for workers, Oracle Cloud free VM for the API, Cloudflare Pages for UI.**

- All-in cost: **$0/mo** + domain (~$10/yr at Cloudflare Registrar).
- EU region: `eu-west-1` (Ireland) or `eu-central-1` (Frankfurt).

**Why Lambda over Cloud Run or ACA Jobs:**
- Permanent free tier (not 12-month trial).
- Native .NET 10 runtime support since Jan 2026 — no container packaging hack needed.
- Sub-50 ms cold starts with NativeAOT → zero latency concern for 30-sec-cadence PublicationWorker.
- 3× headroom inside the free tier.

**Why not Cloudflare Containers:** the technical fit is fine, but $5/mo pays for a problem AWS solves for free. Keep Cloudflare for what it already gives you excellently (R2, Pages, DNS, Tunnel).

## Next actions

- ☐ Decide: refactor Worker for trigger mode (this doc) vs. keep always-on on OCI VM (`deployment-plan.md`).
- ☐ If refactoring: create a feature branch, extract `RunOnceAsync` from each `BackgroundService`, register each as a keyed service, add the CLI/Lambda dispatcher, add tests for the one-shot methods.
- ☐ Draft `deployment-plan-lambda.md` covering: Dockerfile for Lambda container image, AWS CLI commands to create the function + IAM role + EventBridge rules, GitHub Actions workflow for deploy, secret management via AWS Systems Manager Parameter Store or Secrets Manager.

## Sources

- [AWS Weekly Roundup — AWS Lambda for .NET 10 (January 12, 2026)](https://aws.amazon.com/blogs/aws/aws-weekly-roundup-aws-lambda-for-net-10-aws-client-vpn-quickstart-best-of-aws-reinvent-and-more-january-12-2026/)
- [AWS Lambda Pricing Calculator & Cost Guide (Apr 2026)](https://costgoat.com/pricing/aws-lambda)
- [Amazon EventBridge Scheduler pricing](https://aws.amazon.com/eventbridge/pricing/)
- [Schedule AWS Lambda With Amazon EventBridge Scheduler (codewithmukesh)](https://codewithmukesh.com/blog/schedule-aws-lambda-with-amazon-eventbridge-scheduler/)
- [Google Cloud Run pricing](https://cloud.google.com/run/pricing)
- [Google Cloud Scheduler pricing](https://cloud.google.com/scheduler/pricing)
- [Azure Container Apps — Jobs (Microsoft Learn)](https://learn.microsoft.com/en-us/azure/container-apps/jobs)
- [Azure Container Apps — Pricing](https://azure.microsoft.com/en-us/pricing/details/container-apps/)
- [Cloudflare Containers — Pricing](https://developers.cloudflare.com/containers/pricing/)
- [GitHub Actions — Billing and usage](https://docs.github.com/en/actions/concepts/billing-and-usage)
