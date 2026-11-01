# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added
- **Нормалізація вакансій (#3)**: `VacancyDB` отримує `WorkFormat` (прапорці `Remote`/`Hybrid`/`Office`), `ExperienceMonths` (мінімальний досвід у місяцях), `CategoryGroup` (закритий довідник широких IT і не-IT категорій) і `SearchText` (нижній регістр назви, компанії, опису й тегів). Похідні поля обчислюються на кожному записі вакансії — парсерами, `POST`/`PUT` каталогу та сідуванням. Djinni зберігає `monthsOfExperience`, DOU позначає досвід за фасетними стрічками, решту джерел розбирає текст досвіду й опису.
- **Фільтри каталогу (#3)**: `workFormat` (будь-який із вибраних форматів), `maxExperienceMonths` (включна межа; невідомий досвід не відсіюється), `categoryGroup` (точна канонічна група), `location` (підрядок без урахування регістру) і `skills` (усі терміни через AND, кожен — у тегах або тексті). `VacancyDto` віддає `workFormat`, `experienceMonths`, `categoryGroup`.

### Fixed
- Пошук каталогу знаходить кирилицю незалежно від регістру («бухгалтер» → «Бухгалтер») на SQLite і MSSQL завдяки `SearchText`.
- Ключ кешу каталогу враховує всі фільтри, `sortBy` і `sortOrder`; фільтр локації більше не підміняється `null`, а Stale-Cache зберігає лише «чисту» першу сторінку без фільтрів.

## [0.1.1] - 2026-10-27

### Refactoring
- **Address processing deduplication** (*Extract Class*): Extracted shared normalization, query candidate generation, and city conflict detection into `GeoAddressHelper` to eliminate duplicate logic between `GeoLocationService` and `GeoDistanceService`.
- **Regex ReDoS protection and performance** (*Replace Regex with Source Generated Regex & Introduce Timeout*): Converted inline regexes to compile-time generated regexes (`[GeneratedRegex]`) with explicit 1000ms timeouts in `EfGeoLocationCacheRepository` and `GeoAddressHelper` (fixing `SYSLIB1045` and `S6444`).
- **Constant allocation optimization** (*Replace Inline Array with Symbolic Constant*): Replaced recurring inline arrays and JSON serialization with static string constants for vacancy skills in `WaterfallDbSeeder` (fixing `CA1861`), and removed plaintext passwords from code comments (fixing `S2068`).

## [0.1.0] - 2026-10-26

### Added
- Baseline project state before Lab 8 refactoring (PR #118 / commit `f7f94d4`).
