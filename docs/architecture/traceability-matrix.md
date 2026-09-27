# Матриця простежуваності архітектури (Traceability Matrix)

| ID | Тип | Опис | Компонент(и) | Проєктне рішення | Метод верифікації | Тестові випадки |
| --- | --- | --- | --- | --- | --- | --- |
| FR-001 | FR | Збір та стандартизація вакансій | Job Parser Service, Job Ingestion & Normalization Service, Data Repositories, Relational Database | TBD | TBD | TBD |
| FR-002 | FR | Обробка запитів ШІ-асистентом | UI Components, HTTP Gateway Service, AI Consultant Service, Storage Service | TBD | TBD | TBD |
| FR-003 | FR | Ринкова аналітика | UI Components, HTTP Gateway Service, Analytics & Statistics Service, Storage Service, Cache Service | TBD | TBD | TBD |
| FR-004 | FR | Фільтрація та пошук | UI Components, HTTP Gateway Service, Storage Service, Cache Service, Data Repositories, Relational Database | TBD | TBD | TBD |
| FR-005 | FR | Збереження вакансій | UI Components, HTTP Gateway Service, Storage Service, Data Repositories, Relational Database | TBD | TBD | TBD |
| FR-006 | FR | Індивідуальні рекомендації | UI Components, HTTP Gateway Service, Analytics & Statistics Service, Storage Service | TBD | TBD | TBD |
| FR-007 | FR | Особиста статистика користувача | UI Components, HTTP Gateway Service, Analytics & Statistics Service, Storage Service, Cache Service | TBD | TBD | TBD |
| FR-008 | FR | Розділення між ролями | UI Components, HTTP Gateway Service, Authentication Service | TBD | TBD | TBD |
| FR-009 | FR | Збереження контексту ШІ-чату | AI Consultant Service, Storage Service | TBD | TBD | TBD |
| FR-010 | FR | Аналіз вакансії через ШІ | UI Components, HTTP Gateway Service, AI Consultant Service, Storage Service | TBD | TBD | TBD |
| FR-011 | FR | Пагінація каталогу вакансій | UI Components, HTTP Gateway Service, Storage Service, Cache Service | TBD | TBD | TBD |
| FR-012 | FR | Імпорт та експорт даних про вакансії | UI Components, Storage Service, Job Ingestion & Normalization Service, Data Repositories, Relational Database | TBD | TBD | TBD |
| FR-013 | FR | Імпорт та експорт прогресу/статистики | UI Components, Storage Service, Data Repositories, Relational Database | TBD | TBD | TBD |
| PERF-001 | NFR | Каталог 20 вакансій <1с, 95%, 10k записів | TBD | TBD | TBD | TBD |
| PERF-002 | NFR | ШІ: індикатор ≤1с, відповідь ≤30с, 90% | TBD | TBD | TBD | TBD |
| PERF-003 | NFR | Парсинг ≥500 вакансій/10 хв, ≤2 req/s | TBD | TBD | TBD | TBD |
| REL-001 | NFR | 1 джерело down → решта + кеш ≤12 год з міткою | TBD | TBD | TBD | TBD |
| REL-002 | NFR | Crash → відновлення ≤10с без втрат | TBD | TBD | TBD | TBD |
| SEC-001 | NFR | bcrypt/AES-256, 0 секретів у plaintext | TBD | TBD | TBD | TBD |
| USE-001 | NFR | Новачок: пошук → фільтрація → збереження | TBD | TBD | TBD | TBD |
| MAINT-001 | NFR | Покриття парсингу ≥70%, CI ≤5 хв | TBD | TBD | TBD | TBD |
| REL-003 | NFR | LLM down → повідомлення ≤30с + звичайний пошук | TBD | TBD | TBD | TBD |
| FUNC-001 | NFR | 0 вигаданих вакансій/посилань у відповіді ШІ | TBD | TBD | TBD | TBD |
