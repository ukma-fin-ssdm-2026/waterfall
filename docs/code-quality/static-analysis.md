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
Вразливість повністю виправлено в коміті `e6ede5d`: чутливі дані прибрано з журналів; після успішних операцій фіксується виключно сурогатний ідентифікатор `UserId` (GUID), який не розкриває персональних даних.

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

### 5. Регресійний тест та Pull Request (Remediation PR):
Вразливість та її запобігання зафіксовано разом із регресійним тестом, який підтверджує захист від повторної появи проблеми в CI:

- **Посилання на Pull Request:** [Pull Request: fix(security): resolve CWE-532 sensitive logging with regression test](https://github.com/ukma-fin-ssdm-2026/waterfall/pull/71) (посилання на гілку та перегляд змін: [compare/main...fix/security-cwe-532-sensitive-logging](https://github.com/ukma-fin-ssdm-2026/waterfall/compare/main...fix/security-cwe-532-sensitive-logging)).
- **Файл регресійного тесту:** [`tests/waterfall.Security.Tests/SensitiveLoggingRegressionTests.cs`](file:///c:/Users/user/source/repos/ukma-fin-ssdm-2026/waterfall/tests/waterfall.Security.Tests/SensitiveLoggingRegressionTests.cs).
- **Механізм перевірки:**  
Тест верифікує виконання тверджень `Assert.DoesNotContain(SensitiveEmail, logEntry)` та `Assert.DoesNotContain(SensitivePassword, logEntry)`.

#### Результат запуску тесту ДО виправлення (Тест ПАДАЄ — Red State):
При поверненні вихідного логування (`_logger.LogInformation("Запит реєстрації: {Email}", request.Email)`) регресійний тест миттєво падає, сигналізуючи про витік персональних даних:
```text
[xUnit.net 00:00:01.83] Waterfall.Security.Tests.SensitiveLoggingRegressionTests.AuthController_ShouldNotLogSensitiveEmail_RegressionTest [FAIL]
  Не пройдено Waterfall.Security.Tests.SensitiveLoggingRegressionTests.AuthController_ShouldNotLogSensitiveEmail_RegressionTest [109 ms]
  Повідомлення про помилку:
   Assert.DoesNotContain() Failure: Sub-string found
                           ↓ (pos 18)
String: "Запит реєстрації: victim.user@example.com"
Found:  "victim.user@example.com"
  Трасування стека:
     at Waterfall.Security.Tests.SensitiveLoggingRegressionTests.AuthController_ShouldNotLogSensitiveEmail_RegressionTest() in tests/waterfall.Security.Tests/SensitiveLoggingRegressionTests.cs:line 38

Не пройдено!: не пройдено 1, пройдено 1, пропущено 0, усього 2, тривалість 109 ms. - waterfall.Security.Tests.dll (net10.0)
```

#### Результат запуску тесту ПІСЛЯ виправлення (Тест ПРОХОДИТЬ — Green State):
Після виправлення коду (прибрано {Email}, замінено на анонімний GUID `UserId`) регресійні тести безпеки успішно проходять:
```text
Тестовий запуск для waterfall.Security.Tests.dll (.NETCoreApp,Version=v10.0)
Загальна кількість тестових файлів (1), що відповідають указаному шаблону.

Пройдено!  : не пройдено     0, пройдено     2, пропущено     0, усього     2, тривалість 81 ms. - waterfall.Security.Tests.dll (net10.0)
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

## 5. TypeScript / MCP-сервер (`src/mcp`): аудит залежностей і лінтер безпеки

**Зона відповідальності:** issue #62: аудит TypeScript / MCP, лінтер безпеки, Бонус 2 + Код (фоновий воркер збору вакансій)  
**Компонент проєкту:** MCP-сервер `src/mcp` (TypeScript, Node.js ≥ 20), згенерований `openapi-mcp-generator` з OpenAPI-специфікації API: 9 інструментів, які перевіряють аргументи й викликають REST API  
**Коміти:** гілка `feature/issue-62-ingestion-worker`; стан «до» — `0b07a93` (лінтер підключено, код сервера ще не змінено)

### 5.1. Інструменти та конфігурація

| Інструмент | Версія | Що перевіряє |
| :--- | :--- | :--- |
| `npm audit` | npm 11.19.0, Node.js 24.20.0 | Відомі вразливості (CVE / GHSA) у всьому дереві залежностей з `package-lock.json` за базою GitHub Advisory Database |
| ESLint + `eslint-plugin-security` | ESLint 10.12.0, плагін 4.2.0 | Небезпечні конструкції: `eval` і `new Function`, регулярні вирази з ризиком ReDoS, `child_process`, доступ до файлів і полів об'єкта за змінним ключем тощо |
| `typescript-eslint` | 8.71.1 (TypeScript 5.9.3) | Розбір TypeScript для ESLint і рекомендовані правила TS |

Конфігурація — [`src/mcp/eslint.config.js`](../../src/mcp/eslint.config.js) у форматі flat config: `js.configs.recommended`, `tseslint.configs.recommended`, `pluginSecurity.configs.recommended` і правила генератора з колишнього `.eslintrc.json` (ESLint 10 цей формат уже не читає).

Запуск:
```bash
cd src/mcp
npm ci --ignore-scripts   # встановлення точно за package-lock.json, без install-скриптів пакетів
npm run audit:deps        # npm audit --audit-level=moderate
npm run lint              # eslint src --max-warnings 0
npm run typecheck         # tsc --noEmit
```
* `--ignore-scripts` не дає виконатися `postinstall`-скриптам залежностей — типовому каналу атак через ланцюжок постачання. Проєкту install-скрипти не потрібні.
* `--max-warnings 0` додано в коміті `5e72efd`: правила `eslint-plugin-security` мають рівень warning, тож без цього прапорця `npm run lint` завершувався успішно навіть з `eval` у коді.
* Ті самі команди виконує в CI workflow [`.github/workflows/mcp.yml`](../../.github/workflows/mcp.yml) на кожну зміну в `src/mcp` (п. 5.6).

### 5.2. Покриття аналізу

| Об'єкт | Призначення | Інструмент |
| :--- | :--- | :--- |
| [`src/mcp/src/index.ts`](../../src/mcp/src/index.ts) | Згенерований сервер: опис 9 інструментів, перевірка аргументів, виклики API через axios, застосування схем автентифікації | ESLint |
| [`src/mcp/src/jsonSchemaToZod.ts`](../../src/mcp/src/jsonSchemaToZod.ts) | Новий модуль: побудова Zod-схеми без `eval` (п. 5.5) | ESLint |
| `src/mcp/package.json`, `src/mcp/package-lock.json` | Дерево залежностей: 206 пакетів після виправлень | `npm audit` |

Каталог `build/` (результат компіляції) з аналізу виключено.

### 5.3. Результати `npm audit` і знахідка в залежностях

| Стан | Пакетів | `npm audit` | Попередження `npm ci` |
| :--- | :---: | :--- | :--- |
| `shx@0.3.4`, вихідний стан | 223 | 0 вразливостей | `inflight@1.0.6` і `glob@7.2.3` позначено як deprecated, для `inflight` — «leaks memory» |
| `shx@0.4.0`, спроба оновлення | 248 | **5 high:** GHSA-vfj7-8cjw-p6xm | — |
| без `shx`, коміт `d9e7a26` | 206 | 0 вразливостей | — |

1. **Що знайдено.** Dev-залежність `shx@0.3.4`, яка в скрипті `build` лише виконує `shx chmod 755 build/index.js`, тягне `shelljs@0.8.5` → `glob@7.2.3` → `inflight@1.0.6`. Автори позначили обидва пакети як застарілі, `inflight` більше не підтримується. CVE для них `npm audit` не показує, але виправлень для покинутих пакетів уже не буде.
2. **Спроба очевидного виправлення.** `shx@0.4.0` переходить на `shelljs@0.9.2` → `fast-glob@3.3.3` → `micromatch@4.0.8` → `braces@3.0.3`, а для `braces` ≤ 3.0.3 є advisory [GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm): DoS через вичерпання стека на глибоко вкладених шаблонах (CWE-674, CVSS 7.5). Виправленої версії в цьому ланцюжку немає, і `npm audit fix` пропонує повернутися на `shx@0.3.4`. Production-залежностей це не стосується: `npm audit --omit=dev` — 0 вразливостей.
3. **Рішення.** `shx` видалено зовсім: `chmod` виконує вбудований модуль Node.js — `node -e "require('node:fs').chmodSync('build/index.js', 0o755)"`. Дерево залежностей зменшилося на 17 пакетів, попереджень і вразливостей немає. Висновок: оновлення залежності треба перевіряти тим самим `npm audit`, бо «очевидне» оновлення принесло вразливість рівня high.

**Свідомо відкладені major-оновлення** (`npm outdated`):

| Пакет | Поточна → остання | Чому не зараз |
| :--- | :--- | :--- |
| `zod` | 3.25.76 → 4.6.6 | Zod 4 змінює API схем і помилок: треба переписати `jsonSchemaToZod.ts` та обробку `ZodError` в `index.ts`. Робити разом із перегенерацією сервера (п. 5.8) |
| `typescript` | 5.9.3 → 7.0.2 | `typescript-eslint` 8.71.1 підтримує TypeScript `>=4.8.4 <6.1.0`, тож на TS 7 лінтер безпеки працюватиме поза підтримуваним діапазоном |
| `dotenv` | 16.6.1 → 18.0.7 | З 17.0.0 `config()` за замовчуванням друкує службове повідомлення. За CHANGELOG dotenv у 17.x воно йшло в stdout, яким MCP-сервер обмінюється JSON-RPC, і лише 18.0.0 перенесла його в stderr. Оновлювати одразу до ≥ 18 і перевіряти stdio smoke-тестом |
| `@types/node` | 22.20.5 → 26.6.5 | Типи Node 26 дозволять використати API, яких немає у версіях, на яких має працювати сервер (`engines: >=20`) |
| `@modelcontextprotocol/sdk` | 1.32.0 → 1.32.1 | Patch у межах `^1.10.0`, прийде згрупованим PR Dependabot (п. 5.6) |

### 5.4. Звіт лінтера

**До виправлень** (коміт `0b07a93`): 8 проблем, з них 3 помилки й 5 попереджень.
```text
src/mcp/src/index.ts
  257:29  warning  Variable Assigned to Object Injection Sink                                     security/detect-object-injection
  279:13  error    'formData' is never reassigned. Use 'const' instead                            prefer-const
  306:13  warning  Generic Object Injection Sink                                                  security/detect-object-injection
  397:56  error    'scopesArray' is defined but never used. Allowed unused args must match /^_/u  @typescript-eslint/no-unused-vars
  398:28  warning  Variable Assigned to Object Injection Sink                                     security/detect-object-injection
  449:28  warning  Variable Assigned to Object Injection Sink                                     security/detect-object-injection
  581:19  error    'e' is defined but never used                                                  @typescript-eslint/no-unused-vars
  715:27  warning  eval with argument of type Identifier                                          security/detect-eval-with-expression

✖ 8 problems (3 errors, 5 warnings)
```

**Після виправлень** (коміт `5e72efd`): проблем немає.
```text
$ npm run lint

> waterfall-api@v1 lint
> eslint src --max-warnings 0

$ echo $?
0
```

| Рядок (до) | Правило | Рішення | Коміт |
| :--- | :--- | :--- | :--- |
| 715 | `security/detect-eval-with-expression` | **Виправлено**, головна знахідка (п. 5.5) | `ceffa9f` |
| 279 | `prefer-const` | Виправлено: `const formData` | `6b8cba5` |
| 397 | `@typescript-eslint/no-unused-vars` | Виправлено: невикористану `scopesArray` прибрано з деструктуризації | `6b8cba5` |
| 581 | `@typescript-eslint/no-unused-vars` | Виправлено: `catch {` без змінної | `6b8cba5` |
| 257, 306 | `security/detect-object-injection` | **Прийнято як хибне спрацювання.** Ключ кешу OAuth-токенів складається з назви схеми безпеки зі згенерованого коду і змінної середовища `OAUTH_CLIENT_ID_*`; аргументи інструмента сюди не потрапляють. До того ж `securitySchemes = {}`, і гілка OAuth зараз недосяжна | `6b8cba5` |
| 398, 449 | `security/detect-object-injection` | **Прийнято як хибне спрацювання.** Назви схем беруться зі згенерованих `securityRequirements` (у всіх інструментів — `[]`), а не з вхідних даних | `6b8cba5` |

Прийняті попередження придушено директивою з причиною безпосередньо над рядком:
```ts
// eslint-disable-next-line security/detect-object-injection -- scheme names come from generated tool definitions, not from tool arguments
const scheme = allSecuritySchemes[schemeName];
```
Якщо директива стане зайвою, ESLint повідомить «Unused eslint-disable directive», і з `--max-warnings 0` перевірка впаде, тож придушення не переживуть зміну коду непомітно. Побачити прийняті попередження можна командою `npx eslint src --no-inline-config`:
```text
src/mcp/src/index.ts
  259:29  warning  Variable Assigned to Object Injection Sink  security/detect-object-injection
  309:13  warning  Generic Object Injection Sink               security/detect-object-injection
  402:28  warning  Variable Assigned to Object Injection Sink  security/detect-object-injection
  454:28  warning  Variable Assigned to Object Injection Sink  security/detect-object-injection

✖ 4 problems (0 errors, 4 warnings)
```

### 5.5. Головна знахідка: `eval` у перевірці аргументів інструментів (за шаблоном лабораторної)

#### 1. Що знайдено (файл, рядок):
[`src/mcp/src/index.ts`](../../src/mcp/src/index.ts), функція `getZodSchemaFromJsonSchema` (рядки 709–724 у коміті `0b07a93`), рядок 715:
```ts
const zodSchemaString = jsonSchemaToZod(jsonSchema); // JSON Schema → текст JavaScript-коду
const zodSchema = eval(zodSchemaString);             // виконання цього тексту
```
`executeApiTool` викликає цю функцію під час кожного виклику інструмента: бібліотека `json-schema-to-zod` перетворює JSON Schema інструмента на рядок коду на кшталт `z.object({ "id": z.string().uuid() })`, а сервер виконує його через `eval`.

#### 2. ID правила та класифікація:
* **Правило:** `security/detect-eval-with-expression` (`eslint-plugin-security`).
* **CWE-95:** Improper Neutralization of Directives in Dynamically Evaluated Code ('Eval Injection').
* **Супутня вада, знайдена під час перевірки виправлення:** CWE-636 (Not Failing Securely). Якщо схему не вдавалося побудувати, сервер підставляв `z.object({}).passthrough()` і пропускав будь-які аргументи.

#### 3. Чому це ризик:
* Код, що виконується, будується з даних — схем інструментів, які генератор переносить з OpenAPI-специфікації. Зараз схеми є константами у згенерованому файлі, а `json-schema-to-zod` екранує рядки через `JSON.stringify`, тож робочого способу експлуатації ми не знайшли. Але захист тримається лише на безпомилковості генератора коду: досить вади екранування в бібліотеці або підробленої специфікації під час перегенерації, і довільний код виконається з правами MCP-сервера. А цей процес має доступ до токенів у змінних середовища (`BEARER_TOKEN_*`, `OAUTH_CLIENT_SECRET_*`) і до мережі.
* Код, що з'являється лише під час виконання, не бачать ні лінтер, ні рецензент.
* `eval` несумісний із захисним режимом Node.js `--disallow-code-generation-from-strings`, а разом із fallback стає небезпечним: у цьому режимі `eval` кидає `EvalError`, сервер ловить помилку і **мовчки вимикає перевірку аргументів** для всіх інструментів. Перевірка stdio-клієнтом (вивід скорочено; API навмисно не запущено, тож `ECONNREFUSED` означає, що аргументи пройшли перевірку і сервер уже звертався до API):
```text
##### ДО (0b07a93), node --disallow-code-generation-from-strings
GetVacancyById {"id":"not-a-uuid"}
  -> API Network Error: No response received from server. (Code: ECONNREFUSED)
GetVacancies {"source":"Linkedin","page":0}
  -> API Network Error: No response received from server. (Code: ECONNREFUSED)
schema build failures in stderr: 5
  Failed to generate/evaluate Zod schema for 'GetVacancyById': EvalError: Code generation from strings disallowed for this context
```

#### 4. Як виправлено (код до / після):
* `ceffa9f` — новий модуль [`src/mcp/src/jsonSchemaToZod.ts`](../../src/mcp/src/jsonSchemaToZod.ts) будує Zod-схему як об'єкт, обходячи JSON Schema, і нічого не виконує. Підтримано ключові слова зі схем інструментів: `type` (зокрема `["string", "null"]`), `enum`, `format` (`uuid`, `uri`, `email`, `date-time`, `int32` / `int64`), `minLength` / `maxLength`, `minimum` / `maximum`, `items` / `minItems` / `maxItems`, `properties` / `required`, `additionalProperties`. `pattern` пропущено свідомо: компілювати регулярний вираз зі специфікації — ризик ReDoS, а формат значення однаково перевіряє API. Залежність `json-schema-to-zod` видалено.
* `8e93555` — fail-closed: помилка побудови схеми більше не вимикає перевірку. `executeApiTool` уже вмів відповідати `Internal error during validation setup`, але помилка до нього не доходила.
* `5e72efd` — `--max-warnings 0` у `npm run lint` (п. 5.1).

Код ДО виправлення:
```ts
function getZodSchemaFromJsonSchema(jsonSchema: any, toolName: string): z.ZodTypeAny {
    if (typeof jsonSchema !== 'object' || jsonSchema === null) {
        return z.object({}).passthrough();
    }
    try {
        const zodSchemaString = jsonSchemaToZod(jsonSchema);
        const zodSchema = eval(zodSchemaString);
        if (typeof zodSchema?.parse !== 'function') {
            throw new Error('Eval did not produce a valid Zod schema.');
        }
        return zodSchema as z.ZodTypeAny;
    } catch (err: any) {
        console.error(`Failed to generate/evaluate Zod schema for '${toolName}':`, err);
        return z.object({}).passthrough();
    }
}
```

Код ПІСЛЯ виправлення:
```ts
import { jsonSchemaToZod } from './jsonSchemaToZod.js';

function getZodSchemaFromJsonSchema(jsonSchema: any, toolName: string): z.ZodTypeAny {
    if (typeof jsonSchema !== 'object' || jsonSchema === null) {
        return z.object({}).passthrough();
    }
    try {
        // Build the schema object directly: no generated code is passed to eval (CWE-95)
        return jsonSchemaToZod(jsonSchema);
    } catch (err: any) {
        console.error(`Failed to build Zod schema for '${toolName}':`, err);
        // Fail closed: the caller reports a validation setup error instead of skipping validation
        throw err;
    }
}
```

#### 5. Перевірка виправлення:
* **Диференційне порівняння** зі старою реалізацією (`json-schema-to-zod` + `eval`) на схемах усіх 9 інструментів і 19 синтетичних схемах: 670 наборів аргументів (валідні; без обов'язкових полів; з неправильними типами; на межах довжин і діапазонів; з невалідними UUID і URL; із зайвими полями). Рішення «прийняти / відхилити» і результат розбору збіглися в 666 випадках. 4 розбіжності очікувані:
  - `page: 1.5` і `page_size: 1.5` у `GetVacancies` та синтетична схема `format: int32` тепер відхиляються, бо в схемі вказано `int32`; стара реалізація вимагала ціле число лише для `int64`;
  - синтетична схема з `pattern` тепер не перевіряється (у схемах інструментів `pattern` немає).
* **stdio smoke-тест** зібраного сервера (`initialize` → `tools/list` → `tools/call`) у режимі `--disallow-code-generation-from-strings`:
```text
##### ПІСЛЯ (HEAD), node --disallow-code-generation-from-strings
GetVacancyById {"id":"not-a-uuid"}
  -> Invalid arguments for tool 'GetVacancyById': id (invalid_string): Invalid uuid
SaveVacancy {"requestBody":{"vacancy_id":"3f2b8c1e-9d4a-4e7b-8c2f-1a2b3c4d5e6f","extra":1}}
  -> Invalid arguments for tool 'SaveVacancy': requestBody (unrecognized_keys): Unrecognized key(s) in object: 'extra'
GetVacancies {"source":"Linkedin","page":0}
  -> Invalid arguments for tool 'GetVacancies': page (too_small): Number must be greater than or equal to 1, source (invalid_enum_value): Invalid enum value. Expected 'WorkUa' | 'RobotaUa' | 'Djinni' | 'Dou', received 'Linkedin'
schema build failures in stderr: 0
```
* **Ін'єкція збою:** модуль перетворювача підмінено таким, що завжди кидає виняток. До `8e93555` усі 5 викликів ішли в API без перевірки, після — `Internal error during validation setup: injected schema build failure`.
* **Регресійний захист:** пробний файл з `eval(code)` дає `security/detect-eval-with-expression` і «ESLint found too many warnings (maximum: 0)», тобто `npm run lint` завершується з кодом 1.

Скрипти порівняння й smoke-тесту одноразові й у репозиторій не додавалися: стара реалізація залежить від видаленої бібліотеки.

### 5.6. Dependabot (Бонус 2)

[`.github/dependabot.yml`](../../.github/dependabot.yml), коміт `c33e4bd`:

| Екосистема | Каталоги | Налаштування |
| :--- | :--- | :--- |
| `npm` | `/src/mcp` | Щопонеділка о 06:00 (Europe/Kyiv); minor і patch — одним PR; cooldown 7 днів |
| `nuget` | `/src/api`, `/src/waterfall.Services`, `/src/waterfall.Repository`, `/tests/*` | Те саме; major-версії `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore*`, `Microsoft.Extensions.*` ігноруються, бо оновлюються разом зі зміною TargetFramework |
| `github-actions` | `/` (`.github/workflows`) | Те саме |

* Major-оновлення приходять окремими PR (як `zod` 4 чи TypeScript 7 з п. 5.3), щоб їх переглядали, а не зливали пакетом.
* `cooldown` пропонує версію лише через 7 днів після публікації: скомпрометовані релізи зазвичай виявляють і відкликають саме в перші дні. На security updates cooldown не діє.
* `src/waterfall.UI` не включено: MAUI-проєкт потребує воркладів, яких немає в середовищі Dependabot.
* **Потрібно від адміністратора репозиторію:** Settings → Code security → увімкнути Dependabot alerts і Dependabot security updates (PR про вразливості поза розкладом). Version updates запрацюють, щойно `dependabot.yml` потрапить у `main`; результати запусків — Insights → Dependency graph → Dependabot.

**`npm audit` у CI** (друга опція бонусу) — workflow [`.github/workflows/mcp.yml`](../../.github/workflows/mcp.yml):

| Крок | Команда | Падає, якщо |
| :--- | :--- | :--- |
| Встановлення | `npm ci --ignore-scripts` | `package-lock.json` не відповідає `package.json` |
| Лінтер безпеки | `npm run lint` | є хоч одне попередження чи помилка ESLint, зокрема `eval` |
| Типи | `npm run typecheck` | `tsc` знаходить помилку |
| Аудит залежностей | `npm run audit:deps` | у дереві залежностей є вразливість рівня moderate і вище |

* Workflow запускається лише на зміни в `src/mcp/**`, тож нове advisory в npm не блокує PR, які MCP-сервер не чіпають. За новими вразливостями між змінами стежать Dependabot alerts.
* Окремий job `test` у [`ci.yml`](../../.github/workflows/ci.yml) запускає на кожен PR тести `waterfall.Security.Tests`, `waterfall.Ingestion.Tests` і `waterfall.Client.Tests` паралельно зі збіркою, з тим самим лімітом 5 хвилин (MAINT-001).

**NuGet-залежності C#-проєктів** перевірено так само:
```bash
dotnet list src/api/Waterfall.Api.csproj package --vulnerable --include-transitive
```
Для всіх п'яти проєктів (`Waterfall.Api`, `waterfall.Services`, `waterfall.Repository`, `waterfall.Security.Tests`, `waterfall.Ingestion.Tests`) результат однаковий: «has no vulnerable packages given the current sources».

### 5.7. Новий C#-код issue #62 (фоновий воркер) під SecurityCodeScan

Фоновий парсер [`src/api/Workers/JobParserService.cs`](../../src/api/Workers/JobParserService.cs) і сервіс нормалізації [`src/waterfall.Services/VacancyIngestionService.cs`](../../src/waterfall.Services/VacancyIngestionService.cs) входять у проєкти з SecurityCodeScan (розділ 1):
```text
$ dotnet build src/api/Waterfall.Api.csproj -c Release --no-incremental -p:TargetFrameworks=net10.0
    0 Warning(s)
    0 Error(s)
```
`-p:TargetFrameworks=net10.0` потрібен лише на машині без MAUI-воркладів.

Як код протистоїть загрозі **THR-03** (Tampering: шахрайська вакансія з фішинговим посиланням або HTML / JavaScript в описі):
* посилання на першоджерело приймається лише як `https://` на `djinni.co` або його піддомен, а параметри запиту відкидаються;
* HTML в описі перетворюється на текст, а `<script>` і `<style>` видаляються разом із вмістом;
* регулярні вирази мають тайм-аут (захист від ReDoS).

Перевірено 27 юніт-тестами в `tests/waterfall.Ingestion.Tests`, зокрема на демонстраційному дампі `data/raw/djinni-sample.json` з фішинговим доменом `djinni-careers.example.net` і `<script>` в описі. Збереження перевіряється на справжньому `EfVacancyRepository` з SQLite у пам'яті: повторний імпорт оновлює вакансії, а не дублює їх.

Покриття рядків за `coverlet` (`dotnet test --collect "XPlat Code Coverage"`): нормалізація (`VacancyIngestionService`) — 100 % (122 з 122), фоновий парсер (`JobParserService`) — 88,1 % (37 з 42). Вимогу MAINT-001 (не менше 70 %) виконано.

### 5.8. Залишкові ризики та наступні кроки
* `index.ts` згенеровано: повторний запуск `openapi-mcp-generator` поверне `eval` і fallback без перевірки. `npm run lint` це зловить, після чого виправлення з комітів `ceffa9f` і `8e93555` треба застосувати знову.
* Схеми інструментів відстали від API: поля в snake_case (`vacancy_id`, `page_size`, `salary_min`), а API після PR #66 приймає camelCase. Перевірка аргументів у MCP відповідає цим схемам, але API таких параметрів не розпізнає, тож сервер треба перегенерувати з актуальної специфікації.
* У моделі загроз ([`threat-model.md`](../architecture/threat-model.md)) немає загроз для MCP-сервера і ланцюжка постачання залежностей — їх варто додати.
* CI запускає тести й перевірки MCP-сервера (п. 5.6), але не збирає звіт покриття: MAINT-001 поки перевірено лише локально (п. 5.7).
* `src/mcp/jest.config.js` посилається на `ts-jest`, якого немає в залежностях: тестів MCP-сервера поки немає.

---


## Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
| :--- | :--- | :--- | :--- |
| `docs/code-quality/static-analysis.md` | Сидоренко Денис | Допомога у структуруванні та форматуванні звіту статичного аналізу SAST | Самоперевірка за вимогами Лекції 5 та Lab05; перевірка виводу `dotnet build` |
| `tests/waterfall.Security.Tests/SensitiveLoggingRegressionTests.cs` | Сидоренко Денис | Допомога у формулюванні регресійних тестів безпеки | Запуск тестів `dotnet test` (перевірка падіння на вразливому коді та проходження на виправленому) |
| `src/waterfall.Repository/Repositories/EfVacancyRepository.cs` | Сидоренко Денис | Допомога в оптимізації LINQ-запитів для фільтрації та запобігання SQL-ін'єкціям (THR-04) | Ручне тестування запитів, компіляція та перевірка параметризованих виразів EF Core |
| `src/waterfall.Repository/Repositories/EfSavedVacancyRepository.cs` | Сидоренко Денис | Допомога у формулюванні методів збереження вакансій та мапінгу даних | Код-рев'ю, перевірка зв'язків між користувачем і збереженими вакансіями |
| `src/waterfall.Repository/Data/WaterfallDbContext.cs` | Сидоренко Денис | Допомога у налаштуванні Fluent API, індексів та конфігурації сутностей EF Core | Перевірка цілісності схеми БД, компіляція проекту та самоперевірка (§8) |
| `docs/code-quality/static-analysis.md`, розділ 5 | Саєнко Данило | Чернетка розділу, розбір знахідок лінтера та `npm audit` | Звірка з виводом `npm audit`, `npm run lint`, `npm outdated`, `dotnet list package --vulnerable`; самоперевірка за вимогами Lab05 |
| `src/mcp/src/jsonSchemaToZod.ts`, `src/mcp/src/index.ts`, `src/mcp/eslint.config.js`, `src/mcp/package.json` | Саєнко Данило | Перетворювач JSON Schema → Zod без `eval`, fail-closed, налаштування ESLint з `eslint-plugin-security`, видалення `shx` | `npm run lint`, `npm run typecheck`, `npm audit`; диференційне порівняння зі старою реалізацією (670 наборів аргументів); stdio smoke-тест, зокрема з `--disallow-code-generation-from-strings` та ін'єкцією збою |
| `.github/dependabot.yml`, `.github/workflows/mcp.yml`, job `test` у `.github/workflows/ci.yml` | Саєнко Данило | Конфігурація Dependabot і CI-перевірок (Бонус 2) | Звірка з довідником опцій Dependabot і `action.yml` setup-node на GitHub, розбір YAML, ті самі команди локально |
| `src/api/Workers/JobParserService.cs`, `src/waterfall.Services/VacancyIngestionService.cs`, `src/waterfall.Services/Interfaces/IVacancyIngestionService.cs`, `BulkUpsertAsync` у репозиторії вакансій, `tests/waterfall.Ingestion.Tests/` | Саєнко Данило | Фоновий парсер дампу Djinni, нормалізація вакансій, масове збереження та юніт-тести | `dotnet test` (27 тестів, зокрема на SQLite); SecurityCodeScan без попереджень; ручний запуск API: з дампу збережено 10 вакансій, 2 відхилено з причиною |
