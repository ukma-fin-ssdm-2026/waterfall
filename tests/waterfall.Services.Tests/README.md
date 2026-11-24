# Тести сервісного рівня та кешування (waterfall.Services.Tests)

Модуль реалізує ключову бізнес-логіку системи Waterfall:
- Патерн **Cache-Aside** на базі `IMemoryCache` (PERF-001: швидкість відгуку каталогу < 1 с)
- Режим відмовостійкості **Stale-Cache** (REL-001: повернення застарілого кешу при тимчасовій недоступності БД)
- Ізоляція персонального стану «Обраного» (`IsSaved`) між користувачами без витоку в публічний кеш
- Управління закладками користувача (`SavedVacancyService`) та автоматична інвалідація пов'язаного кешу

| Сервіс | Файл реалізації | Що робить |
| :--- | :--- | :--- |
| `VacancyCacheService` | [`src/waterfall.Services/VacancyCacheService.cs`](../../src/waterfall.Services/VacancyCacheService.cs) | In-Memory кешування сторінок каталогу з токенами скасування, кеш окремих вакансій, збереження еталонного каталогу для Stale-Cache режиму, збір метрик (Hits/Misses) |
| `VacancyService` | [`src/waterfall.Services/VacancyService.cs`](../../src/waterfall.Services/VacancyService.cs) | оркестрація доступу до вакансій: Cache HIT/MISS, збагачення прапорцями `IsSaved`, Stale-Cache fallback при падінні БД, створення, оновлення та видалення вакансій, захищений мапінг DTO |
| `SavedVacancyService` | [`src/waterfall.Services/SavedVacancyService.cs`](../../src/waterfall.Services/SavedVacancyService.cs) | бізнес-логіка додавання та видалення закладок з обов'язковою інвалідацією кешу відповідної вакансії |

---

## Опис набору тестів

203 тестових випадків у 21 класі (202 виконуються у CI + 1 опційний `LlmLiveSmokeTests` за `WATERFALL_LLM_LIVE=1`; актуалізовано 22.11.2026):

| Клас тесту | Кількість випадків | Що перевіряє |
| :--- | :---: | :--- |
| [`VacancyServiceTests`](VacancyServiceTests.cs) | 23 | Cache-Aside HIT/MISS, синхронізація з клієнтом лише з `VacancySync:AcceptClientVacancies = true` (за замовчуванням вакансії клієнта ігноруються), Stale-Cache fallback (REL-001), ізоляція `IsSaved`/`IsViewed`, стійкість `ParseSkills`, `SyncVacanciesAsync` (двосторонній обмін з інвалідацією кешу) |
| [`AIConsultantServiceTests`](AIConsultantServiceTests.cs) | 22 | витяг фільтра, збереження контексту діалогу (FR-009), релаксація критеріїв, заборона вигаданих вакансій (FUNC-001), обробка недоступності LLM |
| [`FallbackChatClientTests`](FallbackChatClientTests.cs) | 18 | ланцюжок резервування LLM-провайдерів, таймаути спроби/загальний, поведінка на 429/5xx vs 400/401/403 |
| [`SensitiveTextMaskerTests`](SensitiveTextMaskerTests.cs) | 15 | маскування email і телефонів перед відправкою до LLM (THR-06) |
| [`LlmOptionsValidatorTests`](LlmOptionsValidatorTests.cs) | 12 | валідація конфігурації `Llm` на старті (ADR-004) |
| [`UserStatsServiceTests`](UserStatsServiceTests.cs) | 12 | підрахунок особистої статистики активності, історії пошуків і переглядів (FR-007) |
| [`VacancyAnalysisServiceTests`](VacancyAnalysisServiceTests.cs) | 10 | ШІ-аналіз 1–3 обраних вакансій за профілем користувача (FR-010) |
| [`VacancyFilterParametersTests`](VacancyFilterParametersTests.cs) | 10 | валідація параметрів фільтрації та пагінації каталогу |
| [`GeoLocationServiceTests`](GeoLocationServiceTests.cs) | 9 | геокодування (кеш → Photon → Wikipedia), санітизація й синхронізація геокешу |
| [`LlmGatewayHttpTests`](LlmGatewayHttpTests.cs) | 9 | HTTP-взаємодія офіційних SDK у `LlmClientFactory` |
| [`PreferenceProfileBuilderTests`](PreferenceProfileBuilderTests.cs) | 9 | побудова профілю вподобань шукача зі збережених вакансій |
| [`SalaryRangeParserTests`](SalaryRangeParserTests.cs) | 9 | розбір текстових діапазонів зарплат і перевірка меж |
| [`VacancyCacheServiceTests`](VacancyCacheServiceTests.cs) | 9 | реальний `IMemoryCache`: Cache-Aside, Stale-Cache, каскадна інвалідація, метрики |
| [`SavedVacancyServiceTests`](SavedVacancyServiceTests.cs) | 8 | збереження/видалення закладок, guard clauses (FIND-06), інвалідація кешу |
| [`RecommendationServiceTests`](RecommendationServiceTests.cs) | 6 | ранжування персоналізованих рекомендацій із виключенням збережених (FR-006) |
| [`RelevanceScorerTests`](RelevanceScorerTests.cs) | 6 | обчислення релевантності вакансій профілю користувача |
| [`VacancyTextTokenizerTests`](VacancyTextTokenizerTests.cs) | 4 | токенізація текстів вакансій для скорингу |
| [`AnalyticsServiceTests`](AnalyticsServiceTests.cs) | 3 | агрегація ринкової аналітики (зарплати, технології, частки категорій) |
| [`LlmClientProviderTests`](LlmClientProviderTests.cs) | 3 | розв'язання клієнтів за ролями `Extract`/`Chat`/`Analyze` |
| [`UserProfileServiceTests`](UserProfileServiceTests.cs) | 3 | читання/оновлення адреси користувача |
| [`LlmLiveSmokeTests`](LlmLiveSmokeTests.cs) | 1 (skip) | опційний живий димовий тест реальних LLM-провайдерів (`WATERFALL_LLM_LIVE=1`) |

---

## Фікстури та тестові дублери

- **Моки репозиторіїв та дублери ([`RepositoryMocks.cs`](RepositoryMocks.cs)):**
  - `FakeVacancyRepository`: моделює повноцінне сховище в пам'яті з підтримкою пагінації та прапорцем `ShouldThrowOnGetAll` для емуляції падіння БД і перевірки вимоги REL-001.
  - `FakeSavedVacancyRepository`: відтворює збереження закладок користувачів та швидку фільтрацію за `UserId`.
  - `FakeVacancyCacheService`: шпигун і заглушка кешу для відстеження викликів `InvalidateCatalogAsync` та `InvalidateVacancyAsync`.
  - `FakeLogger<T>`: збирає записи журналу для верифікації попереджень про використання Stale-Cache та діагностичних повідомлень HIT/MISS.
- **Реальний `IMemoryCache`:** у тестах `VacancyCacheServiceTests` використовується справжня реалізація `Microsoft.Extensions.Caching.Memory.MemoryCache` для перевірки роботи механізмів `CancellationChangeToken` та витіснення записів.

---

## Граничні випадки (Edge Cases)

- **Збій бази даних (REL-001):** підтверджено повернення застарілого знімка каталогу без переривання обслуговування користувачів.
- **Витік персональних даних:** персоналізовані результати користувачів із прапорцем `IsSaved = true` ні за яких умов не зберігаються у спільному публічному кеші.
- **Пошкоджений або некоректний JSON навичок:** перевірено стійкість методу `ParseSkills` до `null`, порожніх рядків та синтаксично зламаного JSON (наприклад, `{[`). Помилка перехоплюється, повертається безпечний порожній список.
- **Невідоме джерело вакансії:** нові джерела парсингу безпечно транслюються в `JobSource.Unknown`.

---

## Як запустити

```bash
dotnet test tests/waterfall.Services.Tests/waterfall.Services.Tests.csproj -p:TargetFrameworks=net10.0 -p:TargetFramework=net10.0
```

---

## Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
| :--- | :--- | :--- | :--- |
| `tests/waterfall.Services.Tests/` (тести, тестові дублери, документація) | Сидоренко Денис | Генерація коду тестів і дублерів, налаштування сценаріїв Stale-Cache та Cache-Aside, оптимізація під час рев'ю | `dotnet test` (35 успішних тестів), перевірка обробки збоїв БД, самоперевірка автора |
