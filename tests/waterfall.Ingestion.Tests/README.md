# Тести модуля збору вакансій (issue #62)

Модуль читає JSON-дампи вакансій, приводить їх до єдиного формату й зберігає в БД без дублів.

| Код | Що робить |
| :--- | :--- |
| [`src/api/Workers/JobParserService.cs`](../../src/api/Workers/JobParserService.cs) | фоновий сервіс: раз на `Interval` читає `*.json` з `data/raw` і розбирає дамп Djinni |
| [`src/waterfall.Services/VacancyIngestionService.cs`](../../src/waterfall.Services/VacancyIngestionService.cs) | нормалізація: назва, HTML-опис, зарплата, дата, навички, перевірка посилання (THR-03) |
| [`src/waterfall.Services/Interfaces/IVacancyIngestionService.cs`](../../src/waterfall.Services/Interfaces/IVacancyIngestionService.cs) | контракт сервісу, моделі `RawVacancy` і `IngestionResult` |
| `BulkUpsertAsync` у [`EfVacancyRepository.cs`](../../src/waterfall.Repository/Repositories/EfVacancyRepository.cs) | збереження за ключем «джерело + id на сайті» |

## Тести

46 тестових випадків у трьох класах:

| Клас | Випадків | Що перевіряє |
| :--- | :---: | :--- |
| [`VacancyIngestionServiceTests`](VacancyIngestionServiceTests.cs) | 33 | розбір зарплат і дат, очищення HTML, відсікання фішингових посилань, межі довжини полів, імпорт демо-дампу на SQLite |
| [`JobParserServiceTests`](JobParserServiceTests.cs) | 9 | розбір файлу дампу, життєвий цикл фонового сервісу: вимкнення, повтор за розкладом, штатна зупинка, зламаний файл чи відсутня папка не зупиняють роботу |
| [`BulkUpsertTests`](BulkUpsertTests.cs) | 4 | ключ оновлення: той самий id з іншого сайту, дубль в одному пакеті, порожній пакет, незмінність `Id` і дат першої появи |

### Фікстури, заглушки, шпигуни

| Що | Де | Навіщо |
| :--- | :--- | :--- |
| **Фікстура SQLite в пам'яті** | `BulkUpsertTests`, `SampleDump_IsSavedOnce_AndUpdatedOnRepeatedRun` | справжня реляційна БД, нова на кожен тест: перевіряємо реальні запити EF Core, а не підробку |
| **Фікстура тимчасового каталогу** | `JobParserServiceTests` | окрема папка з дампами на кожен тест; створюється в конструкторі, видаляється в `Dispose` |
| **Заглушка `RecordingIngestionService`** | `JobParserServiceTests` | підміняє сервіс нормалізації: парсер працює без БД, а тест бачить, які вакансії й скільки разів йому передали |
| **Шпигун `RecordingLogger`** | `JobParserServiceTests` | збирає записи логу: перевіряємо, що парсер повідомив про зламаний файл чи відсутню папку |
| **`HostingEnvironment`** | `JobParserServiceTests` | підміна оточення хоста, щоб відносний шлях до дампів рахувався від тимчасової папки |
| **Будівник `Raw()`** | `VacancyIngestionServiceTests` | валідна сира вакансія; тест змінює лише те, що перевіряє: `Raw() with { Title = "QA" }` |
| **Демо-дамп** [`data/raw/djinni-sample.json`](../../data/raw/djinni-sample.json) | `ParseDump_ReadsJobs`, `SampleDump_...` | «еталонний» набір: 12 записів, з них 10 валідних і 2 навмисно погані |

### Граничні випадки

- **Зарплата:** перевернута вилка `$6000-5000`, «до» / «від», погодинна ставка, «за домовленістю», відсутнє поле, число з пробілом `45 000`, валюта (USD, EUR, UAH).
- **Дата:** «щойно», «сьогодні», «вчора», «1 день», «3 дні», «5 годин», «10 хв» тому; `08.10.2026` не плутається з 10 серпня; ISO 8601; дата з майбутнього стає поточною; невідомий чи відсутній формат.
- **Посилання:** фішингові домени `djinni-careers.example.net`, `djinni.co.evil.example`, `fakedjinni.co`; `http://`; не URL; відсутнє посилання. Піддомен `jobs.djinni.co` приймається, трекінгові параметри відрізаються.
- **Текст:** `<script>` і `<style>` вирізаються разом із вмістом, HTML-сутності декодуються, абзаци й пункти списку стають рядками.
- **Довжина:** назва з 2 символів відхиляється, з 3 — приймається; id довше 100 і назва довше 200 символів обрізаються до розміру колонки; рівно 200 — без змін; порожні поля стають `null`, «Не вказано» чи «Інше».
- **Файли:** зламаний JSON, `null`, чужий `source`, дамп без `jobs`, відсутня папка; зламаний файл не зупиняє обробку інших.
- **Збереження:** повторний імпорт оновлює запис, а `Id`, дата публікації й дата першої появи не змінюються; той самий id з іншого сайту — окрема вакансія; дубль у межах одного дампу; порожній пакет.

Тест на `fakedjinni.co` і перевірка незмінної дати публікації з'явилися після практики «Зламай, потім відлагодь» (Lab 06): внесені туди помилки попередній набір тестів не помітив.

## Покриття

| | До Lab 06 | Зараз |
| :--- | :---: | :---: |
| Тестових випадків | 27 | 46 |
| Рядки | 97,5 % | 99,5 % |
| Гілки | 89 % | 99 % |

HTML-звіт: [`coverage-report/index.html`](coverage-report/index.html) (згенеровано ReportGenerator з даних coverlet). У звіті 99,4 % рядків, 98,9 % гілок і 100 % методів. `BulkUpsertAsync` у звіт не входить, бо цей метод лежить у класі репозиторію Дениса разом з чужими методами; окремо він покритий повністю (19 з 19 рядків).

Єдиний непокритий рядок — вихід із циклу в `JobParserService.ExecuteAsync`, коли `PeriodicTimer.WaitForNextTickAsync` повертає `false`. Так буває, лише якщо таймер знищити під час очікування, а наш код цього не робить. Сервіс зупиняється через скасування, і цей шлях покриває тест `Parser_RereadsDirectoryOnSchedule`.

## Як запустити

Тести (прапорці `-p:...` потрібні лише на Windows без MAUI-воркладів):

```bash
dotnet test tests/waterfall.Ingestion.Tests/waterfall.Ingestion.Tests.csproj -p:TargetFrameworks=net10.0 -p:TargetFramework=net10.0
```

Перегенерувати звіт покриття. Спершу зібрати дані:

```bash
dotnet test tests/waterfall.Ingestion.Tests/waterfall.Ingestion.Tests.csproj -p:TargetFrameworks=net10.0 -p:TargetFramework=net10.0 --collect "XPlat Code Coverage" --results-directory TestResults/ingestion
```

Потім побудувати HTML лише для класів модуля:

```bash
dotnet tool restore && dotnet tool run reportgenerator "-reports:TestResults/ingestion/**/coverage.cobertura.xml" "-targetdir:tests/waterfall.Ingestion.Tests/coverage-report" "-reporttypes:Html;TextSummary" "-classfilters:+Waterfall.Api.Workers.*;+Waterfall.Services.VacancyIngestionService;+Waterfall.Services.Interfaces.RawVacancy;+Waterfall.Services.Interfaces.IngestionResult"
```

Каталог `TestResults/` у `.gitignore`. Після генерації абсолютні шляхи до файлів у HTML варто замінити на відносні.

## Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
| :--- | :--- | :--- | :--- |
| `tests/waterfall.Ingestion.Tests/` (тести, фікстури, заглушки) | Саєнко Данило | Генерація тестів, фікстур і заглушок, добір граничних випадків | `dotnet test` (46 випадків, тричі поспіль для тестів із таймером); тести з практики падають на внесених помилках; самоперевірка (§8) |
| `coverage-report/`, цей README | Саєнко Данило | Генерація звіту ReportGenerator, чернетка документа | Звірка цифр зі звітом coverlet, перегляд HTML-звіту |
