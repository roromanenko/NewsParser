# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
npm run dev          # Start Vite development server
npm run build        # Type-check (tsc -b) then build production bundle
npm run lint         # Run ESLint across all files
npm run preview      # Preview production build locally

# Regenerate TypeScript API client from backend Swagger spec (backend must be running on :5172)
npm run generate-api
```

## Architecture Overview

This is a React 19 + TypeScript SPA for managing news article curation and publishing. It communicates with a .NET backend on `http://localhost:5172`.

### Feature-Based Structure

Code is organized under `src/features/` by domain:
- `articles/` — article browsing, detail view, approve/reject workflow
- `auth/` — login, protected routes
- `sources/` — news source CRUD (admin only)
- `users/` — user management (admin only)

Each feature has its own hooks that encapsulate React Query calls. Components stay thin — logic lives in hooks.

### API Layer

The backend exposes Swagger/OpenAPI. The client is auto-generated into `src/api/generated/` via `openapi-generator-cli`. **Do not manually edit files in `src/api/generated/`** — regenerate with `npm run generate-api` when the backend API changes.

The Axios instance in `src/lib/axios.ts`:
- Injects `Authorization: Bearer <token>` on every request (from Zustand auth store)
- Redirects to `/login` automatically on 401 responses

### State Management

Two layers, with clear separation:

| Concern | Tool | Location |
|---|---|---|
| Auth (user, token) | Zustand + localStorage persistence | `src/store/` |
| Server data (articles, sources, users) | TanStack React Query | hooks in each feature |
| UI state (modals, pagination, filters) | Local `useState` | component level |

React Query query keys follow the pattern `['resource', ...filters]`. Mutations invalidate relevant query keys on success.

### Routing & Access Control

Routes are defined in `src/router/`. Two wrappers:
- `ProtectedRoute` — redirects unauthenticated users to `/login`
- `AdminRoute` — restricts to users with `Admin` role (uses `usePermissions()` hook)

Admin-only routes: `/sources`, `/users`.

### UI Component System

Base components in `src/components/ui/` use **Class Variance Authority (CVA)** for variant-based styling with Tailwind CSS. Use `cn()` from `src/lib/utils.ts` to merge Tailwind classes.

Shared structural components in `src/components/shared/`: `DataTable`, `PageHeader`, `Pagination`, `ConfirmDialog`, `SlideOver`.

### Forms

Forms use **React Hook Form** + **Zod** for validation. Pass the Zod schema through `@hookform/resolvers/zod`.

### Path Alias

`@` maps to `./src` — use `@/components/...`, `@/features/...`, etc.
