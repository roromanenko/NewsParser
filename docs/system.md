# NewsParser — System Description

_This document describes what the system does and why. It is the starting point for any strategic discussion with Claude. For technical structure, see `CLAUDE.md`._

---

## Purpose

NewsParser is an AI-powered media platform that automates the full cycle of news production: from collecting raw information across multiple sources, through AI processing and editorial review, to publishing formatted content on target platforms.

The system reduces the manual workload of a news editorial team by automating repetitive tasks — monitoring sources, deduplication, categorisation, article generation — while keeping a human editor in control of what gets published.

**Current use:** Solo operator + few editors (internal). **Target:** A productised platform sellable to external media teams and publishers.

---

## Who Uses It

| Role                  | What they do in the system                                                                          |
| --------------------- | --------------------------------------------------------------------------------------------------- |
| **Admin**             | Manages sources, publish targets, users; can merge events, reclassify articles, change event status |
| **Editor**            | Reviews generated events, approves or rejects them before publication                               |
| _(future)_ **Client** | External media team operating their own instance or tenant                                          |

---

## Core User Flow

```
1. System monitors configured sources (RSS feeds, Telegram channels)
   → New raw articles arrive automatically, no manual action needed
   → Deduplication (fuzzy, SourceId + ExternalId)

2. AI pipeline processes each raw article:
   → Analysis: category, tags, sentiment, language, summary (Gemini)
   → Classification into an existing Event or creation of a new one
   → Article generation in Ukrainian (Claude Sonnet)

3. Editor opens the CMS (React UI), reviews the generated events
   → Approves → event moves to publication queue
   → Rejects → event is dropped with a reason

4. System generates platform-specific content for each active Publish Target
   → Telegram: formatted post with hashtags and reply threading
   → Website / Instagram: adapted format (planned)

5. System publishes to configured targets automatically
   → PublishLog records the external message ID for reply threading
```

---

## What the System Does

### Source Monitoring

- Parses RSS feeds on a configurable interval
- Monitors Telegram channels
- Validates incoming articles: minimum length, keywords, maximum age, excluded keywords
- Deduplicates by SourceId + ExternalId (exact) and by title similarity (FuzzySharp)

### AI Processing Pipeline

- **Analysis** (Gemini 2.0 Flash): extracts category, tags, sentiment, language, summary from raw article
- **Event classification** (Gemini embeddings + Claude Haiku for grey zone): groups articles into Events
    - similarity > 0.90 → same Event (automatic)
    - similarity 0.70–0.90 → Claude Haiku decides
    - similarity < 0.70 → new Event (automatic)
- **Article generation** (Claude Sonnet 4.5): produces Title + Content in Ukrainian, tailored to the event context
- **Content generation per platform** (Claude Sonnet 4.5): adapts article to each Publish Target using its SystemPrompt

### Event Graph (Living Article)

- Articles are grouped into Events representing a single real-world story
- Each new article on the same event updates the Event Summary and content
- Contradictions between articles on the same event are tracked and surfaced to editors

### Editorial CMS

- Editors review events or articles with full event context visible
- Approve / Reject workflow with retry limits
- Admin tools: merge events, reclassify articles, resolve contradictions, change event status
- JWT authentication with Editor and Admin roles

### Publication

- Publishes to Telegram channels with full reply threading (updates as replies to original post)
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

---

## AI Model Allocation

| Task                             | Model                      | Rationale                                   |
| -------------------------------- | -------------------------- | ------------------------------------------- |
| Article analysis                 | Gemini 2.0 Flash           | Cost-efficient for high-volume analysis     |
| Embeddings                       | gemini-embedding-2-preview | Cost-efficient for high-volume analysis     |
| Event classification (grey zone) | Claude Haiku               | Cheap binary decision, sufficient quality   |
| Event summary updates            | Claude Haiku               | Lightweight structured output               |
| Article generation               | Claude Sonnet 4.5          | High quality required for published content |
| Platform content generation      | Claude Sonnet 4.5          | High quality required for published content |

---

## External Dependencies

| Service                     | Purpose                                                          | Criticality                         |
| --------------------------- | ---------------------------------------------------------------- | ----------------------------------- |
| Google Gemini API           | Article analysis, embeddings                                     | High — pipeline stops without it    |
| Anthropic Claude API        | Classification, generation                                       | High — pipeline stops without it    |
| Telegram Bot API            | Publishing to channels                                           | High — publication stops without it |
| Telethon (Telegram MTProto) | Parsing source channels                                          | Medium — RSS fallback available     |
| PostgreSQL + pgvector       | Primary data store; pgvector for event classification embeddings | Critical                            |

---

## Non-Obvious Constraints

These are things that are not visible from the code but matter for decision-making:

- **Language:** All generated content is in Ukrainian. The AI prompts are calibrated for Ukrainian output. Changing language requires prompt changes and re-testing.
- **Event classification thresholds (0.70 / 0.90)** were manually calibrated. Do not change without metrics from a meaningful sample.
- **Reply threading depends on PublishLog:** If a PublishLog entry is missing or corrupted, event updates will be published as standalone posts instead of replies. This is silent — no error is raised.
- **Enums are stored as strings in DB.** Changing an enum value name requires a data migration, not just a code change.
- **Prompts live in files, not in code** (`Infrastructure/AI/Prompts/*.txt`). Prompt changes do not require recompilation but do require redeployment.
- **PublishTarget.SystemPrompt drives platform formatting.** The quality of platform-specific content is entirely dependent on this prompt. It is operator-configured, not hardcoded.

---

## Success Criteria

The system is working correctly when:

1. New articles from configured sources appear in the CMS within minutes of publication
2. Duplicate articles about the same story are grouped under one Event automatically
3. An editor can approve an article and see it published to all active Telegram targets without manual action
4. Event updates (new articles on an existing event) appear as replies in Telegram threads
5. The editor's rejection rate on AI-generated content is low (quality signal)
6. `WasReclassified` rate on EventArticles stays low (classification quality signal)