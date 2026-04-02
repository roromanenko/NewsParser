---
name: api-conventions
description: >
  NewsParser API conventions for ASP.NET Core (.NET 10). Use when adding a new controller,
  endpoint, DTO, validator, or modifying auth/error-handling in the Api/ project. Triggers on:
  "add endpoint", "new controller", "create API route", "add DTO", "add validator", "API conventions",
  "REST endpoint", "HTTP status", "auth middleware", "FluentValidation".
---

# API Conventions — NewsParser

This skill captures the authoritative patterns used in `Api/`. Follow these exactly when adding or modifying API code — do not invent new patterns.

---

## Controller Structure

Every controller follows this template:

```csharp
[ApiController]
[Route("resource-name")]          // lowercase plural noun, no /api/ prefix
[Authorize(Roles = nameof(UserRole.Editor) + "," + nameof(UserRole.Admin))]
public class ResourcesController(
    IResourceRepository repo,
    IResourceService service) : BaseController  // BaseController for authenticated controllers
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ResourceListItemDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    { ... }
}
```

**Rules:**
- `[ApiController]` + `[Route("...")]` on every controller — no method-level route prefixes
- Use **primary constructors** (C# 12) for dependency injection — never field injection
- `CancellationToken cancellationToken = default` is always the **last** parameter
- Authenticated resource controllers extend **`BaseController`** (provides `UserId`, `UserEmail`)
- Auth-specific controllers (e.g. `AuthController`) extend `ControllerBase` directly
- Place in `Api/Controllers/`, namespace `Api.Controllers`

---

## Route Naming

| Pattern | Example |
|---|---|
| Collection | `GET /articles` |
| Single item | `GET /articles/{id:guid}` |
| Sub-action | `POST /articles/{id:guid}/approve` |
| Scoped sub-resource | `PUT /users/editors/{id:guid}` |
| Collection action | `POST /events/merge` |
| Partial update | `PATCH /events/{id:guid}/status` |

**Rules:**
- Routes are **lowercase plural nouns** — no `/api/` prefix, no versioning
- Sub-actions use kebab-case verbs: `/resolve-contradiction`, `/reclassify`
- Route constraints: always `{id:guid}` for Guid parameters
- No trailing slashes

---

## HTTP Methods & Return Types

| Operation | Method | Return type | Status |
|---|---|---|---|
| List (paged) | GET | `ActionResult<PagedResult<TDto>>` | 200 |
| Get by ID | GET | `ActionResult<TDetailDto>` | 200 / 404 |
| Create | POST | `ActionResult<TDto>` + `CreatedAtAction` | 201 |
| Action (approve, reject, merge) | POST | `ActionResult<TDto>` or `IActionResult` | 200 or 204 |
| Full update | PUT | `ActionResult<TDto>` | 200 |
| Partial update | PATCH | `IActionResult` | 204 |
| Delete | DELETE | `IActionResult` | 204 |

**Key distinctions:**
- Use `IActionResult` (not `ActionResult<T>`) when the response has no body → return `NoContent()`
- Use `ActionResult<TDto>` when returning data → return `Ok(dto)`
- `CreatedAtAction` is only used on true resource creation (POST that creates a new entity)
- Actions that mutate state but return the updated entity use `Ok(dto)` (e.g. approve, reject)

---

## HTTP Status Codes

Return these from controller action methods directly:

```csharp
return Ok(dto);                          // 200 — success with body
return CreatedAtAction(nameof(X), dto);  // 201 — resource created
return NoContent();                      // 204 — success, no body
return BadRequest("message");            // 400 — validation failure, inline
return Unauthorized("message");          // 401 — caller not authenticated
return NotFound();                       // 404 — resource not found (no message needed)
return Conflict("message");              // 409 — duplicate or conflicting state
```

**Do not throw exceptions for expected HTTP errors** in controllers — use the above methods directly.
Exception throwing is reserved for domain/service layer errors, which the middleware maps automatically.

---

## Error Response Format

`ExceptionMiddleware` catches **unhandled exceptions** from services/repos and maps them:

| Exception type | HTTP status |
|---|---|
| `KeyNotFoundException` | 404 Not Found |
| `InvalidOperationException` | 409 Conflict |
| `UnauthorizedAccessException` | 403 Forbidden |
| `ArgumentException` | 400 Bad Request |
| Any other | 500 Internal Server Error |

Error response JSON (camelCase):
```json
{
  "status": 404,
  "message": "Article with id ... was not found",
  "path": "/articles/abc-123"
}
```

**Design rule:** Services throw typed exceptions with descriptive messages; controllers handle 404/400/401 themselves via return statements for cases they can detect directly.

---

## Auth & Authorization

**JWT setup** (configured in `ApiServiceExtensions.AddJwt`):
- Scheme: `JwtBearerDefaults.AuthenticationScheme`
- Config section: `JwtOptions` (keys: `SecretKey`, `Issuer`, `Audience`)
- Middleware order: `UseAuthentication()` → `UseAuthorization()` (never swap)

**Role-based auth** — use the `[Authorize]` attribute with `UserRole` enum names:
```csharp
// Multiple roles (Editor OR Admin)
[Authorize(Roles = nameof(UserRole.Editor) + "," + nameof(UserRole.Admin))]

// Admin only
[Authorize(Roles = nameof(UserRole.Admin))]
```

**Accessing the caller's identity** in controllers (via `BaseController`):
```csharp
if (UserId is null) return Unauthorized();
await service.DoSomethingAsync(UserId.Value, ...);

var email = UserEmail; // nullable string from Identity.Name
```

Do not access `User.Claims` directly in controllers — use the `BaseController` helpers.

---

## DTOs and Request Models

**Location:** `Api/Models/`
**Type:** C# `record` — always, no classes

**Naming conventions:**
- `{Resource}ListItemDto` — item in a paged list (lightweight)
- `{Resource}DetailDto` — single item full detail
- `{Resource}Dto` — general DTO (used in create/update responses)
- `Create{Resource}Request` — POST body for creation
- `Update{Resource}Request` — PUT body for full update
- `{Action}{Resource}Request` — POST body for actions (e.g. `ApproveArticleRequest`, `RejectArticleRequest`)

**Pagination:**
```csharp
// Always use PagedResult<T> for list endpoints
return Ok(new PagedResult<ArticleListItemDto>(items, page, pageSize, total));

// PagedResult record (Api/Models/PagedResult.cs):
// Items, Page, PageSize, TotalCount → computed TotalPages, HasNextPage, HasPreviousPage
```

**Enum fields** in DTOs are always strings (`.ToString()` on the enum value) — never raw ints.

---

## Mapping (Domain → DTO)

Controllers never map inline — no `new ArticleListItemDto(a.Id, a.Title, ...)` in action methods.
All Domain → DTO mapping is done via static extension methods in `Api/Mappers/`.
See `.claude/skills/mappers/SKILL.md` for the full pattern and conventions.
```csharp
// CORRECT
var items = articles.Select(a => a.ToListItemDto()).ToList();
var dto = article.ToDetailDto(evt);

// WRONG — never do this in a controller
var dto = new ArticleDetailDto(article.Id, article.Title, ...);
```

---

## Validation

**Two-tier approach:**

1. **FluentValidation** (in `Api/Validators/`) for complex request-level validation:
```csharp
public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).MinimumLength(8).Matches(@"\d");
    }
}
```
- One validator per request type, named `{Request}Validator`
- Registered automatically via `AddFluentValidationAutoValidation()` + `AddValidatorsFromAssemblyContaining<RegisterRequestValidator>()`
- Returns 400 with FluentValidation error format automatically — no controller code needed

2. **Inline validation** in controllers for simple business rule checks:
```csharp
if (request.PublishTargetIds is null || request.PublishTargetIds.Count == 0)
    return BadRequest("At least one publish target must be specified");

if (!Enum.TryParse<EventStatus>(status, ignoreCase: true, out var eventStatus))
    return BadRequest($"Invalid status: {status}. Valid values: {string.Join(", ", Enum.GetNames<EventStatus>())}");
```

Use FluentValidation for structural/format validation; use inline checks for business rules that depend on context.

---

## Pagination Guard Pattern

All list endpoints that accept `page`/`pageSize` clamp the values before use:

```csharp
if (page < 1) page = 1;
if (pageSize is < 1 or > 100) pageSize = 20;
```

Always include this guard — do not silently trust query params.

---

## Middleware Pipeline Order

From `Program.cs` — order is mandatory:

```csharp
app.UseMiddleware<ExceptionMiddleware>();   // 1. catch all unhandled exceptions
app.UseHttpsRedirection();                 // 2. redirect HTTP → HTTPS
app.UseCors("AllowFrontend");             // 3. CORS before auth
app.UseAuthentication();                   // 4. populate User identity
app.UseAuthorization();                    // 5. enforce [Authorize]
app.MapControllers();                      // 6. route to controllers
```

When adding new middleware, place it at the correct position — inserting before `ExceptionMiddleware` means errors in that middleware won't be caught.

---

## Common Anti-Patterns to Avoid

- Do not use `[HttpGet("api/resource")]` — no `/api/` prefix anywhere
- Do not return raw domain models — always map to DTOs
- Do not catch `Exception` in controllers — let `ExceptionMiddleware` handle it
- Do not use `int` or `string` route params for entity IDs — always `{id:guid}`
- Do not access `HttpContext.User.Claims` directly — use `BaseController.UserId` / `UserEmail`
- Do not create `class` DTOs — use `record`
- Do not return `ActionResult<T>` for no-body responses — use `IActionResult` + `NoContent()`
- Do not map inline in controllers — use extension methods from `Api/Mappers/`
