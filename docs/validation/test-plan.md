# План тестування

План Лабораторної 7. Спирається на [стратегію тестування](../testing/testing-strategy.md) (Лаб 6), [вимоги](../requirements/requirements.md) і [матрицю простежуваності](../requirements/rtm.md) (Лаб 2). Дата: 2026-10-26.

**Обсяг:** каталог і пошук вакансій, збережені вакансії, збір вакансій та аналітика; рівні — модульний, інтеграційний, системний, приймальний. Свідомо **НЕ** тестуємо в цьому плані:

- реальні відповіді LLM і зовнішні геоджерела (Photon, OSRM, Wikipedia) — у тестах вони підмінені заглушками;
- MAUI UI на Android — ручний чек-лист перед релізом, Appium у CI не запускається;
- навантаження понад 10 000 вакансій і конкурентність запису в SQLite;
- міграції БД і розгортання (перевіряються в Лаб 10).

**Критерії виходу:** усі шість наборів зелені в CI (`.github/workflows/ci.yml`, кожен job ≤ 5 хв за MAINT-001); зведене покриття не нижче line 75% (поріг кроку `Coverage Quality Gate`) і branch 70% — на 26.10.2026 після мерджу #136 у CI: **526 тестів, line 77.7%, branch 74.3%**; PERF-001 — p95 < 1000 мс (виміряно 133.18 мс), SEC-001 — нуль паролів у відкритому вигляді; немає відкритих дефектів рівня *Blocking*.

**Ризики:** найдорожче коштують (1) розбіжність контракту клієнт ↔ API — уже траплялася, коли сервер почав віддавати enum назвою, а клієнт чекав число; (2) трансляція EF-фільтрів у SQL — тест на репозиторії ловить її, тест на сервісі з підробленим репозиторієм ні; (3) NFR-пороги на спільному self-hosted раннері — можуть «плавати» під навантаженням сусідніх збірок; (4) таймаути зовнішніх геосервісів. Тому таблиця починається з контракту і фільтрів.

## Тест-кейси

| № | Компонент/функція | Рівень тесту | Тип (позитивний/негативний) | Очікуваний результат / критерій прийняття | Власник | Вимога (ID з `rtm.md`) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Каталог: перша сторінка, 20 позицій, сортування за `PublishedAt` | Інтеграційний | Позитивний | Автотест: `EfVacancyRepositoryTests.GetAllAsync_NoFilters_ReturnsActiveVacanciesOrderedByPublishedAtDescending`, `GetAllAsync_Pagination_SkipsAndTakesExpectedWindow` | Денис | US-001 (Test-02) |
| 2 | Каталог: картка показує заголовок, компанію, зарплату, джерело | Модульний | Позитивний | Автотест: `VacancyItemViewModelTests.SalaryText_CoversAllRangeShapes`, `SourceText_MapsEverySource`, `CompanyLine_HandlesMissingLocation` | Денис | US-001 (Test-01) |
| 3 | Каталог: картка показує щонайбільше 3 навички та чип «+N», без порожніх і повторених | Модульний | Позитивний | Автотест: `VacancyItemSkillsTests.VisibleSkills_MoreThanThree_FirstThreeAndOverflowCount`, `VisibleSkills_BlanksAndDuplicates_DroppedBeforeCounting`, `VisibleSkills_EmptySkills_EmptyAndNoOverflow` | Константін | US-001 (Test-34, Test-35) |
| 4 | Каталог: порожній результат і помилка сервера | Модульний | Негативний | Автотест: `CatalogViewModelTests.NoResults_IsEmpty`, `Failure_ShowsServerMessage_RetryClearsIt`, `Failure_KeepsPreviousCards_ShowsBanner_RetryRecovers` | Денис | US-002 (Test-07) |
| 5 | Фільтри: категорія, досвід, графік, локація, межі зарплати, джерело | Інтеграційний | Позитивний | Автотест: `EfVacancyRepositoryTests.GetAllAsync_CategoryFilter_FiltersCaseInsensitively`, `..._ExperienceAndScheduleFilters_FilterCorrectlyAndHandleNulls`, `..._LocationFilter_MatchesLocationSubstring`, `..._MinSalaryFilter_ChecksSalaryMinOrMax`, `..._MaxSalaryFilter_ChecksSalaryMaxOrMin`, `..._SourceFilter_MatchesExactSource` | Денис | US-002 (Test-06) |
| 6 | Фільтри: невалідні межі зарплати (`min > max`, нечисловий ввід) | Модульний | Негативний | Автотест: `SalaryRangeParserTests.Parse_MinGreaterThanMax_ReturnsNull`, `Parse_InvalidOrGarbageInput_ReturnsNull` | Денис | US-002 |
| 7 | Збережені: зберегти/зняти з картки, повторний клік не дублює запис | Модульний | Позитивний | Автотест: `VacancyItemViewModelTests.Toggle_CallsServerAndFlipsState`, `Toggle_ServerAlreadyInTargetState_SyncsWithoutError`, `Toggle_RealFailure_KeepsStateAndReportsMessage` | Денис | US-003 (Test-09) |
| 8 | Збережені: чужі збережені недоступні, видалення чужого запису відхиляється | Інтеграційний | Негативний | Автотест: `SavedVacanciesControllerTests.Delete_ExistingSavedVacancy_ReturnsNoContent`, `EfSavedVacancyRepositoryTests.DeleteAsync_WrongUserOrNonExistent_ReturnsFalse`, `EfSavedVacancyRepositoryTests.GetByIdAsync_FindsBySavedVacancyIdOrVacancyId_ForCorrectUser` | Денис | US-003 (Test-10) |
| 9 | Збережені: прибирання в «Каталозі» оновлює «Обране» без перезавантаження | Модульний | Позитивний | Автотест: `FavoritesViewModelTests.UnsaveFromCatalog_RemovesMatchingItem_SaveIsIgnoredUntilNextLoad`, `UnsaveFromAnotherTab_UpdatesMatchingCardInPlace` | Константін | US-003 (Test-09, Test-10) |
| 10 | Збір: нормалізація полів і відхилення битих або чужих файлів | Модульний | Негативний | Автотест: `JobParserServiceTests.ParseDump_RejectsBrokenEmptyOrForeignFiles`, `BrokenFile_DoesNotStopOtherFiles`; `VacancyIngestionServiceTests.Normalize_RejectsVacancyWithoutTitleOrDescription`, `Normalize_RejectsLinksOutsideDjinni`; `DjinniPageTests`, `DouPageTests`, `WorkUaPageTests` — нормалізація полів кожного джерела | Данило | PERF-003 (Test-04), REL-001 (Test-05) |
| 11 | Збір: повторний прогін не дублює вакансії й зберігає `FirstSeenAt` | Інтеграційний | Позитивний | Автотест: `VacancyIngestionServiceTests.SampleDump_IsSavedOnce_AndUpdatedOnRepeatedRun`; `BulkUpsertTests.Update_KeepsIdentityAndFirstSeenDates`; `EfVacancyRepositoryTests.BulkUpsertAsync_CreatesNewAndUpdatesExisting_PreservingIdAndTimestamps` | Данило | PERF-003 (Test-04) |
| 12 | NFR швидкодія: каталог на 10 000 записів | Системний | Позитивний | Автотест: `PERF_001_CatalogPagination_P95Latency_UnderOneSecond_With10kRecords` — поріг p95 < 1000 мс, виміряно 133.18 мс (100 запитів, сторінка 20) | Денис | PERF-001 (Test-03) |
| 13 | NFR безпека: у БД немає паролів у відкритому вигляді, хеш PBKDF2 | Інтеграційний | Негативний | Автотест: `SEC_001_ZeroPlainTextPasswordsInDatabaseStorage`, `PasswordHasherTests.Hash_DoesNotContainPlaintext` | Денис, Дмитро | SEC-001 (Test-11) |
| 14 | NFR процес: CI у межах 5 хв і покриття не нижче цілі | Системний | Позитивний | CI `ci.yml` (6 наборів, 526 тестів, 0 падінь) + зведений звіт ReportGenerator: line 77.7%, branch 74.3% (прогін 26.10.2026) | Денис | MAINT-001 (Test-31) |
| 15 | Аналітика: роль без прав не бачить даних | Модульний | Негативний | План: автотесту немає (`AnalyticsViewModel` без тестів) — перевірити `CheckAccessAndReloadAsync` і текст відмови в доступі | Константін | US-006 (Test-24) |
| 16 | Аналітика: недостатньо даних для графіків | Модульний | Негативний | План: автотесту немає — перевірити повідомлення замість графіка при < 5 вакансіях або без зарплат | Константін | US-007 (Test-27) |
| 17 | Приймальний: пошук → фільтрація → збереження за ≤ 2 хв | Приймальний | Позитивний | Ручний сценарій на 10 новачках без навчання, ≥ 90% успішних, фіксація часу кожного | Константін | USE-001 (Test-08) |

Рядки 15–17 — заплановані: вони або ручні, або ще не мають автотесту. Для кожного такого рядка в колонці «Очікуваний результат» стоїть позначка «План», тож різниця між заявленим і реально виконуваним у CI видима.

## Прогалини простежуваності, знайдені під час планування

- **Автентифікація (40 тестів `waterfall.Auth.Tests` + 12 у `waterfall.Api.Tests`) не має жодного рядка у `rtm.md`** — вимоги на реєстрацію/вхід/JWT у Лаб 2 не зафіксовані. Або додати FR у `requirements.md` і рядок у RTM, або явно винести auth поза обсяг.
- **Геодані (тести `GeoDistanceServiceTests`, `PhotonAddressAutocompleteServiceTests`, `SqliteLocalGeoCacheServiceTests`, `GeoControllerTests`, `ProfileControllerTests`) теж без вимоги** — функціональність з'явилася в PR #114 уже після Лаб 2.
- **Веб-збір джерел (PR #136: `SiteCrawler`, `RobotsTxt`, `DjinniPageTests`, `DouPageTests`, `WorkUaPageTests`, 186 тестів у наборі Ingestion) без вимоги** — FR на збір з веб-джерел і дотримання `robots.txt` у Лаб 2 не записаний; PERF-003 покриває лише темп запитів.
- **US-005 (ШІ-консультант) і US-008 (особиста статистика) не мають жодного тесту** — Test-16…21 і Test-28…30 лишаються планом; `AIConsultantViewModel` покритий нулем тестів.
- **Розбіжність порогу PERF-001**: у стратегії Лаб 6 стояло «≤ 200 мс», у `rtm.md` і в тесті — p95 < 1 с. Стратегію приведено до 1 с (єдине джерело істини — вимога Лаб 2).

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
