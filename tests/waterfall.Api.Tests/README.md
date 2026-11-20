# Тести рівня REST API (waterfall.Api.Tests)

Модуль тестує кінцеві точки (endpoints) ASP.NET Core Web API для каталогу вакансій та збережених вакансій («лайків»):
- Відповідність HTTP-статусів (200 OK, 201 CreatedAtAction, 204 NoContent, 400 BadRequest, 404 NotFound, 409 Conflict)
- Наскрізна трасування запитів (RFC-сумісні заголовки `X-Correlation-Id` та `X-Request-Id`)
- Безпечна ідентифікація користувача через JWT токени (`sub` / `userId`)
- Єдина специфікація помилок [`ApiErrorResponse`](../../src/api/Models/Common/ApiErrorResponse.cs)

| Контролер | Файл реалізації | Що робить |
| :--- | :--- | :--- |
| `VacanciesController` | [`src/api/Controllers/VacanciesController.cs`](../../src/api/Controllers/VacanciesController.cs) | REST API каталогу вакансій: CRUD, рекомендації (`/recommendations`) і двостороння синхронізація (`POST /sync`) |
| `SiteSearchesController` | [`src/api/Controllers/SiteSearchesController.cs`](../../src/api/Controllers/SiteSearchesController.cs) | пошук на сайтах на запит: одиночний (`POST /api/v1/site-searches`, `GET /{id}`) та пакетний 1–4 сайти (`POST /batch`, `GET /batch/{id}`) |
| `SavedVacanciesController` | [`src/api/Controllers/SavedVacanciesController.cs`](../../src/api/Controllers/SavedVacanciesController.cs) | REST API збережених вакансій (закладок) із захистом від дублювання (`409 Conflict`) |
| `ProfileController`, `GeoController`, `AnalyticsController`, `AIConsultantController` | `src/api/Controllers/` | адреса й статистика профілю, геокодування й геосинхронізація, ринкова аналітика (`Analyst`), потоковий ШІ-консультант і аналіз вакансій (SSE) |
| `ApiControllerBase` | [`src/api/Controllers/ApiControllerBase.cs`](../../src/api/Controllers/ApiControllerBase.cs) | спільний базовий контролер: `RequestId`, парсинг `CurrentUserId`, стандартизовані `ApiErrorResponse` |

---

## Опис набору тестів

82 тестових випадків у 10 класах (актуалізовано 20.11.2026):

| Клас тесту | Кількість випадків | Що перевіряє |
| :--- | :---: | :--- |
| [`VacanciesControllerTests`](VacanciesControllerTests.cs) | 11 | CRUD (`GetAll`, `GetById`, `Create`, `Update`, `Delete`), `GetRecommendations`, `Sync` (`POST /sync`) |
| [`SiteSearchesControllerTests`](SiteSearchesControllerTests.cs) | 8 | одиночний пошук (`POST`, `GET /{id}` — 202/401/404/429) та пакетний мультисайтовий пошук (`POST /batch`, `GET /batch/{id}` — 202/401/404/429) |
| [`AIConsultantControllerTests`](AIConsultantControllerTests.cs) | 18 | SSE-потік `/messages` (`meta`→`vacancies`→`delta`→`done`, `error`, релаксація), `/analyze` (`relevance`→`delta`→`done`), `DELETE /sessions/{id}`, ліміт 429, 503 `LLM_UNAVAILABLE`, контроль ролі та власності |
| [`ProfileControllerTests`](ProfileControllerTests.cs) | 17 | адреса профілю (`GET`/`PUT`), особиста статистика (`GET /stats`), запис і очищення історії пошуків, запис переглядів вакансій |
| [`ApiControllerBaseTests`](ApiControllerBaseTests.cs) | 7 | пріоритет заголовків трасування (`X-Correlation-Id` → `X-Request-Id` → `TraceIdentifier`), парсинг JWT claims (`sub` / `userId`), єдина структура `ApiErrorResponse` |
| [`SavedVacanciesControllerTests`](SavedVacanciesControllerTests.cs) | 6 | `GetAll`, `GetById`, `Save` (201 / 404 / 409 Conflict), `Delete` (204) з ізоляцією за `CurrentUserId` |
| [`GeoControllerTests`](GeoControllerTests.cs) | 5 | `GET /geo/coordinates` (200 / 400 / 404) і `POST /geo/sync` (200 / 400) |
| [`SensitiveLoggingRegressionTests`](SensitiveLoggingRegressionTests.cs) | 5 | Регресійні тести безпеки CWE-532: відсутність витоку email і паролів у логи автентифікації |
| [`AnalyticsControllerTests`](AnalyticsControllerTests.cs) | 3 | доступ до `GET /api/v1/analytics` для ролі `Analyst`, відмова `403` для `JobSeeker`, `401` без токена |
| [`BrowserCrawlerTests`](BrowserCrawlerTests.cs) | 2 | інтеграція браузерного краулера для Work.ua / Robota.ua |

---

## Фікстури та тестові дублери

- **`ControllerTestContext` ([`ServiceMocks.cs`](ServiceMocks.cs)):** налаштовує тестове середовище `HttpContext` для контролерів: підставляє `ClaimsPrincipal` із заданими правами, додає заголовки трасування запитів (`X-Correlation-Id`, `X-Request-Id`).
- **Моки сервісів (`ServiceMocks.cs` — `FakeVacancyService`, `FakeSavedVacancyService`):** ізолюють контролери від доменної логіки та бази даних, відстежуючи передані аргументи (`GetAllCalls`, `CreateCalls`, `DeleteCalls`).
- **Шпигун логування (`FakeLogger<T>`):** перевіряє наявність обов'язкових діагностичних повідомлень при обробці запитів.

---

## Граничні випадки (Edge Cases)

- **Конфлікт повторного додавання у закладки (HTTP 409 Conflict):** спроба вподобати вже збережену вакансію негайно повертає код `CONFLICT`, запобігаючи створенню дублікатів у базі даних.
- **Невдала валідація моделі (HTTP 400 Bad Request):** якщо `ModelState` невалідна, бізнес-сервіси взагалі не викликаються, а клієнт отримує код `VALIDATION_ERROR` з деталями полів.
- **Трасування за відсутності заголовків:** якщо клієнт не надіслав `X-Correlation-Id` чи `X-Request-Id`, контролер безпечно повертає внутрішній `TraceIdentifier` без збоїв.
- **Анонімні запити проти захищених ресурсів:** `CurrentUserId` коректно повертає `null` без генерації винятків парсингу при відсутності JWT claims або передачі невалідного рядка GUID.

---

## Як запустити

```bash
dotnet test tests/waterfall.Api.Tests/waterfall.Api.Tests.csproj -p:TargetFrameworks=net10.0 -p:TargetFramework=net10.0
```

---

## Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
| :--- | :--- | :--- | :--- |
| `tests/waterfall.Api.Tests/` (тести, контекст контролерів, документація) | Сидоренко Денис | Генерація коду тестів контролерів, хелперів `ControllerTestContext` та дублерів, рефакторинг під час рев'ю для концентрації на HTTP-контрактах | `dotnet test` (20 успішних тестів), перевірка обробки HTTP статус-кодів, самоперевірка автора |
