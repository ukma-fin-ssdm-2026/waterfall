# Матриця простежуваності архітектури (Traceability Matrix)

Архітектурний зріз вимог: кожен рядок пов'язує вимогу з компонентами, що реалізують її поведінку, і проєктним рішенням (механізм + посилання на `high-level-design.md`). Реєстр вимог — `docs/requirements/rtm.md`.

`Компонент(и)` — лише компоненти, чию поведінку вимога обмежує або спостерігає; спільні шляхи не дублюються.<br>
`Проєктне рішення` — обраний механізм, не переказ вимоги. Компоненти названо скорочено, деталі — за `§`-посиланнями.

| ID | Тип | Опис | Компонент(и) | Проєктне рішення | Метод верифікації | Тестові випадки |
| --- | --- | --- | --- | --- | --- | --- |
| FR-001 | FR | Збір та стандартизація вакансій | Job Parser Service<br>Job Ingestion & Normalization Service<br>Data Repositories<br>Relational Database | BackgroundService-конвеєр Parser → Ingestion → репозиторії (§3.4.1, §3.4.2) | TBD | TBD |
| FR-002 | FR | Обробка запитів ШІ-асистентом | UI Components<br>HTTP Gateway Service<br>AI Consultant Service<br>Storage Service | REST/JSON через Gateway (JWT Bearer)<br>промпт-контекст через Storage<br>зовнішній LLM API (§3.2, §3.3.5, §3.1) | TBD | TBD |
| FR-003 | FR | Ринкова аналітика | UI Components<br>HTTP Gateway Service<br>Analytics & Statistics Service<br>Storage Service<br>Cache Service | Обчислення в Analytics<br>звіти Cache-Aside (§3.3.4) | TBD | TBD |
| FR-004 | FR | Фільтрація та пошук | UI Components<br>HTTP Gateway Service<br>Storage Service<br>Cache Service<br>Data Repositories<br>Relational Database | Читання через Storage Service з Cache-Aside (§3.3.2, §3.3.3) | TBD | TBD |
| FR-005 | FR | Збереження вакансій | UI Components<br>HTTP Gateway Service<br>Storage Service<br>Data Repositories<br>Relational Database | Запис через Storage Service у DB через репозиторії (§3.3.3) | TBD | TBD |
| FR-006 | FR | Індивідуальні рекомендації | UI Components<br>HTTP Gateway Service<br>Analytics & Statistics Service<br>Storage Service | On-demand ранжування в Analytics<br>списки не кешуються (§3.3.4) | TBD | TBD |
| FR-007 | FR | Особиста статистика користувача | UI Components<br>HTTP Gateway Service<br>Analytics & Statistics Service<br>Storage Service<br>Cache Service | Агрегація в Analytics<br>звіти Cache-Aside (§3.3.4) | TBD | TBD |
| FR-008 | FR | Розділення між ролями | UI Components<br>HTTP Gateway Service<br>Authentication Service | JWT Bearer через Gateway (401/403)<br>рольові політики в Auth (§3.3.1, §3.2) | TBD | TBD |
| FR-009 | FR | Збереження контексту ШІ-чату | AI Consultant Service<br>Storage Service | Сесійна історія в Storage<br>підстановка контексту в промпт (§3.3.5) | TBD | TBD |
| FR-010 | FR | Аналіз вакансії через ШІ | UI Components<br>HTTP Gateway Service<br>AI Consultant Service<br>Storage Service | Промпт-аналіз через Storage-контекст (§3.3.5) | TBD | TBD |
| FR-011 | FR | Пагінація каталогу вакансій | UI Components<br>HTTP Gateway Service<br>Storage Service<br>Cache Service<br>Data Repositories<br>Relational Database | Сторінкова видача через Storage<br>Cache-Aside (§3.3.2, §3.3.3) | TBD | TBD |
| FR-012 | FR | Імпорт та експорт даних про вакансії | UI Components<br>HTTP Gateway Service<br>Storage Service<br>Job Ingestion & Normalization Service<br>Data Repositories<br>Relational Database | Файловий обмін через Storage<br>імпорт через Ingestion-конвеєр (§3.4.2) | TBD | TBD |
| FR-013 | FR | Імпорт та експорт прогресу/статистики | UI Components<br>HTTP Gateway Service<br>Storage Service<br>Data Repositories<br>Relational Database | Файловий обмін профілю через Storage Service (§3.3.3) | TBD | TBD |
| PERF-001 | NFR | Каталог 20 вакансій <1с, 95%, 10k записів | UI Components<br>HTTP Gateway Service<br>Storage Service<br>Cache Service<br>Data Repositories<br>Relational Database | Cache-Aside + TTL<br>In-Memory (§3.3.2) | TBD | TBD |
| PERF-002 | NFR | ШІ: індикатор ≤1с, відповідь ≤30с, 90% | UI Components<br>HTTP Gateway Service<br>AI Consultant Service<br>Storage Service | Негайний ack (UI/Gateway)<br>контекст і історія через Storage (§3.3.5) | TBD | TBD |
| PERF-003 | NFR | Парсинг ≥500 вакансій/10 хв, ≤2 req/s | Job Parser Service<br>Job Ingestion & Normalization Service | Rate-limited фоновий парсинг (§3.4.1) | TBD | TBD |
| REL-001 | NFR | 1 джерело down → решта + кеш ≤12 год з міткою | UI Components<br>HTTP Gateway Service<br>Storage Service<br>Cache Service<br>Job Parser Service<br>Job Ingestion & Normalization Service<br>Data Repositories<br>Relational Database | Ізоляція відмов за джерелами<br>stale-cache з міткою (§3.4.1, §3.3.2) | TBD | TBD |
| REL-002 | NFR | Crash → відновлення ≤10с без втрат | UI Components<br>HTTP Gateway Service<br>Storage Service<br>Data Repositories<br>Relational Database | Синхронний запис Storage→DB (§3.3.3) | TBD | TBD |
| SEC-001 | NFR | bcrypt/AES-256, 0 секретів у plaintext | Authentication Service<br>Security Service<br>Storage Service<br>Data Repositories<br>Relational Database | Хешування/шифрування в Security<br>застосування в Auth/Storage (§3.3.6, §3.3.1) | TBD | TBD |
| USE-001 | NFR | 90% новачків: пошук → фільтрація → збереження за 2 хвилини | UI Components<br>HTTP Gateway Service | Глобальна обробка помилок через Gateway (§3.2)<br>зрозумілі повідомлення про відсутність результатів | TBD | TBD |
| MAINT-001 | NFR | Покриття парсингу ≥70%, CI ≤5 хв | Job Parser Service<br>Job Ingestion & Normalization Service | Модульні тести Parser+Ingestion<br>CI-гейт | TBD | TBD |
| REL-003 | NFR | LLM down → повідомлення ≤30с + звичайний пошук | UI Components<br>HTTP Gateway Service<br>AI Consultant Service | Таймаут/детекція в AI Consultant<br>ізоляція модульними межами (§1, §3.3.5) | TBD | TBD |
| FUNC-001 | NFR | 0 вигаданих вакансій/посилань у відповіді ШІ | AI Consultant Service<br>Storage Service | Видача вакансій тільки через Storage<br>без AI→DB (§3.3.5) | TBD | TBD |
