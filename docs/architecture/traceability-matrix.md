# Матриця простежуваності архітектури (Traceability Matrix)

| ID | Тип | Опис | Компонент(и) | Проєктне рішення | Метод верифікації | Тестові випадки |
| --- | --- | --- | --- | --- | --- | --- |
| FR-001 | FR | Збір та стандартизація вакансій | Job Parser Service, Job Ingestion & Normalization Service, Data Repositories, Relational Database | BackgroundService-конвеєр Parser → Ingestion → репозиторії (§3.4.1, §3.4.2) | TBD | TBD |
| FR-002 | FR | Обробка запитів ШІ-асистентом | UI Components, HTTP Gateway Service, AI Consultant Service, Storage Service | REST/JSON через Gateway (JWT Bearer); промпт-контекст через Storage; зовнішній LLM API (§3.2, §3.3.5, §3.1) | TBD | TBD |
| FR-003 | FR | Ринкова аналітика | UI Components, HTTP Gateway Service, Analytics & Statistics Service, Storage Service, Cache Service | Обчислення в Analytics; звіти Cache-Aside (§3.3.4) | TBD | TBD |
| FR-004 | FR | Фільтрація та пошук | UI Components, HTTP Gateway Service, Storage Service, Cache Service, Data Repositories, Relational Database | Cache-Aside читання через Storage-фасад (§3.3.2, §3.3.3) | TBD | TBD |
| FR-005 | FR | Збереження вакансій | UI Components, HTTP Gateway Service, Storage Service, Data Repositories, Relational Database | Storage-фасад над репозиторіями (§3.3.3) | TBD | TBD |
| FR-006 | FR | Індивідуальні рекомендації | UI Components, HTTP Gateway Service, Analytics & Statistics Service, Storage Service | On-demand ранжування в Analytics; списки не кешуються (§3.3.4) | TBD | TBD |
| FR-007 | FR | Особиста статистика користувача | UI Components, HTTP Gateway Service, Analytics & Statistics Service, Storage Service, Cache Service | Агрегація в Analytics; звіти Cache-Aside (§3.3.4) | TBD | TBD |
| FR-008 | FR | Розділення між ролями | UI Components, HTTP Gateway Service, Authentication Service | JWT Bearer через Gateway (401/403); рольові політики в Auth (§3.3.1, §3.2) | TBD | TBD |
| FR-009 | FR | Збереження контексту ШІ-чату | AI Consultant Service, Storage Service | Сесійна історія в Storage; підстановка контексту в промпт (§3.3.5) | TBD | TBD |
| FR-010 | FR | Аналіз вакансії через ШІ | UI Components, HTTP Gateway Service, AI Consultant Service, Storage Service | Промпт-аналіз через Storage-контекст (§3.3.5) | TBD | TBD |
| FR-011 | FR | Пагінація каталогу вакансій | UI Components, HTTP Gateway Service, Storage Service, Cache Service, Data Repositories, Relational Database | Сторінкова видача через Storage; Cache-Aside (§3.3.2, §3.3.3) | TBD | TBD |
| FR-012 | FR | Імпорт та експорт даних про вакансії | UI Components, HTTP Gateway Service, Storage Service, Job Ingestion & Normalization Service, Data Repositories, Relational Database | Файловий обмін через Storage; імпорт через Ingestion-конвеєр (§3.4.2) | TBD | TBD |
| FR-013 | FR | Імпорт та експорт прогресу/статистики | UI Components, HTTP Gateway Service, Storage Service, Data Repositories, Relational Database | Файловий обмін профілю через Storage-фасад (§3.3.3) | TBD | TBD |
| PERF-001 | NFR | Каталог 20 вакансій <1с, 95%, 10k записів | UI Components, HTTP Gateway Service, Storage Service, Cache Service, Data Repositories, Relational Database | Cache-Aside + TTL; In-Memory (§3.3.2) | TBD | TBD |
| PERF-002 | NFR | ШІ: індикатор ≤1с, відповідь ≤30с, 90% | UI Components, HTTP Gateway Service, AI Consultant Service, Storage Service | Негайний ack (UI/Gateway); контекст і історія через Storage (§3.3.5) | TBD | TBD |
| PERF-003 | NFR | Парсинг ≥500 вакансій/10 хв, ≤2 req/s | Job Parser Service, Job Ingestion & Normalization Service | Rate-limited фоновий парсинг (§3.4.1) | TBD | TBD |
| REL-001 | NFR | 1 джерело down → решта + кеш ≤12 год з міткою | UI Components, HTTP Gateway Service, Storage Service, Cache Service, Job Parser Service, Job Ingestion & Normalization Service, Data Repositories, Relational Database | Ізоляція відмов за джерелами; stale-cache з міткою (§3.4.1, §3.3.2) | TBD | TBD |
| REL-002 | NFR | Crash → відновлення ≤10с без втрат | UI Components, HTTP Gateway Service, Storage Service, Data Repositories, Relational Database | Синхронний запис Storage→DB (§3.3.3) | TBD | TBD |
| SEC-001 | NFR | bcrypt/AES-256, 0 секретів у plaintext | Authentication Service, Security Service, Storage Service, Data Repositories, Relational Database | Хешування/шифрування в Security; застосування в Auth/Storage (§3.3.6, §3.3.1) | TBD | TBD |
| USE-001 | NFR | 90% новачків: пошук → фільтрація → збереження за 2 хвилини | UI Components, HTTP Gateway Service | Зрозумілі повідомлення про помилки (§3.2); порожні стани з підказками та скиданням фільтрів | TBD | TBD |
| MAINT-001 | NFR | Покриття парсингу ≥70%, CI ≤5 хв | Job Parser Service, Job Ingestion & Normalization Service | Модульні тести Parser+Ingestion; CI-гейт | TBD | TBD |
| REL-003 | NFR | LLM down → повідомлення ≤30с + звичайний пошук | UI Components, HTTP Gateway Service, AI Consultant Service | Таймаут/детекція в AI Consultant; ізоляція модульними межами (§1, §3.3.5) | TBD | TBD |
| FUNC-001 | NFR | 0 вигаданих вакансій/посилань у відповіді ШІ | AI Consultant Service, Storage Service | Видача вакансій тільки через Storage; без AI→DB (§3.3.5) | TBD | TBD |
