---
description: Generate the initial PROJECT_MAP.md (run once at the start of the review workflow)
---

Produce `docs/reviews/PROJECT_MAP.md` by mapping this solution. Do not review or
critique anything yet — just map. Keep the output under ~400 lines.

Cover the following sections:

## 1. Solution structure
- All projects in the solution (`.sln` file)
- Target framework(s) of each project
- Project references between projects
- Project types: web, class library, tests, etc.

## 2. Key NuGet packages
- Top 20 packages by importance (framework, ORM, DI, logging, auth,
  serialization, test framework)
- Installed version of each
- Note whether the project uses `packages.config` or `PackageReference`

## 3. Entry points and composition
- `Global.asax` / `Startup.cs` / `Program.cs`
- Where routes are registered
- Where filters are registered
- DI container and where services are registered
- Any `App_Start` files worth noting

## 4. Data access
- ORM in use (EF6 / EF Core / Dapper / ADO / mix)
- Location of DbContext(s)
- Repository pattern present? Unit of Work?
- Raw SQL usage: where and how much
- Migrations: approach and location

## 5. Authentication and authorization
- Auth scheme (Forms, OWIN cookie, ASP.NET Identity, JWT, custom)
- Where the auth pipeline is configured
- Role/claim model if visible

## 6. Views and frontend
- Razor layout structure (`_Layout.cshtml`, sections, shared partials)
- Bundle configuration (`BundleConfig.cs` or webpack)
- JavaScript organization: where viewmodels live, how they're loaded
- Knockout and jQuery versions
- Any other frontend libraries (Bootstrap, Moment, etc.)

## 7. Tests
- Test projects, framework used
- Rough coverage posture (has tests / sparse / none)

## 8. Structural observations
- Anything notable about the organization, conventions, or obvious concerns.
  Keep this factual — flag things like "three different DI containers across
  projects" without judging them yet.

## Rules
- Do not modify source files.
- Do not produce findings. This is a map, not a review.
- Be specific with paths. Prefer "src/Web/App_Start/BundleConfig.cs" over
  "the bundle config file".
- If the solution is huge, cover the web project and domain project in full
  and summarize the rest.
