# Тести клієнтського модуля (issue #60)

Модуль `waterfall.Client` — це тестована частина застосунку MAUI: HTTP-шлюз до API, DTO, локальна офлайн-БД SQLite (`ClientWaterfallDbContext`, `EfLocalVacancyStore`, `VacancySyncService`, `OfflineSiteCollector`) і viewmodels усіх вкладок («Каталог», «База вакансій», «Обране», «Рекомендації», «Аналітика», «ШІ-консультант», «Системні налаштування», «Вхід / Профіль»). Проєкт `waterfall.UI` (XAML, code-behind, `SecureStorageTokenStore`) лише прив'язує ці viewmodels до екрана і потребує MAUI-воркладів, тому модульними тестами не покривається (див. [стратегію тестування](../../docs/testing/testing-strategy.md)).

| Код | Що робить |
| :--- | :--- |
| [`Services/HttpGatewayService.cs`](../../src/waterfall.Client/Services/HttpGatewayService.cs) | запити до `/api/v1/*` (зокрема `/site-searches/batch`, `/vacancies/sync`, SSE ШІ-консультанта); помилки мережі, тайм-аут, коди 4xx/5xx і зіпсовані відповіді → `ApiException` українською |
| [`Services/EfLocalVacancyStore.cs`](../../src/waterfall.Client/Services/EfLocalVacancyStore.cs), [`VacancySyncService.cs`](../../src/waterfall.Client/Services/VacancySyncService.cs), [`OfflineSiteCollector.cs`](../../src/waterfall.Client/Services/OfflineSiteCollector.cs) | локальна SQLite БД (`ClientVacancyDB`, `ClientSyncStateDB`), ідемпотентний апгрейд legacy-схеми, пакетна двостороння синхронізація, прямий офлайн-збір DOU/Djinni |
| [`Services/AuthHeaderHandler.cs`](../../src/waterfall.Client/Services/AuthHeaderHandler.cs) | додає `Authorization: Bearer` зі збереженого токена |
| [`Models/Auth/AuthSession.cs`](../../src/waterfall.Client/Models/Auth/AuthSession.cs) | читає email, роль і термін дії зі збереженого JWT, щоб після перезапуску показати, хто увійшов |
| [`ViewModels/`](../../src/waterfall.Client/ViewModels/) | каталог (мультисайтовий парсинг 1–4 сайтів), «База вакансій» (`DatabaseViewModel`), «Обране», «Рекомендації», «Аналітика», «ШІ-консультант», «Системні налаштування», «Вхід / Профіль», картка вакансії |

## Тести

354 тестових випадків у 20 класах (актуалізовано 22.11.2026):

| Клас | Випадків | Що перевіряє |
| :--- | :---: | :--- |
| [`LoginViewModelTests`](LoginViewModelTests.cs) | 41 | локальна перевірка email/пароля, вхід/реєстрація, сесія, адреса профілю, особиста статистика (Test-28…30) |
| [`HttpGatewayServiceTests`](HttpGatewayServiceTests.cs) | 37 | URL і тіла всіх REST-ендпоінтів (зокрема `GetVacanciesAsync(VacancyQueryOptions)`, `StartMultiSiteSearchAsync`/`GetMultiSiteSearchAsync`, `SyncVacanciesAsync`), Bearer, enum-рядки, обробка помилок/кодувань/тайм-аутів |
| [`VacancyItemViewModelTests`](VacancyItemViewModelTests.cs) | 69 | форматування зарплати/джерела/компанії/статусу, відстані, перемикач «Обраного» та реакція на повідомлення |
| [`CatalogSiteSearchTests`](CatalogSiteSearchTests.cs) | 33 | пошук на 4 сайтах із каталогу (DOU, Djinni, Work.ua, Robota.ua), коди фільтрів, `fromPage`, `listOnly`, прогрес, кеш |
| [`CatalogViewModelTests`](CatalogViewModelTests.cs) | 30 | локальна фільтрація каталогу, вайт-/бан-лист, «Без переглянутих», «Включно без ЗП», debounce, сортування, скидання фільтрів, банер помилок |
| [`AIConsultantViewModelTests`](AIConsultantViewModelTests.cs) | 22 | чат ШІ-консультанта (SSE `meta`/`vacancies`/`delta`/`done`/`error`), аналіз вакансій (`relevance`), відновлення після помилки |
| [`GeoDistanceServiceTests`](GeoDistanceServiceTests.cs) | 28 | геокодування, розрахунок відстані OSRM і формулою гаверсинів, офлайн-кешування |
| [`AiConsultantGatewayTests`](AiConsultantGatewayTests.cs) | 14 | парсинг SSE-потоку `/messages` і `/analyze`, обробка 429/503 |
| [`RecommendationsViewModelTests`](RecommendationsViewModelTests.cs) | 9 | завантаження персоналізованих рекомендацій, порожній стан, оновлення |
| [`SystemSettingsViewModelTests`](SystemSettingsViewModelTests.cs) | 8 | читання/збереження `appsettings.json` (`Ingestion`), перемикання фонового збору, відображення статусу та запуск ручної синхронізації БД |
| [`FavoritesViewModelTests`](FavoritesViewModelTests.cs) | 8 | завантаження, видалення, порожній стан, реакція на зміни з інших вкладок |
| [`EfLocalGeoCacheServiceTests`](SqliteLocalGeoCacheServiceTests.cs) | 7 | локальний SQLite геокеш та двостороння синхронізація з сервером |
| [`DatabaseViewModelTests`](DatabaseViewModelTests.cs) | 8 | повне багатосторінкове завантаження БД 4 сайтів, лічильники, крос-сайтові фільтри (джерела, категорії, формат, досвід, крос-валютна ЗП, свіжість, статус, слова, debounce), сортування, офлайн-фолбек + ручна синхронізація, JSON експорт/імпорт |
| [`AnalyticsViewModelTests`](AnalyticsViewModelTests.cs) | 8 | рольовий доступ (`Analyst`), графіки зарплат/категорій/технологій, стан недостатності даних |
| [`AuthSessionTests`](AuthSessionTests.cs) | 6 | регресійний тест розбору JWT і property-based тест (FsCheck) |
| [`PhotonAddressAutocompleteServiceTests`](PhotonAddressAutocompleteServiceTests.cs) | 6 | автодоповнення адрес (Photon API + Wikipedia fallback) |
| [`CatalogMultiSiteSearchTests`](CatalogMultiSiteSearchTests.cs) | 5 | прапорці вмикання/вимикання 4 сайтів («Усі 4»/«Жодного»), маршрутизація 1 сайту vs 2–4 сайти (`/batch`), незалежні `fromPage`/фільтри, живий прогрес, офлайн-фолбек `OfflineSiteCollector` |
| [`VacancyPrefetchDistancesTests`](VacancyPrefetchDistancesTests.cs) | 6 | фоновий префетч відстаней: без точної адреси або з іншого міста — нуль звернень до сервісу; точна адреса у місті користувача — один запит і заповнений `DistanceText`; порожня адреса, скасований токен, порожній список |
| [`VacancyItemSkillsTests`](VacancyItemSkillsTests.cs) | 5 | показ щонайбільше 3 навичок, чип `+N`, фільтрація порожніх і дублів |
| [`VacancySyncTests`](VacancySyncTests.cs) | 4 | наскрізна двостороння синхронізація на реальній SQLite (клієнт ↔ сервер, дедуплікація, Last-Write-Wins), ідемпотентний апгрейд legacy SQLite без колонок синхронізації, парсинг DOU RSS + Djinni JSON-LD у `OfflineSiteCollector`, ручна синхронізація із `SystemSettingsViewModel` |

### Фікстури, заглушки, шпигуни

| Що | Де | Навіщо |
| :--- | :--- | :--- |
| **Заглушка `FakeGateway`** | viewmodels | кожен запит отримує власний `TaskCompletionSource`, який скасовується разом із токеном запиту, як у справжнього `HttpClient`: тест сам вирішує, коли і в якому порядку відповідає «сервер», тому гонки (debounce, застарілий пошук, вхід, що ще триває) перевіряються детерміновано |
| **Заглушка `StubHandler`** | `HttpGatewayServiceTests` | підмінений `HttpMessageHandler`: справжній `HttpClient` без мережі, тест бачить запит і задає будь-яку відповідь чи виняток |
| **Фейк `InMemoryTokenStore`** | шлюз, вхід | сховище токена в пам'яті замість `SecureStorage` MAUI; рахує виклики `ClearAsync`, щоб перевірити, що токен не стирають без потреби |
| **`FakeTimeProvider`** | каталог, вхід | керований час: debounce і термін дії сесії без `Thread.Sleep` |
| **`StrongReferenceMessenger`** | viewmodels | окремий месенджер на тест, щоб повідомлення «збережено / прибрано» не протікали між тестами |
| **Генератор `UnicodeTextArbitraries`** | `AuthSessionTests` | довільні рядки з коректного юнікоду для property-based тесту |

### Граничні випадки

- **Мережа:** немає з'єднання, тайм-аут `HttpClient` (відрізняється від скасування користувачем), тіло `null`, зіпсований JSON, HTML замість JSON від проксі, сторінка в кодуванні, якого .NET не знає (`windows-1251`, captive portal), порожнє тіло 401, API за проксі з префіксом шляху (`/waterfall`) зі слешем у кінці і без.
- **Вхід:** пароль рівно 12 і 32 символи, 33 і коротший за 12, без цифри, email з пробілами по краях; повторне натискання під час запиту; токена немає; токен, що спливає рівно зараз; токен не з трьох частин; payload без email чи exp; роль у короткій і повній URI-формі.
- **JWT:** довжина base64url-сегмента з остачею 0, 2 і 3 (див. журнал налагодження); довільний юнікод в email.
- **Каталог:** пошук під час завантаження, відповідь на старий запит після нового, скасований застарілий запит, той самий запит із пробілом у кінці, введення до першого завантаження, «ще» без наступної сторінки, помилка під час підвантаження.
- **Картка:** зарплата `min == max`, лише `min`, лише `max`, без зарплати; невідоме джерело; порожня локація.

## Покриття

Покриття рахується лише для рукописного коду: [`tests/coverage.runsettings`](../coverage.runsettings) виключає згенероване (`*.g.cs` System.Text.Json і CommunityToolkit.Mvvm, атрибут `GeneratedCode`). Без цього файлу у звіт потрапляють ~2000 згенерованих рядків, і цифра (84,1 %) говорить більше про генератор, ніж про тести. `CompilerGenerated` навмисно не виключено: ним позначені стан-машини async-методів, тобто тіла майже всіх команд viewmodels. Перша версія налаштувань його виключала, і звіт показував 99 %, хоча скасування застарілих запитів не перевіряв жоден тест.

| | До Lab 06 | Зараз |
| :--- | :---: | :---: |
| Тестових випадків | 61 | 354 |
| Рядки | 89,6 % | 71,5 % |
| Гілки | 79,4 % | 64,3 % |
| Методи | 88,2 % | 77,9 % |

HTML-звіт: [`coverage-report/index.html`](coverage-report/index.html).

Що лишилося непокритим і чому:

- `VacancyItemViewModel.Title` і `Description` (рядки 44–45) та `SavedVacancyDto.Notes` (рядок 19): властивості лише передають поле DTO; тест на них перевіряв би присвоєння, а не поведінку. Їх показ перевіряє запуск застосунку.
- `FavoritesViewModel.IsEmpty` (рядок 69), 3 з 6 гілок: решта комбінацій (наприклад, «завантажується», але «ще не завантажено») неможливі в жодному досяжному стані.

Аналіз покриття знайшов і справжню ваду. `catch (NotSupportedException)` у `HttpGatewayService` не покривав жоден тест. Перевірка показала, що на .NET 10 `ReadFromJsonAsync` такого винятку не кидає взагалі, натомість для невідомого кодування в `Content-Type` кидає `InvalidOperationException`. Її ніхто не ловив (viewmodels чекають лише `ApiException`), тож сторінка проксі чи captive portal у `windows-1251` вилітала з команди необробленим винятком замість банера з помилкою. Виправлено з тестами `ErrorBodyWithUnsupportedCharset_FallsBackToStatusMessage` і `SuccessBodyWithUnsupportedCharset_ThrowsInvalidResponse`.

## Як запустити

```bash
dotnet test tests/waterfall.Client.Tests/waterfall.Client.Tests.csproj
```

Перегенерувати звіт покриття:

```bash
dotnet test tests/waterfall.Client.Tests/waterfall.Client.Tests.csproj --settings tests/coverage.runsettings --collect "XPlat Code Coverage" --results-directory TestResults/client
dotnet tool restore && dotnet tool run reportgenerator "-reports:TestResults/client/**/coverage.cobertura.xml" "-targetdir:tests/waterfall.Client.Tests/coverage-report" "-reporttypes:Html;TextSummary"
```

Каталог `TestResults/` у `.gitignore`. Після генерації абсолютні шляхи у HTML замінено на відносні від кореня репозиторію.

## Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
| :--- | :--- | :--- | :--- |
| `tests/waterfall.Client.Tests/` (тести, заглушки, генератор FsCheck), `tests/coverage.runsettings` | Садиков Константін | Генерація тестів і заглушок, добір граничних випадків, аналіз непокритих рядків | `dotnet test` (106 випадків); регресійний і property-тест падають на внесеній помилці; звірка цифр зі звітом ReportGenerator; самоперевірка (§8) |
| `coverage-report/`, цей README | Садиков Константін | Генерація звіту ReportGenerator, чернетка документа | Перегляд HTML-звіту, звірка таблиць зі списком тестів (`dotnet test --list-tests`) |
