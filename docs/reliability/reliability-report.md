# 🛡️ Командний звіт про надійність (Reliability Report)
**Проєкт**: Waterfall — Career Aggregator Platform  
**Лабораторна робота 9**: Надійність та "Resilience Scavenger Hunt"  
**Команда**: Денис Сидоренко, Дмитро, Данило  

---

## 1. 📊 Таблиця всіх знайдених проблем надійності

У ході проведення активності **Resilience Scavenger Hunt** у кодовій базі проєкту було виявлено **8 реальних проблем із надійністю**, які покривають усі категорії ризиків (A–E).

| ID | Категорія | Компонент / Файл | Несправність (Fault) | Помилка (Error) | Відмова (Failure) | Критичність | Статус |
|---|---|---|---|---|---|---|---|
| **FIND-01** | **A. Обробка помилок** / **B. Залежності** | `SiteCrawler.cs` (Workers) | Відсутній `try/catch` навколо `http.GetAsync(robotsUrl)` | Необроблений `HttpRequestException` або `TaskCanceledException` обриває `CrawlAsync` | Краулер падає; нові вакансії з сайту (Djinni, Work.ua) не збираються | **Висока** | ✅ **Виправлено** |
| **FIND-02** | **A. Обробка помилок** / **D. Мовчазні збої** | `PhotonAddressAutocompleteService.cs` (Client) | Порожній `catch {}` без типу; перехоплює `OperationCanceledException` | Потік виконання продовжує фоновий запит до Вікіпедії замість негайного виходу | Зайвий мережевий трафік, розряд акумулятора, затримки автокомпліту | **Середня** | ⏳ Відкрито (Backlog) |
| **FIND-03** | **B. Залежності** / **E. Блокування** | `GeoLocationService.cs` (Services) | Виклик зовнішнього Photon API під глобальним семафором без таймауту черги | Черга потоків ASP.NET блокується в очікуванні повільного зовнішнього API | Ендпоінт геокодування зависає на 30+ с, thread pool starvation, HTTP 504 | **Висока** | ⏳ Відкрито (Backlog) |
| **FIND-04** | **B. Залежності** / **E. Блокування** | `JobParserService.cs` (Workers) | `File.ReadAllTextAsync` без ліміту розміру та режиму `FileShare.Read` | Виникнення `IOException` при паралельному записі або виділення LOH | Збій імпорту вакансій, сплески пам'яті до ризику OutOfMemory | **Середня** | ⏳ Відкрито (Backlog) |
| **FIND-05** | **C. Валідація вхідних даних** | `GeoController.cs` & `GeoLocationService.cs` | Відсутність перевірки моделі та фізичних меж координат ([-90..90], [-180..180], NaN) | Збереження некоректних координат у таблиці геокешу (Cache Poisoning) | Мапа у клієнті падає з помилкою або відображає маркери в океані; спотворення розрахунку відстаней | **Висока** | ✅ **Виправлено** |
| **FIND-06** | **C. Валідація вхідних даних** | `SavedVacancyService.cs` (Services) | Відсутність guard clauses на `null` та `Guid.Empty` | Звернення до властивостей `null` або порушення Foreign Key у базі даних | Клієнт отримує неінформативну відповідь 500 Internal Server Error | **Середня** | ✅ **Виправлено** |
| **FIND-07** | **D. Мовчазні збої** | `VacancyService.cs` (Services) | Мовчазне ковтання помилок десеріалізації JSON у `ParseSkills` без логування | Пошкоджений JSON у базі мовчки повертає порожній список `[]` | Вакансії непомітно втрачають навички, пошук не знаходить релевантні посади | **Середня** | ✅ **Виправлено** |
| **FIND-08** | **E. Блокування та пам'ять** | `EfGeoLocationCacheRepository.cs` (Repository) | Завантаження всієї таблиці геокешу в пам'ять процесу (`ToListAsync`) при промаху | Необмежене виділення RAM та навантаження на диск СУБД | Затримки запитів, ризик OOM при розростанні кешу адрес | **Висока** | ⏳ Відкрито (Backlog) |

---

## 2. 🛠️ Опис виправлень та застосованих патернів надійності

Для виправлення в коді реалізовано 4 ключові проблеми, обрані на Етапі 3 (FIND-01, FIND-05, FIND-06, FIND-07).

### 2.1. FIND-01: Стійке завантаження `robots.txt` у `SiteCrawler.cs`
* **Застосовані патерни**:
  * **Безпечна відмова (Fail-Safe)**: при мережевому збої краулер не аварійно зупиняється.
  * **Специфічний перехоплювач винятків**: `catch (Exception ex) when (IsSiteFailure(ex, ct))` фільтрує мережеві збої та таймаути.
  * **Контрольована пауза (Backoff)**: виклик `PauseAfterRefusal` переводить джерело у режим очікування для запобігання перевантаженню.
  * **Структуроване логування**: подія фіксується у логах через `_logger.LogWarning`.

#### Код До / Після:
```csharp
// ДО: відсутній захист навколо мережевого виклику
Uri robotsUrl = new(_site.Home, "/robots.txt");
using HttpResponseMessage response = await http.GetAsync(robotsUrl, ct); // Виняток падав назовні
if (response.IsSuccessStatusCode)
{
    return RobotsTxt.Parse(await response.Content.ReadAsStringAsync(ct));
}
```

```csharp
// ПІСЛЯ: безпечна відмова, перехоплення винятків транспорту та пауза сайту
Uri robotsUrl = new(_site.Home, "/robots.txt");
HttpResponseMessage response;
try
{
    response = await http.GetAsync(robotsUrl, ct);
}
catch (Exception ex) when (IsSiteFailure(ex, ct))
{
    _logger.LogWarning("Не вдалося завантажити robots.txt з {Url} ({Error}). Пропускаємо сайт для запобігання перевантаженню.", robotsUrl, ex.Message);
    PauseAfterRefusal(null);
    return null;
}

using (response)
{
    if (response.IsSuccessStatusCode)
    {
        return RobotsTxt.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
    {
        _logger.LogWarning("{Url} недоступний (код {Status}), тож сайт пропускаємо.", robotsUrl, (int)response.StatusCode);
        PauseAfterRefusal(response.Headers.RetryAfter);
        return null;
    }

    return RobotsTxt.AllowAll;
}
```

---

### 2.2. FIND-05: Валідація координат та захист від отруєння кешу (Cache Poisoning)
* **Застосовані патерни**:
  * **Швидка відмова (Fail-Fast)** на рівні контролера: перевірка `request == null || !ModelState.IsValid` з поверненням HTTP 400 Bad Request.
  * **Захисна фільтрація (Defensive Validation)** на рівні сервісу: перевірка фізичних меж координат Землі:
    `Latitude` у діапазоні [-90.0, +90.0], `Longitude` у діапазоні [-180.0, +180.0].
  * **Санітизація чисел з плаваючою крапкою**: явна перевірка на `!double.IsNaN` та `!double.IsInfinity`.
  * **Обмеження розміру пакета**: обмеження батчу через `.Take(500)` для захисту СУБД від атак відмови в обслуговуванні (DoS).

#### Код До / Після:
```csharp
// ДО: GeoController.cs
[HttpPost("sync")]
public async Task<ActionResult<GeoSyncResponseDto>> SyncCoordinates([FromBody] GeoSyncRequestDto request, CancellationToken ct)
{
    GeoSyncResponseDto response = await _geoService.SyncCacheAsync(request, ct);
    return Ok(response);
}
```

```csharp
// ПІСЛЯ: GeoController.cs
[HttpPost("sync")]
public async Task<ActionResult<GeoSyncResponseDto>> SyncCoordinates([FromBody] GeoSyncRequestDto? request, CancellationToken ct)
{
    if (request == null || !ModelState.IsValid)
    {
        return ErrorBadRequest("Тіло запиту синхронізації геокешу не може бути порожнім або невалідним.");
    }

    GeoSyncResponseDto response = await _geoService.SyncCacheAsync(request, ct);
    return Ok(response);
}
```

У `GeoLocationService.cs`:
```csharp
// ДО: координати записувалися без перевірки діапазонів
if (!string.IsNullOrWhiteSpace(item.Address))
{
    await _cacheRepository.SetCoordinatesAsync(item.Address, item.Latitude, item.Longitude, ct);
}
```

```csharp
// ПІСЛЯ: сувора фільтрація та ліміт пакета (500 елементів)
ArgumentNullException.ThrowIfNull(request);

if (request.ClientItems != null && request.ClientItems.Count > 0)
{
    IEnumerable<GeoSyncItemDto> batch = request.ClientItems.Take(500);

    foreach (var item in batch)
    {
        if (item != null &&
            !string.IsNullOrWhiteSpace(item.Address) &&
            !double.IsNaN(item.Latitude) && !double.IsInfinity(item.Latitude) &&
            !double.IsNaN(item.Longitude) && !double.IsInfinity(item.Longitude) &&
            item.Latitude >= -90.0 && item.Latitude <= 90.0 &&
            item.Longitude >= -180.0 && item.Longitude <= 180.0)
        {
            await _cacheRepository.SetCoordinatesAsync(item.Address, item.Latitude, item.Longitude, ct);
        }
    }
}
```

---

### 2.3. FIND-06: Захисні перевірки (Guard Clauses) у `SavedVacancyService.cs`
* **Застосовані патерни**:
  * **Швидка відмова (Fail-Fast)**: негайний викид типізованих `ArgumentNullException` та `ArgumentException` на початку виконання методу.
  * Запобігання переходу об'єкта в некоректний внутрішній стан та усунення неінформативних HTTP 500 NRE.

#### Код До / Після:
```csharp
// ДО:
public async Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, Guid userId, CancellationToken ct = default)
{
    SavedVacancyDB entity = new SavedVacancyDB
    {
        Id = Guid.NewGuid(),
        VacancyId = request.VacancyId, // NullReferenceException якщо request == null!
        UserId = userId,
...
```

```csharp
// ПІСЛЯ:
public async Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, Guid userId, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(request);
    if (request.VacancyId == Guid.Empty)
    {
        throw new ArgumentException("Ідентифікатор вакансії не може бути порожнім (Guid.Empty).", nameof(request));
    }
    if (userId == Guid.Empty)
    {
        throw new ArgumentException("Ідентифікатор користувача не може бути порожнім (Guid.Empty).", nameof(userId));
    }

    SavedVacancyDB entity = new SavedVacancyDB
    {
        Id = Guid.NewGuid(),
        VacancyId = request.VacancyId,
        UserId = userId,
...
```

---

### 2.4. FIND-07: Усунення мовчазного збою в `VacancyService.ParseSkills`
* **Застосовані патерни**:
  * **Звуження catch-блоку**: замість абстрактного `catch` перехоплюється виключно `JsonException`.
  * **Спостережуваність (Observability)**: факт пошкодження даних логується на рівні Warning з фіксацією проблемного JSON-рядка.
  * **Безпечне значення за замовчуванням (Safe Fallback)**: повертається порожній масив `Array.Empty<string>()`, що дозволяє решті даних вакансії коректно відображатися.

#### Код До / Після:
```csharp
// ДО: мовчазне проковтування будь-яких винятків
internal static IReadOnlyList<string> ParseSkills(string? json)
{
    if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
    try
    {
        return JsonSerializer.Deserialize<List<string>>(json) ?? (IReadOnlyList<string>)Array.Empty<string>();
    }
    catch
    {
        return Array.Empty<string>(); // Помилка зникала без сліду
    }
}
```

```csharp
// ПІСЛЯ: типізоване перехоплення та структуроване логування
public static IReadOnlyList<string> ParseSkills(string? json, ILogger? logger = null)
{
    if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
    try
    {
        return JsonSerializer.Deserialize<List<string>>(json) ?? (IReadOnlyList<string>)Array.Empty<string>();
    }
    catch (JsonException ex)
    {
        logger?.LogWarning(ex, "Помилка десеріалізації JSON навичок вакансії: '{Json}'", json);
        return Array.Empty<string>();
    }
}
```

---

## 3. 🔄 Режими відмов і стратегії відновлення (Failure Modes & Recovery)

| Проблема | Обрана стратегія | Що бачить користувач під час збою? | Як система повертається до норми (Recovery)? |
|---|---|---|---|
| **FIND-01** (`robots.txt`) | **Безпечна відмова (Fail-Safe)** | Користувач бачить раніше збережені вакансії без помилок; нові вакансії тимчасово не додаються. | Автоматичний повтор: фоновий воркер встановлює паузу для сайту через `PauseAfterRefusal` і повторює спробу в наступній ітерації циклу. |
| **FIND-05** (Координати) | **Швидка відмова (Fail-Fast)** | Клієнт отримує статус HTTP 400 Bad Request зі зрозумілим описом проблеми: `"Тіло запиту синхронізації геокешу не може бути порожнім або невалідним."` | Клієнтський додаток виправляє вхідні координати або пропускає невалідні записи офлайн-кешу; серверний кеш залишається чистим. |
| **FIND-06** (Saved Vacancy Guards) | **Швидка відмова (Fail-Fast)** | Зрозуміла помилка валідації 400 Bad Request із зазначенням конкретного некоректного поля (`VacancyId` чи `UserId`). | Користувач/клієнт повторює запит з валідним ідентифікатором; жодних пошкоджених записів у базі даних. |
| **FIND-07** (`ParseSkills`) | **Безпечна відмова (Safe Fallback with Logging)** | Картка вакансії відкривається без навичок, але решта даних (заголовок, опис, компанія, зарплата) доступні. | Самозцілення: інцидент зафіксовано в журналі; інженер або повторний парсер оновлює запис вакансії з коректним JSON. |

---

## 4. ⏳ Залишені відкритими проблеми (Backlog)

1. **FIND-02 (`PhotonAddressAutocompleteService.cs` — мовчазний catch на клієнті)**:
   * *Чому відкладено*: Проблема локалізована в клієнтському коді (.NET MAUI) і не призводить до збою бекенду або спотворення даних.
   * *План усунення*: У наступному релізі клієнта замінити `catch` на типізований `catch (HttpRequestException ex)` та додати `catch (OperationCanceledException) { throw; }`.
2. **FIND-03 (`GeoLocationService.cs` — семафор без таймауту черги)**:
   * *Чому відкладено*: Поточне навантаження на геокодування не створює тривалих черг до семафора, а запити кешуються в базі даних.
   * *План усунення*: Додати таймаут очікування `WaitAsync(timeout)` та налаштувати автоматичний fallback при черзі.
3. **FIND-04 (`JobParserService.cs` — зчитування дампів без потокової обробки)**:
   * *Чому відкладено*: Поточні файли дампів займають не більше 2–5 МБ і зчитуються локально в ізольованому процесі краулера.
   * *План усунення*: Перевести парсинг на `FileStream` з `System.Text.Json.Utf8JsonReader` для потокового читання великих JSON без алокацій у LOH.
4. **FIND-08 (`EfGeoLocationCacheRepository.cs` — повне сканування таблиці кешу)**:
   * *Чому відкладено*: Розмір кешу на поточному етапі не перевищує кількох сотень записів, запити виконуються менш ніж за 10 мс.
   * *План усунення*: Додати пагінацію або перейти на SQL-пошук за префіксом адреси (`EF.Functions.Like`) замість завантаження всієї сутності в оперативну пам'ять.

---

## 5. 🏆 Бонуси (+0,25 бала)

### Бонус: Тести надійності як обов'язкова перевірка PR
* **Що зроблено**:
  * Тести надійності додано окремим кроком до `test` job у воркфлоу `CI`:
```yml
      - name: Reliability tests
        run: dotnet test tests/reliability/waterfall.Reliability.Tests.csproj -c Release --nologo --collect:"XPlat Code Coverage" --results-directory ./TestResults/Reliability
```
  * `test` додана як обов'язкова перевірка у branch ruleset.

* Прогін в якому доданий крок пройшов: https://github.com/ukma-fin-ssdm-2026/waterfall/actions/runs/42507166594/job/128812180756

### Бонус: Перевірка здоров'я (Health Checks) сервісу з ін'єкцією збою (§3.4) — +0,25 бала
* **Що зроблено**:
  * Створено ендпоінти перевірки здоров'я: `/health` (готовність до обробки трафіку, readiness) та `/health/live` (працездатність процесу, liveness).
  * Реалізовано перевірки для критичних залежностей:
    * `DatabaseHealthCheck` (`src/api/Health/DatabaseHealthCheck.cs`): перевіряє доступність реляційної СУБД через `CanConnectAsync` з таймаутом 3 секунди.
    * `PhotonGeocodingHealthCheck` (`src/api/Health/PhotonGeocodingHealthCheck.cs`): перевіряє доступність зовнішнього сервісу геокодування.
  * Додано `HealthCheckResponseWriter` для стандартизованої JSON-відповіді з деталізацією стану кожної підсистеми.
* **Приклад відповіді `/health`**:
  ```json
  {
    "status": "Healthy",
    "totalDuration": "00:00:00.015",
    "entries": {
      "database": {
        "status": "Healthy",
        "description": "Підключення до бази даних активне та відповідає.",
        "duration": "00:00:00.005"
      },
      "geocoding_api": {
        "status": "Healthy",
        "description": "Зовнішній Photon API доступний.",
        "duration": "00:00:00.010"
      }
    }
  }
  ```
* **Доказ у тестах з ін'єкцією збою**:
  * Клас `tests/reliability/HealthCheckTests.cs` (8 тестів):
    * `DatabaseHealthCheck_ReturnsHealthy_WhenDatabaseConnectionIsAvailable` підтверджує статус `Healthy` (HTTP 200) у нормальному стані.
    * `DatabaseHealthCheck_ReturnsUnhealthy_WhenDatabaseConnectionFails_FaultInjection` ін'єктує збій (пошкоджене/закрите підключення до БД) і доводить, що перевірка повертає статус `Unhealthy` (HTTP 503) із детальним описом збою.
    * `DatabaseHealthCheck_ReturnsUnhealthy_WhenOperationIsCancelled_ThrowsException` перевіряє поведінку при таймауті/перериванні операції перевірки.
    * `PhotonGeocodingHealthCheck_ReturnsHealthy_WhenPhotonApiIsReachable` підтверджує `Healthy` при доступності геосервісу.
    * `PhotonGeocodingHealthCheck_ReturnsDegraded_WhenRateLimited_429` підтверджує перехід у стан `Degraded` при отриманні коду 429.
    * `PhotonGeocodingHealthCheck_ReturnsUnhealthy_WhenApiReturnsServerError_500` перевіряє перехід у `Unhealthy` при коді 500.
    * `PhotonGeocodingHealthCheck_ReturnsUnhealthy_WhenPhotonApiFails_FaultInjection` доводить перехід у стан `Unhealthy` при повному обриві мережі (мережевий виняток).
    * `HealthCheckResponseWriter_WritesExpectedJsonStructure_WhenReportProvided` перевіряє коректну серіалізацію структури JSON і статусів у відповідь клієнту.

