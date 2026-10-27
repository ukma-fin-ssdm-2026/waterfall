# Changelog

All notable changes to this project will be documented in this file.

## [0.1.1] - 2026-10-27

### Refactoring
- **Address processing deduplication** (*Extract Class*): Extracted shared normalization, query candidate generation, and city conflict detection into `GeoAddressHelper` to eliminate duplicate logic between `GeoLocationService` and `GeoDistanceService`.
- **Regex ReDoS protection and performance** (*Replace Regex with Source Generated Regex & Introduce Timeout*): Converted inline regexes to compile-time generated regexes (`[GeneratedRegex]`) with explicit 1000ms timeouts in `EfGeoLocationCacheRepository` and `GeoAddressHelper` (fixing `SYSLIB1045` and `S6444`).
- **Constant allocation optimization** (*Replace Inline Array with Symbolic Constant*): Replaced recurring inline arrays and JSON serialization with static string constants for vacancy skills in `WaterfallDbSeeder` (fixing `CA1861`), and removed plaintext passwords from code comments (fixing `S2068`).

## [0.1.0] - 2026-10-26

### Added
- Baseline project state before Lab 8 refactoring (PR #118 / commit `f7f94d4`).
