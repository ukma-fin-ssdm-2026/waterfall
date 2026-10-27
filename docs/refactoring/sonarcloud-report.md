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

| Показник | До рефакторингу (`f7f94d4`) | Після рефакторингу (`4cb9b0f`) | Різниця та ефект |
| :--- | :---: | :---: | :--- |
| **Security Rating** | **C** (15 вразливостей: 4 Major, 11 Minor) | **A** (усунуто 11 ReDoS + 3 S2068) | Піднято до найвищого рівня якості A |
| **Code Smells** | 60 (59 у коді) | **44** (зменшено на 16) | Закрито 11 `SYSLIB1045` та 5 `CA1861` |
| **Duplicated Lines** | 8,4 % у `src/` (970 рядків) | **< 6.8 %** у `src/` | Вилучено понад 158 рядків дублювання між клієнтом і сервером |
| **Maintainability** | **A** | **A** | Збережено найвищу оцінку, зменшено техборг |
| **Reliability Rating** | C у `src/` | **A** | Усунуто потенційні збої та підтверджено регресією |

---

## Скриншоти панелі SonarQube Cloud

* Стан «До» (коміт `f7f94d4`): [SonarQube Cloud Overview — f7f94d4](https://sonarcloud.io/summary/overall?id=ukma-fin-ssdm-2026_waterfall&branch=main)
* Стан «Після» (коміт `4cb9b0f` після злиття PR #139): [SonarQube Cloud Overview — main](https://sonarcloud.io/summary/overall?id=ukma-fin-ssdm-2026_waterfall&branch=main)

