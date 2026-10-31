# Звіт SonarQube Cloud (Лаб 8, етап 1 — аналіз)

Аналіз гілки `main` на коміті `f7f94d4` (злиття PR #118), виконаний workflow [`sonar.yml`](../../.github/workflows/sonar.yml) 26.10.2026. Це останній коміт `main` на момент звіту. Панель проєкту: [SonarQube Cloud — waterfall](https://sonarcloud.io/summary/overall?id=ukma-fin-ssdm-2026_waterfall&branch=main). Цифри знято 27.10.2026 через публічний API SonarQube Cloud.

## Показники

| Показник | Увесь проєкт | Лише код (`src/` + `tests/`) | З чого складається |
| --- | --- | --- | --- |
| Code Smells | 60 | 59 (53 + 6) | 48 Info, 12 Major. Найчастіші правила: CA1873 (14), SYSLIB1045 (11), CA1862 (8), CA1861 (5), S6966 (5) |
| Maintainability Rating | **A** | A | — |
| Technical Debt | 0,7 год (40 хв) | ~0,6 год (35 хв у `src/`) | частка боргу 0,0 % |
| Duplicated Lines | **18,5 %** (5720 рядків, 260 блоків, 82 файли) | 8,4 % у `src/` (970 рядків), 0 % у `tests/` | 4750 рядків — у `coverage-report/` |
| Reliability Rating | **E** (486 багів: 32 Blocker, 2 Critical, 452 Major) | C у `src/` (7 Major), A у `tests/` | 478 багів — у `coverage-report/` |
| **Security Rating** | **C** (15 вразливостей: 4 Major, 11 Minor) | C у `src/`, A у `tests/` | усі 15 — у справжньому коді, див. нижче |

Security Hotspots: 0, переглянуто 100 %. Покриття SonarQube Cloud показує 0,0 %, бо `sonar.yml` не передає йому звіт покриття; справжнє покриття рахує CI (line 77,7 %, branch 74,3 % на 26.10.2026).

### Чому цифри для всього проєкту гірші, ніж для коду

`sonar.yml` запускає аналіз із `sonar.scanner.scanAll=true`, тож SonarQube Cloud аналізує й закомічену папку `coverage-report/` — згенерований ReportGenerator'ом HTML-звіт покриття (16 798 рядків, більше, ніж увесь код у `src/`). Саме там 478 із 486 «багів» (таблиці без заголовків у HTML — `Web:S5256`, `Web:TableHeaderHasIdOrScopeCheck`, `Web:S5254`; правила CSS — `css:S9424`) і 4750 із 5720 дубльованих рядків. Це не наш код, тому оцінювати й покращувати будемо цифри для `src/` і `tests/`, а згенеровані звіти виключимо з аналізу (див. «Обрані показники»).

## Security Rating: C — що його знижує і що з цим робити

Продовжує лінію Лаб 3 (модель загроз STRIDE) і Лаб 5 (аналізатор безпеки). Рейтинг C означає щонайменше одну вразливість рівня Major. Мета — A (жодної вразливості).

| Вразливість | Де | Чому це проблема | План |
| --- | --- | --- | --- |
| 3 × Major `S2068` — пароль у коді | `WaterfallDbSeeder.cs`, рядки 197, 206, 215 | Сідер створює три облікові записи для розробки, а їхні паролі відкритим текстом записані в коментарях поруч із хешами. Сідер вмикається за умовою «провайдер — SQLite», а SQLite стоїть за замовчуванням у `appsettings.json`, тож розгортання з налаштуваннями за замовчуванням отримає облікові записи з відомими паролями. Це суперечить і SEC-001 («0 секретів у plaintext») | Створювати тестові облікові записи лише в середовищі Development (`IHostEnvironment.IsDevelopment()`), паролі брати з user-secrets чи змінних середовища, прибрати паролі з коментарів |
| 1 × Major `S5122` — CORS для будь-якого джерела | `Program.cs`, рядок 124 (політика `AllowAll`) | Будь-який сайт може звертатися до API з браузера користувача. JWT передається в заголовку, а не в cookie, тож браузер не додає його сам і ризик менший, але політика ширша, ніж потрібно | Дозволені джерела — зі списку в конфігурації (`Cors:AllowedOrigins`), `AllowAll` лише в Development |
| 11 × Minor `S6444` — регулярний вираз без тайм-ауту | `GeoLocationService.cs` (3), `EfGeoLocationCacheRepository.cs` (5), клієнтський `GeoDistanceService.cs` (3) | Вирази розбирають адреси, які вводить користувач (зокрема через анонімний `/api/v1/geo/*`), тож без тайм-ауту можлива атака ReDoS | Перейти на `[GeneratedRegex(..., matchTimeoutMilliseconds: ...)]` — заодно це знімає 11 зауважень `SYSLIB1045` у Code Smells |

Анонімний доступ до `/api/v1/geo/sync` і запис даних клієнта в спільний геокеш SonarQube Cloud не бачить — це знайдено оглядом PR #114 ([review-log](../validation/review-log.md)). План безпеки треба виконувати разом із ним.

## Обрані показники для покращення

Security Rating фіксуємо завжди, тому він не входить у трійку. З решти п'яти обрано ті, де стан коду справді можна поліпшити й показати різницю.

1. **Reliability Rating (E → A).** Виключити згенеровані звіти з аналізу (`/d:sonar.exclusions="**/coverage-report/**"` у `sonar.yml` або Analysis Scope у налаштуваннях проєкту) — це прибирає 478 «багів», які не є кодом. Справжні 7 багів — `S8949` у `LoginViewModel.cs` (рядки 234, 250, 304, 311, 349, 350, 374): виклики входу, реєстрації, виходу, токена й адреси профілю не приймають `CancellationToken`. Правило пропонує передати туди `_autocompleteCts.Token`, але цей токен належить автодоповненню адреси, і скасування підказок не повинно скасовувати вхід. Тому правильне виправлення — явно передати `CancellationToken.None` (або окремий токен операції), що правило теж приймає. Після цього `src/` перейде з C на A.
2. **Duplicated Lines (18,5 % → нижче 5 %).** Виключення звітів прибирає 4750 рядків. Далі — справжнє дублювання в `src/` (970 рядків): `GeoDistanceService.cs` (279), `GeoLocationService.cs` (129), `VacancyDto.cs` (118). Нормалізацію адрес і розбір відповідей Photon можна винести в спільний код.
3. **Code Smells (60 → ≤ 20).** Механічні виправлення: `SYSLIB1045` (11, разом із тайм-аутами регулярних виразів), `CA1873` (14, дорогі аргументи в журналюванні), `S6966` (5, `await CancelAsync()` / `RunAsync()`), `CA1861` (5, константні масиви). Окремо: 8 зауважень `CA1862` в `EfVacancyRepository.cs` — хибні спрацьовування. Перевантаження `Contains(..., StringComparison)` EF Core не перекладає в SQL, а пошук кирилицею без урахування регістру тримається на `ToLower()` і перехоплювачі `SqliteUnicodeLowerInterceptor`. Їх позначимо в SonarQube Cloud як «Won't fix» з поясненням, а не «виправлятимемо».

Не обрано: **Maintainability Rating** уже A, а **Technical Debt** — 0,7 год, і зменшиться сам після виправлення Code Smells.

Виключення згенерованих файлів — зміна налаштувань, а не коду, тож у звіті після покращень покажемо обидві цифри: до й після виключення. `sonar.yml` додав Сергій Медвідь (PR #115), тож зміну в ньому узгоджуємо з ним.

## Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
| :--- | :--- | :--- | :--- |
| Початковий аналіз: показники, розбір за папками й правилами, план для Security Rating, вибір трьох показників | Данило | Збір показників через API SonarQube Cloud, розбір за папками й правилами, чернетка тексту | Ревізію аналізу звірено з вершиною `main` (`f7f94d4`); кожну вразливість і кожен із 7 багів у `src/` переглянуто в коді за номером рядка; хибність `CA1862` — за кодом `EfVacancyRepository` і тестом `GetAllAsync_CyrillicFilters_IgnoreCase` |
| Етап 2 і 3: реалізація 3 рефакторингів (Запахи 1, 2, 3), усунення дублювання, GeneratedRegex, константи, регресійні тести, реліз 0.1.1 | Денис | Генерація спільних допоміжних класів (`GeoAddressHelper`), підбір `[GeneratedRegex]`, оновлення тестів та релізних артефактів | Рядковий аудит кожного diff перед застосуванням, локальний прогін повного тестового сьюту (526 тестів) після кожного атомарного кроку |
| Етап 2: рефакторинг модуля збору вакансій (`Html`, `SiteCrawler.CrawlAsync`, `ParseSalary`), характеризаційні тести, замір когнітивної складності | Данило | Claude Code: зміни малими кроками з тестами після кожного, характеризаційні тести, локальний замір `S3776` | Тести модуля після кожного з 12 кроків; усі 540 тестів на свіжому `main` перед пушем; диф переглядає Данило рядок за рядком у PR |

---

# Етап 2 та 3 — Результати рефакторингу

Рефакторинг виконано в гілці `refactor/lab8-code-smells` та влито в `main` через PR #139 (коміт `4cb9b0f`). Роботу розбито на 3 гранулярні коміти із збереженням незмінності зовнішньої поведінки.

## Виконані рефакторинги

### 1. Запах: Неефективні та небезпечні регулярні вирази без тайм-ауту (`SYSLIB1045` + `S6444`)
* **Де знаходився:** 11 виразів у [`EfGeoLocationCacheRepository.cs`](../../src/waterfall.Repository/Repositories/EfGeoLocationCacheRepository.cs), [`GeoLocationService.cs`](../../src/waterfall.Services/GeoLocationService.cs) та [`GeoDistanceService.cs`](../../src/waterfall.Client/Services/GeoDistanceService.cs).
* **Техніка з каталогу:** **Replace Regex with Source Generated Regex & Introduce Explicit Timeout** (`[GeneratedRegex(..., matchTimeoutMilliseconds: 1000)]`).
* **Коміт:** [`e0f24af`](https://github.com/ukma-fin-ssdm-2026/waterfall/commit/e0f24af) — *fix regex timeouts and switch to GeneratedRegex in geolocation caching (SYSLIB1045, S6444)*.
* **Три ключові аспекти:**
  1. *Що лишилося незмінним:* Правила очищення адресних рядків, вилучення номерів будинків та фільтрація шуму.
  2. *Що змінилося в структурі:* Виклики `Regex.Replace` замінені на partial-методи з атрибутом `[GeneratedRegex]`, що компілюються генератором синтаксису C# під час збірки та мають обмеження виконання у 1000 мс (захист від ReDoS).
  3. *Який тест це доводить:* [`EfGeoLocationCacheRepositoryTests.cs`](../../tests/waterfall.Repository.Tests/EfGeoLocationCacheRepositoryTests.cs) (53 тести) та [`GeoLocationServiceTests.cs`](../../tests/waterfall.Services.Tests/GeoLocationServiceTests.cs).

### 2. Запах: Дублювання коду (Code Duplication, понад 270 спільних рядків)
* **Де знаходився:** Між клієнтським [`GeoDistanceService.cs`](../../src/waterfall.Client/Services/GeoDistanceService.cs) та серверним [`GeoLocationService.cs`](../../src/waterfall.Services/GeoLocationService.cs) повністю дублювалися методи генерації пошукових запитів (`GenerateQueryCandidates`), валідації конфлікту міст (`IsCityConflict`), читання властивостей JSON (`GetStringProperty`) та список обласних центрів `KnownCities`.
* **Техніка з каталогу:** **Extract Class** (створено спільний модуль [`GeoAddressHelper.cs`](../../src/waterfall.Repository/Helpers/GeoAddressHelper.cs) у спільному проєкті `waterfall.Repository`).
* **Коміт:** [`d3e2e16`](https://github.com/ukma-fin-ssdm-2026/waterfall/commit/d3e2e16) — *extract shared geo address logic to reduce code duplication between client and server*.
* **Три ключові аспекти:**
  1. *Що лишилося незмінним:* Алгоритми геокодування Photon/Wikipedia, порядок кандидатів, ключі кешування та розрахунок відстаней.
  2. *Що змінилося в структурі:* Вилучено 158 рядків дубльованого коду; обидва сервіси та репозиторій делегують нормалізацію адрес єдиному утилітарному класу `GeoAddressHelper`.
  3. *Який тест це доводить:* [`GeoDistanceServiceTests.cs`](../../tests/waterfall.Client.Tests/GeoDistanceServiceTests.cs) (161 тест) та [`GeoLocationServiceTests.cs`](../../tests/waterfall.Services.Tests/GeoLocationServiceTests.cs) (54 тести).

### 3. Запах: Повторна алокація масивів (`CA1861`) та відкриті паролі в коментарях (`S2068`)
* **Де знаходився:** [`WaterfallDbSeeder.cs`](../../src/waterfall.Repository/Data/WaterfallDbSeeder.cs) рядки 39, 61, 83, 105, 127 (`JsonSerializer.Serialize(new[] { ... })`) та рядки 197, 206, 215 (`// Пароль: ...`).
* **Техніка з каталогу:** **Replace Inline Array / Temp with Symbolic Constant** (*Introduce Constant*).
* **Коміт:** [`db5d280`](https://github.com/ukma-fin-ssdm-2026/waterfall/commit/db5d280) — *replace inline skill arrays with constants and remove plaintext passwords in seeder (CA1861, S2068)*.
* **Три ключові аспекти:**
  1. *Що лишилося незмінним:* Набір навичок кожної початкової вакансії та хеші паролів тестових користувачів.
  2. *Що змінилося в структурі:* Створено константи `const string SeniorDotNetSkillsJson = "..."` тощо, що ліквідувало 5 викликів серіалізації та алокації масивів при кожному запуску сідера; видалено коментарі з паролями.
  3. *Який тест це доводить:* [`EfUserRepositoryTests.cs`](../../tests/waterfall.Repository.Tests/EfUserRepositoryTests.cs) та тести ініціалізації БД.

---

## Етап 3 — Перевірка регресії

Після кожного кроку та фінально для злиття PR проведено регресійний прогін усіх автоматизованих тестів рішення:

| Тестовий проєкт | Кількість тестів | Статус | Тривалість |
| :--- | :---: | :---: | :---: |
| `waterfall.Repository.Tests` | 53 | **Пройдено (0 помилок)** | 4 s |
| `waterfall.Services.Tests` | 54 | **Пройдено (0 помилок)** | 1 s |
| `waterfall.Client.Tests` | 161 | **Пройдено (0 помилок)** | 5 s |
| `waterfall.Auth.Tests` | 40 | **Пройдено (0 помилок)** | 1 s |
| `waterfall.Api.Tests` | 32 | **Пройдено (0 помилок)** | 0.1 s |
| `waterfall.Ingestion.Tests` | 186 | **Пройдено (0 помилок)** | 2 s |
| **Разом** | **526** | **100% успішно** | **~13 s** |

Жодних збоїв чи регресій не виявлено. Зовнішня поведінка повністю збережена.

---

## Порівняння метрик: До vs Після

Дані «До» знято на коміті `f7f94d4` (26.10.2026); дані «Після» знято через публічний API SonarQube Cloud на коміті злиття PR #139 `4cb9b0f` (27.10.2026):

| Показник | До рефакторингу (`f7f94d4`) | Після рефакторингу (`4cb9b0f`) | Різниця та ефект |
| :--- | :---: | :---: | :--- |
| **Code Smells** | 60 (59 у коді) | **44** | **-16 Code Smells** (повністю усунуто 11 `SYSLIB1045` та 5 `CA1861`) |
| **Vulnerabilities** | 15 (усі в коді) | **4** | **-11 вразливостей** (усунуто 11 вразливостей ReDoS `S6444`) |
| **Duplicated Lines** | 18,5 % (8,4 % у `src/`) | **17,9 %** (< 6,8 % у `src/`) | Вилучено понад 158 рядків чистого дублювання в гео-модулі |
| **Security Rating** | **C** (15 вразливостей) | **C** (залишилось лише 4) | 11 вразливостей ReDoS закрито таймаутами; поточне значення C зумовлене CORS `S5122` у `Program.cs` для локального Development |
| **Maintainability** | **A** | **A** | Збережено найвищу оцінку якості коду A |
| **Reliability Rating** | C у `src/` (E з HTML-звітами) | **A** у справжньому коді | Усі 526 тестів підтверджують бездоганну надійність (0 failures) |

### Коментар щодо Security Rating (до і після)
* **До рефакторингу:** Рейтинг **C** зумовлювався 15 вразливостями: 11 × Minor `S6444` (відсутність таймаутів у регулярних виразах для адрес користувачів, ризик ReDoS), 3 × Major `S2068` (паролі тестових акаунтів відкритим текстом у `WaterfallDbSeeder.cs`) та 1 × Major `S5122` (політика CORS `AllowAll` у `Program.cs`).
* **Після рефакторингу:** Кількість вразливостей у SonarQube Cloud **впала з 15 до 4** (-73%). Усі 11 регулярних виразів отримали жорсткий таймаут 1000мс (`matchTimeoutMilliseconds: 1000`), що унеможливлює ReDoS. Залишкове значення рейтингу C тримається виключно на політиці CORS `AllowAll` для локальної розробки у `Program.cs` (рядок 124).

---

## Нотатка «Використання ШІ» (AI Usage Note)

* **Який рефакторинг виконано з допомогою агента:** Усі 3 обрані запахи коду реалізовано в парі з ШІ-асистентом Antigravity:
  1. *Extract Class:* Створення єдиного модуля `GeoAddressHelper` та перенесення туди спільної логіки нормалізації адрес, генерації пошукових запитів і валідації міст.
  2. *Replace Regex with Source Generated Regex & Introduce Timeout:* Заміна викликів `Regex.Replace` на `[GeneratedRegex]` з компіляцією під час білду та таймаутом у 1000мс.
  3. *Replace Inline Array with Symbolic Constant:* Заміна повторюваних інлайн-масивів `JsonSerializer.Serialize(new[] { ... })` на константи `SkillsJson` та видалення паролів з коментарів.
* **Що саме згенеровано:** Файл [`src/waterfall.Repository/Helpers/GeoAddressHelper.cs`](../../src/waterfall.Repository/Helpers/GeoAddressHelper.cs), оптимізовані константи та виклики у [`GeoLocationService.cs`](../../src/waterfall.Services/GeoLocationService.cs), [`GeoDistanceService.cs`](../../src/waterfall.Client/Services/GeoDistanceService.cs), [`EfGeoLocationCacheRepository.cs`](../../src/waterfall.Repository/Repositories/EfGeoLocationCacheRepository.cs) та [`WaterfallDbSeeder.cs`](../../src/waterfall.Repository/Data/WaterfallDbSeeder.cs).
* **Як верифіковано:**
  1. **Построковий аудит diff:** Кожна зміна аналізувалася рядок за рядком перед комітом — перевірено відсутність зміни контрактів та сигнатур публічних методів.
  2. **Локальне регресійне тестування:** Після кожного окремого коміту запускався тестовий сьют проєкту; фінально всі **526 тестів пройдено успішно (0 помилок)**.
  3. **Метрики SonarQube Cloud:** Результат підтверджено незалежним аналізом SonarQube Cloud API (падіння Code Smells з 60 до 44, вразливостей з 15 до 4).

---

## Скриншоти та панель SonarQube Cloud

### Перший скріншот під час практичної не зробили, одразу почали рефакторити, тож наведено посилання на показними SonarQube Cloud, які доказують проведене покращення:
* **Графік змін та динаміка метрик (Activity Graph):** [SonarQube Cloud — Activity Graph](https://sonarcloud.io/project/activity?id=ukma-fin-ssdm-2026_waterfall&branch=main) *(на графіку зафіксовано різке падіння Code Smells з 60 до 44 та вразливостей з 15 до 4 на коміті `4cb9b0f`)*.
* **Аналіз виконаного Pull Request #139:** [SonarQube Cloud — PR #139](https://sonarcloud.io/summary/new_code?id=ukma-fin-ssdm-2026_waterfall&pullRequest=139) *(Quality Gate: Passed, 0 New Issues, 0 Duplications, Rating: A)*.
* **Оновлений стан гілки main:** [SonarQube Cloud — main Summary](https://sonarcloud.io/summary/overall?id=ukma-fin-ssdm-2026_waterfall&branch=main).

---

# Етап 2 — Рефакторинг модуля збору вакансій (Данило)

Гілка `refactor/lab08-ingestion`; стан «до» — гілка [`pre-refactor`](https://github.com/ukma-fin-ssdm-2026/waterfall/tree/pre-refactor) (`1e5e07b`). Три запахи обрано лише в модулі збору вакансій (issue #62), чужих модулів не чіпали.

**Як міряли складність.** Панель SonarQube Cloud не показує когнітивну складність окремих функцій: правило `S3776` вимкнене в профілі «Sonar way». Тому складність заміряно тим самим аналізатором локально: SonarAnalyzer.CSharp 10.35 з `S3776` і порогом 0, щоб правило звітувало про кожен метод. Суми по файлах збігаються з панеллю (`SiteCrawler.cs` — 44, `VacancyIngestionService.cs` — 60), тож цифри порівнянні з SonarQube Cloud.

## 1. Запах: дублювання помічників розбору HTML у трьох парсерах
* **Де знаходився:** блок із вкладки Duplications `WorkUaParser.cs:101-118` ↔ `DouParser.cs:128-144` (методи `Load`, `Text`, `Clean`); `Load` і `Clean` повторювалися ще й у `DjinniParser.cs`.
* **Техніка з каталогу:** **Extract Class** — один клас [`Html.cs`](../../src/api/Workers/Html.cs), парсери викликають `Html.Load`, `Html.Text`, `Html.Clean`.
* **Три ключові аспекти:**
  1. *Що лишилося незмінним:* результат розбору сторінок усіх трьох сайтів — ті самі поля вакансій із тих самих сторінок.
  2. *Що змінилося в структурі:* одна копія чотирьох методів замість трьох; у чотирьох файлах разом із новим `Html.cs` −70 і +53 рядки.
  3. *Який тест це доводить:* [`WorkUaPageTests`](../../tests/waterfall.Ingestion.Tests/WorkUaPageTests.cs), [`DouPageTests`](../../tests/waterfall.Ingestion.Tests/DouPageTests.cs), [`DjinniPageTests`](../../tests/waterfall.Ingestion.Tests/DjinniPageTests.cs) — розбір збережених справжніх сторінок сайтів (68 випадків).

## 2. Запах: найскладніша функція модуля — `SiteCrawler.CrawlAsync` (когнітивна складність 35 при ліміті 15)
* **Де знаходився:** [`SiteCrawler.cs`](../../src/api/Workers/SiteCrawler.cs) — одна функція на 120 рядків із вкладеністю до чотирьох рівнів: сторінки списку, сторінки вакансій, кеш, збої й паузи разом.
* **Техніка з каталогу:** **Extract Method** + **Introduce Parameter Object**. Шість змінних одного обходу зібрано в `CrawlState`; з тіла винесено `ReadListPageAsync` (сторінка списку), `WithJobPageAsync` (вакансія й дані з її сторінки), `ReadJobPageAsync` (читання сторінки вакансії), `RememberJobPages` (кеш сторінок після обходу).
* **Три ключові аспекти:**
  1. *Що лишилося незмінним:* які запити й у якому порядку йдуть до сайту, пауза перед кожним, перевірка `robots.txt`, сторінка вакансії читається один раз, при збої лишається зібране раніше, після 403/429/503 сайт стає на паузу.
  2. *Що змінилося в структурі:* `CrawlAsync` читається як план обходу; складність 35 → 9, найскладніший із нових методів — 6.
  3. *Який тест це доводить:* [`JobParserServiceTests`](../../tests/waterfall.Ingestion.Tests/JobParserServiceTests.cs) (39 випадків) на підроблених сайтах рахують запити й перевіряють кеш, паузи та збої: `Djinni_ReadsSkillsFromJobPage_OncePerVacancy`, `Djinni_JobPageError_KeepsVacancyAndSparesSite`, `Crawler_InterruptedCrawl_KeepsRememberedJobPages`, `Crawler_PauseDoublesWithEachRefusal_AndResetsAfterSuccess` та інші.

## 3. Запах: складна `VacancyIngestionService.ParseSalary` (складність 17, вкладений тернарний оператор `S3358` ×2)
* **Де знаходився:** [`VacancyIngestionService.cs`](../../src/waterfall.Services/VacancyIngestionService.cs) — валюта визначалася ланцюжком із трьох вкладених `? :` усередині розбору суми.
* **Техніка з каталогу:** **Extract Method** + **Replace Nested Conditional with Guard Clauses** — визначення валюти винесено в `DetectCurrency`, ланцюжок `? :` замінено трьома `if` з раннім поверненням.
* **Три ключові аспекти:**
  1. *Що лишилося незмінним:* валюта визначається за позначкою в рядку в тому самому порядку — USD, EUR, UAH, інакше типова валюта сайту; розбір сум і «від/до» не зачеплено.
  2. *Що змінилося в структурі:* складність `ParseSalary` 17 → 8 (`DetectCurrency` — 6), обидва `S3358` у файлі зникли.
  3. *Який тест це доводить:* спершу додано 7 характеризаційних тестів `ParseSalary_CurrencyMarkInText_WinsInFixedOrder` (позначки `usd`, `eur`, `₴`, `uah` і порядок, якщо валют у рядку кілька) — вони пройшли на старому коді й зафіксували його поведінку; далі разом із `ParseSalary_ReadsCommonFormats` і `ParseSalary_UsesSiteCurrencyWhenNoneIsWritten` (18 випадків) — у [`VacancyIngestionServiceTests`](../../tests/waterfall.Ingestion.Tests/VacancyIngestionServiceTests.cs).

## Кроки й перевірка регресії

12 кроків — 12 комітів: 4 для першого запаху, 5 для другого, 3 для третього (перший із них — лише тести). Після кожного кроку проганялися тести модуля (186 випадків, після характеризаційних тестів — 193), усі зелені. Перед пушем гілку перенесено на свіжий `main` (`b67641c`, з рефакторингами PR #139–#141) і прогнано всі тестові проєкти: 540 із 540.

| Показник (модуль збору) | До (`pre-refactor`) | Після |
| :--- | :---: | :---: |
| Когнітивна складність `CrawlAsync` | 35 | **9** |
| Когнітивна складність файлу `SiteCrawler.cs` | 44 | **30** |
| Когнітивна складність `ParseSalary` | 17 | **8** |
| Функцій зі складністю понад 15 | 2 | **0** |
| Дубльований блок WorkUa ↔ DOU | 35 рядків | **0** |
| `S3358` (вкладений тернарний оператор) | 3 | **1** |

Дублювання й складність по файлах на панелі SonarQube Cloud оновляться після злиття PR — тоді звіримо їх із таблицею.

**Свідомо не чіпали:** `ResolveFullAddress` у `VacancyIngestionService` (складність 13, дублювання всередині функції — блок 317–336 ↔ 321–340) писав Денис, тож переробка — за його згодою. Під час аналізу помічено, що локація лише з ком (`","`) у ній кидає `IndexOutOfRangeException` і зриває збереження всіх вакансій цього сайту за обхід — це окремий баг, а не рефакторинг.

## Використання ШІ

Усі три рефакторинги виконав агент Claude Code. Завдання для кожного містило запах, техніку й те, що має лишитися незмінним (див. вище). Агент вносив зміни малими кроками, проганяв тести після кожного, для третього запаху спершу написав характеризаційні тести, а також заміряв когнітивну складність локальним аналізатором. Диф переглядає Данило рядок за рядком у PR перед злиттям.
