# Тести рівня сховища даних (waterfall.Repository.Tests)

Модуль відповідає за доступ до реляційної бази даних SQLite (Entity Framework Core) для каталогу вакансій, збережених вакансій («лайків» / закладок) та облікових записів користувачів згідно з принципами Clean Architecture.

| Компонент сховища | Файл реалізації | Що робить |
| :--- | :--- | :--- |
| `EfVacancyRepository` | [`src/waterfall.Repository/Repositories/EfVacancyRepository.cs`](../../src/waterfall.Repository/Repositories/EfVacancyRepository.cs) | CRUD вакансій, повнотекстовий пошук за 4 полями, багатокритеріальна фільтрація, сортування за 5 полями, пагінація, пакетний `BulkUpsertAsync` |
| `EfSavedVacancyRepository` | [`src/waterfall.Repository/Repositories/EfSavedVacancyRepository.cs`](../../src/waterfall.Repository/Repositories/EfSavedVacancyRepository.cs) | збереження («лайки») вакансій, отримання списку з навігаційними властивостями, перевірка існування, видалення за `Id` чи `VacancyId`, масова перевірка `GetSavedVacancyIdsAsync` |
| `EfUserRepository` | [`src/waterfall.Repository/Repositories/EfUserRepository.cs`](../../src/waterfall.Repository/Repositories/EfUserRepository.cs) | пошук користувача за нормалізованим email, збереження та оновлення облікових записів |

---

## Опис набору тестів

33 сфокусовані тестові випадки у 2 класах тестів (тривіальні обгортки CRUD над EF Core вилучено під час рев'ю для концентрації на реальній бізнес- та запитовій логіці):

| Клас тесту | Кількість випадків | Що перевіряє |
| :--- | :---: | :--- |
| [`EfVacancyRepositoryTests`](EfVacancyRepositoryTests.cs) | 20 | `GetAllAsync`: сортування desc/asc за 5 полями (заголовок, зарплата min/max, дата появи); фільтри пошуку за заголовком, описом, компанією та JSON-навичками; фільтри категорії (з нормалізацією регістру та пробілів), досвіду, локації, графіку, джерела; межі вилки зарплат; конкатенація активних та архівних записів; віконна пагінація; пакетний `BulkUpsertAsync` зі збереженням первинних ключів і дат |
| [`EfSavedVacancyRepositoryTests`](EfSavedVacancyRepositoryTests.cs) | 13 | отримання активних та архівних збережених вакансій, ізоляція користувачів (чужі закладки не відображаються), отримання за `Id` або `VacancyId`, перевірка прав доступу при читанні й видаленні, автогенерація `Guid` і часової мітки `SavedAt`, перевірка зв'язку з `VacancyDB`, метод `ExistsForVacancyAsync` (включно з `userId == null`), пакетне витягування збережених `GetSavedVacancyIdsAsync` з обробкою порожнього входу |

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
