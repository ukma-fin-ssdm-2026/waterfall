# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added
- **Multi-site simultaneous parsing** (`POST /api/v1/site-searches/batch`, `GET /api/v1/site-searches/batch/{id}`): users can enable/disable any subset of the 4 supported platforms (**DOU**, **Work.ua**, **Djinni**, **Robota.ua**) via per-site checkboxes, configure filters and page ranges independently for each site, and run a parallel batch crawl with live per-site progress and partial-failure resilience. Single-site requests continue to use `POST /api/v1/site-searches`.
- **Dedicated «База вакансій» tab** (`DatabasePage` / `DatabaseViewModel`): loads and displays the full database of vacancies across all 4 platforms upon tab switch, providing cross-site source toggles with live counts, debounced full-text search, canonical `CategoryGroup` and dynamic category/location filters, work format, experience, cross-currency salary filtering (`USD`/`UAH`/`EUR`), publication freshness, active/archived status, saved/viewed filters, white-list (`Any`/`All`) & ban-list, 13 sorting options, and real JSON export/import.
- **Bidirectional client-server vacancy DB synchronization & offline mode** (`POST /api/v1/vacancies/sync`, `ClientVacancyDB`, `ClientSyncStateDB`, `EfLocalVacancyStore`, `VacancySyncService`, `OfflineSiteCollector`): client stores vacancies locally in SQLite with `IsSynced`/`LocalUpdatedAt` tracking and automatic legacy schema upgrade; supports offline collection (direct DOU RSS & Djinni JSON-LD) and offline category edits; exchanges separately collected data bidirectionally with the server (deduplication by `(Source, ExternalId)` / `Id` / `SourceUrl`, Last-Write-Wins conflict resolution on `LastSeenAt` while preserving fuller descriptions) automatically on app start / network reconnect and manually from «База вакансій» and «Системні налаштування».
- **On-demand site search** (`POST /api/v1/site-searches`, `GET /api/v1/site-searches/{id}`): the catalog's «Знайти» button sends the Djinni, DOU, Work.ua, or Robota.ua filters to the server, saves the vacancies through normalization, and returns them with live progress polling. One active search per site at a time, identical searches served from the database cache for 3 hours, one active search (single or batch) and 20 site searches per hour per user.
- **Robota.ua parser** (`RobotaUaParser`): reads schema.org `JobPosting` JSON-LD and HTML vacancy cards/pages (`#description-wrap`, `data-id` attributes); wired into `JobParserService` (`Ingestion:RobotaUaPages`), on-demand site search, URL domain check and UAH default in `VacancyIngestionService`, and page count on the system settings screen.
- **Headless-browser fetcher for Work.ua and Robota.ua** (`BrowserFetcher` / `IBrowserFetcher` via `PuppeteerSharp`): both sites sit behind Cloudflare and render their vacancy list client-side, so a plain `HttpClient` gets `403` or an empty Angular shell. `SiteCrawler` fetches pages of `IBrowserOnlySite` implementations through a thread-safe headless Chromium instance (`PuppeteerSharp`, which downloads Chromium automatically on first run) with per-host warm session pages and `BotCheckPage` Cloudflare detection, keeping `robots.txt` (in background crawl), 2-second delay, and page-limit behaviour intact. Djinni and DOU still use plain HTTP.
- **Нормалізація вакансій (#3)**: `VacancyDB` отримує `WorkFormat` (прапорці `Remote`/`Hybrid`/`Office`), `ExperienceMonths` (мінімальний досвід у місяцях), `CategoryGroup` (закритий довідник широких IT і не-IT категорій) і `SearchText` (нижній регістр назви, компанії, опису й тегів). Похідні поля обчислюються на кожному записі вакансії — парсерами, `POST`/`PUT`/`PATCH`, синхронізацією та сідуванням. Djinni зберігає `monthsOfExperience`, DOU позначає досвід за фасетними стрічками, решту джерел розбирає текст досвіду й опису.
- **Фільтри каталогу (#3)**: `workFormat` (будь-який із вибраних форматів), `maxExperienceMonths` (включна межа; невідомий досвід не відсіюється), `categoryGroup` (точна канонічна група), `location` (підрядок без урахування регістру) і `skills` (усі терміни через AND, кожен — у тегах або тексті). `VacancyDto` віддає `externalId`, `workFormat`, `experienceMonths`, `categoryGroup`.

### Changed
- **Background ingestion can be switched on and off without restarting the API**: `JobParserService` watches `Ingestion:Enabled` in `appsettings.json` (edited by hand or from the system settings screen) and starts or stops collecting within a second; `Ingestion:EnableOnStart` turns collection on at startup without editing the file. It still starts disabled by default.
- **`robots.txt` answered with 401/403** now means the site is skipped and paused, like 5xx and 429; before, any 4xx counted as a missing file that allows everything.

### Fixed
- Пошук каталогу знаходить кирилицю незалежно від регістру («бухгалтер» → «Бухгалтер») на SQLite і MSSQL завдяки `SearchText`.
- Ключ кешу каталогу враховує всі фільтри, `sortBy` і `sortOrder`; фільтр локації більше не підміняється `null`, а Stale-Cache зберігає лише «чисту» першу сторінку без фільтрів.

### Refactoring
- **HTML parsing helpers deduplication** (*Extract Class*): Moved `Load`, `Text` and `Clean`, copied across `WorkUaParser`, `DouParser` and `DjinniParser`, into one shared `Html` class (removes the duplicated block `WorkUaParser.cs:101-118` ↔ `DouParser.cs:128-144` reported by SonarQube Cloud).
- **Crawl loop decomposition** (*Extract Method, Introduce Parameter Object*): Split `SiteCrawler.CrawlAsync` into `ReadListPageAsync`, `WithJobPageAsync`, `ReadJobPageAsync` and `RememberJobPages`, keeping the state of one crawl in `CrawlState`; cognitive complexity of `CrawlAsync` 35 → 9.
- **Salary currency detection** (*Extract Method, Replace Nested Conditional with Guard Clauses*): Moved currency detection out of `VacancyIngestionService.ParseSalary` into `DetectCurrency` and replaced the nested ternary (fixing `S3358`); cognitive complexity of `ParseSalary` 17 → 8.

### Tests
- Characterization tests for currency marks in salary text (`ParseSalary_CurrencyMarkInText_WinsInFixedOrder`), added before the salary refactoring.

## [0.1.1] - 2026-10-27

### Refactoring
- **Address processing deduplication** (*Extract Class*): Extracted shared normalization, query candidate generation, and city conflict detection into `GeoAddressHelper` to eliminate duplicate logic between `GeoLocationService` and `GeoDistanceService`.
- **Regex ReDoS protection and performance** (*Replace Regex with Source Generated Regex & Introduce Timeout*): Converted inline regexes to compile-time generated regexes (`[GeneratedRegex]`) with explicit 1000ms timeouts in `EfGeoLocationCacheRepository` and `GeoAddressHelper` (fixing `SYSLIB1045` and `S6444`).
- **Constant allocation optimization** (*Replace Inline Array with Symbolic Constant*): Replaced recurring inline arrays and JSON serialization with static string constants for vacancy skills in `WaterfallDbSeeder` (fixing `CA1861`), and removed plaintext passwords from code comments (fixing `S2068`).

## [0.1.0] - 2026-10-26

### Added
- Baseline project state before Lab 8 refactoring (PR #118 / commit `f7f94d4`).
