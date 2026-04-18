# Fix Telegram Album Media Loss

## Goal

When a Telegram channel post is an album (multiple photos/videos sharing the same `grouped_id`), all media items must be collected into a single `Article` with multiple `MediaReference` entries instead of silently dropping every item except the one carrying the caption.

## Affected Layers

- Infrastructure (`TelegramClientService`, `TelegramParser`)
- Tests (`Infrastructure.Tests/Parsers/TelegramParserTests.cs`)

---

## Tasks

### Infrastructure

- [x] **Modify `Infrastructure/Services/TelegramClientService.cs`** — on line 77, replace the filter

  ```csharp
  .Where(m => !string.IsNullOrWhiteSpace(m.message))
  ```

  with

  ```csharp
  .Where(m => !string.IsNullOrWhiteSpace(m.message) || m.media is not null)
  ```

  This keeps text-only messages (status quo) and adds media-only messages (album items without captions). Service messages that have neither text nor media are still excluded.

  _Acceptance: the filter change is the only modification in this file; `dotnet build` is green; no other method is touched._
  _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/Parsers/TelegramParser.cs` — refactor `ExtractMediaReferences` to accept `(TelegramChannelMessage item, string username, int groupIndex)`** — replace the single hard-coded `mediaIndex = 0` in the `#media-N` URL suffix with the caller-supplied `groupIndex` parameter. The `ExternalHandle` encoding stays as `TelegramMediaHandle.Encode(item.ChannelId, item.ChannelAccessHash, msg.id, 0)` (each album message carries exactly one media item, so the per-message index remains 0). Rename the parameter on the existing call-sites that are updated in the next task.

  _Acceptance: method signature is `private static List<MediaReference> ExtractMediaReferences(TelegramChannelMessage item, string username, int groupIndex)`; the `Url` field uses `groupIndex`; the `ExternalHandle` still encodes `mediaIndex = 0`; `dotnet build` is green._
  _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Modify `Infrastructure/Parsers/TelegramParser.cs` — add private method `GroupMessages`** — extract album grouping into a private static helper:

  ```csharp
  private static (List<IGrouping<long, TelegramChannelMessage>> albums, List<TelegramChannelMessage> singles)
      GroupMessages(List<TelegramChannelMessage> messages)
  ```

  - Messages where `Message.grouped_id != 0` are collected into album groups (grouped by `grouped_id`).
  - Messages where `Message.grouped_id == 0` are returned as singles.

  _Acceptance: method exists, is `private static`, has no side effects, and all messages appear in exactly one output collection; `dotnet build` is green._
  _Skill: .claude/skills/clean-code/SKILL.md_
  _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Parsers/TelegramParser.cs` — add private method `BuildAlbumArticle`** — given a list of `TelegramChannelMessage` (all sharing a `grouped_id`) and the channel username, produce one `Article`:

  1. Sort the group by `Message.id` ascending.
  2. Select the primary message: first (by ID) that has a non-empty `message` field; fall back to the first message by ID if none has text.
  3. Build the `Article` using the primary message for `ExternalId`, `OriginalUrl`, `Title`, `OriginalContent`, and `PublishedAt`.
  4. Collect `MediaReference` entries from every message in the sorted group by calling `ExtractMediaReferences(msg, username, groupIndex)` where `groupIndex` is the 0-based position in the sorted list; skip messages that produce an empty list.

  _Acceptance: method is `private static Article BuildAlbumArticle(IEnumerable<TelegramChannelMessage> group, string username)`; produces exactly one `Article` regardless of group size; `dotnet build` is green._
  _Skill: .claude/skills/clean-code/SKILL.md_
  _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Parsers/TelegramParser.cs` — rewrite `ParseAsync` to use `GroupMessages` and `BuildAlbumArticle`** — replace the current `messages.Select(item => new Article { ... })` with:

  1. Call `GroupMessages(messages)` to obtain `albums` and `singles`.
  2. For each album group, call `BuildAlbumArticle(group, username)` and add the result to the output list.
  3. For each single message, build an `Article` exactly as today, calling `ExtractMediaReferences(item, username, groupIndex: 0)`.

  _Acceptance: `ParseAsync` delegates grouping and album article construction entirely to the new helpers; the method body contains no inline LINQ projection over all messages; `dotnet build` is green._
  _Skill: .claude/skills/clean-code/SKILL.md_

### Tests

- [ ] **Modify `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs` — update `BuildMessage` helper to accept an optional `grouped_id` parameter** — add `long groupedId = 0` as a default parameter so existing call-sites require no changes, and assign `grouped_id = groupedId` on the constructed `Message`.

  _Acceptance: all existing tests still compile and pass; the new parameter has a default of `0`._
  _Agent: test-writer_
  _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs` — add test: album of 3 photos with caption on the first message produces 1 Article with 3 MediaReferences** — arrange 3 messages sharing a `grouped_id`; the first (lowest ID) carries a caption and a `MessageMediaPhoto`; the other two have empty `message` and their own `MessageMediaPhoto`. Assert: `articles.Count == 1`; `articles[0].ExternalId == firstMessageId.ToString()`; `articles[0].OriginalContent == captionText`; `articles[0].MediaReferences.Count == 3`; `MediaReferences[0].Url` ends with `#media-0`, `[1]` ends with `#media-1`, `[2]` ends with `#media-2`.

  _Acceptance: test is named `ParseAsync_WhenAlbumHas3PhotosWithCaptionOnFirst_Returns1ArticleWith3MediaReferences`; follows AAA layout; passes._
  _Agent: test-writer_
  _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs` — add test: album where no message has a caption uses the first message by ID as primary** — arrange 2 messages sharing a `grouped_id`, both with empty `message` field and `MessageMediaPhoto`. Assert: `articles.Count == 1`; `articles[0].ExternalId == lowestId.ToString()`; `articles[0].Title == string.Empty`; `articles[0].MediaReferences.Count == 2`.

  _Acceptance: test is named `ParseAsync_WhenAlbumHasNoCaptions_UsesFirstMessageByIdAsPrimary`; follows AAA layout; passes._
  _Agent: test-writer_
  _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs` — add test: album with mixed photo and video produces MediaReferences with correct Kind per item** — arrange 2 messages sharing a `grouped_id`; first has `MessageMediaPhoto`, second has `MessageMediaDocument { mime_type = "video/mp4" }`. Assert: `MediaReferences[0].Kind == MediaKind.Image`; `MediaReferences[1].Kind == MediaKind.Video`.

  _Acceptance: test is named `ParseAsync_WhenAlbumHasMixedPhotoAndVideo_MediaReferenceKindsAreCorrect`; follows AAA layout; passes._
  _Agent: test-writer_
  _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs` — add regression test: ungrouped message with media still produces 1 Article with 1 MediaReference** — arrange 1 message with `grouped_id = 0` and a `MessageMediaPhoto`. Assert: `articles.Count == 1`; `articles[0].MediaReferences.Count == 1`; `Url` ends with `#media-0`.

  _Acceptance: test is named `ParseAsync_WhenUngroupedMessageHasMedia_Returns1ArticleWith1MediaReference`; follows AAA layout; passes._
  _Agent: test-writer_
  _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs` — add regression test: ungrouped text-only message (no media) still produces 1 Article with 0 MediaReferences** — arrange 1 message with `grouped_id = 0` and `media = null`. Assert: `articles.Count == 1`; `articles[0].MediaReferences` is empty.

  _Acceptance: test is named `ParseAsync_WhenUngroupedTextOnlyMessage_Returns1ArticleWithNoMediaReferences`; follows AAA layout; passes._
  _Agent: test-writer_
  _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs` — add test: album messages where caption is on the second message (not the lowest ID) selects the captioned message as primary** — arrange 2 messages sharing a `grouped_id`; the lower ID has no caption and has `MessageMediaPhoto`; the higher ID has a non-empty `message` and `MessageMediaPhoto`. Per ADR, the primary is "the first message (by ID) that has a non-empty `message` field", so the higher-ID captioned message is the primary. Assert: `articles[0].ExternalId == higherIdMessage.ToString()`; `articles[0].OriginalContent` equals the caption text; `articles[0].MediaReferences.Count == 2`.

  _Acceptance: test is named `ParseAsync_WhenAlbumCaptionIsOnLaterMessage_ThatMessageIsPrimary`; assertion matches ADR primary-selection rule exactly; follows AAA layout; passes._
  _Agent: test-writer_
  _Skill: .claude/skills/testing/SKILL.md_

## Open Questions

- None. The ADR fully specifies the approach, changed files, primary-selection logic, `ExternalHandle` encoding, and `#media-N` indexing.
