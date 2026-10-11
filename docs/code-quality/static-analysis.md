# Статичний аналіз безпеки (SAST) C# бекенду проєкту Waterfall

**Лабораторна робота 5:** Конструювання I – Якість коду, стандарти та огляд  
**Зона відповідальності:** Пункт 2 (Безпековий аналіз C# / SAST) + Код  
**Компонент проєкту:** C# .NET 10 Бекенд (ASP.NET Core Web API, Core Services, EF Core Repositories)  

---

## 1. Стек інструментів та конфігурація SAST

Для безпекового аналізу бекенду використовується інтегрований у процес збірки статичний аналізатор безпеки:

* **Інструмент:** NuGet-пакет **`SecurityCodeScan.VS2019` (версія `5.6.7`)** — офіційний SAST-аналізатор для .NET / C# на базі Roslyn, що перевіряє код на правила безпеки (SCS0001–SCS0035, SQL Injection, CSRF, Path Traversal, Sensitive Data Exposure, небезпечна криптографія).
* **Спеціальний імпорт у проєктах:**  
  Аналізатор імпортовано безпосередньо у файли проєктів бекенду як Roslyn Analyzer:
  - [`src/api/Waterfall.Api.csproj`](file:///c:/Users/user/source/repos/ukma-fin-ssdm-2026/waterfall/src/api/Waterfall.Api.csproj#L15-L18):
    ```xml
    <PackageReference Include="SecurityCodeScan.VS2019" Version="5.6.7">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    ```
  - [`src/waterfall.Services/waterfall.Services.csproj`](file:///c:/Users/user/source/repos/ukma-fin-ssdm-2026/waterfall/src/waterfall.Services/waterfall.Services.csproj#L34-L37)
  - [`src/waterfall.Repository/waterfall.Repository.csproj`](file:///c:/Users/user/source/repos/ukma-fin-ssdm-2026/waterfall/src/waterfall.Repository/waterfall.Repository.csproj#L37-L40)
* **Механізм запуску:**  
  Аналіз не потребує сторонніх утиліт і запускається автоматично компілятором Roslyn під час стандартної збірки проєкту:
  ```bash
  dotnet build src/api/Waterfall.Api.csproj
  ```

---

## 2. Покриття аналізу (Coverage)

Статичний аналізатор безпеки перевіряє всі C# файли вихідного коду бекенд-шарів застосунку:

| Проєкт | Каталог / Файли | Призначення в архітектурі |
| :--- | :--- | :--- |
| **`Waterfall.Api`** | `Controllers/AuthController.cs`, `VacanciesController.cs`, `SavedVacanciesController.cs` | Обробка вхідних HTTP-запитів, передача параметрів, випуск токенів та авторизація |
| **`Waterfall.Api`** | `Services/AuthenticationService.cs`, `JwtTokenService.cs`, `AspNetPasswordHasher.cs` | Безпека автентифікації: обробка паролів, створення JWT, валідація облікових даних |
| **`Waterfall.Api`** | `Program.cs`, `Extensions/ServiceCollectionExtensions.cs` | Конфігурація безпеки конвеєра: JWT Bearer, CORS, хешування, обробка помилок |
| **`waterfall.Services`** | `VacancyService.cs`, `VacancyCacheService.cs` | Доменні сервіси обробки та кешування даних вакансій |
| **`waterfall.Repository`** | `Repositories/EfVacancyRepository.cs`, `EfSavedVacancyRepository.cs`, `EfUserRepository.cs` | Рівень доступу до бази даних SQLite/MSSQL через Entity Framework Core |
| **`waterfall.Repository`** | `Data/WaterfallDbContext.cs`, `Data/WaterfallDbSeeder.cs`, сутності `Entities/` | Схема таблиць, індекси, збереження хешів паролів та зв'язків |

### Результат виконання збірки із запущеним SAST:
```text
  waterfall.Repository -> ...\bin\Debug\net10.0\waterfall.Repository.dll
  waterfall.Services -> ...\bin\Debug\net10.0\waterfall.Services.dll
  Waterfall.Api -> ...\bin\Debug\net10.0\Waterfall.Api.dll

Збірка успішно завершена.
    Попереджень: 0
    Помилок: 0
```
Синтаксичний сканер не виявив прямих помилок типу відкритих SQL-ін'єкцій чи незахищених викликів процесів.

---

## 3. Головна знахідка безпеки (за шаблоном лабораторної)

Під час аудиту коду автентифікації та журналювання було виявлено та усунено вразливість витоку персональних даних.

### 1. Що знайдено (файл, рядок):
У файлах [`src/api/Controllers/AuthController.cs`](file:///c:/Users/user/source/repos/ukma-fin-ssdm-2026/waterfall/src/api/Controllers/AuthController.cs) (рядки 56, 93) та [`src/api/Services/AuthenticationService.cs`](file:///c:/Users/user/source/repos/ukma-fin-ssdm-2026/waterfall/src/api/Services/AuthenticationService.cs) (рядки 50, 72, 79, 97, 105, 108) у початковій реалізації здійснювалося логування email-адрес користувачів під час запитів на вхід і реєстрацію:
- `_logger.LogInformation("Запит реєстрації: {Email}", request.Email);`
- `_logger.LogInformation("Запит входу: {Email}", request.Email);`
- `_logger.LogWarning("Спроба реєстрації на зайнятий email: {Email}", email);`
- `_logger.LogWarning("Невдала спроба входу: {Email}", email);`

### 2. ID правила та класифікація:
* **CWE-532:** Insertion of Sensitive Information into Log File
* **Правило безпеки:** аналог **SCS0028** (SecurityCodeScan: Sensitive Data Exposure / Log Forging) / **CA1848**
* **Стандарт курсу:** порушення вимоги **SEC-001** та загроза **THR-08** із [`threat-model.md`](file:///c:/Users/user/source/repos/ukma-fin-ssdm-2026/waterfall/docs/architecture/threat-model.md) («Зона 2, журнали застосунку — Information disclosure / небезпечне логування email та облікових даних»).

### 3. Чому це ризик:
Журнали застосунку записуються у стандартний вивід і зберігаються на сервері. Логування email-адрес перетворює лог-файли на незахищене сховище персональних даних (ПД). У разі компрометації доступу до серверних логів або систем централізованого збору журналів (ELK, Seq, Grafana Loki) зловмисник отримує готову базу адрес користувачів платформи, що порушує вимоги GDPR та принцип конфіденційності. Крім того, наявність email у логах спрощує атаки типу credential stuffing.

### 4. Як виправлено (код до / після):
Вразливість повністю виправлено в комміті `e6ede5d`: чутливі дані прибрано з журналів; після успішних операцій фіксується виключно сурогатний ідентифікатор `UserId` (GUID), який не розкриває персональних даних.

#### Код ДО виправлення:
```csharp
// AuthController.cs
_logger.LogInformation("Запит реєстрації: {Email}", request.Email);
_logger.LogInformation("Запит входу: {Email}", request.Email);

// AuthenticationService.cs
_logger.LogWarning("Спроба реєстрації на зайнятий email: {Email}", email);
_logger.LogInformation("Користувача зареєстровано: {Email} ({UserId})", email, entity.Id);
_logger.LogWarning("Невдала спроба входу: {Email}", email);
_logger.LogInformation("Успішний вхід: {Email} ({UserId})", email, user.Id);
```

#### Код ПІСЛЯ виправлення:
```csharp
// AuthController.cs
_logger.LogInformation("Запит реєстрації отримано.");
_logger.LogInformation("Запит входу отримано.");

// AuthenticationService.cs
_logger.LogWarning("Спроба реєстрації на вже зайняту адресу.");
_logger.LogInformation("Користувача зареєстровано: {UserId}", entity.Id);
_logger.LogWarning("Невдала спроба входу.");
_logger.LogInformation("Успішний вхід: {UserId}", user.Id);
```

---

## 4. Протистояння загрозам із Моделі загроз (Lab 3 `threat-model.md`)

Оскільки синтаксичний сканер безпеки завершив перевірку з `0` попереджень, опис як код архітектурно протистоїть головним загрозам із моделі загроз:

### Загроза THR-04: SQL-ін'єкція (Tampering, межа TB-2)
- **Сценарій з моделі загроз:** Вхідний рядок пошуку або значення фільтра потрапляє в SQL-запит конкатенацією рядків.
- **Як код протистоїть:**  
  У [`EfVacancyRepository.cs`](file:///c:/Users/user/source/repos/ukma-fin-ssdm-2026/waterfall/src/waterfall.Repository/Repositories/EfVacancyRepository.cs#L41-L55) сирий SQL (`FromSqlRaw`) суворо заборонено правилом проектування. Усі запити конструюються виключно через типізовані вирази LINQ:
  ```csharp
  if (!string.IsNullOrWhiteSpace(search))
  {
      string term = search.Trim().ToLower();
      query = query.Where(v =>
          v.Title.ToLower().Contains(term) ||
          v.Company.ToLower().Contains(term) ||
          v.Description.ToLower().Contains(term));
  }
  ```
  EF Core транслює ці вирази у виключно параметризовані SQL-запити (`@p0`, `@p1`), що унеможливлює зміну структури запиту користувацьким вводом.

### Загроза THR-01: Перебір паролів та атака за часом відповіді (Timing Attack, межа TB-1)
- **Сценарій з моделі загроз:** Зловмисник визначає існування облікового запису за різницею часу відповіді сервера: обчислення для існуючого користувача з хешем виконується довше, ніж швидкий вихід при відсутності email у БД.
- **Як код протистоїть:**  
  У [`AuthenticationService.cs`](file:///c:/Users/user/source/repos/ukma-fin-ssdm-2026/waterfall/src/api/Services/AuthenticationService.cs#L89-L103) та [`AspNetPasswordHasher.cs`](file:///c:/Users/user/source/repos/ukma-fin-ssdm-2026/waterfall/src/api/Services/AspNetPasswordHasher.cs#L25-L38) реалізовано вирівнювання часу обчислень через статичний `DummyHash`:
  ```csharp
  // Якщо користувача з таким email немає, IPasswordHasher все одно обчислює
  // повну перевірку проти DummyHash (виконуючи еквівалентні раунди PBKDF2):
  PasswordVerifyResult result = _passwordHasher.Verify(user?.PasswordHash, request.Password);

  if (user is null || result == PasswordVerifyResult.Failed)
  {
      _logger.LogWarning("Невдала спроба входу.");
      throw new InvalidCredentialsException(); // однаковий 401 Unauthorized
  }
  ```
  Це нівелює часову різницю між гілками й не дає зловмиснику визначити наявність email у базі за часом відповіді API.

---


## Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
| :--- | :--- | :--- | :--- |
| `docs/code-quality/static-analysis.md` | Сидоренко Денис | Допомога у структуруванні та форматуванні звіту статичного аналізу SAST | Самоперевірка за вимогами Лекції 5 та Lab05; перевірка виводу `dotnet build` |
| `src/waterfall.Repository/Repositories/EfVacancyRepository.cs` | Сидоренко Денис | Допомога в оптимізації LINQ-запитів для фільтрації та запобігання SQL-ін'єкціям (THR-04) | Ручне тестування запитів, компіляція та перевірка параметризованих виразів EF Core |
| `src/waterfall.Repository/Repositories/EfSavedVacancyRepository.cs` | Сидоренко Денис | Допомога у формулюванні методів збереження вакансій та мапінгу даних | Код-рев'ю, перевірка зв'язків між користувачем і збереженими вакансіями |
| `src/waterfall.Repository/Data/WaterfallDbContext.cs` | Сидоренко Денис | Допомога у налаштуванні Fluent API, індексів та конфігурації сутностей EF Core | Перевірка цілісності схеми БД, компіляція проекту та самоперевірка) |
