# Тести клієнтського модуля (issue #60)

Модуль `waterfall.Client` — це тестована частина застосунку MAUI: HTTP-шлюз до API, DTO і viewmodels вкладок «Каталог», «Обране» і «Вхід». Проєкт `waterfall.UI` (XAML, code-behind, `SecureStorageTokenStore`) лише прив'язує ці viewmodels до екрана і потребує MAUI-воркладів, тому модульними тестами не покривається (див. [стратегію тестування](../../docs/testing/testing-strategy.md)).

| Код | Що робить |
| :--- | :--- |
| [`Services/HttpGatewayService.cs`](../../src/waterfall.Client/Services/HttpGatewayService.cs) | запити до `/api/v1/*`; помилки мережі, тайм-аут, коди 4xx/5xx і зіпсовані відповіді перетворює на `ApiException` з повідомленням українською |
| [`Services/AuthHeaderHandler.cs`](../../src/waterfall.Client/Services/AuthHeaderHandler.cs) | додає `Authorization: Bearer` зі збереженого токена |
| [`Models/Auth/AuthSession.cs`](../../src/waterfall.Client/Models/Auth/AuthSession.cs) | читає email, роль і термін дії зі збереженого JWT, щоб після перезапуску показати, хто увійшов |
| [`ViewModels/`](../../src/waterfall.Client/ViewModels/) | каталог (пошук із debounce, підвантаження сторінок), «Обране», вхід і реєстрація, картка вакансії |

## Тести

106 тестових випадків у шести класах:

| Клас | Випадків | Що перевіряє |
| :--- | :---: | :--- |
| [`HttpGatewayServiceTests`](HttpGatewayServiceTests.cs) | 27 | URL і тіла запитів, адреса API з префіксом шляху, Bearer-заголовок, enum як рядки, розбір помилок сервера, порожня / зіпсована / HTML-відповідь, невідоме кодування, 401/403/404/5xx, обрив мережі й тайм-аут, скасування не стає помилкою |
| [`LoginViewModelTests`](LoginViewModelTests.cs) | 29 | локальна перевірка email і пароля (межі 12 / 32 символи, цифра), вхід і реєстрація, кнопка заблокована на час запиту, причини відмови по полях, вихід, відновлення сесії з токена, прострочений, зіпсований і відсутній токен |
| [`VacancyItemViewModelTests`](VacancyItemViewModelTests.cs) | 20 | зарплата («від», «до», діапазон, одне число, без зарплати), назва джерела, рядок «Компанія · Локація», статус, перемикач «Обраного» і його відкат при помилці |
| [`CatalogViewModelTests`](CatalogViewModelTests.cs) | 17 | debounce пошуку, застарілі й скасовані відповіді не перезаписують новіші, підвантаження сторінок, повтор після помилки, синхронізація з «Обраним» |
| [`FavoritesViewModelTests`](FavoritesViewModelTests.cs) | 7 | завантаження, видалення, стан «порожньо», скасоване застаріле завантаження, реакція на зміни з каталогу |
| [`AuthSessionTests`](AuthSessionTests.cs) | 6 | регресійний тест із [журналу налагодження](../../docs/testing/debugging-log.md) і властивість (FsCheck) для розбору JWT |

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
| Тестових випадків | 61 | 106 |
| Рядки | 89,6 % | 99,2 % |
| Гілки | 79,4 % | 98,3 % |
| Методи | 88,2 % | 97,9 % |

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
