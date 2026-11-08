## 🧭 Етап 1: Знайдені ризики за категоріями (A–E)

У ході аналізу кодової бази було виявлено **8 реальних проблем із надійністю**, які покривають усі категорії (A–E), щонайменше по одній знахідці на категорію.

| ID | Категорія | Компонент / Файл | Короткий опис проблеми |
|---|---|---|---|
| **FIND-01** | **A. Обробка помилок** | `SiteCrawler.cs` (Workers) | Відсутній `try/catch` навколо HTTP-запиту завантаження `robots.txt` |
| **FIND-02** | **A. Обробка помилок** | `PhotonAddressAutocompleteService.cs` (Client) | Порожній catch-блок без типу (`catch {}`), що ковтає скасування операції |
| **FIND-03** | **B. Зовнішні залежності** | `GeoLocationService.cs` (Services) | Виклик зовнішнього HTTP API Photon під глобальним семафором без тайм-ауту черги |
| **FIND-04** | **B. Зовнішні залежності** | `JobParserService.cs` (Workers) | Блокуюче зчитування JSON-файлів дампів через `File.ReadAllTextAsync` без ліміту розміру та обробки конфліктів блокування |
| **FIND-05** | **C. Валідація вхідних даних** | `GeoController.cs` & `GeoLocationService.cs` | Відсутність перевірки моделі запиту та відсутність граничних перевірок геокоординат (`Latitude`/`Longitude`, NaN, нескінченність) |
| **FIND-06** | **C. Валідація вхідних даних** | `SavedVacancyService.cs` (Services) | Відсутність захисних перевірок (guard clauses) на `null` та `Guid.Empty` при збереженні вакансії |
| **FIND-07** | **D. Потенційні мовчазні збої** | `VacancyService.cs` (Services) | Мовчазне проковтування помилок десеріалізації JSON у `ParseSkills` без логування |
| **FIND-08** | **E. Продуктивність і блокування** | `EfGeoLocationCacheRepository.cs` (Repository) | Завантаження всієї таблиці геокешу в пам'ять процесу (`ToListAsync`) при промаху точного збігу адреси |

---

## 🔍 Детальний опис знахідок (Етап 1)

### FIND-01: Відсутній `try/catch` навколо завантаження `robots.txt`
* **Категорія**: `A. Обробка помилок` / `B. Зовнішні залежності`
* **Файл**: `src/api/Workers/SiteCrawler.cs` (рядки 345–362)
* **Фрагмент коду**:
  ```csharp
  private async Task<RobotsTxt?> LoadRobotsAsync(HttpClient http, CancellationToken ct)
  {
      Uri robotsUrl = new(_site.Home, "/robots.txt");
      using HttpResponseMessage response = await http.GetAsync(robotsUrl, ct); // Немає try/catch!
      if (response.IsSuccessStatusCode)
      {
          return RobotsTxt.Parse(await response.Content.ReadAsStringAsync(ct));
      }
      ...
  }
  ```
* **Чому небезпечно**: Мережевий збій (DNS error, connection reset, timeout) при завантаженні `robots.txt` викидає `HttpRequestException` або `TaskCanceledException`. Оскільки у `CrawlAsync` цей виклик не обгорнуто в `try/catch`, виняток вилітає назовні, зриваючи весь обхід для поточного сайту і викликаючи аварійне падіння фонового завдання без ввімкнення механізму паузи/експоненційного відкладання.
* **Потенційний вплив**: Повна зупинка парсингу свіжих вакансій з відповідного джерела (Djinni, Work.ua або DOU) при найменшому збої зв'язку або блокуванні хоста.

---

### FIND-02: Порожній `catch` без типу, що ковтає скасування операції
* **Категорія**: `A. Обробка помилок` / `D. Потенційні мовчазні збої`
* **Файл**: `src/waterfall.Client/Services/PhotonAddressAutocompleteService.cs` (рядки 46–57, 60–86)
* **Фрагмент коду**:
  ```csharp
  try
  {
      List<string> photonResults = await QueryPhotonAsync(cleanQuery, ct);
      if (photonResults.Count > 0) return photonResults;
  }
  catch
  {
      // Fallback нижче — ковтає абсолютно ВСЕ, навіть OperationCanceledException!
  }
  ```
* **Чому небезпечно**: Конструкція `catch {}` перехоплює будь-який виняток, включаючи `OperationCanceledException` та фатальні помилки пам'яті. Коли користувач змінює введений текст і старий запит скасовується через `CancellationToken`, клієнт ігнорує скасування і робить другий дорогий мережевий виклик до Вікіпедії, марно витрачаючи трафік і батарею мобільного пристрою.
* **Потенційний вплив**: Спустошення пулу з'єднань, зависання інтерфейсу користувача під час швидкого введення тексту адреси, неможливість зупинити фонові HTTP-запити.

---

### FIND-03: Зовнішній виклик під глобальним статичним SemaphoreSlim без тайм-ауту
* **Категорія**: `B. Зовнішні залежності` / `E. Продуктивність і блокування`
* **Файл**: `src/waterfall.Services/GeoLocationService.cs` (рядки 20, 113–127)
* **Фрагмент коду**:
  ```csharp
  private static readonly SemaphoreSlim PhotonSemaphore = new(1, 1);
  ...
  await PhotonSemaphore.WaitAsync(ct); // Немає обмеження тайм-ауту очікування семафора!
  try
  {
      if (_photonDelay > TimeSpan.Zero)
      {
          await Task.Delay(_photonDelay, ct);
      }
      string url = $"https://photon.komoot.io/api/?q={Uri.EscapeDataString(candidate)}&limit=5";
      json = await _httpClient.GetStringAsync(url, ct);
  }
  finally
  {
      PhotonSemaphore.Release();
  }
  ```
* **Чому небезпечно**: Статичний семафор синхронізує всі запити геокодування через Photon на рівні всього веб-сервера. Якщо зовнішній сервіс Photon зависає, або якщо одночасно надходить кілька десятків запитів від клієнтів, черга до семафора зростає необмежено. Оскільки `WaitAsync` не має таймауту, HTTP-потоки ASP.NET блокуються на десятки секунд, викликаючи лавиноподібне вичерпання потоків пулу (thread pool starvation).
* **Потенційний вплив**: Деградація або повне зависання ендпоінта геокодування та створення вакансій, затримки у відповідях API до 30–60 секунд.

---

### FIND-04: Блокуюче зчитування JSON-файлів дампів без обмеження розміру
* **Категорія**: `B. Зовнішні залежності` / `E. Продуктивність і блокування`
* **Файл**: `src/api/Workers/JobParserService.cs` (рядки 110–114)
* **Фрагмент коду**:
  ```csharp
  foreach (string file in Directory.GetFiles(_directory, "*.json").Order(StringComparer.Ordinal))
  {
      await IngestAsync(Path.GetFileName(file), async () => ParseDump(await File.ReadAllTextAsync(file, ct)), ct);
  }
  ```
* **Чому небезпечно**: Метод `File.ReadAllTextAsync` зчитує весь файл у пам'ять одним рядком. Якщо сторонній скрипт покладе в каталог дамп розміром 500 МБ+, виникне сплеск споживання пам'яті (LOH allocation) аж до `OutOfMemoryException`. Крім того, відсутній режим блокування `FileShare.Read`, тому якщо файл ще записується зовнішнім процесом, виникає неперехоплений `IOException: The process cannot access the file because it is being used by another process`.
* **Потенційний вплив**: Збій фонового імпорту даних, надмірне споживання пам'яті API-сервером.

---

### FIND-05: Відсутність валідації координат та відсутність перевірки на `null` у синхронізації геокешу
* **Категорія**: `C. Валідація вхідних даних`
* **Файл**: `src/api/Controllers/GeoController.cs` (рядки 63–67) та `src/waterfall.Services/GeoLocationService.cs` (рядки 74–86)
* **Фрагмент коду**:
  ```csharp
  [HttpPost("sync")]
  public async Task<ActionResult<GeoSyncResponseDto>> SyncCoordinates([FromBody] GeoSyncRequestDto request, CancellationToken ct)
  {
      GeoSyncResponseDto response = await _geoService.SyncCacheAsync(request, ct);
      return Ok(response);
  }
  ```
  і в `GeoLocationService`:
  ```csharp
  if (request?.ClientItems != null && request.ClientItems.Count > 0)
  {
      foreach (var item in request.ClientItems)
      {
          if (!string.IsNullOrWhiteSpace(item.Address))
          {
              await _cacheRepository.SetCoordinatesAsync(item.Address, item.Latitude, item.Longitude, ct);
          }
      }
  }
  ```
* **Чому небезпечно**:
  1. Немає перевірки `request == null` чи `ModelState.IsValid`.
  2. Немає граничних перевірок на `item.Latitude` (допустимо [-90..+90]) та `item.Longitude` (допустимо [-180..+180]).
  3. Немає перевірки на `double.IsNaN` чи `double.IsInfinity`. Невалідні координати потрапляють у базу даних і отруюють геокеш (Cache Poisoning), після чого клієнти на мапі отримують артефакти або аварійні збої картографічного компонента.
  4. Клієнт може надіслати масив із 100 000 елементів, що викличе перевантаження бази даних.
* **Потенційний вплив**: Засмічення або пошкодження бази геоданих некоректними координатами, вихід з ладу розрахунку відстаней між шукачем та вакансією.

---

### FIND-06: Відсутність guard clauses при збереженні вакансії у закладки
* **Категорія**: `C. Валідація вхідних даних`
* **Файл**: `src/waterfall.Services/SavedVacancyService.cs` (рядки 44–55)
* **Фрагмент коду**:
  ```csharp
  public async Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, Guid userId, CancellationToken ct = default)
  {
      SavedVacancyDB entity = new SavedVacancyDB
      {
          Id = Guid.NewGuid(),
          VacancyId = request.VacancyId, // Якщо request == null -> NullReferenceException
          UserId = userId,
          Notes = request.Notes?.Trim(),
          SavedAt = DateTimeOffset.UtcNow
      };
      ...
  ```
* **Чому небезпечно**: Сервісний шар не захищений від некоректних аргументів (`request == null`, `request.VacancyId == Guid.Empty`, `userId == Guid.Empty`). Якщо контролер або внутрішній компонент передасть некоректні дані, станеться або `NullReferenceException`, або порушення foreign key у базі даних (HTTP 500 замість чіткого повідомлення про невалідні параметри).
* **Потенційний вплив**: Непередбачуваний внутрішній стан, неочевидні HTTP 500 Internal Server Error, ускладнення діагностики.

---

### FIND-07: Мовчазне проковтування винятків десеріалізації у `ParseSkills`
* **Категорія**: `D. Потенційні мовчазні збої`
* **Файл**: `src/waterfall.Services/VacancyService.cs` (рядки 273–284)
* **Фрагмент коду**:
  ```csharp
  internal static IReadOnlyList<string> ParseSkills(string? json)
  {
      if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
      try
      {
          return JsonSerializer.Deserialize<List<string>>(json) ?? (IReadOnlyList<string>)Array.Empty<string>();
      }
      catch
      {
          return Array.Empty<string>(); // Помилка ковтається мовчки!
      }
  }
  ```
* **Чому небезпечно**: Якщо в базі даних опинився пошкоджений або некоректний JSON (наприклад, через збій міграції або помилку парсера джерела), `JsonException` перехоплюється і повністю замовчується. Вакансії залишаються без навичок, пошук за навичками перестає їх знаходити, а в системних логах немає жодного попередження про те, що дані пошкоджено.
* **Потенційний вплив**: Непомітна деградація якості даних у каталозі вакансій, труднощі виявлення помилок імпорту.

---

### FIND-08: Завантаження всієї таблиці бази даних у пам'ять при промаху кешу
* **Категорія**: `E. Продуктивність і блокування`
* **Файл**: `src/waterfall.Repository/Repositories/EfGeoLocationCacheRepository.cs` (рядки 53–54)
* **Фрагмент коду**:
  ```csharp
  List<GeoLocationCacheDB> cachedList = await _context.GeoLocationCache.ToListAsync(ct);
  ```
* **Чому небезпечно**: Якщо точний збіг нормалізованої адреси не знайдено, репозиторій виконує `ToListAsync(ct)` без пагінації та ліміту `Take(...)`. Уся таблиця кешу адрес завантажується в оперативну пам'ять для послідовного перебору в пам'яті C#. Коли база даних розростеться до десятків тисяч рядків, кожний запит невідомої адреси буде викликати гігантський обсяг пам'яті, GC-паузи та навантаження на диск і мережу СУБД.
* **Потенційний вплив**: Різке падіння пропускної здатності сервера, тривалі блокування I/O, ризик падіння API від браку пам'яті (OOM).

---

## ⛓️ Етап 2: Класифікація Несправність → Помилка → Відмова


| ID | Несправність (Fault) | Помилка (Error) | Відмова (Failure) | Критичність |
|---|---|---|---|---|
| **FIND-01** | Відсутній `try/catch` навколо HTTP-запиту завантаження `robots.txt` у `SiteCrawler.LoadRobotsAsync` | Необроблений `HttpRequestException` або `TaskCanceledException` обриває виконання методу `CrawlAsync` | Фоновий сервіс збору вакансій припиняє обхід джерела; користувачі не бачать оновлення вакансій з сайту | **Висока** |
| **FIND-02** | Порожній `catch {}` у `PhotonAddressAutocompleteService` без фільтрації `OperationCanceledException` | Виняток скасування перехоплюється; потік виконання продовжує виконувати резервний запит замість виходу | Мобільний додаток відправляє зайві HTTP-запити, розряджає акумулятор, спричиняє затримки інтерфейсу автокомпліту | **Середня** |
| **FIND-03** | Необмежене очікування взяття семафора `PhotonSemaphore.WaitAsync(ct)` під час зовнішнього HTTP-виклику | Накопичення черги потоків ASP.NET, які чекають доступу до повільного або завислого зовнішнього API | Зависання ендпоінта геокодування; користувач чекає відповіді 30+ секунд або отримує помилку 504 Gateway Timeout | **Висока** |
| **FIND-04** | Читання локальних файлів дампів через `File.ReadAllTextAsync` без обмеження розміру та без безпечного `FileShare.Read` | Виникнення `IOException` при паралельному записі або сплеск виділення пам'яті в LOH | Збій фонової обробки дампів; пропуск нових вакансій, високе споживання RAM процесом API | **Середня** |
| **FIND-05** | Відсутність перевірки моделі та діапазону значень координат у `GeoController.SyncCoordinates` | Збереження некоректних координат (`NaN`, поза межами [-90..90]/[-180..180]) у базі даних | Мапа у додатку некоректно відображає маркери або аварійно падає при спробі обчислити відстань; отруєння геокешу | **Висока** |
| **FIND-06** | Відсутність захисних перевірок (guard clauses) на `null` та `Guid.Empty` у `SavedVacancyService.SaveAsync` | Перехід сервісу у стан виклику властивості у `null` або порушення зовнішнього ключа БД | Клієнт отримує неінформативну відповідь 500 Internal Server Error замість зрозумілого 400 Bad Request | **Середня** |
| **FIND-07** | Ковтання `JsonException` без логування у `VacancyService.ParseSkills` | Некоректний або пошкоджений запис у базі призводить до повернення порожнього списку без реєстрації події | Вакансії мовчки втрачають теги навичок; фільтрація та рекомендації працюють некоректно, збій неможливо діагностувати | **Середня** |
| **FIND-08** | Виклик `_context.GeoLocationCache.ToListAsync(ct)` без ліміту записів при неточному пошуку | Завантаження всього набору даних таблиці в оперативну пам'ять на кожний запит | Критичне падіння продуктивності бази даних та бекенду при зростанні бази, зависання пошуку | **Висока** |

---

## 🛠️ Етап 3: Виправлення найкритичніших проблем

Для виправлення обрано **4 найбільш критичні проблеми**:
1. **FIND-01**: Стійке завантаження `robots.txt` у `SiteCrawler.cs` (патерн: безпечна відмова, fallback, перехоплення винятків та логування).
2. **FIND-05**: Валідація координат та захист від отруєння кешу в `GeoController.cs` та `GeoLocationService.cs` (патерн: швидка відмова / fail-fast, перевірка меж).
3. **FIND-06**: Захисні перевірки (guard clauses) у `SavedVacancyService.cs` (патерн: fail-fast з типізованими аргументними винятками).
4. **FIND-07**: Усунення мовчазного збою в `VacancyService.ParseSkills` (патерн: вузький catch-блок, логування попередження про пошкоджений JSON).

### 1. Виправлення FIND-01 (`SiteCrawler.cs` — завантаження robots.txt)
* **Стратегія**: **Безпечна відмова (Fail-Safe)**. Якщо сайт відмовив або мережа тимчасово недоступна при читанні `robots.txt`, система не повинна аварійно падати. Якщо сайт явно повернув код відмови (429, 503) або мережевий запит зазнав тайм-ауту/збою, записуємо попередження в журнал і призупиняємо сайт через `PauseAfterRefusal`, або використовуємо безпечний fallback.
* **До**:
  ```csharp
  Uri robotsUrl = new(_site.Home, "/robots.txt");
  using HttpResponseMessage response = await http.GetAsync(robotsUrl, ct);
  ```
* **Після**:
  ```csharp
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
  ```

### 2. Виправлення FIND-05 (`GeoController.cs` & `GeoLocationService.cs` — валідація координат)
* **Стратегія**: **Швидка відмова (Fail-Fast)** у контролері та **захисна фільтрація (Defensive Validation)** у сервісі. Якщо тіло запиту порожнє або координати виходять за фізичні межі Землі (широта [-90..90], довгота [-180..180]), запит негайно відхиляється з помилкою 400 Bad Request, запобігаючи отруєнню бази даних.
* **До**:
  ```csharp
  [HttpPost("sync")]
  public async Task<ActionResult<GeoSyncResponseDto>> SyncCoordinates([FromBody] GeoSyncRequestDto request, CancellationToken ct)
  {
      GeoSyncResponseDto response = await _geoService.SyncCacheAsync(request, ct);
      return Ok(response);
  }
  ```
* **Після**:
  ```csharp
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
  У `GeoLocationService.SyncCacheAsync`:
  ```csharp
  // Перевірка допустимих фізичних меж координат та відкидання NaN/Infinity
  if (!double.IsNaN(item.Latitude) && !double.IsInfinity(item.Latitude) &&
      !double.IsNaN(item.Longitude) && !double.IsInfinity(item.Longitude) &&
      item.Latitude >= -90.0 && item.Latitude <= 90.0 &&
      item.Longitude >= -180.0 && item.Longitude <= 180.0)
  {
      await _cacheRepository.SetCoordinatesAsync(item.Address, item.Latitude, item.Longitude, ct);
  }
  ```

### 3. Виправлення FIND-06 (`SavedVacancyService.cs` — guard clauses)
* **Стратегія**: **Швидка відмова (Fail-Fast)**.
* **До**:
  ```csharp
  public async Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, Guid userId, CancellationToken ct = default)
  {
      SavedVacancyDB entity = new SavedVacancyDB
      {
          Id = Guid.NewGuid(),
          VacancyId = request.VacancyId,
          UserId = userId,
  ```
* **Після**:
  ```csharp
  public async Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, Guid userId, CancellationToken ct = default)
  {
      ArgumentNullException.ThrowIfNull(request);
      if (request.VacancyId == Guid.Empty)
      {
          throw new ArgumentException("Ідентифікатор вакансії не може бути порожнім.", nameof(request));
      }
      if (userId == Guid.Empty)
      {
          throw new ArgumentException("Ідентифікатор користувача не може бути порожнім.", nameof(userId));
      }
  ```

### 4. Виправлення FIND-07 (`VacancyService.cs` — звуження catch та явне логування)
* **Стратегія**: **Безпечна відмова з логуванням (Safe Fallback with Observability)**. Замість універсального порожнього `catch` перехоплюється лише `JsonException`, а факт пошкодження фіксується у системному журналі.
* **До**:
  ```csharp
  internal static IReadOnlyList<string> ParseSkills(string? json)
  {
      if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
      try
      {
          return JsonSerializer.Deserialize<List<string>>(json) ?? (IReadOnlyList<string>)Array.Empty<string>();
      }
      catch
      {
          return Array.Empty<string>();
      }
  }
  ```
* **Після**:
  ```csharp
  internal static IReadOnlyList<string> ParseSkills(string? json, ILogger? logger = null)
  {
      if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
      try
      {
          return JsonSerializer.Deserialize<List<string>>(json) ?? (IReadOnlyList<string>)Array.Empty<string>();
      }
      catch (JsonException ex)
      {
          logger?.LogWarning(ex, "Не вдалося десеріалізувати навички з JSON: '{Json}'", json);
          return Array.Empty<string>();
      }
  }
  ```

---

## Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
| :--- | :--- | :--- | :--- |
| Етап 1: Пошук та деталізація 8 вразливостей (FIND-01..FIND-08) за категоріями A–E | Сидоренко Денис | Евристичний пошук антипатернів у кодовій базі, первинне структурування описів | Рядковий аудит знахідок у коді `SiteCrawler`, `PhotonAddressAutocompleteService`, `GeoLocationService`, `JobParserService`, `GeoController`, `SavedVacancyService`, `VacancyService`, `EfGeoLocationCacheRepository` |
| Етап 2: Матриця класифікації Несправність → Помилка → Відмова (Fault → Error → Failure) | Сидоренко Денис | Допомога у формалізації ланцюжків збоїв та оцінці рівнів критичності | Звірка з вимогами надійності (REL-001..REL-003) та архітектурними обмеженнями HLD |
| Етап 3: Реалізація виправлень 4 критичних проблем (FIND-01, FIND-05, FIND-06, FIND-07) | Сидоренко Денис | Генерація захисних guard clauses, фільтрації координат та звуження catch-блоків | Покриття юніт-тестами, запуск повного тестового сьюту (540+ тестів), рев'ю в PR |
| Аналіз стійкості зовнішніх залежностей і скасування запитів (FIND-02, FIND-03) | Сидоренко Денис | Аналіз поведінки під час відмов мережі та CancellationToken | Перевірка коду клієнтського сервісу автокомпліту та тайм-аутів HTTP-клієнта |
| Аналіз надійності обробки файлових дампів та I/O блокувань (FIND-04, FIND-08) | Сидоренко Денис | Аналіз ризиків LOH-виділень та блокування файлових дескрипторів | Перевірка сценаріїв багатопотокового доступу та конфігурації воркерів у CI |
