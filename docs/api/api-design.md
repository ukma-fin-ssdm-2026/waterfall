# Документація проєктування API (API Design)

## 1. Огляд архітектури
- **Базова адреса:** `http://localhost:3000/api/v1`
- **Стиль API:** RESTful API
- **Клієнт:** .NET MAUI мобільний/десктопний додаток (патерн MVVM через HTTP Gateway Service)
- **Бекенд-платформа:** ASP.NET Core Web API (.NET 10, C#)
- **Формат обміну даними:** JSON (`application/json`)
- **Стратегія версіонування:** Версія в шляху URL (`/api/v1/...`). Це забезпечує явність контракту, зворотну сумісність для мобільних клієнтів .NET MAUI та легку підтримку життєвого циклу API.
- **Автентифікація:** Відкритий доступ (`AllowAnonymous`) для каталогу вакансій, пошуку, синхронізації бази вакансій (`/vacancies/sync`), реєстрації/входу, геокодування (`/geo/*`) та перевірок здоров'я (`/health`, `/health/live`); JWT Bearer — для збережених вакансій (`/saved-vacancies`), рекомендацій (`/vacancies/recommendations`), профілю (`/profile/*`), пошуку на сайтах (`/site-searches/*`), ШІ-консультанта (`/ai-consultant/*`, роль `JobSeeker`) та ринкової аналітики (`/analytics`, роль `Analyst`). Access-JWT (HS256) діє 15 хвилин, роль міститься в claims.

---

## 2. Модель ресурсів

### 2.1. Ресурс Vacancies (Вакансії)
- **Кінцева точка (URI):** `/api/v1/vacancies`
- **Опис:** Централізований каталог стандартизованих вакансій, зібраних із різних зовнішніх ресурсів (Djinni, Work.ua, Robota.ua, Dou) або розміщених роботодавцями напряму.
- **Атрибути:**
  - `id` (uuid): Унікальний ідентифікатор вакансії.
  - `external_id` (string/null): Зовнішній ідентифікатор вакансії на сайті-джерелі (використовується для дедуплікації при парсингу та двосторонній синхронізації клієнт ↔ сервер).
  - `title` (string, 3–200 символів): Назва посади.
  - `company` (string, 2–100 символів): Назва компанії-роботодавця.
  - `description` (string, min 20 символів): Детальний опис вакансії та вимоги.
  - `category` (string): Спеціалізація (наприклад, `.NET`, `QA`, `Mobile`, `DevOps`).
  - `location` (string): Локація або формат роботи (наприклад, `Київ (Віддалено)`).
  - `full_address` (string/null, max 200 символів): Повна адреса офісу чи локації (наприклад: `Київ, проспект Академіка Глушкова, 1`).
  - `schedule` (string/null, max 100 символів): Графік і тип зайнятості (наприклад: `Повна зайнятість · Стандартний графік: 5/2`).
  - `experience` (string/null, max 50 символів): Вимоги до досвіду роботи (наприклад, `3+ роки`, `1-3 роки`, `Без досвіду`).
  - `experience_months` (number/null): Мінімальний необхідний досвід у місяцях (`0` — без досвіду, `null` — джерело не вказало).
  - `work_format` (array enum): Формат роботи прапорцями — `Remote`, `Hybrid`, `Office`; можливі кілька одночасно.
  - `category_group` (string/null): Широка канонічна категорія зі спільного довідника (IT і не-IT напрями).
  - `salary_min` (number/null, >= 0): Мінімальна заробітна плата.
  - `salary_max` (number/null, >= 0): Максимальна заробітна плата (`salary_max >= salary_min`).
  - `currency` (string, 3 символи ISO): Валюта (наприклад, `USD`, `UAH`).
  - `source` (enum: `WorkUa`, `RobotaUa`, `Djinni`, `Dou`): Первинне джерело розміщення.
  - `source_url` (string, valid URL): Пряме посилання на оригінальне оголошення.
  - `skills` (array of strings): Ключові технології та теги.
  - `published_at` (datetime ISO 8601): Дата первинної публікації.
  - `is_active` (boolean): Статус актуальності пропозиції.
- **Зв'язки:**
  - Може бути додана у збережені (`SavedVacancies`) багатьма користувачами.
  - Використовується сервісом ШІ-консультанта як контекст для рекомендацій.

### 2.2. Ресурс SavedVacancies (Збережені вакансії / Вподобання)
- **Кінцева точка (URI):** `/api/v1/saved-vacancies`
- **Опис:** Список вакансій, відібраних («лайкнутих») шукачем роботи, які формують профіль вподобань для рекомендаційного алгоритму та особистої аналітики.
- **Атрибути:**
  - `id` (uuid): Ідентифікатор запису збереженої вакансії.
  - `vacancy_id` (uuid): Ідентифікатор вакансії з каталогу.
  - `vacancy` (object/null): Вкладені дані вакансії (`VacancyDto`).
  - `notes` (string/null, max 500 символів): Власні нотатки користувача щодо вакансії.
  - `saved_at` (datetime ISO 8601): Час додавання у збережені.
- **Зв'язки:**
  - Належить до конкретної вакансії (`Vacancy`).
  - Належить профілю поточного шукача роботи.

### 2.3. Ресурс Auth (Автентифікація)
- **Кінцева точка (URI):** `/api/v1/auth`
- **Опис:** Реєстрація нового користувача та вхід із видачею access-JWT. Обидва ендпоінти відкриті (`AllowAnonymous`).
- **Атрибути запиту (`RegisterRequest` / `LoginRequest`):**
  - `email` (string, 3–80 символів): Унікальна електронна пошта, використовується як логін.
  - `password` (string, 12–32 символи, мінімум одна цифра): Пароль; зберігається лише як хеш (SEC-001).
  - `role` (enum: `JobSeeker`, `Analyst`): Роль користувача (лише під час реєстрації).
- **Відповідь (`AuthResponse`):** `token` (access-JWT, HS256, 15 хв), `expires_at`, `user_id`, `email`, `role`.
- **Зв'язки:** JWT використовується як `Authorization: Bearer` для `/saved-vacancies` і `/profile`; `user_id` береться лише з перевіреного токена (THR-12).

### 2.4. Ресурс Profile (Профіль користувача)
- **Кінцева точка (URI):** `/api/v1/profile`
- **Опис:** Персональні дані користувача — домашня/поточна адреса, яка використовується для розрахунку відстані до вакансій. Доступ лише з JWT; історія переглядів та пошукових запитів.
- **Атрибути (`UserAddressDto`):**
  - `address` (string/null, до 300 символів): Адреса користувача; під час збереження обрізаються зайві пробіли, порожня адреса відхиляється `400`.
- **Зв'язки:** Запис у `UserPersonalData` (1:1 з користувачем); адреса передається клієнтському `GeoDistanceService`.

### 2.5. Ресурс Geo (Геокодування та геокеш)
- **Кінцева точка (URI):** `/api/v1/geo`
- **Опис:** Отримання координат адреси (з постійного кешу `GeoLocationCache` або через Photon/Wikipedia) та двостороння синхронізація офлайн-кешу клієнта. Ендпоінти відкриті.
- **Атрибути (`CoordinatesDto`):** `latitude`, `longitude`.
- **Синхронізація (`GeoSyncRequestDto` / `GeoSyncResponseDto`):** список `{ id, address, latitude, longitude, created_at }`; клієнт надсилає знайдені офлайн записи, сервер повертає всі актуальні.
- **Зв'язки:** Кеш спільний для всіх користувачів і не містить `user_id`; результати Wikipedia не кешуються.


### 2.6. Ресурс SiteSearches (Пошук на сайтах на запит — 1 або 1–4 сайти одночасно)
- **Кінцеві точки (URI):** `/api/v1/site-searches` (один сайт) та `/api/v1/site-searches/batch` (кілька сайтів одночасно)
- **Опис:** Користувач обирає один сайт або довільну комбінацію з 1–4 сайтів (Djinni, DOU, Work.ua, Robota.ua) із незалежними фільтрами й параметрами для кожного — сервер читає сторінки списку з цими фільтрами (Djinni — сторінка пошуку, DOU — офіційна RSS-стрічка, 25 найсвіжіших; Work.ua і Robota.ua — браузером разом зі сторінкою кожної вакансії; для Work.ua `listOnly: true` — швидко, лише картки списку), зберігає вакансії в БД і повертає їх. Поки йде обхід, клієнт опитує стан і показує шкалу прогресу (для пакетного пошуку — загальний прогрес і деталізацію по кожному сайту).
- **Запит одного сайту (`SiteSearchRequest`):** `source` (`Djinni` | `Dou` | `WorkUa` | `RobotaUa`), `categories`, `experience`, `city`, `remote`, `employment`, `salaryFrom`, `keywords`, `options` (інші фільтри Work.ua чи Robota.ua як пари «назва=значення»; кілька значень через кому, `category=1,9` на Work.ua стає `category=1+9`) — у позначеннях сайту; `pages` 1–5 (DOU — завжди 1); `fromPage` 1–50 — з якої сторінки почати (читаються `fromPage` … `fromPage + pages − 1`; для DOU лише 1); `listOnly` (лише Work.ua).
- **Запит кількох сайтів (`MultiSiteSearchRequest`):** `requests` — масив з 1–4 елементів `SiteSearchRequest` без повторення `source` (кожен елемент валідується рекурсивно).
- **Стан одного сайту (`SiteSearchStatusDto`):** `id`, `state` (`queued` | `running` | `completed` | `failed`), `pagesDone`, `pagesTotal`, `found`, `fromCache`, `message`, `vacancies` (лише коли `completed`; кожна з `isSaved` і `isViewed`).
- **Стан пакетного пошуку (`MultiSiteSearchStatusDto`):** `id`, `state`, `pagesDone`, `pagesTotal`, `found`, `fromCache`, `message`, `items` (`List<SiteSearchItemStatusDto>` — джерело, стан, прогрес, `found`, `fromCache`, `message` по кожному сайту), `vacancies` (об'єднаний дедуплікований список після завершення).
- **Обмеження:** потрібен JWT; один активний некешований пошук (одиночний або пакетний) на користувача (`429 SITE_SEARCH_LIMIT`); до одного сайту одночасно йде один обхід (решта — `queued`); кожен окремий сайт у пакетному чи одиночному пошуку кешується незалежно на 3 години в БД (`SiteSearchCache`) без повторного звернення до сайту (`fromCache = true`). Збій одного сайту в пакетному пошуку не зупиняє решту.

### 2.7. Двостороння синхронізація вакансій
- **Двостороння синхронізація (`POST /api/v1/vacancies/sync`):** приймає `VacancySyncRequestDto` (`client_vacancies` до 500 записів за пакет, опційне `last_synced_at`, `full_sync`), об'єднує дані з серверною БД (дедуплікація за `(Source, ExternalId)` → `Id` → `SourceUrl`; розв'язання конфліктів Last-Write-Wins за `last_seen_at` зі збереженням повнішого опису та заповненням відсутніх полів) і повертає `VacancySyncResponseDto` з актуальними серверними вакансіями (`server_vacancies`), лічильниками створених/оновлених записів і міткою `server_time`.

### 2.8. Ресурси Recommendations, Analytics, AI Consultant та Health Checks
- **Рекомендації (`GET /api/v1/vacancies/recommendations?limit=20`):** потребує JWT; формує персоналізовану добірку (`RecommendationsResultDto`) на основі збережених вакансій та історії переглядів/пошуків, виключаючи вже збережені.
- **Ринкова аналітика (`GET /api/v1/analytics?category=&months=3`):** потребує JWT із роллю `Analyst` (`403` для інших ролей); повертає `MarketAnalyticsDto` (середня ЗП, частки категорій, зарплати за грейдами/технологіями, динаміка публікацій).
- **ШІ-консультант (`/api/v1/ai-consultant`):** потребує JWT із роллю `JobSeeker`; `POST /messages` (SSE: `meta` → `vacancies` → `delta*` → `done` або `error`), `POST /analyze` (SSE: `relevance` → `delta*` → `done` для 1–3 вакансій), `DELETE /sessions/{id}`.
- **Перевірка здоров'я (`/health`, `/health/live`):** відкриті ендпоінти готовності (перевіряє БД `DatabaseHealthCheck` і зовнішній `PhotonGeocodingHealthCheck`) та життєздатності процесу.

---

## 3. Таблиця кінцевих точок (Endpoint Mapping)

| HTTP Метод | URI | Опис | Тіло запиту | Статус-коди успіху | Статус-коди помилок |
|:---|:---|:---|:---|:---|:---|
| **GET** | `/api/v1/vacancies` | Пагінований список вакансій із фільтрацією | — | `200 OK` (`PagedResult<VacancyDto>`) | `400 Bad Request` |
| **GET** | `/api/v1/vacancies/recommendations` | Персоналізовані рекомендації вакансій | — (JWT, `?limit=`) | `200 OK` (`RecommendationsResultDto`) | `401 Unauthorized` |
| **GET** | `/api/v1/vacancies/{id}` | Деталі вакансії за ID | — | `200 OK` (`VacancyDto`) | `404 Not Found` |
| **POST** | `/api/v1/vacancies` | Створення/імпорт вакансії | `CreateVacancyRequest` | `201 Created` (+ `Location`) | `400 Bad Request` |
| **PUT** | `/api/v1/vacancies/{id}` | Повне оновлення вакансії | `UpdateVacancyRequest` | `200 OK` (`VacancyDto`) | `400 Bad Request`, `404 Not Found` |
| **POST** | `/api/v1/vacancies/sync` | Двостороння синхронізація бази вакансій (клієнт ↔ сервер) | `VacancySyncRequestDto` | `200 OK` (`VacancySyncResponseDto`) | `400 Bad Request` |
| **DELETE** | `/api/v1/vacancies/{id}` | Видалення вакансії з каталогу | — | `204 No Content` | `404 Not Found` |
| **GET** | `/api/v1/saved-vacancies` | Перелік збережених вакансій | — (JWT) | `200 OK` (`List<SavedVacancyDto>`) | `401 Unauthorized` |
| **GET** | `/api/v1/saved-vacancies/{id}` | Отримання збереженої вакансії | — (JWT) | `200 OK` (`SavedVacancyDto`) | `401 Unauthorized`, `404 Not Found` |
| **POST** | `/api/v1/saved-vacancies` | Додавання вакансії до збережених | `SaveVacancyRequest` | `201 Created` (+ `Location`) | `400 Bad Request`, `401 Unauthorized`, `404 Not Found`, `409 Conflict` |
| **DELETE** | `/api/v1/saved-vacancies/{id}` | Видалення зі збережених | — (JWT) | `204 No Content` | `401 Unauthorized`, `404 Not Found` |
| **POST** | `/api/v1/auth/register` | Реєстрація користувача | `RegisterRequest` | `201 Created` (`AuthResponse`) | `400 Bad Request`, `409 Conflict` |
| **POST** | `/api/v1/auth/login` | Вхід і видача access-JWT | `LoginRequest` | `200 OK` (`AuthResponse`) | `400 Bad Request`, `401 Unauthorized` |
| **GET** | `/api/v1/profile/address` | Отримання адреси профілю | — (JWT) | `200 OK` (`UserAddressDto`) | `401 Unauthorized` |
| **PUT** | `/api/v1/profile/address` | Збереження/оновлення адреси | `UserAddressDto` | `200 OK` (`UserAddressDto`) | `400 Bad Request`, `401 Unauthorized` |
| **GET** | `/api/v1/profile/stats` | Отримання персональної статистики | — (JWT) | `200 OK` (`UserStatsDto`) | `401 Unauthorized` |
| **POST** | `/api/v1/profile/searches` | Записати пошуковий запит | `RecordSearchRequest` | `202 Accepted`, `204 No Content` | `400 Bad Request`, `401 Unauthorized`, `403 Forbidden` |
| **DELETE** | `/api/v1/profile/searches` | Очистити історію пошукових запитів | — (JWT) | `204 No Content` | `401 Unauthorized` |
| **POST** | `/api/v1/profile/views` | Відправити запис про перегляд вакансії | `RecordVacancyViewRequest` | `202 Accepted` | `401 Unauthorized`, `403 Forbidden`, `404 Not Found` |
| **GET** | `/api/v1/geo/coordinates` | Координати адреси | — (`?address=`) | `200 OK` (`CoordinatesDto`) | `400 Bad Request`, `404 Not Found` |
| **POST** | `/api/v1/geo/sync` | Двостороння синхронізація геокешу | `GeoSyncRequestDto` | `200 OK` (`GeoSyncResponseDto`) | `400 Bad Request` |
| **POST** | `/api/v1/site-searches` | Запустити пошук на одному сайті за фільтрами | `SiteSearchRequest` | `202 Accepted` (`SiteSearchStatusDto`, + `Location`) | `400 Bad Request`, `401 Unauthorized`, `429 Too Many Requests` |
| **GET** | `/api/v1/site-searches/{id}` | Стан пошуку на сайті і знайдені вакансії | — (JWT) | `200 OK` (`SiteSearchStatusDto`) | `401 Unauthorized`, `404 Not Found` |
| **POST** | `/api/v1/site-searches/batch` | Запустити одночасний пошук на 1–4 сайтах | `MultiSiteSearchRequest` | `202 Accepted` (`MultiSiteSearchStatusDto`, + `Location`) | `400 Bad Request`, `401 Unauthorized`, `429 Too Many Requests` |
| **GET** | `/api/v1/site-searches/batch/{id}` | Агрегований і посайтовий стан пакетного пошуку | — (JWT) | `200 OK` (`MultiSiteSearchStatusDto`) | `401 Unauthorized`, `404 Not Found` |
| **GET** | `/api/v1/analytics` | Ринкова аналітика вакансій (роль `Analyst`) | — (JWT, `?category=&months=`) | `200 OK` (`MarketAnalyticsDto`) | `401 Unauthorized`, `403 Forbidden` |
| **POST** | `/api/v1/ai-consultant/messages` | Повідомлення ШІ-консультанту (SSE-потік) | `AIConsultantMessageRequest` | `200 OK` (`text/event-stream`) | `400`, `401`, `403`, `404`, `429`, `503` |
| **POST** | `/api/v1/ai-consultant/analyze` | ШІ-аналіз 1–3 обраних вакансій (SSE-потік) | `AIAnalysisRequest` | `200 OK` (`text/event-stream`) | `400`, `401`, `403`, `404`, `429`, `503` |
| **DELETE** | `/api/v1/ai-consultant/sessions/{id}` | Видалити діалог ШІ-консультанта | — (JWT) | `204 No Content` | `401 Unauthorized`, `403 Forbidden`, `404 Not Found` |
| **GET** | `/health` | Readiness-перевірка здоров'я (БД + Photon API) | — | `200 OK` (JSON) | `503 Service Unavailable` |
| **GET** | `/health/live` | Liveness-перевірка працездатності процесу API | — | `200 OK` (`text/plain`, `Healthy`) | — |

---

## 4. Проєктні рішення (Design Decisions)

### 4.1. Чому Code-First?
Для нашої команди обрано підхід **Code-First** (з використанням C# .NET 10, Swashbuckle / OpenAPI generator) з таких причин:
1. **Єдине джерело правди (Single Source of Truth):** Специфікація `openapi-generated.yaml` генерується автоматично безпосередньо з типізованих C# класів, анотацій та XML-документації. Це унеможливлює розсинхронізацію між документацією та реальною поведінкою API.
2. **Типобезпечність та інтеграція з .NET MAUI:** Клієнтський шар у .NET MAUI може безпосередньо використовувати ті самі DTO або згенеровані клієнти без ручного переписування моделей.
3. **Швидкість розробки:** Будь-які зміни у валідації чи додавання полів автоматично оновлюють OpenAPI специфікацію при кожному запуску чи в CI-конвеєрі.

### 4.2. Стратегія пагінації (Offset-based Pagination)
- Реалізовано пагінацію зі зсувом (Offset Pagination) через параметри запиту `page` (номер сторінки, починаючи з 1) та `page_size` (розмір вибірки).
- **Значення за замовчуванням:** `page = 1`, `page_size = 10`.
- **Обмеження:** `page_size` обмежено діапазоном від 1 до 100 елементів для захисту від перевантаження сервера та надмірного споживання пам'яті клієнтом.
- **Метадані у відповіді:** Контейнер `PagedResult<T>` повертає:
  - `items`: список знайдених елементів;
  - `total_count`: загальна кількість знайдених записів;
  - `page`: поточний номер сторінки;
  - `page_size`: фактичний розмір сторінки;
  - `total_pages`: кількість сторінок;
  - `has_next_page` та `has_previous_page`: булеві прапорці для спрощення навігації у ViewModel клієнтського застосунку .NET MAUI.

### 4.3. Стратегія фільтрації (Filtering & Search Strategy)
Для забезпечення зручного пошуку роботи підтримуються такі комбіновані параметри фільтрації (`GET /api/v1/vacancies`):
- `search`: Повнотекстовий пошук за ключовими словами у назві посади, компанії, описі та навичках; не залежить від регістру, зокрема для кирилиці.
- `category`: Фільтр за професійною категорією (`.NET`, `QA`, `Mobile`, `Full Stack` тощо).
- `category_group`: Фільтр за широкою канонічною категорією зі спільного довідника (`Software Development`, `QA`, `Legal`, `Sales`, `Other` тощо); точний збіг.
- `experience`: Фільтр за вимогами до досвіду (`3+ роки`, `1-3 роки`, `Без досвіду`).
- `max_experience_months`: Верхня межа мінімального досвіду в місяцях, включно з межею; вакансії з невідомим досвідом не відсіюються.
- `work_format`: Формат роботи (`Remote`, `Hybrid`, `Office`); можна вибрати кілька — повертаються вакансії, які дозволяють будь-який із них.
- `location`: Підрядок міста без урахування регістру.
- `skills`: Список навичок; кожен термін має знайтися в тегах або тексті вакансії, терміни поєднуються через AND.
- `min_salary` та `max_salary`: Діапазон зарплатних очікувань (валідується бізнес-правило `min_salary <= max_salary`).
- `source`: Фільтр за джерелом агрегації (`WorkUa`, `RobotaUa`, `Djinni`, `Dou`).

### 4.4. Обробка помилок (SSDM Error Structure)
Усі помилки API повертаються за єдиним передбачуваним каркасом відповідно до довідника курсу:
```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Запит містить невалідні поля",
    "details": [
      {
        "field": "salary_min",
        "message": "Мінімальна заробітна плата не може бути від'ємною.",
        "provided_value": -100
      }
    ],
    "request_id": "ba8df28ee6cc4ad6ba8bcb26ab9f5096",
    "documentation": "https://ssdm-2026.cs-courses.org/reference/error-structure.html"
  }
}
```
- **Машинозчитувані коди:** `VALIDATION_ERROR`, `NOT_FOUND`, `CONFLICT`.
- **Людинозчитувані повідомлення:** Зрозумілий опис проблеми українською мовою.
- **Деталізація:** Масив `details` вказує на конкретне некоректне поле та отримане значення.
- **Трасування:** Поле `request_id` дублюється в заголовках відповіді `X-Correlation-Id` та `X-Request-Id` для швидкого пошуку в логах.

### 4.5. Автентифікація та авторизація (JWT)
- Реєстрація та вхід повертають access-JWT (HS256, 15 хв); роль користувача дублюється в claims `role`.
- `user_id` для операцій із профілем і збереженими вакансіями береться лише з перевіреного токена, а не з параметрів запиту (THR-12).
- Відкриті ендпоінти: каталог (`/vacancies`), двостороння синхронізація (`/vacancies/sync`, `/geo/sync`), автентифікація (`/auth/*`), геокодування (`/geo/coordinates`), перевірки здоров'я (`/health`, `/health/live`); захищені JWT: `/saved-vacancies`, `/vacancies/recommendations`, `/profile/*`, `/site-searches/*`, `/ai-consultant/*` (роль `JobSeeker`) та `/analytics` (роль `Analyst`).
- Помилки: `401` — невірні облікові дані або відсутній/прострочений токен; `403` — недостатньо прав для ролі.

---

## 5. Використання ШІ та внесок

| Об'єкт | Внесок | ШІ | Як перевірено |
|--------|--------|----|---------------|
| Специфікація та проєктування REST API (моделі ресурсів, ендпоінти, обробка помилок, автогенерація OpenAPI) | Денис | генерація початкової структури документа, узгодження описів полів та схем запитів/відповідей | ручне проєктування архітектури, ретельна перевірка та ручне редагування специфікації, коду та документації |
| Поля `experience_months`, `work_format`, `category_group` і параметри фільтрації каталогу | Садиков Константін | так, текст опису | самоперевірка автора (§8); звірка з фактичним OpenAPI (`openapi-generated.yaml`) і тестами каталогу |
| Ресурси Auth, Profile і Geo | Дмитро | доповнення описів і схем | звірка з `AuthController`, `ProfileController`, `GeoController` і `docs/api/openapi-generated.yaml` |
