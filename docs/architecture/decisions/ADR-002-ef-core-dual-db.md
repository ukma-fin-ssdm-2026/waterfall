# ADR-002: MSSQL + SQLite через EF Core

- Статус: прийнято
- Дата: 2026-09-27
- Автор рішення: Данило

## Контекст

Продакшн потребує серверної СУБД, локальна розробка/демо — zero-setup. Два діалекти без ORM = подвійні запити.

## Рішення

EF Core репозиторії (§3.4.3); MSSQL на сервері (TDS/TCP), SQLite локально (файл). Типи атрибутів абстрактні (`uuid`, `json`, `enum` як рядок), мапінг на діалект — на рівні провайдера. Сутності з ідентифікаторами в API (`User`, `UserSession`, `ChatSession`) мають PK `uuid`, щоб унеможливити перебір записів інкрементом.

## Наслідки

+ Один код доступу, складений ключ `SavedVacancy(user_id, vacancy_id)` працює в обох СУБД.
− Без специфічних фіч MSSQL (full-text пошук — зарезервовано).

## Посилання

- [`../high-level-design.md`](../high-level-design.md) §3.1/§3.4
- [`../data-model.md`](../data-model.md) §1–§3
- [`../traceability-matrix.md`](../traceability-matrix.md) FR-005, SEC-001
