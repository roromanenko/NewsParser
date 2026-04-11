# Relocate Telegram Parser Helper Files

## Context

Three files were introduced into `Infrastructure/Services/` as part of ADR 0007
(Telegram media extraction) but they do not belong there conceptually:

1. **`Infrastructure/Services/ITelegramChannelReader.cs`** — a thin seam used
   exclusively by `Infrastructure/Parsers/TelegramParser.cs` to decouple the
   parser from the concrete WTelegram-backed channel reader. Exposes
   `IsReady` and `GetChannelMessagesAsync(...)`.
2. **`Infrastructure/Services/TelegramChannelMessage.cs`** — a sealed record
   wrapping a `TL.Message` (from WTelegram) together with `ChannelId` and
   `ChannelAccessHash`. It is the return type of `ITelegramChannelReader` and
   serves as an Infrastructure-internal transport DTO from the reader to the
   parser so the parser can build `MediaReference.ExternalHandle` values.
3. **`Infrastructure/Services/TelegramMediaHandle.cs`** — `internal static`
   class with pure `Encode`/`TryDecode` methods converting
   `(channelId, accessHash, messageId, mediaIndex)` to/from the
   `"channelId:accessHash:messageId:mediaIndex"` string stored in
   `MediaReference.ExternalHandle`. It is a pure utility — no dependencies,
   no services.

`Infrastructure/Services/` currently holds actual services (`SourceService`,
`JwtService`, `MediaIngestionService`, `TelegramMediaContentDownloader`,
`TelegramClientService`, etc.). The three files above are **not** services:
two are parser-only collaborators, one is a value-encoding helper. Dumping
them into `Services/` hides their true role and makes discovery harder.

### Constraints discovered during exploration

- **`TelegramChannelMessage` must stay in Infrastructure.** It references
  `TL.Message` from WTelegram. `Core/` forbids infrastructure dependencies
  per `code-conventions` (layer boundaries table). This rules out moving it
  — or `ITelegramChannelReader`, whose signature leaks `TelegramChannelMessage`
  — into `Core/Interfaces/`.
- **Contrast with `ITelegramMediaGateway`.** That interface lives in
  `Core/Interfaces/Storage/` because its signature uses only primitive types
  (`string externalHandle`, `Stream destination`). No `TL` leakage. The rule
  is concrete: *an interface can live in `Core/` only if no parameter or
  return type forces a reference to Infrastructure libraries.*
- **Infrastructure groups by role, not by technology.** Existing folders:
  `Parsers/`, `Publishers/`, `Services/`, `Storage/`, `Persistence/`, `AI/`,
  `Configuration/`, `Validators/`. There is no `Infrastructure/Telegram/`
  grouping folder, and this refactor should not introduce one — it would
  diverge from the project convention.
- **Scope of this ADR is file relocation only.** The existing DI bug —
  `ITelegramChannelReader` is referenced by `TelegramParser` but
  `TelegramClientService` neither implements it nor is registered as
  `ITelegramChannelReader` in `InfrastructureServiceExtensions.cs` — is out
  of scope and must be addressed by a separate task. This ADR must not
  change production runtime behavior.

## Options

### Option 1 — Split by role: parser seam to `Parsers/`, handle to `Storage/`

Move the files as follows:

- `ITelegramChannelReader.cs`          → `Infrastructure/Parsers/ITelegramChannelReader.cs`
- `TelegramChannelMessage.cs`          → `Infrastructure/Parsers/TelegramChannelMessage.cs`
- `TelegramMediaHandle.cs`             → `Infrastructure/Storage/TelegramMediaHandle.cs`

Rationale:
- `ITelegramChannelReader` is consumed by exactly one class: `TelegramParser`.
  Co-locating the seam and its DTO next to the parser that owns them mirrors
  the way the codebase already groups by role (`Infrastructure/Parsers/`
  already holds `RssParser.cs` and `TelegramParser.cs`).
- `TelegramMediaHandle` encodes a reference to a *stored* Telegram media item
  — the very thing `MediaReference.ExternalHandle` represents. Its two
  consumers (`TelegramParser` on the write side, `TelegramClientService`'s
  future `DownloadMediaAsync` on the read side per ADR 0007) are a producer
  and a consumer of storage references. `Infrastructure/Storage/` already
  holds `CloudflareR2Storage` and is the folder aligned with the
  `Core/Interfaces/Storage/` abstractions (`IMediaStorage`,
  `IMediaContentDownloader`, `ITelegramMediaGateway`). A storage-reference
  encoder fits there.
- Keeps `internal` visibility for `TelegramMediaHandle` — `Parsers/` and
  `Storage/` are both in the `Infrastructure` assembly, so `internal` still
  works for both consumers. Visibility is verified via
  `Infrastructure/AssemblyInfo.cs` (InternalsVisibleTo for tests).

**Pros:**
- Every file lives in the folder whose role matches its responsibility.
- `TelegramParser` and its seam live side by side — the most common navigation
  pattern (open parser → find collaborators) works naturally.
- Reinforces the project convention of role-based grouping.
- No new folders, no new namespaces beyond existing ones.

**Cons:**
- `TelegramMediaHandle` is physically separated from `ITelegramChannelReader`
  even though both are "Telegram-specific Infrastructure helpers." A reader
  looking for "all Telegram-related helpers" has to look in two folders.
- `Infrastructure/Storage/` becomes slightly less cohesive: it now holds both
  an `IMediaStorage` implementation and a string-encoding utility.

### Option 2 — Everything to `Infrastructure/Parsers/`

Move all three files under `Infrastructure/Parsers/`:

- `ITelegramChannelReader.cs`          → `Infrastructure/Parsers/ITelegramChannelReader.cs`
- `TelegramChannelMessage.cs`          → `Infrastructure/Parsers/TelegramChannelMessage.cs`
- `TelegramMediaHandle.cs`             → `Infrastructure/Parsers/TelegramMediaHandle.cs`

Rationale: the parser is the only current **writer** of the handle, so pulling
everything next to the parser groups "Telegram parser concerns" in one place.

**Pros:**
- All three files sit next to their primary consumer (`TelegramParser`).
- Easiest navigation for someone adjusting the Telegram parser specifically.

**Cons:**
- Misrepresents `TelegramMediaHandle`. The handle's second consumer is the
  download path in `TelegramClientService` / `TelegramMediaContentDownloader`
  (see ADR 0007, Step 6 and Step 8). Placing it under `Parsers/` implies
  parser ownership when the encoding is a shared Infrastructure convention.
- Pollutes `Parsers/` with a value helper that is not a parser or a parser
  support type.

### Option 3 — Create `Infrastructure/Telegram/` grouping folder

Introduce a new folder that groups all Telegram-specific helpers:

- `Infrastructure/Telegram/ITelegramChannelReader.cs`
- `Infrastructure/Telegram/TelegramChannelMessage.cs`
- `Infrastructure/Telegram/TelegramMediaHandle.cs`

**Pros:**
- Perfect cohesion — one folder for every Telegram-internal helper.

**Cons:**
- Diverges from the project's role-based grouping convention (`Parsers/`,
  `Publishers/`, `Services/`, `Storage/`, `Persistence/`, `AI/`). No other
  technology has its own top-level Infrastructure folder (RSS helpers don't,
  Anthropic/Gemini helpers don't — they live in `Parsers/` and `AI/`).
- Would logically force a follow-up question: should `TelegramClientService`,
  `TelegramPublisher`, and `TelegramMediaContentDownloader` also move here?
  Expanding the refactor beyond the three files the user asked about.
- Introduces a new namespace (`Infrastructure.Telegram`) that existing code
  and the DI extensions do not use.

## Decision

**Choose Option 1 — split by role.**

Final placement:

| File (current)                                        | File (new)                                               |
|-------------------------------------------------------|----------------------------------------------------------|
| `Infrastructure/Services/ITelegramChannelReader.cs`   | `Infrastructure/Parsers/ITelegramChannelReader.cs`       |
| `Infrastructure/Services/TelegramChannelMessage.cs`   | `Infrastructure/Models/TelegramChannelMessage.cs`        |
| `Infrastructure/Services/TelegramMediaHandle.cs`      | `Infrastructure/Storage/TelegramMediaHandle.cs`          |

Namespace changes:

- `ITelegramChannelReader`: namespace changes from `Infrastructure.Services` to `Infrastructure.Parsers`.
- `TelegramChannelMessage`: namespace changes from `Infrastructure.Services` to `Infrastructure.Models`.
  Rationale: it is a pure data record (DTO) with no parser-specific logic — its only reason for being
  in Infrastructure (rather than Core) is the `TL.Message` field which is a WTelegram dependency.
  `Infrastructure/Models/` is the correct home for infrastructure-internal transport/data types
  that are not tied to any single role folder.
- `TelegramMediaHandle`: namespace changes from `Infrastructure.Services` to
  `Infrastructure.Storage`.

Rationale:
- Honors the layer rule that Infrastructure leaks (the `TL.Message` reference)
  cannot be pushed into `Core/`. Both `Parsers/` and `Storage/` are
  Infrastructure-internal, so the `TL` reference remains contained.
- Co-locates each helper with the role it serves: the channel-reader seam
  belongs with the parser that is its only consumer; the
  `MediaReference.ExternalHandle` encoder belongs with the Storage-layer
  concept it encodes (see ADR 0007 terminology: the handle is "a reference
  to a Telegram media item").
- Preserves `internal` visibility for `TelegramMediaHandle` — both
  `Infrastructure.Parsers.TelegramParser` (writer) and the future
  Infrastructure consumer of the handle are in the same assembly.
- Keeps the refactor contained to file moves + namespace edits + `using`
  updates. No behavioral change, no DI change, no API surface change.

Why not Option 2: `TelegramMediaHandle` is not a parser concern; it is a
shared Infrastructure encoding convention with two consumers, and ADR 0007
explicitly frames it as a link between parse-time and download-time.
Why not Option 3: it introduces a technology-grouped folder with no
precedent in the project and would require a broader refactor to be
consistent.

## Consequences

**Positive:**
- `Infrastructure/Services/` shrinks back to holding only actual services.
- `Infrastructure/Parsers/` becomes self-contained for Telegram parsing
  (parser + seam in one folder).
- `Infrastructure/Models/` holds the internal transport DTO (`TelegramChannelMessage`)
  separately from the parser, reflecting that it is a data record, not a parser concern.
- `Infrastructure/Storage/` gains the encoding helper that matches the
  `MediaReference.ExternalHandle` abstraction used by the storage layer.
- Navigation improves: readers looking at `TelegramParser` find
  `ITelegramChannelReader` in the same folder; `TelegramChannelMessage` is one
  folder away in `Infrastructure/Models/`.

**Negative / risks:**
- Any file with a `using Infrastructure.Services;` that reached for one of
  these three types must update its `using` list. Grep confirms the
  affected files are:
  - `Infrastructure/Parsers/TelegramParser.cs` (references
    `ITelegramChannelReader`, `TelegramChannelMessage`,
    `TelegramMediaHandle` — after the move, `ITelegramChannelReader` becomes
    intra-namespace; must add `using Infrastructure.Models;` for
    `TelegramChannelMessage` and `using Infrastructure.Storage;` for
    `TelegramMediaHandle`; drop `using Infrastructure.Services;`).
  - `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs` (references
    `ITelegramChannelReader`, `TelegramChannelMessage`, `TelegramMediaHandle` —
    must add `using Infrastructure.Parsers;`, `using Infrastructure.Models;`,
    and `using Infrastructure.Storage;`; drop the old
    `using Infrastructure.Services;` if no longer needed).
- `TelegramMediaHandle` remains `internal static` — verify that
  `Infrastructure/AssemblyInfo.cs` already exposes internals to the
  Infrastructure test assembly (it must, because the current test file at
  `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs` already
  references the type). The move does not change the assembly, so
  `InternalsVisibleTo` continues to apply.
- No new DI registration is introduced. Callers still resolve
  `ITelegramChannelReader` the same way (which, as noted below, is
  currently an unresolved DI concern outside this ADR's scope).

**Files affected:**

Moved (3):
- `Infrastructure/Services/ITelegramChannelReader.cs` → `Infrastructure/Parsers/ITelegramChannelReader.cs`
- `Infrastructure/Services/TelegramChannelMessage.cs` → `Infrastructure/Models/TelegramChannelMessage.cs`
- `Infrastructure/Services/TelegramMediaHandle.cs` → `Infrastructure/Storage/TelegramMediaHandle.cs`

Edited (namespace + usings):
- `Infrastructure/Parsers/TelegramParser.cs` — update `using Infrastructure.Services;`
  to `using Infrastructure.Storage;` (needed for `TelegramMediaHandle`);
  `ITelegramChannelReader` and `TelegramChannelMessage` become intra-namespace.
- `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs` — replace
  `using Infrastructure.Services;` with `using Infrastructure.Parsers;` and
  `using Infrastructure.Storage;`.

Not edited (explicitly):
- `Infrastructure/Services/TelegramClientService.cs` — NOT touched by this
  refactor. Its role and placement (as a real service / hosted singleton)
  are correct for `Infrastructure/Services/`.
- `Infrastructure/Extensions/InfrastructureServiceExtensions.cs` — NOT
  touched. The DI registration is unchanged. The separate observation that
  `ITelegramChannelReader` is not bound to any concrete registration is a
  **pre-existing issue** that must be filed as a follow-up task, not mixed
  into this relocation ADR.

## Implementation Notes

### Order of changes

1. Create the new files in their target locations with the updated
   namespaces (`Infrastructure.Parsers` for the two parser-seam files,
   `Infrastructure.Storage` for `TelegramMediaHandle`). Keep file contents
   otherwise byte-identical — same XML docs, same access modifiers, same
   members.
2. Delete the three original files from `Infrastructure/Services/`.
3. Update `Infrastructure/Parsers/TelegramParser.cs`: drop
   `using Infrastructure.Services;` (no longer needed) and add
   `using Infrastructure.Models;` for `TelegramChannelMessage` and
   `using Infrastructure.Storage;` for `TelegramMediaHandle`.
   `ITelegramChannelReader` becomes a same-namespace reference and needs no import.
4. Update `Tests/Infrastructure.Tests/Parsers/TelegramParserTests.cs`:
   add `using Infrastructure.Parsers;`, `using Infrastructure.Models;`,
   and `using Infrastructure.Storage;`; remove `using Infrastructure.Services;`
   if no longer referenced.
5. Build the solution — any residual compile error will pinpoint a missed
   `using` update.
6. Run `Tests/Infrastructure.Tests` to confirm `TelegramParserTests` still
   pass. No test logic should change.

### Skills `feature-planner` and `implementer` must follow

- **`code-conventions`** (`.claude/skills/code-conventions/SKILL.md`) — layer
  boundaries table confirms that types leaking `TL` cannot move into `Core/`;
  Infrastructure role-based grouping (`Parsers/`, `Services/`, `Storage/`,
  etc.) is the governing convention for this move.
- **`clean-code`** (`.claude/skills/clean-code/SKILL.md`) — the refactor is a
  single-responsibility clarification (helpers are not services). No other
  refactoring should be smuggled into the same change.

### Explicitly out of scope

- Do **not** rename types, change access modifiers, add new members, or
  restructure `TelegramMediaHandle`'s encoding format.
- Do **not** implement `ITelegramChannelReader` on `TelegramClientService`
  and do **not** add a DI registration for it. That pre-existing wiring
  gap was introduced by ADR 0007 / task 0011 and must be handled by a
  dedicated follow-up task — mixing it into a file-relocation change would
  make the refactor risky to review.
- Do **not** move `TelegramClientService`, `TelegramMediaContentDownloader`,
  or `TelegramPublisher`. Their current locations are correct.
- Do **not** introduce an `Infrastructure/Telegram/` folder.
