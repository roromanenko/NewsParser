# NewsParser — Roadmap

_Use this file to start any planning session with Claude. For current system state see `docs/system/status.md`. For ideas without priority see `docs/planning/backlog.md`._

---

## Completed

- [x] Database: all entities, migrations, EF configurations, pgvector
- [x] RSS parser with validation and deduplication
- [x] AI processing pipeline: Gemini analysis, Claude generation, retry logic
- [x] CMS / Editorial approval: JWT auth, Articles UI, approve/reject workflow
- [x] Platform content generation: PublishTarget, ClaudeContentGenerator
- [x] Publication: TelegramPublisher, reply threading, PublishLog
- [x] Event Graph + Living Article: classification, summary updates, contradictions, EventUpdateWorker
- [x] **Telegram parser** — Telethon-based channel monitoring
- [x] **Race conditions in workers** — гонка состояний, требует диагностики и фикса
- [x] **Worker overlap** — лишние воркеры которые переобрабатывают статьи, требует ревизии
- [x] **Event Publishing** Use Event as Publication source item instead of Article since Event includes >= 1 article and it is more siutable for review and publication content generation.
- [x] Make Cotradiction and Update name in ukrainian. 
- [x] Generate Event title, not take from the first article
- [x] **Media support**

---

## Now (активная разработка)

_Текущие проблемы которые нужно решить до новых фич:_
 - [ ] **AI requsts tracking** 

---

## Next (следующий этап)

- [ ] **AI Image Composition** 
    - Генерация обложек публикаций через gpt-image-2: композит из медиа статьи + айдентика канала. 
    - Опционально по флагу в PublishTarget.

- [ ] **Website publishing** PublishTarget model готов, нужен WebsitePublisher. Зависит от: выбора целевой CMS (WordPress API / custom endpoint).

---

## Planned (приоритизировано, не начато)

- [ ] **Analytics**
    - Расширить PublishLog метриками (просмотры, переходы)
    - Dashboard в UI: статистика по источникам, каналам, статусам
    - Интеграция с TGStat API
    - WasReclassified dashboard для калибровки классификатора Зависит от: TGStat API доступа, стабильного PublishLog.

- [ ] **Source Credibility Score** Динамический рейтинг доверия источника на основе истории:
    - Частота противоречий с другими источниками
    - Частота опровержений фактов
    - Скорость публикации (первым = выше доверие)
    - Процент отклонений редактором Влияет на: EventClassifier (статья приходит с весом источника), логику противоречий (низкий score = автоматически менее достоверное). Зависит от: накопленной истории публикаций (нужны данные).

- [ ] **Predictive Publishing** Оптимальное время публикации на основе истории охвата:
    - `ScheduledAt` в Publication
    - Отдельный SchedulerWorker
    - Интеграция с TGStat API для исторических данных
    - Предсказание трендов и вероятности просмотров Зависит от: Step 10 (аналитика), TGStat API, накопленных данных.

---

## Backlog (идеи без даты)
- Instagram publishing
- Image generation (для Instagram)
- Voice / video generation (TikTok)
- Multi-tenancy / SaaS billing
- A/B тест промптов генерации

---

_Source Credibility и Predictive Publishing требуют накопленных данных — запускать не раньше чем через несколько месяцев реальной работы системы._