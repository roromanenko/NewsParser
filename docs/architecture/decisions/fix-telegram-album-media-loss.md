# Fix Telegram Album Media Loss

## Context

When a Telegram channel post is an **album** (multiple photos/videos grouped together), only one media item is saved; the rest are silently dropped. Single-media posts work correctly.

Telegram represents albums as multiple `Message` objects sharing the same `grouped_id` (a `long`). Typically only the **first** message in the group carries the caption text in `message`; the remaining messages have an empty or null `message` field but still carry their own `media` (`MessageMediaPhoto` or `MessageMediaDocument`).

Two bugs in the current code cause the loss:

### Bug 1 -- Caption filter drops captionless album items

`TelegramClientService.GetChannelMessagesAsync` (line 77) applies:

```csharp
.Where(m => !string.IsNullOrWhiteSpace(m.message))
```

This discards every `Message` whose `message` field is empty -- which is every album item except the one carrying the caption. Result: out of an album of N items, only 1 survives filtering.

### Bug 2 -- No album grouping logic

`TelegramParser.ParseAsync` creates one `Article` per `Message`. Even if Bug 1 were fixed, an album of N items would produce N separate `Article` objects, each with one `MediaReference`, different `ExternalId`s (individual message IDs), and different `OriginalUrl`s. The downstream pipeline would treat them as N independent articles, most of which have no text content. The correct behavior is one `Article` with the album caption as its text and N `MediaReference` entries.

### Scope

Only the Telegram parsing path is affected. RSS and website sources are not touched.

## Options

### Option 1 -- Group albums in TelegramClientService (data layer)

Change `GetChannelMessagesAsync` to:
- Remove the `!string.IsNullOrWhiteSpace(m.message)` filter.
- Group messages by `grouped_id` before returning.
- Return a new DTO shape (e.g., `TelegramChannelPost`) that contains one "primary" message (with caption) and a list of all messages in the group (for media extraction).

`TelegramParser` would then iterate over the grouped structure.

**Pros:** Parser stays simple -- it receives pre-grouped data.
**Cons:** Changes the `ITelegramChannelReader` interface and its return type, which is also used in tests. Mixes grouping responsibility into a service whose job is raw MTProto communication. The existing `TelegramChannelMessage` record and all test stubs would need updating.

### Option 2 -- Group albums in TelegramParser (parser layer)

Keep `TelegramClientService` as a thin MTProto wrapper:
- Only fix Bug 1 there: relax the filter to allow messages that have media even if they lack text.
- `TelegramParser.ParseAsync` receives all individual messages and groups them by `grouped_id` before building `Article` objects.

For each group:
- The message with a non-empty caption becomes the "primary" (provides `ExternalId`, `OriginalUrl`, `Title`, `OriginalContent`).
- If no message in the group has a caption, use the first message by ID as the primary.
- All messages in the group contribute `MediaReference` entries (indexed by position within the group).
- Ungrouped messages (no `grouped_id`, i.e., `grouped_id == 0`) are treated as standalone posts, same as today.

**Pros:** Minimal interface change -- `ITelegramChannelReader` return type stays `List<TelegramChannelMessage>`. Grouping is a parser concern (interpreting raw data into domain articles), which aligns with the parser's single responsibility. `TelegramClientService` remains a thin MTProto wrapper. Existing test infrastructure for `ITelegramChannelReader` needs only minor additions.
**Cons:** Parser logic becomes moderately more complex (grouping + primary selection).

## Decision

**Adopt Option 2.** Grouping album messages into a single article is an interpretation/parsing concern, not a transport concern. `TelegramClientService` stays a thin MTProto wrapper; `TelegramParser` gains the grouping logic.

### Change 1 -- Relax the message filter in `TelegramClientService.GetChannelMessagesAsync`

Current filter (line 77):
```csharp
.Where(m => !string.IsNullOrWhiteSpace(m.message))
```

Replace with:
```csharp
.Where(m => !string.IsNullOrWhiteSpace(m.message) || m.media is not null)
```

This keeps text-only messages (status quo) and adds media-only messages (album items without captions). Messages with neither text nor media (e.g., service messages) are still excluded.

### Change 2 -- Add album grouping logic in `TelegramParser.ParseAsync`

After receiving the flat list of `TelegramChannelMessage` from the channel reader, group them:

1. **Partition** messages into two sets:
   - **Grouped**: messages where `Message.grouped_id != 0` -- group them by `grouped_id`.
   - **Ungrouped**: messages where `Message.grouped_id == 0` -- each produces one `Article` (current behavior, unchanged).

2. **For each album group** (same `grouped_id`):
   - Sort messages by `Message.id` ascending (Telegram assigns sequential IDs within an album).
   - Select the **primary message**: the first message (by ID) that has a non-empty `message` field. If none has text, use the first message by ID.
   - Build one `Article` using the primary message for `ExternalId`, `OriginalUrl`, `Title`, `OriginalContent`, `PublishedAt`.
   - Collect `MediaReference` entries from **all** messages in the group (not just the primary). The media index in `Url` and `ExternalHandle` increments across the group (0, 1, 2, ...).

3. **For ungrouped messages**: process exactly as today -- one `Article` per message, with `ExtractMediaReferences` producing 0 or 1 `MediaReference`.

### Change 3 -- Refactor `ExtractMediaReferences` to handle a list of messages

Currently `ExtractMediaReferences` takes a single `TelegramChannelMessage` and always uses `mediaIndex = 0`. Refactor to accept a list of messages (or call it per-message with an explicit index) so album items get sequential indices:

```
Message 1 (photo)  -> MediaReference with #media-0, ExternalHandle ...:{msgId1}:0
Message 2 (photo)  -> MediaReference with #media-1, ExternalHandle ...:{msgId2}:0
Message 3 (video)  -> MediaReference with #media-2, ExternalHandle ...:{msgId3}:0
```

Note: each album message has its own `Message.id`, so the `ExternalHandle` encodes that message's ID (not the primary's ID). The `mediaIndex` in the handle remains `0` because each message carries exactly one media item. The `#media-N` suffix in the `Url` uses the group-level index for dedup uniqueness.

### What does NOT change

- `ITelegramChannelReader` interface -- return type stays `List<TelegramChannelMessage>`.
- `TelegramChannelMessage` record -- unchanged.
- `TelegramMediaHandle` encoding -- unchanged.
- `SourceFetcherWorker` -- unchanged; it already handles articles with multiple `MediaReference` entries.
- `MediaIngestionService` and `IMediaContentDownloader` implementations -- unchanged.
- Database schema -- unchanged.
- RSS/website parsing -- unchanged.
- `Core/` layer -- no changes.

## Implementation Notes

### Files modified

1. **`Infrastructure/Services/TelegramClientService.cs`** -- relax the `.Where(...)` filter on line 77.
2. **`Infrastructure/Parsers/TelegramParser.cs`** -- add album grouping logic in `ParseAsync`, refactor `ExtractMediaReferences` to support sequential indexing across a group of messages.

### Files for new/updated tests

3. **`Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs`** -- add test cases:
   - Album of 3 photos with caption on first message produces 1 Article with 3 MediaReferences.
   - Album where no message has a caption uses first message by ID as primary.
   - Album with mixed photo + video produces MediaReferences with correct `Kind` per item.
   - Ungrouped message with media still produces 1 Article with 1 MediaReference (regression guard).
   - Ungrouped text-only message (no media) still produces 1 Article with 0 MediaReferences.

### Skills to follow

- `.claude/skills/code-conventions/SKILL.md` -- layer boundaries, method naming, primary constructor style.
- `.claude/skills/clean-code/SKILL.md` -- method length (extract grouping into a private method), no magic numbers, guard clauses.
- `.claude/skills/testing/SKILL.md` -- AAA pattern, naming convention, test the grouping edge cases.

### Order of changes

1. Fix the filter in `TelegramClientService.GetChannelMessagesAsync` (Bug 1).
2. Add album grouping logic in `TelegramParser.ParseAsync` (Bug 2).
3. Add/update tests in `TelegramParserTests.cs`.

Build and run tests after each step.
