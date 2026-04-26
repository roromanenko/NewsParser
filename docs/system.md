# NewsParser — System Description

_This document describes what the system does and why. It is the starting point for any strategic discussion with Claude. For technical structure, see `CLAUDE.md`._

---

## Purpose

NewsParser is an AI-powered media platform that automates the full cycle of news production: from collecting raw information across multiple sources, through AI processing and editorial review, to publishing formatted content on target platforms.

The system reduces the manual workload of a news editorial team by automating repetitive tasks — monitoring sources, deduplication, categorisation, article generation — while keeping a human editor in control of what gets published.

**Current use:** Solo operator + few editors (internal). **Target:** A productised platform sellable to external media teams and publishers.

---

## Who Uses It

| Role                  | What they do in the system                                                                                                                 |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| **Admin**             | Manages sources, publish targets, users; can merge events, reclassify articles, change event status; reviews AI cost and operations dashboard |
| **Editor**            | Reviews generated events, approves or rejects them before publication; reviews, edits, and regenerates publication content with feedback   |
| _(future)_ **Client** | External media team operating their own instance or tenant                                                                                 |

---

## Core User Flow

```
1. System monitors configured sources (RSS feeds, Telegram channels)
   → New raw articles arrive automatically, no manual action needed
   → For RSS, the system additionally fetches the full article page to enrich
     content beyond the feed snippet (additive over RSS-supplied data)
   → Deduplication (fuzzy, SourceId + ExternalId)
   → Media (images, video) from RSS enclosures, og:image / twitter:image,
     and Telegram channels ingested to Cloudflare R2

2. AI pipeline processes each raw article:
   → Analysis: category, tags, sentiment, language, summary (Gemini)
   → Key facts extraction: 3-7 structured fact statements per article (Claude Haiku)
   → Classification into an existing Event or creation of a new one
   → Event title generated or updated in the configured output language (Claude Haiku)
   → Event summary updated and intrinsic importance label inferred (Claude Haiku)
   → Event importance score recomputed (article volume, distinct sources,
     short-window velocity, AI label) and tier assigned (Breaking / High / Normal / Low)
   → Article generation in the configured output language (Claude Sonnet)

3. Editor opens the CMS (React UI), reviews the generated events
   → Filters and sorts events by importance tier and recency
   → Approves → event moves to publication queue
   → Rejects → event is dropped with a reason
   (Approval is event-level; individual articles are data, not the approval unit)

4. System generates platform-specific publication content for each active Publish Target
   → Telegram: formatted post with hashtags and reply threading
   → Editor can review, edit, and format the generated text before it goes out
   → Editor can request a regeneration with free-form feedback (e.g. "make it shorter",
     "emphasize the second source") — the worker re-runs the generator using the feedback
   → Editor can upload custom media (cover images, composed graphics) attached to the
     publication in addition to the event-pool media
   → Editor approves or rejects the publication; rejected content can be regenerated
   → Website / Instagram: adapted format (planned)

5. System publishes approved publications to configured targets automatically
   → PublishLog records the external message ID for reply threading
   → Media selected by the editor (event-pool + custom uploads) is attached to the post

6. Every AI call is logged with token counts, latency, and computed USD cost
   → Admins can review spend by provider, worker, model, article, or correlation id
     in the AI Operations dashboard
```

---

## What the System Does

### Source Monitoring

- Parses RSS feeds on a configurable interval
- For RSS articles, performs a follow-up full-page scrape to enrich content beyond the feed snippet; failures are isolated per article and do not break the batch
- Monitors Telegram channels
- Validates incoming articles: minimum length, keywords, maximum age, excluded keywords
- Deduplicates by SourceId + ExternalId (exact) and by title similarity (FuzzySharp)

### Media Management

- Ingests images and video from RSS enclosures, Media RSS, og:image / twitter:image (scraped), and Telegram channel messages
- Stores media files in Cloudflare R2; metadata (type, size, URL) persisted in the database
- Media is displayed in article and event detail views in the CMS
- Editor selects which event-pool media to attach to a publication before approval
- Editor can upload **custom media** (cover image, better-quality photo, composed graphic) directly to a publication; custom media lives alongside event-pool media in the same selection list
- Media ingestion is best-effort: a failed download does not block article processing

### AI Processing Pipeline

- **Analysis** (Gemini 2.0 Flash): extracts category, tags, sentiment, language, summary from raw article
- **Key facts extraction** (Claude Haiku): produces 3-7 structured fact statements per article, used to enrich event summaries and give the classifier richer context
- **Event classification** (Gemini embeddings + Claude Haiku for grey zone): groups articles into Events
    - similarity > 0.90 → same Event (automatic)
    - similarity 0.70–0.90 → Claude Haiku decides with full event context
    - similarity < 0.70 → new Event (automatic)
- **Contradiction detection** (Claude Haiku): flags factual conflicts between articles on the same event
- **Event title generation** (Claude Haiku): creates and updates a title in the configured output language when the event is created or its summary changes
- **Event summary update + intrinsic importance** (Claude Haiku): refreshes the event summary and emits an `intrinsic_importance` label that feeds the importance scorer
- **Event importance scoring** (pure function): combines article count, distinct source count, articles-last-hour velocity, and the AI intrinsic importance label into a base score and tier (Breaking / High / Normal / Low). Recomputed synchronously after every summary update.
- **Article generation** (Claude Sonnet 4.5): produces Title + Content in the configured output language, tailored to the event context
- **Content generation per platform** (Claude Sonnet 4.5): adapts article to each Publish Target using its SystemPrompt
- **Content regeneration with editor feedback** (Claude Sonnet 4.5): when the editor requests a regeneration, the generator runs a dedicated regeneration prompt branch that includes the editor's free-form feedback and the previous draft

### Event Graph (Living Article)

- Articles are grouped into Events representing a single real-world story
- Each new article on the same event updates the Event Summary and content
- Event importance is recalculated on every summary update; the tier and base score are surfaced in the CMS for filtering and sorting
- Contradictions between articles on the same event are tracked and surfaced to editors

### AI Cost Tracking & Operations

- Every Anthropic and Gemini call is persisted to `ai_request_log` with provider, model, operation, worker, token counts (including Anthropic cache write/read), latency, status, error message (truncated), correlation id, and article id
- USD cost is computed per-call from configurable per-million pricing (`ModelPricing` options) and stored as `NUMERIC(18,8)`
- Logging is synchronous-but-fail-safe: a DB write failure logs a warning and never breaks the AI call
- Admin-only **AI Operations dashboard** in the CMS exposes:
    - KPIs: total cost, total calls, success/error counts, average latency, token totals
    - Time series: cost / calls / tokens per day, broken down by provider
    - Breakdowns: by model and by worker
    - Paginated, filterable request log (provider, worker, model, status, search, date range)
    - Per-request detail view (full token breakdown, error message, correlation chain)

### Editorial CMS

- Editors review events with full event context visible (all articles, key facts, media, importance tier)
- Approve / Reject workflow with retry limits
- Events list supports server-side search, sort by recency or importance, and filter by importance tier
- Publication content editing: Telegram-specific formatting toolbar (bold, italic, link, code) with edit/preview toggle
- Publication regeneration with editor feedback (inline expanding panel mirroring the rejection flow, max 2000 chars)
- Custom media upload affordance on the publication detail page
- Server-side search (by title/summary) and sort (newest/oldest) for articles and events
- Admin tools: merge events, reclassify articles, resolve contradictions, change event status, AI Operations dashboard
- JWT authentication with Editor and Admin roles

### Publication

- Two-stage workflow: generation worker (AI content) → editor review/approval → publishing worker (send to platform)
- Publications move through a defined status machine: Created → GenerationInProgress → ContentReady → Approved → Published (or Rejected at review, Failed on generation error)
- Regeneration sends a `ContentReady` or `Failed` publication back to `Created` along with the editor's feedback; the generation worker picks it up on the next cycle
- Publishes to Telegram channels with full reply threading (updates as replies to original post)
- Media selected by the editor (event-pool + custom uploads) is attached to the Telegram post
- PublishLog stores external message IDs for threading
- Publish Targets are configurable: each target has its own platform, identifier, and SystemPrompt

---

## What the System Does NOT Do (Intentional Boundaries)

|Out of scope now|Notes|
|---|---|
|Website publishing|Planned; PublishTarget model is ready, publisher not implemented|
|Instagram publishing|Planned; content generation format needs definition|
|Image generation|Post-MVP (mentioned in original spec as advanced feature)|
|Voice / video generation|Post-MVP (TikTok use case)|
|Analytics (GA4, TGStat)|Post-MVP|
|Multi-tenancy / SaaS billing|Future product direction, not in current architecture|
|Automated contradiction resolution|Surfaced to editor, resolved manually|
|AI cost retention / archival|Append-only log; no partitioning or pruning policy yet|
|Regeneration history|Only the latest editor feedback and draft are retained per publication|

---

## AI Model Allocation

| Task                             | Model                      | Rationale                                       |
| -------------------------------- | -------------------------- | ----------------------------------------------- |
| Article analysis                 | Gemini 2.0 Flash           | Cost-efficient for high-volume analysis         |
| Embeddings                       | gemini-embedding-001       | Cost-efficient for high-volume analysis         |
| Event classification (grey zone) | Claude Haiku 4.5           | Cheap binary decision, sufficient quality       |
| Key facts extraction             | Claude Haiku 4.5           | Lightweight structured extraction, high volume  |
| Event title generation           | Claude Haiku 4.5           | Short output, quality sufficient                |
| Contradiction detection          | Claude Haiku 4.5           | Cheap binary/structured decision                |
| Event summary + importance label | Claude Haiku 4.5           | Lightweight structured output                   |
| Article generation               | Claude Sonnet 4.5          | High quality required for published content     |
| Platform content generation      | Claude Sonnet 4.5          | High quality required for published content     |
| Content regeneration             | Claude Sonnet 4.5          | Same generator with regeneration prompt branch  |

---

## External Dependencies

| Service                     | Purpose                                                          | Criticality                                  |
| --------------------------- | ---------------------------------------------------------------- | -------------------------------------------- |
| Google Gemini API           | Article analysis, embeddings                                     | High — pipeline stops without it             |
| Anthropic Claude API        | Classification, generation                                       | High — pipeline stops without it             |
| Telegram Bot API            | Publishing to channels                                           | High — publication stops without it          |
| Telethon (Telegram MTProto) | Parsing source channels, downloading Telegram media              | Medium — RSS fallback available              |
| Cloudflare R2               | Media file storage (RSS enclosures, scraped og:image, Telegram, custom uploads) | Medium — media ingestion degrades gracefully |
| PostgreSQL + pgvector       | Primary data store; pgvector for event classification embeddings | Critical                                     |
| Article publisher web pages | Full-page HTML scrape to enrich RSS feed snippets                | Low — scrape failure leaves the RSS-only article intact |

---

## Non-Obvious Constraints

These are things that are not visible from the code but matter for decision-making:

- **Output language is configurable** via `AnthropicOptions.OutputLanguage`. All AI-generated internal fields (summary, key facts, event title, generated article, platform content) are normalised to that language. Source-language fields (`Article.Title`, `Article.OriginalContent`, `Source.Name`) are intentionally preserved as-is.
- **Approval is event-level, not article-level.** Articles are data entities under an Event. The Event status drives the publication lifecycle; approving or rejecting an individual article has no effect on whether content gets published.
- **Publications have a strict status machine** (Created → GenerationInProgress → ContentReady → Approved → Published / Rejected / Failed). Skipping states is not supported; each transition has explicit business meaning. Regeneration is a controlled transition from `ContentReady` or `Failed` back to `Created`.
- **Event classification thresholds (0.70 / 0.90)** were manually calibrated. Do not change without metrics from a meaningful sample.
- **Event importance scoring weights, caps, and thresholds** are configured via Options classes; changing them re-tiers existing events on the next summary update only — there is no historical backfill.
- **Reply threading depends on PublishLog:** If a PublishLog entry is missing or corrupted, event updates will be published as standalone posts instead of replies. This is silent — no error is raised.
- **`ParentPublicationId` means "Telegram reply parent", not "previous regeneration".** Regeneration mutates the same publication row; do not overload the parent field with regeneration lineage.
- **Enums are stored as strings in DB.** Changing an enum value name requires a data migration, not just a code change.
- **Prompts live in files, not in code** (`Infrastructure/AI/Prompts/*.txt`). Prompt changes do not require recompilation but do require redeployment.
- **PublishTarget.SystemPrompt drives platform formatting.** The quality of platform-specific content is entirely dependent on this prompt. It is operator-configured, not hardcoded.
- **Media ingestion is best-effort.** A failed download or R2 upload does not block article ingestion; the article is saved without media. Missing media on a publication is not an error state.
- **Custom publication media is owned by the publication, not an article.** `media_files.OwnerKind` discriminates `Article` vs `Publication`; `ArticleId` and `PublicationId` are mutually exclusive (CHECK constraint). Custom media uses the R2 key prefix `publications/{publicationId}/...`.
- **Full-page scraping is purely additive over the RSS pipeline.** RSS-supplied media URLs are always preserved; scraped og:image / twitter:image entries are appended. A scrape failure leaves the article with whatever the RSS feed already provided.
- **AI cost logging never breaks the business flow.** A DB failure during cost log persistence emits a single `LogWarning` and is swallowed; the AI result is returned to the caller normally. `OperationCanceledException` is allowed to propagate so cancellation works correctly.
- **AI cost is `NUMERIC(18,8)`**, not `double` or `float`. Pricing is denominated in dollars per million tokens; tiny per-call costs (sub-cent) must be representable without precision loss.
- **`ai_request_log` has no FK to `articles`.** The audit trail must survive article deletes; `ArticleId` is nullable and unconstrained.
- **The AI Operations metrics endpoint ignores `status` and `search` filters intentionally.** KPIs and time-series aggregate across both success and error rows; only the request-list endpoint accepts those filters.

---

## Success Criteria

The system is working correctly when:

1. New articles from configured sources appear in the CMS within minutes of publication
2. Duplicate articles about the same story are grouped under one Event automatically
3. Events are tiered by importance and editors can filter / sort the list to surface what matters
4. An editor can approve an event, review the generated publication content, regenerate it with feedback if needed, approve it, and see it published to all active Telegram targets without manual action
5. Event updates (new articles on an existing event) appear as replies in Telegram threads
6. Media from source articles, og:image scrapes, and editor uploads appears in the CMS and can be attached to publications
7. Every AI call is recorded in `ai_request_log` with a non-zero `CostUsd` (zeros indicate missing pricing config) and is queryable from the AI Operations dashboard
8. The editor's rejection rate on AI-generated content is low (quality signal)
9. `WasReclassified` rate on EventArticles stays low (classification quality signal)
