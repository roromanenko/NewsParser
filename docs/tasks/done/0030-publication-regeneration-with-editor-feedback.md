# Publication Regeneration with Editor Feedback

## Goal

Allow editors to request AI regeneration of a `ContentReady` or `Failed` publication draft with
free-text guidance (e.g., "make it shorter"), so they can iterate on AI output instead of
discarding drafts and starting over.

## Affected Layers

- Core
- Infrastructure
- Worker
- Api
- Tests

---

## Tasks

### Phase 1 — Schema + Domain groundwork (non-breaking)

- [x] **Modify `Core/DomainModels/Publication.cs`** — add one nullable property after the
      existing `UpdateContext` field:
      ```csharp
      public string? EditorFeedback { get; set; }
      ```
      No new status values, no new enums.
      _Acceptance: file compiles; no EF or infrastructure references; `Publication` has both
      `UpdateContext` and `EditorFeedback` as distinct nullable strings._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Entity/PublicationEntity.cs`** — add the matching
      nullable property after `UpdateContext`:
      ```csharp
      public string? EditorFeedback { get; set; }
      ```
      _Acceptance: class compiles in `Infrastructure.Persistence.Entity` namespace; no domain
      references._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Mappers/PublicationMapper.cs`** — add `EditorFeedback`
      to both `ToDomain` and `ToEntity`:
      - In `ToDomain`: `EditorFeedback = entity.EditorFeedback,`
      - In `ToEntity`: `EditorFeedback = domain.EditorFeedback,`
      _Acceptance: mapper compiles; both mapping directions carry the new field; no inline logic
      (value is copied as-is, no transformation)._
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Create `Infrastructure/Persistence/Sql/0003_add_publication_editor_feedback.sql`** — new
      DbUp forward-only migration (embedded resource):
      ```sql
      ALTER TABLE publications ADD COLUMN IF NOT EXISTS "EditorFeedback" TEXT NULL;
      ```
      `ADD COLUMN IF NOT EXISTS` makes the script idempotent. `TEXT NULL` requires no backfill.
      No index, no default, no constraint.
      _Acceptance: file exists under `Infrastructure/Persistence/Sql/`; the existing
      `<EmbeddedResource Include="Persistence/Sql/*.sql" />` glob in `Infrastructure.csproj`
      picks it up automatically; `DbUpMigrator.Migrate()` applies it on next startup._
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/Sql/PublicationSql.cs`** — propagate
      `EditorFeedback` through all read/write SQL:
      1. Append `p."EditorFeedback"` to the `PublicationColumns` private const and to every
         explicit SELECT column list where `PublicationColumns` is not reused:
         `GetPendingForGeneration`, `GetPendingForPublish`, `GetById`,
         `GetDetailPublicationWithTarget`, `GetByEventId`, `GetAll`, `GetOriginalEventPublication`.
      2. Add `"EditorFeedback"` to the `Insert` column list and `@EditorFeedback` to its VALUES
         clause.
      3. Add a new public const:
         ```csharp
         public const string RequestRegeneration = """
             UPDATE publications
             SET "Status"           = 'Created',
                 "EditorFeedback"   = @feedback,
                 "GeneratedContent" = ''
             WHERE "Id" = @id
             """;
         ```
      _Acceptance: file compiles; every SELECT that returns a `PublicationEntity` now includes
      `EditorFeedback`; `Insert` and `RequestRegeneration` are correct; no raw SQL in any
      repository method._
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/PublicationRepository.cs`** — carry
      `EditorFeedback` through the existing builder helpers and `Insert`:
      1. In `BuildPublication`: add `EditorFeedback = pub.EditorFeedback,`
      2. In `BuildPublicationWithoutArticle`: add `EditorFeedback = pub.EditorFeedback,`
      3. In `BuildInsertParameters`: add
         `parameters.Add("EditorFeedback", entity.EditorFeedback);`
      _Acceptance: class compiles; existing behaviour is unchanged; no `RequestRegenerationAsync`
      implementation yet (that comes in Phase 2)._
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

---

### Phase 2 — Repository method

- [x] **Modify `Core/Interfaces/Repositories/IPublicationRepository.cs`** — add one method:
      ```csharp
      Task RequestRegenerationAsync(
          Guid id,
          string feedback,
          CancellationToken cancellationToken = default);
      ```
      _Acceptance: interface compiles; method is the only change; no implementation in Core._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Persistence/Repositories/PublicationRepository.cs`** — implement
      `RequestRegenerationAsync`:
      ```csharp
      public async Task RequestRegenerationAsync(
          Guid id, string feedback, CancellationToken cancellationToken = default)
      {
          await using var conn = await factory.CreateOpenAsync(cancellationToken);
          await conn.ExecuteAsync(new CommandDefinition(
              PublicationSql.RequestRegeneration,
              new { id, feedback },
              cancellationToken: cancellationToken));
      }
      ```
      _Acceptance: class satisfies the full `IPublicationRepository` interface; uses
      `IDbConnectionFactory.CreateOpenAsync`, `CommandDefinition` with `cancellationToken`, and
      `PublicationSql.RequestRegeneration`; no raw SQL string in the method body._
      _Skill: .claude/skills/dapper-conventions/SKILL.md_

---

### Phase 3 — AI generator

- [x] **Modify `Core/Interfaces/AI/IContentGenerator.cs`** — extend the signature with a new
      optional parameter:
      ```csharp
      Task<string> GenerateForPlatformAsync(
          Event evt,
          PublishTarget target,
          CancellationToken cancellationToken = default,
          string? updateContext = null,
          string? editorFeedback = null);
      ```
      `updateContext` and `editorFeedback` are mutually exclusive; the interface does not enforce
      this — the caller is responsible.
      _Acceptance: interface compiles; existing callers that omit `editorFeedback` continue to
      compile without changes (optional parameter)._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/AI/ClaudeContentGenerator.cs`** — implement the regeneration
      prompt branch:
      1. Add `string? editorFeedback = null` to the `GenerateForPlatformAsync` signature.
      2. Replace the existing two-branch dispatcher with a three-branch one:
         ```csharp
         var userPrompt = editorFeedback is not null
             ? BuildRegenerationPrompt(evt, target, editorFeedback)
             : updateContext is not null
                 ? BuildUpdatePrompt(evt, target, updateContext)
                 : BuildEventPrompt(evt, target);
         ```
      3. Add a new private static method `BuildRegenerationPrompt(Event evt, PublishTarget target,
         string editorFeedback)` that returns:
         ```
         CHANNEL: {target.Name}
         This is a REGENERATION request. The previous draft was rejected by the editor.
         EDITOR FEEDBACK (apply carefully, do not quote literally):
         {editorFeedback}
         EVENT TITLE: {evt.Title}
         EVENT SUMMARY: {evt.Summary}
         SOURCES:
         {BuildArticlesSection(evt.Articles)}
         ```
      _Acceptance: class satisfies the updated `IContentGenerator` interface; `BuildUpdatePrompt`
      and `BuildEventPrompt` are unchanged; `editorFeedback` branch is first in the dispatcher so
      it takes precedence when set._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 4 — Worker

- [x] **Modify `Worker/Workers/PublicationGenerationWorker.cs`** — pass `EditorFeedback` to the
      generator in `GenerateContentForPublicationAsync`:
      ```csharp
      var content = await contentGenerator.GenerateForPlatformAsync(
          publication.Event,
          publication.PublishTarget,
          cancellationToken,
          updateContext: publication.UpdateContext,
          editorFeedback: publication.EditorFeedback);
      ```
      _Acceptance: worker compiles and runs without exception; no other logic changes; the worker
      lifecycle (`Created → GenerationInProgress → ContentReady | Failed`) is unchanged._
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 5 — Service layer

- [x] **Modify `Core/Interfaces/Services/IPublicationService.cs`** — add one method:
      ```csharp
      Task<Publication> RegenerateAsync(
          Guid publicationId,
          string feedback,
          CancellationToken cancellationToken = default);
      ```
      _Acceptance: interface compiles; all existing methods are unchanged._
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `Infrastructure/Services/PublicationService.cs`** — implement `RegenerateAsync`
      following the pattern of `ApproveAsync` / `RejectAsync`:
      1. Load via `publicationRepository.GetByIdAsync`; throw `KeyNotFoundException` if null:
         `$"Publication {publicationId} not found"`.
      2. Validate `feedback` is not null/whitespace; throw `ArgumentException` if empty:
         `"Feedback must not be empty"`.
      3. Status guard — allowed source statuses: `ContentReady`, `Failed`; throw
         `InvalidOperationException` for any other status:
         `$"Publication {publicationId} cannot be regenerated: status is {publication.Status}"`.
      4. Call `publicationRepository.RequestRegenerationAsync(publicationId, feedback,
         cancellationToken)`.
      5. Log at Information: `"Publication {PublicationId} queued for regeneration with editor
         feedback"`.
      6. Update the in-memory domain object and return it:
         `publication.Status = PublicationStatus.Created;`
         `publication.EditorFeedback = feedback;`
         `publication.GeneratedContent = string.Empty;`
      _Acceptance: class satisfies the full `IPublicationService` interface; guard clauses use the
      same exception types as `ApproveAsync` / `RejectAsync`; `ReviewedByEditorId` is not set
      (regeneration is not a review action)._
      _Skill: .claude/skills/clean-code/SKILL.md_

---

### Phase 6 — API

- [x] **Modify `Api/Models/PublicationDtos.cs`** — two changes:
      1. Add a new request record (after the existing records):
         ```csharp
         public record RegeneratePublicationRequest(string Feedback);
         ```
      2. Extend `PublicationDetailDto` by appending `string? EditorFeedback` as the last
         positional parameter (preserves wire format for existing callers):
         ```csharp
         public record PublicationDetailDto(
             Guid Id,
             string Status,
             string TargetName,
             string Platform,
             string GeneratedContent,
             List<MediaFileDto> AvailableMedia,
             List<Guid> SelectedMediaFileIds,
             DateTimeOffset CreatedAt,
             DateTimeOffset? ApprovedAt,
             DateTimeOffset? PublishedAt,
             string? RejectionReason,
             string? EditorFeedback);
         ```
         `PublicationListItemDto` is NOT extended.
      _Acceptance: file compiles; Swagger reflects `EditorFeedback` on the detail DTO and
      `RegeneratePublicationRequest` as a body schema._
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Create `Api/Validators/RegeneratePublicationRequestValidator.cs`** — FluentValidation
      validator:
      ```csharp
      public class RegeneratePublicationRequestValidator
          : AbstractValidator<RegeneratePublicationRequest>
      {
          public RegeneratePublicationRequestValidator()
          {
              RuleFor(x => x.Feedback).NotEmpty().MaximumLength(2000);
          }
      }
      ```
      _Acceptance: file compiles in `Api.Validators` namespace; validator is auto-discovered by
      the existing FluentValidation registration (no manual DI registration needed); invalid
      requests return 400 before reaching the controller._
      _Skill: .claude/skills/api-conventions/SKILL.md_

- [x] **Modify `Api/Mappers/PublicationMapper.cs`** — add `EditorFeedback` as the last argument
      in `ToDetailDto`:
      ```csharp
      public static PublicationDetailDto ToDetailDto(
          this Publication pub, List<MediaFile> availableMedia, string publicBaseUrl) => new(
          pub.Id,
          pub.Status.ToString(),
          pub.PublishTarget.Name,
          pub.PublishTarget.Platform.ToString(),
          pub.GeneratedContent,
          availableMedia.Select(m => m.ToDto(publicBaseUrl)).ToList(),
          pub.SelectedMediaFileIds,
          pub.CreatedAt,
          pub.ApprovedAt,
          pub.PublishedAt,
          pub.RejectionReason,
          pub.EditorFeedback);
      ```
      _Acceptance: mapper compiles; `ToListItemDto` is unchanged; no inline mapping logic in
      the controller._
      _Skill: .claude/skills/mappers/SKILL.md_

- [x] **Modify `Api/Controllers/PublicationsController.cs`** — add one endpoint:
      ```csharp
      [HttpPost("{id:guid}/regenerate")]
      public async Task<ActionResult<PublicationDetailDto>> Regenerate(
          Guid id,
          [FromBody] RegeneratePublicationRequest request,
          CancellationToken cancellationToken = default)
      {
          if (UserId is null)
              return Unauthorized();

          await publicationService.RegenerateAsync(id, request.Feedback, cancellationToken);

          var detail = await publicationRepository.GetDetailAsync(id, cancellationToken);
          if (detail is null)
              return NotFound();

          var availableMedia = ExtractAvailableMedia(detail);
          return Ok(detail.ToDetailDto(availableMedia, _publicBaseUrl));
      }
      ```
      Follows the exact shape of `Approve`, `Reject`, and `Send`.
      _Acceptance: `POST /publications/{id}/regenerate` appears in Swagger; FluentValidation runs
      automatically on the request body; `ExceptionMiddleware` maps `KeyNotFoundException` → 404
      and `InvalidOperationException` → 409; no inline DTO construction._
      _Skill: .claude/skills/api-conventions/SKILL.md_

---

### Phase 7 — Tests

- [ ] **Modify `Tests/Infrastructure.Tests/Services/PublicationServiceTests.cs`** — add tests for
      `RegenerateAsync`: _Delegated to test-writer agent_
      - Happy path: status `ContentReady` → `RequestRegenerationAsync` called, returned
        publication has `Status = Created`, `EditorFeedback = feedback`, `GeneratedContent = ""`.
      - Happy path: status `Failed` → same assertions.
      - Status guard rejects `Created` → `InvalidOperationException`.
      - Status guard rejects `GenerationInProgress` → `InvalidOperationException`.
      - Status guard rejects `Approved` → `InvalidOperationException`.
      - Status guard rejects `Published` → `InvalidOperationException`.
      - Status guard rejects `Rejected` → `InvalidOperationException`.
      - Not found: `GetByIdAsync` returns null → `KeyNotFoundException`.
      - Empty feedback (null/whitespace variants) → `ArgumentException`.
      _Acceptance: all new tests pass; existing tests unchanged; AAA pattern; Moq for
      `IPublicationRepository`._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Infrastructure.Tests/Repositories/PublicationRepositoryContractTests.cs`**
      — add tests for `RequestRegenerationAsync`: _Delegated to test-writer agent_
      - After calling `RequestRegenerationAsync`, re-fetch by id and assert: `Status = Created`,
        `EditorFeedback` equals the feedback string, `GeneratedContent` is empty string.
      - Also assert that a second call overwrites the previous feedback ("latest wins").
      _Acceptance: tests pass against a real (test) database; uses the same test infrastructure
      pattern as existing contract tests in the file._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Create `Tests/Worker.Tests/Workers/PublicationGenerationWorkerTests.cs`** — add tests for
      the new `editorFeedback` path: _Delegated to test-writer agent_
      - When `publication.EditorFeedback` is set, `IContentGenerator.GenerateForPlatformAsync` is
        called with `editorFeedback` equal to that value and `updateContext` null.
      - When `publication.EditorFeedback` is null but `UpdateContext` is set, `updateContext` is
        passed and `editorFeedback` is null (regression guard).
      - When both are null, neither optional arg is non-null.
      _Acceptance: tests pass; `IContentGenerator` is mocked with Moq; no real DB or AI calls._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

- [ ] **Modify `Tests/Api.Tests/Controllers/PublicationsControllerTests.cs`** — add tests for
      `POST /{id}/regenerate`: _Delegated to test-writer agent_
      - Returns 200 with `PublicationDetailDto` on success (mock service + repository).
      - Returns 401 when `UserId` is null.
      - Returns 404 when `GetDetailAsync` returns null after service call.
      - Returns 400 when `Feedback` is empty (FluentValidation fires before controller).
      - Returns 400 when `Feedback` exceeds 2000 characters.
      - Returns 409 when service throws `InvalidOperationException` (wrong status).
      _Acceptance: tests pass; uses `WebApplicationFactory` or the existing controller test
      infrastructure; Swagger shape is not regressed._
      _Agent: test-writer_
      _Skill: .claude/skills/testing/SKILL.md_

---

### Phase 8 — Build & verify

- [x] **Run `dotnet build NewsParser.slnx`** — confirm zero errors and zero warnings introduced
      by this feature across all projects (Core, Infrastructure, Worker, Api, Tests).
      _Acceptance: build output shows `Build succeeded` with 0 Error(s); any pre-existing
      warnings are unchanged in count._

- [ ] **Run `dotnet test`** — confirm all tests pass including the new ones added above.
      _Acceptance: test run reports 0 failures; new test methods appear in the output._

---

## Open Questions

None — the ADR fully specifies the design, file paths, status guards, prompt structure,
sequencing, and out-of-scope items.
