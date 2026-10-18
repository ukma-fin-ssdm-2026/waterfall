# Тести рівня REST API (waterfall.Api.Tests)

Модуль тестує кінцеві точки (endpoints) ASP.NET Core Web API для каталогу вакансій та збережених вакансій («лайків»):
- Відповідність HTTP-статусів (200 OK, 201 CreatedAtAction, 204 NoContent, 400 BadRequest, 404 NotFound, 409 Conflict)
- Наскрізна трасування запитів (RFC-сумісні заголовки `X-Correlation-Id` та `X-Request-Id`)
- Безпечна ідентифікація користувача через JWT токени (`sub` / `userId`)
- Єдина специфікація помилок [`ApiErrorResponse`](../../src/api/Models/Common/ApiErrorResponse.cs)

| Контролер | Файл реалізації | Що робить |
| :--- | :--- | :--- |
| `VacanciesController` | [`src/api/Controllers/VacanciesController.cs`](../../src/api/Controllers/VacanciesController.cs) | REST API каталогу вакансій: `GET /api/v1/vacancies`, `GET /api/v1/vacancies/{id}`, `POST /api/v1/vacancies`, `PUT /api/v1/vacancies/{id}`, `DELETE /api/v1/vacancies/{id}` |
| `SavedVacanciesController` | [`src/api/Controllers/SavedVacanciesController.cs`](../../src/api/Controllers/SavedVacanciesController.cs) | REST API збережених вакансій (закладок): `GET /api/v1/saved-vacancies`, `GET /api/v1/saved-vacancies/{id}`, `POST /api/v1/saved-vacancies` (із захистом від повторного збереження 409 Conflict), `DELETE /api/v1/saved-vacancies/{id}` |
| `ApiControllerBase` | [`src/api/Controllers/ApiControllerBase.cs`](../../src/api/Controllers/ApiControllerBase.cs) | спільний базовий контролер: вилучення `RequestId`, парсинг `CurrentUserId`, стандартизовані методи генерації помилок |

---

## Опис набору тестів

25 сфокусованих тестових випадків у 4 класах:

| Клас тесту | Кількість випадків | Що перевіряє |
| :--- | :---: | :--- |
| [`VacanciesControllerTests`](VacanciesControllerTests.cs) | 7 | `GetAll`: 200 OK з `PagedResult<VacancyDto>` та прокидання `CurrentUserId` для збагачення прапорцями; `GetById`: 200 OK при успіху та 404 NotFound з RFC/структурованим `ApiErrorResponse`; `Create`: 201 CreatedAtAction із правильним route value `id` і Location, 400 BadRequest при невалідному `ModelState` (сервіс не викликається); `Update`: 200 OK з оновленим DTO; `Delete`: 204 NoContent з викликом сервісу видалення |
| [`SavedVacanciesControllerTests`](SavedVacanciesControllerTests.cs) | 6 | `GetAll`: 200 OK з фільтрацією за тенантом (`CurrentUserId`); `GetById`: 200 OK з даними закладки; `Save`: 404 NotFound якщо вакансії немає в каталозі; 409 Conflict при спробі повторного збереження (перевірка бізнес-правила); 201 CreatedAtAction з новим `SavedVacancyDto`; `Delete`: 204 NoContent із передачею ID користувача |
| [`ApiControllerBaseTests`](ApiControllerBaseTests.cs) | 7 | пріоритет трасування: `X-Correlation-Id` перемагає `X-Request-Id`, який у свою чергу перемагає системний `TraceIdentifier`; парсинг claims: успішне зчитування `sub`, запасний варіант `userId`, повернення `null` для анонімних запитів або некоректного GUID; формування однакової структури `ApiErrorResponse` для 400, 401, 404, 409 |
| [`SensitiveLoggingRegressionTests`](SensitiveLoggingRegressionTests.cs) | 5 | Регресійні тести безпеки CWE-532: перевірка відсутності витоку конфіденційних даних (email, пароль) у журнал логирования під час реєстрації та входу (`AuthController`, `AuthenticationService`) |

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
