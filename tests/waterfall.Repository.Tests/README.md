# Тести рівня сховища даних (waterfall.Repository.Tests)

Модуль відповідає за доступ до реляційної бази даних SQLite (Entity Framework Core) для каталогу вакансій, збережених вакансій («лайків» / закладок) та облікових записів користувачів згідно з принципами Clean Architecture.

| Компонент сховища | Файл реалізації | Що робить |
| :--- | :--- | :--- |
| `EfVacancyRepository` | [`src/waterfall.Repository/Repositories/EfVacancyRepository.cs`](../../src/waterfall.Repository/Repositories/EfVacancyRepository.cs) | CRUD вакансій, повнотекстовий пошук за 4 полями, багатокритеріальна фільтрація, сортування за 5 полями, пагінація, пакетний `BulkUpsertAsync` |
| `EfSavedVacancyRepository` | [`src/waterfall.Repository/Repositories/EfSavedVacancyRepository.cs`](../../src/waterfall.Repository/Repositories/EfSavedVacancyRepository.cs) | збереження («лайки») вакансій, отримання списку з навігаційними властивостями, перевірка існування, видалення за `Id` чи `VacancyId`, масова перевірка `GetSavedVacancyIdsAsync` |
| `EfUserRepository` | [`src/waterfall.Repository/Repositories/EfUserRepository.cs`](../../src/waterfall.Repository/Repositories/EfUserRepository.cs) | пошук користувача за нормалізованим email, збереження та оновлення облікових записів |

---

## Опис набору тестів

147 тестових випадків в 11 класах на реальній in-memory SQLite (актуалізовано 22.11.2026):

| Клас тесту | Кількість випадків | Що перевіряє |
| :--- | :---: | :--- |
| [`EfVacancyRepositoryTests`](EfVacancyRepositoryTests.cs) | 39 | `GetAllAsync` (сортування, пошук кирилицею, усі фільтри: `CategoryGroup`, `WorkFormat`, `ExperienceMonths`, `Location`, `Skills`, зарплати, джерела, пагінація), `BulkUpsertAsync`, `SyncMergeAsync` (дедуплікація та Last-Write-Wins), `GetAllForSyncAsync` (повна та дельта-вибірка), архівація застарілих |
| [`VacancyNormalizerTests`](VacancyNormalizerTests.cs) | 32 | обчислення `CategoryGroup`, `WorkFormat`, `ExperienceMonths` |
| [`VacancyExperienceParserTests`](VacancyExperienceParserTests.cs) | 27 | розбір досвіду роботи з текстів українською й англійською у місяці |
| [`EfChatSessionRepositoryTests`](EfChatSessionRepositoryTests.cs) | 13 | діалоги ШІ, вікно історії, FK-захист від вигаданих вакансій (FUNC-001) |
| [`EfSavedVacancyRepositoryTests`](EfSavedVacancyRepositoryTests.cs) | 9 | закладки, ізоляція користувачів, перевірки існування та пакетне `GetSavedVacancyIdsAsync` |
| [`EfGeoLocationCacheRepositoryTests`](EfGeoLocationCacheRepositoryTests.cs) | 7 | кешування й нормалізація адрес геокодування |
| [`EfSearchQueryRepositoryTests`](EfSearchQueryRepositoryTests.cs) | 5 | збереження, отримання й очищення історії пошукових запитів |
| [`EfUserRepositoryTests`](EfUserRepositoryTests.cs) | 3 | пошук і збереження облікових записів користувачів |
| [`EfVacancyViewRepositoryTests`](EfVacancyViewRepositoryTests.cs) | 3 | фіксація та підрахунок переглядів вакансій |
| [`NonFunctionalPerformanceTests`](NonFunctionalPerformanceTests.cs) | 2 | виконувані NFR-тести: `PERF_001` (p95 < 1000 мс на 10 000 записів) та `SEC_001` (0 паролів у відкритому вигляді) |
| [`WaterfallDbSeederTests`](WaterfallDbSeederTests.cs) | 4 | ідемпотентне сідування початкових даних, геокешу та автоматичний апгрейд legacy SQLite схеми; без демо-даних (поза Development) — лише структура бази, без фейкових вакансій і тестових акаунтів |

---

## Фікстури та тестове середовище

- **In-Memory SQLite Fixture ([`SqliteTestFixture`](SqliteTestFixture.cs)):** на кожен клас або тестовий прогін створюється відкрите ізольоване з'єднання `DataSource=:memory:`. База створюється через `db.Database.EnsureCreated()`, що повністю відтворює реальні обмеження SQLite (зовнішні ключі, унікальні індекси, збереження типів), на відміну від InMemory-провайдера EF Core. Після завершення тестів з'єднання коректно закривається (`Dispose`).
- **Ізоляція стану (`ChangeTracker.Clear()`):** після підготовки даних у фазі Arrange трекер змін очищується, моделюючи поведінку нового HTTP-запиту в реальному застосунку.

---

## Граничні випадки (Edge Cases)

- **Зарплатні межі:** перевірка перетину діапазонів (`minSalary` та `maxSalary` для вакансій з односторонніми або двосторонніми межами), граничні умови рівності.
- **Обробка `null`:** пошук за вакансіями з відсутнім графіком (`Schedule == null`) або досвідом (`Experience == null`) не викликає `NullReferenceException`.
- **Порожні колекції:** передача порожнього списку ID у `GetSavedVacancyIdsAsync` повертає порожній `HashSet<Guid>` без виконання зайвих SQL-запитів.
- **Ізоляція користувачів:** користувач A ні за яких умов не може отримати чи видалити закладки користувача B через `ISavedVacancyRepository`.
- **Кирилиця:** вбудована в SQLite `lower()` переводить у нижній регістр лише латиницю, тож без [`SqliteUnicodeLowerInterceptor`](../../src/waterfall.Repository/Data/SqliteUnicodeLowerInterceptor.cs) «київ» не знаходив «Київ». Тест перевіряє пошук і всі текстові фільтри («РОЗРОБНИК», «київ», «тестування» тощо) і падає, якщо перехоплювач вимкнути.
- **Ідемпотентність та збереження метаданих:** при оновленні чи повторному `BulkUpsertAsync` первинні ідентифікатори `Id` та дати `FirstSeenAt` і `PublishedAt` лишаються незмінними.

---

## Як запустити

```bash
dotnet test tests/waterfall.Repository.Tests/waterfall.Repository.Tests.csproj -p:TargetFrameworks=net10.0 -p:TargetFramework=net10.0
```

---

## Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
| :--- | :--- | :--- | :--- |
| `tests/waterfall.Repository.Tests/` (тести, фікстура SQLite, документація) | Сидоренко Денис | Генерація коду тестів і фікстури SQLite, добір граничних випадків, рефакторинг під час рев'ю для видалення тавтологічних тестів | `dotnet test` (33 успішні тести), перевірка виконання запитів проти справжньої SQLite, самоперевірка автора |
| `SqliteUnicodeLowerInterceptor`, `ToLowerInvariant` у фільтрах `EfVacancyRepository`, тест `GetAllAsync_CyrillicFilters_IgnoreCase` | Саєнко Данило (на прохання команди) | Діагностика й виправлення пошуку кирилицею, генерація тесту | `dotnet test` (39 тестів); 6 випадків нового тесту падають без перехоплювача й проходять із ним; самоперевірка (§8) |
