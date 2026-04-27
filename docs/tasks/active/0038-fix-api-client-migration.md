# Fix API Client Migration Regression

## Goal
Replace all raw `apiClient` (manual axios) calls in `UI/src/` with the correct
generated OpenAPI client classes, so no feature hook bypasses the generated client
for API calls.

## Affected Layers
- UI

---

## Background

After the multi-project tenancy feature (`0022`), the backend routes for articles,
events, sources, publish-targets, and publications were moved to
`/projects/{projectId}/…`. The UI hooks were rewritten during that feature but used
raw `apiClient.get/post/put/patch/delete` calls instead of generated client classes.

Three files (`useAuth.ts`, `useRegister.ts`, `useUsers.ts`) are **already correct** —
they import `apiClient` solely to pass it as the axios instance to the generated class
constructor (`new AuthApi(undefined, '', apiClient)`). Those files are **not touched**
by this tasklist.

The generated client at `UI/src/api/generated/` currently still uses the old
(non-scoped) routes. It must be regenerated before scoped hooks can be migrated.
The three AI-ops hooks can be migrated immediately because `AiOperationsApi` already
exists in the generated client and its routes (`/ai-operations/…`) are unchanged.

---

## Tasks

### Phase 1 — Verify and regenerate the OpenAPI client

- [ ] **Inspect `UI/src/api/generated/api.ts`** — confirm the generated client does NOT
      yet contain a `ProjectsApi` class or paths beginning with
      `/projects/{projectId:guid}/`. If it does, skip Phase 1 and proceed to Phase 2
      immediately.
      _Acceptance: reviewer confirms presence or absence of `ProjectsApi` and
      `/projects/{projectId}` path prefixes before deciding whether to regenerate_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [ ] **Regenerate the OpenAPI client** — with the backend running on port 5172
      (`dotnet run --project Api --launch-profile http`), run `npm run generate-api`
      from `UI/`. The script calls:
      `openapi-generator-cli generate -i http://localhost:5172/swagger/v1/swagger.json
      -g typescript-axios -o src/api/generated --additional-properties=supportsES6=true,withInterfaces=true`
      _Acceptance: command exits 0; `UI/src/api/generated/api.ts` now contains a
      `ProjectsApi` class; scoped paths such as
      `/projects/{projectId}/articles` appear in the file; `npm run build` in `UI/`
      exits 0 before any hook changes_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 2 — Migrate AI operations hooks (no regeneration required)

These three hooks call `/ai-operations/…` routes, which are global (not project-scoped)
and already exist in the generated `AiOperationsApi` class.

- [x] **Modify `UI/src/features/aiOperations/useAiRequestMetrics.ts`** — remove the
      `import { apiClient } from '@/lib/axios'` line. Add
      `import { AiOperationsApi } from '@/api/generated'` and declare a module-level
      `const aiOpsApi = new AiOperationsApi(undefined, '', apiClient)` (keep the
      `apiClient` import **only** for passing to the constructor, as `AuthApi` does in
      `useAuth.ts`). Replace the `apiClient.get('/ai-operations/metrics', { params: … })`
      call with
      `aiOpsApi.aiOperationsMetricsGet(filters.from || undefined, filters.to || undefined,
      filters.provider || undefined, filters.worker || undefined, filters.model || undefined)`.
      Access response data via `res.data`.
      _Acceptance: `tsc --noEmit` in `UI/` exits 0; no raw `apiClient.get` call remains
      in this file; the hook's return type is unchanged_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/aiOperations/useAiRequestList.ts`** — same pattern.
      Remove the raw `apiClient.get('/ai-operations/requests', { params: … })` call.
      Add module-level `const aiOpsApi = new AiOperationsApi(undefined, '', apiClient)`.
      Replace with
      `aiOpsApi.aiOperationsRequestsGet(filters.from || undefined, filters.to || undefined,
      filters.provider || undefined, filters.worker || undefined, filters.model || undefined,
      filters.status || undefined, filters.search || undefined, page, pageSize)`.
      _Acceptance: `tsc --noEmit` exits 0; no raw `apiClient` call remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/aiOperations/useAiRequestDetail.ts`** — same pattern.
      Remove raw `apiClient.get('/ai-operations/requests/${id}')`. Add module-level
      `const aiOpsApi = new AiOperationsApi(undefined, '', apiClient)`. Replace with
      `aiOpsApi.aiOperationsRequestsIdGet(id!)`.
      _Acceptance: `tsc --noEmit` exits 0; no raw `apiClient` call remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 3 — Migrate project-scoped hooks (requires regenerated client from Phase 1)

Each hook below follows the same three-step pattern:
1. Remove `import { apiClient } from '@/lib/axios'`.
2. Import the appropriate generated API class and declare a module-level instance
   `const xApi = new XApi(undefined, '', apiClient)`, keeping `apiClient` imported
   only for the constructor.
3. Replace each raw `apiClient.get/post/put/patch/delete(...)` call with the
   corresponding generated method. Access data via `.then(r => r.data)` or
   `(await res).data`.

- [x] **Modify `UI/src/features/articles/useArticles.ts`** — replace
      `apiClient.get('/projects/${selectedProjectId}/articles', { params: … })` with
      the generated `ArticlesApi` method. The regenerated method will include a leading
      `projectId` parameter; pass `selectedProjectId!` as the first argument.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/articles/useArticleDetail.ts`** — replace
      `apiClient.get('/projects/${selectedProjectId}/articles/${id}')` with the
      generated `ArticlesApi` detail method, passing `selectedProjectId!` and `id`.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/events/useEvents.ts`** — replace
      `apiClient.get('/projects/${selectedProjectId}/events', { params: … })` with the
      generated `EventsApi` list method.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/events/useEventDetail.ts`** — replace
      `apiClient.get('/projects/${selectedProjectId}/events/${id}')` with the
      generated `EventsApi` detail method.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/events/useEventMutations.ts`** — replace all four raw
      calls (`resolveContradiction`, `mergeEvents`, `reclassifyArticle`, `changeStatus`)
      with the corresponding generated `EventsApi` mutation methods. The
      `changeStatus` call uses `Content-Type: application/json` with a raw string body;
      confirm the generated method signature and pass the body accordingly.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file;
      all four mutations compile without type errors_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/publications/usePublications.ts`** — replace
      `apiClient.get('/projects/${selectedProjectId}/publications/by-event/${eventId}')`
      with the generated `PublicationsApi` by-event method.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/publications/useAllPublications.ts`** — replace
      `apiClient.get('/projects/${selectedProjectId}/publications', { params: … })`
      with the generated `PublicationsApi` paged-list method.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/publications/usePublicationDetail.ts`** — replace
      `apiClient.get('/projects/${selectedProjectId}/publications/${id}')` with the
      generated `PublicationsApi` detail method.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/publications/usePublicationMutations.ts`** — replace
      all seven raw calls (`generateContent`, `updateContent`, `approve`, `reject`,
      `regenerate`, `uploadMedia`, `deleteMedia`) with the corresponding generated
      `PublicationsApi` methods. The `uploadMedia` call sends `multipart/form-data`;
      confirm the generated method accepts a `File` or `Blob` parameter and adjust
      accordingly.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file;
      all seven mutations compile without type errors_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/sources/useSources.ts`** — replace
      `apiClient.get('/projects/${selectedProjectId}/sources')` with the generated
      `SourcesApi` list method.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/sources/useSourceMutations.ts`** — replace all three
      raw calls (`createSource`, `updateSource`, `deleteSource`) with the corresponding
      generated `SourcesApi` mutation methods.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/publishTargets/usePublishTargets.ts`** — replace both
      raw calls (`usePublishTargets` and `useActivePublishTargets`) with the generated
      `PublishTargetsApi` methods.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/publishTargets/usePublishTargetMutations.ts`** — replace
      all three raw calls (`createTarget`, `updateTarget`, `deleteTarget`) with the
      corresponding generated `PublishTargetsApi` methods.
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/projects/useProjects.ts`** — replace
      `apiClient.get<ProjectListItemDto[]>('/projects')` with the generated
      `ProjectsApi` list method. Remove the hand-typed local `ProjectListItemDto`
      interface and import the generated type instead (if the regenerated client
      contains a `ProjectListItemDto` interface).
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file;
      no locally-defined duplicate DTO interface_
      _Skill: .claude/skills/code-conventions/SKILL.md_

- [x] **Modify `UI/src/features/projects/useProjectMutations.ts`** — replace all four
      raw calls (`useCreateProject`, `useUpdateProject`, `useToggleProjectActive`,
      `useDeleteProject`) with the corresponding generated `ProjectsApi` methods.
      Remove the locally-defined `CreateProjectData` and `UpdateProjectData` interfaces
      and use the generated request types instead (e.g. `CreateProjectRequest`,
      `UpdateProjectRequest`).
      _Acceptance: `tsc --noEmit` exits 0; no `apiClient` import remains in this file;
      no locally-defined request shape duplicates_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

### Phase 4 — Final verification

- [x] **Verify no stray `apiClient` API-call imports remain** — run:
      `grep -r "apiClient" UI/src/ --include="*.ts" --include="*.tsx"
       | grep -v "UI/src/lib/axios.ts"
       | grep -v "UI/src/api/generated/"
       | grep -v "new.*Api(undefined, '', apiClient)"`
      The output must be empty (zero lines). Any remaining hits indicate a file that
      was missed and must be migrated before closing this task.
      _Acceptance: grep produces no output; every legitimate `apiClient` reference
      is the constructor-injection pattern already used in `useAuth.ts`, `useRegister.ts`,
      and `useUsers.ts`_
      _Skill: .claude/skills/clean-code/SKILL.md_

- [x] **Run full UI build** — from `UI/`, run `npm run build`.
      _Acceptance: TypeScript compilation (`tsc -b`) exits 0 with no errors; Vite
      bundle step completes without errors_
      _Skill: .claude/skills/code-conventions/SKILL.md_

---

## Open Questions

- The generated `PublicationsApi.uploadMedia` method signature is unknown until the
  client is regenerated. If the generated method does not accept a `File` directly
  (some generators emit a `Blob` parameter or a `{ file: File }` object), the
  implementer must adapt the call in `usePublicationMutations.ts` to match the actual
  generated signature without creating a raw `axios` call.

- The `changeStatus` mutation in `useEventMutations.ts` sends a raw JSON string as
  the body with `Content-Type: application/json`. If the regenerated `EventsApi`
  method does not accept a raw string body, the implementer should check the
  `EventsController` action signature in `Api/Controllers/EventsController.cs` to
  confirm the expected body type and adapt accordingly.
