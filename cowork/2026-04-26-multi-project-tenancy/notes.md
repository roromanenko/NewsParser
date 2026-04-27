# Multi-Project Tenancy — Planning Notes

**Date:** 2026-04-26
**Topic:** Введение Project как корневой сущности тенантности

## Контекст

Сейчас все сущности (Source, Article, Event, Publication, PublishTarget) живут в едином глобальном
скоупе. Тематика разных PublishTarget'ов может радикально отличаться, и держать все источники,
статьи и события в одном пуле — неэффективно: смешивается аналитика, embedding-поиск
по событиям ищет соседей не по теме, а главное — `Infrastructure/AI/Prompts/analyzer.txt`
содержит **жёстко зашитый список категорий** (Politics, Economics, Technology, Sports, Culture,
Science, War, Society, Health, Environment), который заведомо не подходит для всех будущих
тематических каналов.

## Рассмотренные варианты

1. **Project как жёсткий тенант, Source принадлежит одному проекту.** Простая схема, дублирование
   при пересечении источников.
2. **Source — общий ресурс, M:N с Project, анализ per-project.** Шеринг fetch'а, но AI-стоимость
   всё равно умножается.
3. **Двухстадийный анализ (universal Stage 1 + project Stage 2).** Embedding и базовая
   экстракция один раз; категории/теги/summary per-project. Самое сложное, но дешёвое
   при множестве проектов на общих источниках.
4. **Project как фильтр без полноценного тенанта.** Минимально инвазивно, но не решает
   корневую проблему.

## Выбранный вариант: 1

Жёсткий tenant-boundary через `Project`. Source принадлежит одному проекту через FK.
Если один и тот же RSS нужен в двух проектах — заводится **две записи Source** с одним URL.
Принимаем дублирование fetch'а и AI-анализа сознательно — в обмен на простоту схемы и кода.

## Зафиксированные решения по развилкам

| Развилка | Решение |
|---|---|
| Где хранить проектный analyzer prompt и категории | **Полностью в БД**: `Project.AnalyzerPromptText` (text), `Project.Categories` (text[]). Редактируется из UI без деплоя. |
| Как пользователь выбирает текущий проект в API/UI | **URL segment**: `/api/projects/{projectId}/articles`, `/api/projects/{projectId}/events`, и т.д. Project ID — часть маршрута scoped-эндпоинтов. |
| Связь User ↔ Project | **Все пользователи видят все проекты**. Никакой M:N таблицы, никакой проектной авторизации в auth-middleware. |
| Миграция существующих данных | **Один Default project на всё**. Создаётся при первой миграции, все существующие Source/Article/Event/PublishTarget привязываются к нему. |

## Дополнительные предположения для ADR

- `PublishTarget` тоже становится per-project (FK `ProjectId`) — это ядро всей идеи.
- `Event.Embedding` + pgvector kNN-поиск событий должен фильтровать по `ProjectId` (запрос вида
  `WHERE project_id = @x ORDER BY embedding <-> @vec LIMIT k`). Отдельные partial-индексы
  пока не нужны — обычный индекс + предикат.
- `AiRequestLog` получает `ProjectId` для атрибуции стоимости по проектам.
- Embedding на `Article` остаётся — он завязан на текст, не на проект.
- Workers (`SourceFetcherWorker`, `ArticleAnalysisWorker`, `PublicationGenerationWorker`,
  `PublishingWorker`) продолжают итерировать глобально, но при создании/чтении проставляют
  и фильтруют `ProjectId` через source.

## Что осталось на усмотрение архитектора

- Точная схема `Project` (slug? IsActive? DefaultLanguage? Description? CreatedAt?).
- Хранение `Categories` — `text[]` в `Project` или отдельная таблица `ProjectCategory`?
  (склоняюсь к `text[]` — нет атрибутов на категории).
- Маршрутизация ASP.NET Core: route prefix через `[Route("api/projects/{projectId:guid}/[controller]")]`
  или через Minimal API group? Как валидируется существование проекта (middleware/filter/action filter)?
- Где живёт «глобальный» эндпоинт `GET /api/projects` (CRUD проектов) — отдельный
  `ProjectsController` без префикса.
- Миграция SQL (DbUp script `0006_introduce_projects.sql`):
  - CREATE TABLE Project
  - INSERT Default project
  - ALTER TABLE на Source/Article/Event/PublishTarget/AiRequestLog: добавить ProjectId NOT NULL
    с DEFAULT (Default.Id), затем drop default
  - индексы на (project_id, ...) для всех ключевых выборок
- UI: project switcher в layout, добавление projectId в React Router routes,
  обновление сгенерированного API-клиента.

## Out of scope для этого ADR

- Per-project авторизация (решено: все видят всё).
- Шеринг источников между проектами (решено: дублируем).
- Per-project AI model selection (можно добавить позже как поле в `Project`).
- Per-project кастомизация других промптов (`event_classifier`, `generator`, etc.) —
  на первой итерации только `analyzer`. Остальные остаются общими.
