---
name: mappers
description: NewsParser mapping conventions. Use when adding a new mapper class, adding a ToDomain/ToEntity/ToDto method, extracting inline DTO construction from a controller, or mapping between Entity↔Domain (Infrastructure) or Domain→DTO (Api). Triggers on: "add mapper", "create mapper", "extract mapping", "ToDto", "ToDomain", "ToEntity", "map article/event/source to DTO", "inline mapping".
---

# Mapper Conventions

Mappers in NewsParser are **static classes of pure static extension methods**. There are two distinct mapping layers:

| Layer | Location | Direction | Pattern |
|---|---|---|---|
| Infrastructure | `Infrastructure/Persistence/Mappers/` | `Entity ↔ Domain` | `ToDomain` / `ToEntity` |
| Api | `Api/Mappers/` | `Domain → DTO` | `ToDto` / `ToListItemDto` / `ToDetailDto` |

The Api mapper layer **does not yet exist** — derive its structure from the Infrastructure pattern below and follow it consistently.

---

## Universal Rules

- **One file per root aggregate** (e.g., `ArticleMapper.cs`, `EventMapper.cs`). Related sub-types live in the **same file** (e.g., `EventMapper.cs` contains mappers for `Event`, `EventUpdate`, `Contradiction`, `ContradictionArticle`).
- **Only static extension methods** — no instances, no DI, no state.
- Extension method receiver is always the **source** type, return type is the **target**.
- Never add logging, try/catch, or side effects inside a mapper.

---

## Infrastructure Layer — `Entity ↔ Domain`

**Namespace:** `Infrastructure.Persistence.Mappers`
**File location:** `Infrastructure/Persistence/Mappers/{Aggregate}Mapper.cs`

### Method naming

| Method | Signature |
|---|---|
| Load from DB | `public static TDomain ToDomain(this TEntity entity)` |
| Persist to DB | `public static TEntity ToEntity(this TDomain domain)` |
| Factory (special construction) | `public static TDomain FromXxx(...)` — plain static, no `this` |

### Enum mapping pattern

```csharp
// Entity → Domain: parse from string
Status = Enum.Parse<ArticleStatus>(entity.Status),

// Nullable enum from entity
Role = entity.Role != null ? Enum.Parse<EventArticleRole>(entity.Role) : null,

// Domain → Entity: serialize to string
Status = domain.Status.ToString(),

// Nullable enum to entity
Role = domain.Role?.ToString(),
```

**Never use `int` for enum storage.** Enums are always stored as strings.

### Navigation property pattern

```csharp
// Required nav property — fallback to empty domain object
RawArticle = entity.RawArticle?.ToDomain() ?? new RawArticle(),

// Collection nav property — may be null if not Include()'d
Articles = entity.Articles?.Select(a => a.ToDomain()).ToList() ?? [],
```

Use `?? []` (not `?? new List<T>()`) for collection fallbacks.

### pgvector pattern

```csharp
// Entity → Domain: unwrap Vector to float[]
Embedding = entity.Embedding?.ToArray(),

// Domain → Entity: wrap float[] in Vector
Embedding = domain.Embedding != null ? new Vector(domain.Embedding) : null,
```

### Extra parameters on `ToEntity`

When the entity requires context that the domain model doesn't carry, add required/optional parameters **after** `this`:

```csharp
// PublicationMapper — articleId is required context, editorId is optional
public static PublicationEntity ToEntity(this Publication domain, Guid articleId, Guid? editorId = null)
```

Use optional parameters when a value is sometimes unavailable; add required parameters only when the value is structurally absent from the domain model.

### Factory methods

Use a named static factory (not `ToDomain`) when construction requires **multiple source objects**:

```csharp
// ArticleMapper — combines RawArticle + analysis result + external metadata
public static Article FromAnalysisResult(
    RawArticle rawArticle,
    ArticleAnalysisResult analysis,
    string modelVersion) => new() { ... };
```

---

## Api Layer — `Domain → DTO`

**Namespace:** `Api.Mappers`
**File location:** `Api/Mappers/{Aggregate}Mapper.cs`

DTO mapping is **one-way** (Domain → DTO only). No `ToEntity` or `ToDomain` methods belong here.

### Method naming

| DTO type | Method name |
|---|---|
| List/summary DTO | `ToListItemDto(this TDomain domain)` |
| Detail DTO | `ToDetailDto(this TDomain domain, ...)` |
| Single-purpose DTO with no variants | `ToDto(this TDomain domain)` |

### Enum pattern (DTOs)

Enums map to string via `.ToString()`. Nullable enums use null-coalescing:

```csharp
Sentiment = article.Sentiment.ToString(),
Role = article.Role?.ToString() ?? string.Empty,
```

Never use `Enum.Parse` in DTO mappers — DTOs receive string representations.

### Overloads vs optional parameters

Use **optional parameters** when the same method handles a variant that conditionally includes richer data:

```csharp
// Article detail includes event context when available — same method, optional param
public static ArticleDetailDto ToDetailDto(this Article article, Event? evt = null)
{
    ArticleEventDto? eventDto = evt is null ? null : new ArticleEventDto(
        evt.Id, evt.Title, evt.Status.ToString(),
        article.Role?.ToString() ?? string.Empty
    );

    return new ArticleDetailDto(
        article.Id, article.Title, article.Content, article.Category,
        article.Tags, article.Sentiment.ToString(), article.Language,
        article.Summary, article.ProcessedAt, article.ModelVersion,
        article.RawArticle.ToDto(),
        eventDto
    );
}
```

Use **overloads** only when parameter types conflict or the logic diverges significantly — prefer optional params for "same shape, more data" cases.

### Sub-object mapping

Sub-objects inside the same aggregate are mapped via their own `ToDto` extension:

```csharp
// RawArticleMapper.cs in Api/Mappers/
public static RawArticleDto ToDto(this RawArticle raw) => new(
    raw.Id, raw.Title, raw.OriginalUrl, raw.PublishedAt, raw.Language
);
```

Keep related sub-mappers in the **same file** as their parent aggregate mapper.

### Collection mapping in controllers

After extracting mappers, controller code becomes:

```csharp
// Before (inline)
var items = articles.Select(a => new ArticleListItemDto(
    a.Id, a.Title, a.Category, a.Tags,
    a.Sentiment.ToString(), a.Language, a.Summary, a.ProcessedAt
)).ToList();

// After (with mapper)
var items = articles.Select(a => a.ToListItemDto()).ToList();
```

---

## Example: Extracting Inline Controller Mapping

When a controller constructs DTOs inline, extract them to `Api/Mappers/`:

1. Create `Api/Mappers/{Aggregate}Mapper.cs`
2. Add `using Api.Mappers;` to the controller
3. Replace each `new XxxDto(...)` block with the appropriate `ToXxxDto()` call
4. Keep sub-DTO mappers (e.g., `RawArticleDto`, `ArticleEventDto`) in the same file

---

## Checklist

Before submitting a new mapper:

- [ ] Static class, static extension methods only
- [ ] File in correct layer directory
- [ ] Correct namespace (`Infrastructure.Persistence.Mappers` or `Api.Mappers`)
- [ ] Enums use `Enum.Parse<T>` (load) / `.ToString()` (save/DTO)
- [ ] Navigation properties guarded with `?.` and `?? []` / `?? new T()`
- [ ] pgvector wrapped/unwrapped correctly
- [ ] Sub-type mappers co-located in same file as parent aggregate
- [ ] No logging, no side effects, no DI
