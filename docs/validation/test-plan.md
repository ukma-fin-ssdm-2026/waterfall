# План тестування

План Лабораторної 7 (актуалізовано 20.11.2026). Спирається на [стратегію тестування](../testing/testing-strategy.md) (Лаб 6), [вимоги](../requirements/requirements.md) і [матрицю простежуваності](../requirements/rtm.md) (Лаб 2).

**Обсяг:** каталог і мультисайтовий парсинг, повна база вакансій і крос-сайтова фільтрація, двостороння синхронізація офлайн/онлайн, збережені вакансії, рекомендації, ШІ-консультант, аналітика та надійність; рівні — модульний, інтеграційний, системний, приймальний. Свідомо **НЕ** тестуємо в цьому плані:

- реальні відповіді LLM і зовнішні геоджерела (Photon, OSRM, Wikipedia) — у тестах вони підмінені заглушками (живий LLM-тест пропускається без змінної `WATERFALL_LLM_LIVE=1`);
- MAUI UI на Android — ручний чек-лист перед релізом, Appium у CI не запускається;
- навантаження понад 10 000 вакансій і конкурентність запису в SQLite;
- міграції БД і розгортання (перевіряються в Лаб 10).

**Критерії виходу:** усі сім наборів зелені в CI (`.github/workflows/ci.yml`, кожен job ≤ 5 хв за MAINT-001); зведене покриття не нижче line 75% (поріг кроку `Coverage Quality Gate`) — на 20.11.2026: **1088 пройдено (+1 skip live LLM), line 82.2%, branch 72.6%**; PERF-001 — p95 < 1000 мс (виміряно 133.18 мс), SEC-001 — нуль паролів у відкритому вигляді; немає відкритих дефектів рівня *Blocking*.

**Ризики:** найдорожче коштують (1) розбіжність контракту клієнт ↔ API — уже траплялася, коли сервер почав віддавати enum назвою, а клієнт чекав число; (2) трансляція EF-фільтрів у SQL — тест на репозиторії ловить її, тест на сервісі з підробленим репозиторієм ні; (3) NFR-пороги на спільному self-hosted раннері — можуть «плавати» під навантаженням сусідніх збірок; (4) таймаути зовнішніх геосервісів. Тому таблиця починається з контракту і фільтрів.

## Тест-кейси

ID кейсів (TP-xx) вписано в стовпець «Тестові випадки» [`rtm.md`](../requirements/rtm.md) біля вимог, на які кейс посилається в колонці «Вимога».

| ID | Компонент/функція | Рівень тесту | Тип (позитивний/негативний) | Очікуваний результат / критерій прийняття | Власник | Вимога (ID з `rtm.md`) |
| --- | --- | --- | --- | --- | --- | --- |
| TP-01 | Каталог: перша сторінка, 20 позицій, сортування за `PublishedAt` | Інтеграційний | Позитивний | Автотест: `EfVacancyRepositoryTests.GetAllAsync_NoFilters_ReturnsActiveVacanciesOrderedByPublishedAtDescending`, `GetAllAsync_Pagination_SkipsAndTakesExpectedWindow` | Денис | US-001 (Test-02) |
| TP-02 | Каталог: картка показує заголовок, компанію, зарплату, джерело | Модульний | Позитивний | Автотест: `VacancyItemViewModelTests.SalaryText_CoversAllRangeShapes`, `SourceText_MapsEverySource`, `CompanyLine_HandlesMissingLocation` | Денис | US-001 (Test-01) |
| TP-03 | Каталог: картка показує щонайбільше 3 навички та чип «+N», без порожніх і повторених | Модульний | Позитивний | Автотест: `VacancyItemSkillsTests.VisibleSkills_MoreThanThree_FirstThreeAndOverflowCount`, `VisibleSkills_BlanksAndDuplicates_DroppedBeforeCounting`, `VisibleSkills_EmptySkills_EmptyAndNoOverflow` | Константін | US-001 (Test-34, Test-35) |
| TP-04 | Каталог: порожній результат і помилка сервера | Модульний | Негативний | Автотест: `CatalogViewModelTests.NoResults_IsEmpty`, `Failure_ShowsServerMessage_RetryClearsIt`, `Failure_KeepsPreviousCards_ShowsBanner_RetryRecovers` | Денис | US-002 (Test-07) |
| TP-05 | Фільтри: категорія, досвід, графік, локація, межі зарплати, джерело | Інтеграційний | Позитивний | Автотест: `EfVacancyRepositoryTests.GetAllAsync_CategoryFilter_FiltersCaseInsensitively`, `..._ExperienceAndScheduleFilters_FilterCorrectlyAndHandleNulls`, `..._LocationFilter_MatchesLocationSubstring`, `..._MinSalaryFilter_ChecksSalaryMinOrMax`, `..._MaxSalaryFilter_ChecksSalaryMaxOrMin`, `..._SourceFilter_MatchesExactSource` | Денис | US-002 (Test-06) |
| TP-06 | Фільтри: невалідні межі зарплати (`min > max`, нечисловий ввід) | Модульний | Негативний | Автотест: `SalaryRangeParserTests.Parse_MinGreaterThanMax_ReturnsNull`, `Parse_InvalidOrGarbageInput_ReturnsNull` | Денис | US-002 |
| TP-07 | Збережені: зберегти/зняти з картки, повторний клік не дублює запис | Модульний | Позитивний | Автотест: `VacancyItemViewModelTests.Toggle_CallsServerAndFlipsState`, `Toggle_ServerAlreadyInTargetState_SyncsWithoutError`, `Toggle_RealFailure_KeepsStateAndReportsMessage` | Денис | US-003 (Test-09) |
| TP-08 | Збережені: чужі збережені недоступні, видалення чужого запису відхиляється | Інтеграційний | Негативний | Автотест: `SavedVacanciesControllerTests.Delete_ExistingSavedVacancy_ReturnsNoContent`, `EfSavedVacancyRepositoryTests.DeleteAsync_WrongUserOrNonExistent_ReturnsFalse`, `EfSavedVacancyRepositoryTests.GetByIdAsync_FindsBySavedVacancyIdOrVacancyId_ForCorrectUser` | Денис | US-003 (Test-10) |
| TP-09 | Збережені: прибирання в «Каталозі» оновлює «Обране» без перезавантаження | Модульний | Позитивний | Автотест: `FavoritesViewModelTests.UnsaveFromCatalog_RemovesMatchingItem_SaveIsIgnoredUntilNextLoad`, `UnsaveFromAnotherTab_UpdatesMatchingCardInPlace` | Константін | US-003 (Test-09, Test-10) |
| TP-10 | Збір: нормалізація полів і відхилення битих або чужих файлів | Модульний | Негативний | Автотест: `JobParserServiceTests.ParseDump_RejectsBrokenEmptyOrForeignFiles`, `BrokenFile_DoesNotStopOtherFiles`; `VacancyIngestionServiceTests.Normalize_RejectsVacancyWithoutTitleOrDescription`, `Normalize_RejectsLinksOutsideDjinni`; `DjinniPageTests`, `DouPageTests`, `WorkUaPageTests` — нормалізація полів кожного джерела | Данило | PERF-003 (Test-04), REL-001 (Test-05) |
| TP-11 | Збір: повторний прогін не дублює вакансії й зберігає `FirstSeenAt` | Інтеграційний | Позитивний | Автотест: `VacancyIngestionServiceTests.SampleDump_IsSavedOnce_AndUpdatedOnRepeatedRun`; `BulkUpsertTests.Update_KeepsIdentityAndFirstSeenDates`; `EfVacancyRepositoryTests.BulkUpsertAsync_CreatesNewAndUpdatesExisting_PreservingIdAndTimestamps` | Данило | PERF-003 (Test-04) |
| TP-12 | NFR швидкодія: каталог на 10 000 записів | Системний | Позитивний | Автотест: `PERF_001_CatalogPagination_P95Latency_UnderOneSecond_With10kRecords` — поріг p95 < 1000 мс, виміряно 133.18 мс (100 запитів, сторінка 20) | Денис | PERF-001 (Test-03) |
| TP-13 | NFR безпека: у БД немає паролів у відкритому вигляді, хеш PBKDF2 | Інтеграційний | Негативний | Автотест: `SEC_001_ZeroPlainTextPasswordsInDatabaseStorage`, `PasswordHasherTests.Hash_DoesNotContainPlaintext` | Денис, Дмитро | SEC-001 (Test-11) |
| TP-14 | NFR процес: CI у межах 5 хв і покриття не нижче цілі | Системний | Позитивний | CI `ci.yml` (7 наборів, 1087 пройдено, 0 падінь) + зведений звіт ReportGenerator: line 82.2%, branch 72.6% (20.11.2026) | Денис | MAINT-001 (Test-31) |
| TP-15 | Аналітика: роль без прав не бачить даних | Модульний / Інтеграційний | Негативний | Автотест: `AnalyticsControllerTests`, `AnalyticsViewModelTests` — перевірка 403 і тексту відмови в доступі для не-аналітика | Константін | US-006 (Test-24) |
| TP-16 | Аналітика: недостатньо даних для графіків | Модульний | Негативний | Автотест: `AnalyticsServiceTests`, `AnalyticsViewModelTests` — перевірка стану недостатності даних при малій вибірці | Константін | US-007 (Test-27) |
| TP-17 | Приймальний: пошук → фільтрація → збереження за ≤ 2 хв | Приймальний | Позитивний | Ручний сценарій на 10 новачках без навчання, ≥ 90% успішних, фіксація часу кожного | Константін | USE-001 (Test-08) |
| TP-18 | Збір: збій або відмова одного сайту не зупиняє інші; після 403/429/503 сайт ставиться на паузу | Модульний | Негативний | Автотест: `JobParserServiceTests.Sites_AndDumps_AreProcessedSeparately`, `Djinni_SiteError_KeepsEarlierPages`, `Crawler_PausesOnlyWhenSiteRefuses`, `Crawler_HonorsRetryAfter` | Данило | REL-001 (Test-05) |
| TP-19 | Збір: ввічливість до сайтів — `robots.txt` за RFC 9309, пауза перед кожним запитом; API не стартує зі збором частіше ніж раз на 10 хв чи паузою < 0,5 с | Модульний | Негативний | Автотест: `RobotsTxtTests.WorkUa_FollowsWildcardsEndAnchorsAndLongestRule`, `JobParserServiceTests.Crawler_SkipsJobPagesForbiddenByRobots`, `OptionsValidator_RejectsIntrusiveSettings` | Данило | PERF-003 (Test-04) |
| TP-20 | Пошук і фільтри кирилицею без урахування регістру; навички лише з поля сайту, без вгадування з опису | Інтеграційний | Позитивний | Автотест: `EfVacancyRepositoryTests.GetAllAsync_CyrillicFilters_IgnoreCase`, `VacancyIngestionServiceTests.Normalize_WithoutSkillsField_DoesNotGuessSkillsFromText` | Данило | US-002 (Test-06) |
| TP-21 | Геокеш: некоректні координати (`NaN`, широта поза ±90°) не зберігаються, порожнє тіло відхиляється 400 | Інтеграційний | Негативний | Автотест: `GeoControllerTests`, `BoundaryTests`, `FaultInjectionTests` (FIND-05) | Денис | US-010 (FR-016) |
| TP-22 | Автентифікація: реєстрація з валідними даними видає JWT та зберігає користувача | Інтеграційний | Позитивний | Автотест: `AuthControllerTests.Register_Success_Returns201_WithBody`, `AuthenticationServiceTests.Register_*` | Дмитро | US-009 (Test-36) |
| TP-23 | Мультисайтовий парсинг: одночасний запуск 1–4 сайтів, ізоляція збою одного сайту, валідація дублікатів і ліміту | Модульний / Інтеграційний | Позитивний / Негативний | Автотест: `SiteSearchServiceTests.StartBatch_*`, `SiteSearchesControllerTests.StartBatch_*`, `CatalogMultiSiteSearchTests` | Денис | US-011 (Test-42…44) |
| TP-24 | Повна база вакансій: завантаження всіх сторінок 4 сайтів, крос-сайтові фільтри, сортування, JSON експорт/імпорт | Модульний / Інтеграційний | Позитивний / Негативний | Автотест: `DatabaseViewModelTests` | Денис | US-012 (Test-45…46) |
| TP-25 | Двостороння синхронізація БД вакансій: офлайн-збір DOU/Djinni, обмін клієнт ↔ сервер без дублів, Last-Write-Wins і апгрейд старої SQLite схеми | Інтеграційний | Позитивний / Негативний | Автотест: `VacancySyncTests`, `DatabaseViewModelTests.OfflineFallback_*`, `EfVacancyRepositoryTests.SyncMergeAsync_*`, `VacancyServiceTests.SyncVacanciesAsync_*`, `VacanciesControllerTests.Sync_*` | Денис | US-013 (Test-48…50) |

З усіх кейсів лише приймальний юзабіліті-кейс **TP-17** є ручним; решта (TP-01…TP-16, TP-18…TP-25) автоматизовані й виконуються у CI.

## Прогалини простежуваності (оновлено)

- **Закрито:** веб-збір і мультисайтовий парсинг (FR-017 / US-011), повна база вакансій (FR-018 / US-012), офлайн-синхронізація БД (FR-019 / US-013), автотести для аналітики (TP-15, TP-16), ШІ-консультанта (`AIConsultantViewModelTests`, `AIConsultantServiceTests`, `AIConsultantControllerTests`), особистої статистики (`UserStatsServiceTests`, `LoginViewModelTests`) та валідації геокешу (TP-21).
- **Залишається ручним:** приймальне юзабіліті-тестування TP-17 (USE-001) та живий LLM-прогін (`LlmLiveSmokeTests`, опційно за `WATERFALL_LLM_LIVE=1`).

## Бонуси

### Бонус 4 — виконуваний нефункціональний тест

| Вимога | Поріг | Виміряне значення | Тест і команда |
| --- | --- | --- | --- |
| PERF-001 | p95 < 1000 мс на 10 000 записів, сторінка 20 | p95 = 133.18 мс, середнє 49.97 мс, min/max 1.94 / 668.62 мс, 100 запитів | `PERF_001_CatalogPagination_P95Latency_UnderOneSecond_With10kRecords`; `dotnet test tests/waterfall.Repository.Tests --filter "FullyQualifiedName~PERF_001"` (у CI — job `test`, крок `Repository tests`) |
| SEC-001 | 0 паролів у відкритому вигляді, хеш PBKDF2 | 0 збігів, усі хеші починаються з `PBKDF2` | `SEC_001_ZeroPlainTextPasswordsInDatabaseStorage`; `dotnet test tests/waterfall.Repository.Tests --filter "FullyQualifiedName~SEC_001"` |

Обидва тести — звичайні `[Fact]` у наборі, тому червоні вони валять CI, а не залишаються звітом.

## Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
| :--- | :--- | :--- | :--- |
| План тестування: обсяг, критерії виходу, ризики, таблиця кейсів, розділ «Бонуси» | Константін | Чернетка тексту й звірка назв тестів із кодом | Кожну назву тесту звірено з `tests/**`; кількість тестів і покриття — з прогоном `dotnet test` і звітом ReportGenerator; PERF/SEC-значення — з власного прогону з `--filter` |
| ID кейсів (TP-xx), кейси TP-18…TP-21 | Данило | Добір кейсів і звірка назв тестів із кодом | Кожну назву тесту знайдено пошуком у `tests/**`; TP-21 — за висновками огляду PR #114, відтвореними тимчасовим тестом |
| TP-22 та закриття прогалин | Дмитро | Добір кейсів і звірка назв тестів із кодом | Кожну назву тесту знайдено пошуком у `tests/**` |
