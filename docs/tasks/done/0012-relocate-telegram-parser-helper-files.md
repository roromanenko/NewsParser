# Relocate Telegram Parser Helper Files

## Goal

Move three misplaced files from `Infrastructure/Services/` to their correct
role-based folders and update all namespace declarations and `using` directives,
with no behavioral changes.

## Affected Layers

- Infrastructure

## ADR Reference

`docs/architecture/decisions/0008-relocate-telegram-parser-helper-files.md`

---

## Tasks

### Infrastructure — create files in target locations

- [x] **Create `Infrastructure/Parsers/ITelegramChannelReader.cs`** — copy content
      from `Infrastructure/Services/ITelegramChannelReader.cs` verbatim; change the
      namespace declaration from `Infrastructure.Services` to `Infrastructure.Parsers`.
      No other changes.
      _Acceptance: file compiles; interface members (`IsReady`, `GetChannelMessagesAsync`) are
      unchanged; no `using Infrastructure.Services` reference remains in this file._
      _Skill: `.claude/skills/code-conventions/SKILL.md`_

- [x] **Create `Infrastructure/Models/TelegramChannelMessage.cs`** — copy content
      from `Infrastructure/Services/TelegramChannelMessage.cs` verbatim; change the
      namespace declaration from `Infrastructure.Services` to `Infrastructure.Models`.
      Note: `Infrastructure/Models/` does not yet exist — this file creates the folder.
      No other changes.
      _Acceptance: file compiles; sealed record has the three positional parameters
      (`Message`, `ChannelId`, `ChannelAccessHash`) unchanged; `using TL;` is retained._
      _Skill: `.claude/skills/code-conventions/SKILL.md`_

- [x] **Create `Infrastructure/Storage/TelegramMediaHandle.cs`** — copy content
      from `Infrastructure/Services/TelegramMediaHandle.cs` verbatim; change the
      namespace declaration from `Infrastructure.Services` to `Infrastructure.Storage`.
      No other changes.
      _Acceptance: file compiles; `internal static class TelegramMediaHandle` with `Encode`
      and `TryDecode` members is byte-identical to the source aside from the namespace line._
      _Skill: `.claude/skills/code-conventions/SKILL.md`_

### Infrastructure — delete original files

- [x] **Delete `Infrastructure/Services/ITelegramChannelReader.cs`** — remove the
      original file now that the replacement exists at `Infrastructure/Parsers/ITelegramChannelReader.cs`.
      _Acceptance: file no longer exists; solution still builds (no dangling reference)._

- [x] **Delete `Infrastructure/Services/TelegramChannelMessage.cs`** — remove the
      original file now that the replacement exists at `Infrastructure/Models/TelegramChannelMessage.cs`.
      _Acceptance: file no longer exists; solution still builds._

- [x] **Delete `Infrastructure/Services/TelegramMediaHandle.cs`** — remove the
      original file now that the replacement exists at `Infrastructure/Storage/TelegramMediaHandle.cs`.
      _Acceptance: file no longer exists; solution still builds._

### Infrastructure — update `using` directives in `TelegramParser.cs`

- [x] **Modify `Infrastructure/Parsers/TelegramParser.cs`** — replace
      `using Infrastructure.Services;` with `using Infrastructure.Models;` and
      `using Infrastructure.Storage;`. Remove `using Infrastructure.Services;`
      entirely. (`ITelegramChannelReader` is now in the same namespace
      `Infrastructure.Parsers` and needs no explicit import.)
      _Acceptance: file compiles without any `using Infrastructure.Services;` line;
      `TelegramChannelMessage` resolves from `Infrastructure.Models`,
      `TelegramMediaHandle` resolves from `Infrastructure.Storage`,
      `ITelegramChannelReader` resolves as a same-namespace type._
      _Skill: `.claude/skills/code-conventions/SKILL.md`_

### Tests — update `using` directives in `TelegramParserTests.cs`

- [x] **Modify `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs`** — replace
      `using Infrastructure.Services;` with three imports:
      `using Infrastructure.Parsers;`, `using Infrastructure.Models;`,
      and `using Infrastructure.Storage;`. The existing `using Infrastructure.Parsers;`
      line is already present — do not duplicate it; only add the two new ones and
      remove `using Infrastructure.Services;`.
      _Acceptance: file compiles; `ITelegramChannelReader`, `TelegramChannelMessage`,
      and `TelegramMediaHandle` all resolve to their new namespaces;
      no `using Infrastructure.Services;` line remains._
      _Agent: test-writer_
      _Skill: `.claude/skills/testing/SKILL.md`_

### Verification

- [x] **Build `Infrastructure/Infrastructure.csproj`** — run `dotnet build` on the
      Infrastructure project; confirm zero errors and zero warnings introduced by
      this change.
      _Acceptance: build output shows `Build succeeded` with 0 error(s)._

- [x] **Build `Tests/Infrastructure.Tests/`** — run `dotnet build` on the test
      project; confirm zero errors.
      _Acceptance: build output shows `Build succeeded` with 0 error(s)._

- [x] **Run `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs`** — execute
      the `TelegramParserTests` fixture; all 6 tests must pass without modification
      to any test logic.
      _Acceptance: NUnit reports 6 tests passing; no test logic was altered during
      this task._
      _Agent: test-writer_
      _Skill: `.claude/skills/testing/SKILL.md`_

## Open Questions

- None. The ADR is unambiguous: Option 1 (split by role) is the decision.
  `TelegramChannelMessage` moves to `Infrastructure/Models/` (new folder),
  not `Infrastructure/Parsers/` as described in the Option 1 summary —
  the ADR's final placement table (and the Consequences section) is the
  authoritative source and names `Infrastructure/Models/`.
