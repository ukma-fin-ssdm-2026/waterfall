# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

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
