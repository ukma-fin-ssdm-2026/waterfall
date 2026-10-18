# Стратегія тестування


## 1. Поточний статус

У рішенні налічується **247 модульних тестів**, розподілених за 6 проектами:

| Набір тестів | Модуль | Тестів | Що перевіряє |
| :--- | :--- | :---: | :--- |
| [`waterfall.Ingestion.Tests`](../../tests/waterfall.Ingestion.Tests/README.md) | збір вакансій | 50 | нормалізація полів, фоновий парсер, `BulkUpsertAsync` на справжній SQLite; фікстури, заглушки, граничні випадки |
| `waterfall.Auth.Tests` | автентифікація | 40 | сервіс автентифікації, JWT, хешування паролів, контролер, валідація запитів |
| `waterfall.Client.Tests` | клієнт / UI | 61 | `HttpGatewayService`, viewmodels каталогу, «Обраного» і входу |
| [`waterfall.Repository.Tests`](../../tests/waterfall.Repository.Tests/README.md) | сховище (репозиторії) | 36 | SQLite in-memory: пошук за 4 полями та JSON, фільтрація, сортування, пагінація, `BulkUpsert`, ізоляція закладок та операції користувачів |
| [`waterfall.Services.Tests`](../../tests/waterfall.Services.Tests/README.md) | бізнес-сервіси та кеш | 35 | Cache-Aside (PERF-001), Stale-Cache fallback (REL-001), збагачення `IsSaved`, каскадна інвалідація кешу, стійкість до битого JSON |
| [`waterfall.Api.Tests`](../../tests/waterfall.Api.Tests/README.md) | REST API та безпека | 25 | Ендпоинти вакансій і закладок (HTTP 200/201/400/404/409), трасування запитів, витяг claims з JWT, захист від витоку даних у логи (CWE-532) |
| `src/mcp` (TypeScript) | MCP-сервер | — | тестів немає; лише статичні перевірки: ESLint з `eslint-plugin-security`, `tsc`, `npm audit` |

Усі набори .NET запускаються в CI на кожен PR (job `test` у [`ci.yml`](../../.github/workflows/ci.yml)).

## 2. Покриття та філософія якості

Замість штучної гонитви за «100% рядковим покриттям» через тривіальні перевірки вбудованих методів ORM чи авто-властивостей, набір тестів сфокусовано на критичній бізнес-логіці, стійкості (resilience) та граничних умовах.

Загальне рядкове покриття по всьому рішенню становить **77,6 %** (покриття методів — **86,2 %**).
Покриття цільових сервісів та репозиторіїв:
- `VacancyService`: **97,9 %**
- `VacancyCacheService`: **98,7 %**
- `SavedVacancyService`: **100 %**
- `VacancyIngestionService`: **99,3 %**
- `EfSavedVacancyRepository`: **96,2 %**
- `EfUserRepository`: **100 %**
- `ApiControllerBase`: **100 %**
- `AuthController`: **100 %**
- `AuthenticationService`: **98,5 %**
- `VacanciesController` / `SavedVacanciesController`: **86,6 %**
- `EfVacancyRepository`: **78,2 %** 

[Єдиний зведений HTML-дашборд покриття](../../coverage-report/index.html) (доступний також через кореневий [`index.html`](../../index.html)).

## 3. Плани

**Інтеграційні.** Збір вакансій: повний конвеєр без підробок — JSON-файл у тимчасовій папці → `JobParserService` → `VacancyIngestionService` → SQLite, з перевіркою вакансій у БД. API: `WebApplicationFactory<Program>` із SQLite у пам'яті, щоб справжні HTTP-запити проходили весь конвеєр middleware.

**Системні.** Окремий job у CI після тестів: запуск API з демо-дампом, перевірочні запити до `/api/v1/vacancies` і `/api/v1/auth/*`, а також MCP-сервер через stdio проти запущеного API.

**Приймальні.** Критерії Given-When-Then з [`user-stories.md`](../requirements/user-stories.md) стають автоматизованими сценаріями поверх `WebApplicationFactory`. Для збору (FR-001): «дамп із N валідними й M невалідними записами → у каталозі N вакансій із нормалізованими полями, M відхилено з причиною в лозі».

**Продуктивність.** PERF-003 (≥ 500 вакансій за 10 хв): згенерований дамп на 5000 записів, вимір часу `ProcessRawVacanciesAsync` разом із `BulkUpsertAsync` на SQLite, поріг у вигляді тесту, що запускається окремо від PR-конвеєра.

**Безпека.** Вже є SecurityCodeScan, NuGet Audit, `eslint-plugin-security`, `npm audit`, Dependabot і регресійні тести (CWE-532, фішинговий домен із [журналу налагодження](debugging-log.md)). План для збору: перебір підозрілих посилань у `NormalizeUrl` (домени-двійники, кириличні літери-двійники на кшталт `djіnni.co`, логін у посиланні) і перевірка тайм-аутів регулярних виразів на дуже довгих рядках (ReDoS).

**Property-based.** FsCheck для парсерів збору: `ParseSalary` ніколи не повертає min > max; `ParsePublishedAt` для будь-якого рядка не кидає винятків і не повертає дату з майбутнього; `Normalize` не падає на довільній `RawVacancy`. Переповнення в «99999999999 днів тому» знайшов TDD-цикл вручну, а такі тести шукали б подібні вади автоматично.


## Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
| :--- | :--- | :--- | :--- |
| Структура документа, розділи про збір вакансій, плани | Данило | Чернетка тексту, підрахунок покриття | Цифри звірено зі звітом ReportGenerator і результатами `dotnet test` усіх наборів |
| Розділи про каталог вакансій, кеш, сховище та API, фокус на якості тестів та стійкості | Денис (s1ngleo) | Допомога у формулюванні, рефакторинг тестів під бізнес-правила, розрахунок покриття зведеного звіту | Звірка цифр зі звітом ReportGenerator (`coverage-report/`), `dotnet test` (96 якісних тестів: 36 Repository, 35 Services, 25 Api) |
