# Task: Extract Inline DTO Mapping to Api/Mappers/

**Created:** 2026-04-02
**Status:** Active
**Target directory:** `Api/Mappers/`

---

## Background

All six non-base controllers in `Api/Controllers/` construct DTOs inline. Two controllers (`SourcesController`, `PublishTargetsController`) already extracted the logic into a `private static` helper inside the controller — these still need to move to the dedicated mapper layer. The goal is a uniform `Api/Mappers/` layer matching the conventions in `.claude/skills/mappers/SKILL.md`.

---

## Inline DTO Construction Audit

| Controller | Location | Inline construction found |
|---|---|---|
| `ArticlesController` | `GetPending` | `articles.Select(a => new ArticleListItemDto(...))` |
| `ArticlesController` | `GetById` | `new ArticleDetailDto(...)`, nested `new RawArticleDto(...)`, conditional `new ArticleEventDto(...)` |
| `ArticlesController` | `Approve` | `new ArticleListItemDto(...)` |
| `ArticlesController` | `Reject` | `new ArticleListItemDto(...)` |
| `EventsController` | `GetAll` | `events.Select(e => new EventListItemDto(...))` |
| `EventsController` | `GetById` | `new EventDetailDto(...)` wrapping `Select(... new EventArticleDto(...))`, `Select(... new EventUpdateDto(...))`, `Select(... new ContradictionDto(...))` |
| `SourcesController` | `private static ToDto` | `new SourceDto(...)` — private helper, not yet in `Api/Mappers/` |
| `PublishTargetsController` | `private static ToDto` | `new PublishTargetDto(...)` — private helper, not yet in `Api/Mappers/` |
| `UsersController` | `GetAllUsers` | `users.Select(user => new UserDto(...))` |
| `UsersController` | `CreateUser` | `new UserDto(...)` |
| `UsersController` | `UpdateEditor` | `new UserDto(...)` |
| `AuthController` | `Login` | `new LoginResponse(...)` |
| `AuthController` | `Register` | `new LoginResponse(...)` |

---

## Mapper Files to Create

| File | Namespace | Aggregate root | DTOs covered |
|---|---|---|---|
| `Api/Mappers/ArticleMapper.cs` | `Api.Mappers` | `Article`, `RawArticle` | `ArticleListItemDto`, `ArticleDetailDto`, `RawArticleDto`, `ArticleEventDto` |
| `Api/Mappers/EventMapper.cs` | `Api.Mappers` | `Event`, `EventUpdate`, `Contradiction` | `EventListItemDto`, `EventDetailDto`, `EventArticleDto`, `EventUpdateDto`, `ContradictionDto` |
| `Api/Mappers/SourceMapper.cs` | `Api.Mappers` | `Source` | `SourceDto` |
| `Api/Mappers/PublishTargetMapper.cs` | `Api.Mappers` | `PublishTarget` | `PublishTargetDto` |
| `Api/Mappers/UserMapper.cs` | `Api.Mappers` | `User` | `UserDto`, `LoginResponse` |

---

## Tasks

### TASK-1 — Create `Api/Mappers/ArticleMapper.cs`

**What to do:**
Create `Api/Mappers/ArticleMapper.cs` as `public static class ArticleMapper` in namespace `Api.Mappers`. This is the first file and implicitly creates the `Api/Mappers/` directory. Implement the following static extension methods:

- `public static ArticleListItemDto ToListItemDto(this Article article)` — maps `Id`, `Title`, `Category`, `Tags`, `Sentiment.ToString()`, `Language`, `Summary`, `ProcessedAt`.
- `public static RawArticleDto ToDto(this RawArticle raw)` — maps `Id`, `Title`, `OriginalUrl`, `PublishedAt`, `Language`. Keep in this same file (sub-object of the Article aggregate).
- `public static ArticleDetailDto ToDetailDto(this Article article, Event? evt = null)` — conditionally builds `ArticleEventDto` when `evt` is non-null using `evt.Id`, `evt.Title`, `evt.Status.ToString()`, `article.Role?.ToString() ?? string.Empty`; calls `article.RawArticle.ToDto()` for the source sub-field.

**Acceptance criteria:**
- File exists at `Api/Mappers/ArticleMapper.cs`.
- Namespace is `Api.Mappers`.
- Class is `public static`; no instance members, no DI, no side effects.
- All three method signatures match exactly as above; `ToDetailDto` uses an optional `Event?` parameter.
- `RawArticleDto` and `ArticleEventDto` mappers are co-located in this same file.
- Enums use `.ToString()` only (no `Enum.Parse`).

---

### TASK-2 — Update `ArticlesController.cs` to use `ArticleMapper`

**What to do:**
In `Api/Controllers/ArticlesController.cs`:

1. Add `using Api.Mappers;` at the top.
2. `GetPending` — replace `articles.Select(a => new ArticleListItemDto(...))` with `articles.Select(a => a.ToListItemDto())`.
3. `GetById` — remove the `eventDto` block and the `new ArticleDetailDto(...)` block; replace with `article.ToDetailDto(evt)` (pass `evt` which is already fetched in the same method).
4. `Approve` — replace `new ArticleListItemDto(...)` with `article.ToListItemDto()`.
5. `Reject` — replace `new ArticleListItemDto(...)` with `article.ToListItemDto()`.

**Acceptance criteria:**
- No `new ArticleListItemDto(`, `new ArticleDetailDto(`, `new RawArticleDto(`, or `new ArticleEventDto(` literals remain in `ArticlesController.cs`.
- `using Api.Mappers;` is present.
- All four endpoints compile and return the same DTO shapes as before.

---

### TASK-3 — Create `Api/Mappers/EventMapper.cs`

**What to do:**
Create `Api/Mappers/EventMapper.cs` as `public static class EventMapper` in namespace `Api.Mappers`. All sub-type mappers live in this same file. Implement:

- `public static EventListItemDto ToListItemDto(this Event evt)` — maps `Id`, `Title`, `Summary`, `Status.ToString()`, `FirstSeenAt`, `LastUpdatedAt`, `Articles.Count`, `Contradictions.Count(c => !c.IsResolved)`.
- `public static EventDetailDto ToDetailDto(this Event evt)` — maps all scalar fields; delegates to `ToEventArticleDto()`, `ToDto()` on `EventUpdate`, `ToDto()` on `Contradiction`; preserves the `OrderBy(u => u.CreatedAt)` on updates; computes `ReclassifiedCount` as `evt.Articles.Count(a => a.WasReclassified)`.
- `public static EventArticleDto ToEventArticleDto(this Article article)` — maps `Id`, `Title`, `Summary`, `Role?.ToString() ?? string.Empty`, `AddedToEventAt ?? ProcessedAt`.
- `public static EventUpdateDto ToDto(this EventUpdate eu)` — maps `Id`, `FactSummary`, `IsPublished`, `CreatedAt`.
- `public static ContradictionDto ToDto(this Contradiction c)` — maps `Id`, `Description`, `IsResolved`, `CreatedAt`, `ContradictionArticles.Select(ca => ca.ArticleId).ToList()`.

**Acceptance criteria:**
- File exists at `Api/Mappers/EventMapper.cs`.
- Namespace is `Api.Mappers`.
- Class is `public static`; no instance members, no DI.
- All five methods present with exact signatures above.
- Sub-type mappers (`EventArticleDto`, `EventUpdateDto`, `ContradictionDto`) are in this same file.
- Enums use `.ToString()` only.

---

### TASK-4 — Update `EventsController.cs` to use `EventMapper`

**What to do:**
In `Api/Controllers/EventsController.cs`:

1. Add `using Api.Mappers;` at the top.
2. `GetAll` — replace `events.Select(e => new EventListItemDto(...))` with `events.Select(e => e.ToListItemDto())`.
3. `GetById` — replace the entire `new EventDetailDto(...)` block (all nested projections included) with `evt.ToDetailDto()`.

**Acceptance criteria:**
- No `new EventListItemDto(`, `new EventDetailDto(`, `new EventArticleDto(`, `new EventUpdateDto(`, or `new ContradictionDto(` literals remain in `EventsController.cs`.
- `using Api.Mappers;` is present.
- Both endpoints compile and return the same DTO shapes as before.

---

### TASK-5 — Create `Api/Mappers/SourceMapper.cs`

**What to do:**
Create `Api/Mappers/SourceMapper.cs` as `public static class SourceMapper` in namespace `Api.Mappers`. Implement:

- `public static SourceDto ToDto(this Source source)` — maps `Id`, `Name`, `Url`, `Type.ToString()`, `IsActive`, `LastFetchedAt`.

**Acceptance criteria:**
- File exists at `Api/Mappers/SourceMapper.cs`.
- Namespace is `Api.Mappers`.
- Class is `public static`, single `ToDto` extension method.
- Enum `SourceType` mapped via `.ToString()`.

---

### TASK-6 — Update `SourcesController.cs` to use `SourceMapper`

**What to do:**
In `Api/Controllers/SourcesController.cs`:

1. Add `using Api.Mappers;` at the top.
2. Delete the `private static SourceDto ToDto(Source source)` method at the bottom of the class.
3. Update all call sites to use the extension method syntax: `source.ToDto()` and `sources.Select(s => s.ToDto())`.

**Acceptance criteria:**
- No `private static SourceDto ToDto(` method exists in `SourcesController.cs`.
- `using Api.Mappers;` is present.
- All four endpoints (`GetAll`, `GetById`, `Create`, `Update`) compile and return `SourceDto`.

---

### TASK-7 — Create `Api/Mappers/PublishTargetMapper.cs`

**What to do:**
Create `Api/Mappers/PublishTargetMapper.cs` as `public static class PublishTargetMapper` in namespace `Api.Mappers`. Implement:

- `public static PublishTargetDto ToDto(this PublishTarget target)` — maps `Id`, `Name`, `Platform.ToString()`, `Identifier`, `SystemPrompt`, `IsActive`.

**Acceptance criteria:**
- File exists at `Api/Mappers/PublishTargetMapper.cs`.
- Namespace is `Api.Mappers`.
- Class is `public static`, single `ToDto` extension method.
- Enum `Platform` mapped via `.ToString()`.

---

### TASK-8 — Update `PublishTargetsController.cs` to use `PublishTargetMapper`

**What to do:**
In `Api/Controllers/PublishTargetsController.cs`:

1. Add `using Api.Mappers;` at the top.
2. Delete the `private static PublishTargetDto ToDto(PublishTarget target)` method at the bottom of the class.
3. Update all call sites to use extension method syntax: `target.ToDto()` and `targets.Select(t => t.ToDto())`.

**Acceptance criteria:**
- No `private static PublishTargetDto ToDto(` method exists in `PublishTargetsController.cs`.
- `using Api.Mappers;` is present.
- All five endpoints (`GetAll`, `GetActive`, `GetById`, `Create`, `Update`) compile and return `PublishTargetDto`.

---

### TASK-9 — Create `Api/Mappers/UserMapper.cs`

**What to do:**
Create `Api/Mappers/UserMapper.cs` as `public static class UserMapper` in namespace `Api.Mappers`. Both `UserDto` and `LoginResponse` belong to the User aggregate and are co-located in this file. Implement:

- `public static UserDto ToDto(this User user)` — maps `Id`, `Email`, `FirstName`, `LastName`, `Role.ToString()`.
- `public static LoginResponse ToLoginResponse(this User user, string token)` — maps `Id`, `Email`, `Role.ToString()`, and passes `token` through as the extra parameter (token is not part of the domain model, so it is a required plain parameter, not optional).

**Acceptance criteria:**
- File exists at `Api/Mappers/UserMapper.cs`.
- Namespace is `Api.Mappers`.
- Class is `public static`, two extension methods with exact signatures above.
- `LoginResponse` mapper is in this same file.
- `token` is a required parameter on `ToLoginResponse`, not optional.
- Enum `UserRole` mapped via `.ToString()`.

---

### TASK-10 — Update `UsersController.cs` to use `UserMapper`

**What to do:**
In `Api/Controllers/UsersController.cs`:

1. Add `using Api.Mappers;` at the top.
2. `GetAllUsers` — replace `users.Select(user => new UserDto(...))` with `users.Select(u => u!.ToDto())`.
3. `CreateUser` — replace `new UserDto(...)` with `user.ToDto()`.
4. `UpdateEditor` — replace `new UserDto(...)` with `user.ToDto()`.

**Acceptance criteria:**
- No `new UserDto(` literals remain in `UsersController.cs`.
- `using Api.Mappers;` is present.
- All three endpoints compile and return the same `UserDto` shape as before.

---

### TASK-11 — Update `AuthController.cs` to use `UserMapper`

**What to do:**
In `Api/Controllers/AuthController.cs`:

1. Add `using Api.Mappers;` at the top.
2. `Login` — replace `new LoginResponse(user.Id, user.Email, user.Role.ToString(), token)` with `user.ToLoginResponse(token)`.
3. `Register` — replace `new LoginResponse(user.Id, user.Email, user.Role.ToString(), token)` with `user.ToLoginResponse(token)`.

**Acceptance criteria:**
- No `new LoginResponse(` literals remain in `AuthController.cs`.
- `using Api.Mappers;` is present.
- Both endpoints compile and return the same `LoginResponse` shape as before.

---

### TASK-12 — Project-wide verification

**What to do:**
After all previous tasks are complete:

1. Run `dotnet build` from the solution root and confirm exit code 0 with zero errors.
2. Grep `Api/Controllers/` for all known DTO constructor patterns — every search must return zero hits:
   - `new ArticleListItemDto(`, `new ArticleDetailDto(`, `new RawArticleDto(`, `new ArticleEventDto(`
   - `new EventListItemDto(`, `new EventDetailDto(`, `new EventArticleDto(`, `new EventUpdateDto(`, `new ContradictionDto(`
   - `new SourceDto(`, `new PublishTargetDto(`, `new UserDto(`, `new LoginResponse(`
3. Confirm `Api/Mappers/` contains exactly five files: `ArticleMapper.cs`, `EventMapper.cs`, `SourceMapper.cs`, `PublishTargetMapper.cs`, `UserMapper.cs`.
4. Confirm no mapper file contains `private` members, instance fields, constructors, or DI injections.
5. Confirm all mapper files carry namespace `Api.Mappers`.

**Acceptance criteria:**
- `dotnet build` exits with code 0.
- Zero inline `new XxxDto(` constructions remain in any file under `Api/Controllers/`.
- Exactly five mapper files exist under `Api/Mappers/`, each a `public static class`.
- No controller retains any DTO construction logic.

---

## Execution Order

```
TASK-1  →  TASK-2    (ArticleMapper → ArticlesController)
TASK-3  →  TASK-4    (EventMapper → EventsController)
TASK-5  →  TASK-6    (SourceMapper → SourcesController)
TASK-7  →  TASK-8    (PublishTargetMapper → PublishTargetsController)
TASK-9  →  TASK-10   (UserMapper → UsersController)
            TASK-11  (UserMapper → AuthController, depends on TASK-9)
                      TASK-12  (verification — depends on all above)
```

The five mapper-creation tasks (TASK-1, TASK-3, TASK-5, TASK-7, TASK-9) are independent of each other and can be worked in parallel. Each controller-update task depends only on its corresponding mapper task.
